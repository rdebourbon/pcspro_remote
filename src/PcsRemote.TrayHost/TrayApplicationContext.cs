using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using PcsRemote.Core;
using Serilog;

namespace PcsRemote.TrayHost;

/// <summary>
/// WinForms <see cref="ApplicationContext"/> that owns the system tray icon for
/// PCS Remote. Provides a context menu with manual mode toggle, Open Browser, and Exit.
/// Hides and disposes resources on disposal to avoid ghost tray icons on process exit.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly IManualModeService _manualModeService;
    private readonly IPcsProAutomationService _automationService;
    private readonly IYouTubeLiveStreamService _youTubeService;
    private readonly IConfiguration _configuration;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _youTubeSetupItem;
    private readonly Icon _normalIcon;
    private readonly Icon _manualIcon;
    private readonly HotkeyWindow _hotkeyWindow;

    // Spec R-3 specifies SynchronizationContext.Post as the primary marshaling mechanism,
    // with a Control fallback when the context is null at construction time. We use the
    // Control approach exclusively: TrayApplicationContext is constructed inside the
    // factory lambda BEFORE Application.Run() installs the WindowsFormsSynchronizationContext,
    // so SynchronizationContext.Current here is the default (not STA-aware) context.
    // Control.BeginInvoke is functionally equivalent (both post WM_USER to the STA pump)
    // and is reliably STA-aware as long as the HWND has been created on the STA thread.
    private readonly Control _invoker;

    private bool _disposed;
    private bool _setupInProgress;

    public TrayApplicationContext(
        IManualModeService manualModeService,
        IPcsProAutomationService automationService,
        IYouTubeLiveStreamService youTubeService,
        IConfiguration configuration)
    {
        _manualModeService = manualModeService;
        _automationService = automationService;
        _youTubeService = youTubeService;
        _configuration = configuration;

        // Create the invoker on the STA thread and force HWND creation now so
        // BeginInvoke is always safe regardless of whether the message loop has started.
        _invoker = new Control();
        _ = _invoker.Handle;

        _hotkeyWindow = new HotkeyWindow(manualModeService);

        _toggleItem = new ToolStripMenuItem();
        _toggleItem.Click += OnToggleClicked;

        _youTubeSetupItem = new ToolStripMenuItem("YouTube Setup...");
        _youTubeSetupItem.Click += OnYouTubeSetupClicked;

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add(_toggleItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Open Browser", null, OnOpenBrowserClicked);
        _contextMenu.Items.Add(_youTubeSetupItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Exit", null, OnExitClicked);

        // Load custom tray icons from embedded resources (R-3: fail-fast if missing).
        _normalIcon = LoadEmbeddedIcon("PcsRemote.TrayHost.Resources.pcs-remote-normal.ico");
        _manualIcon = LoadEmbeddedIcon("PcsRemote.TrayHost.Resources.pcs-remote-manual.ico");

        // Initialize with the normal icon; UpdateToggleState corrects it below.
        // NotifyIcon must have a valid icon handle before Visible=true triggers NIM_ADD.
        _notifyIcon = new NotifyIcon
        {
            Icon = _normalIcon,
            Text = "PCS Remote",
            ContextMenuStrip = _contextMenu,
            Visible = false
        };

        // Subscribe BEFORE reading initial state to prevent TOCTOU race:
        // if a mode change fires between reading IsManualModeActive and subscribing,
        // the event would be missed and the UI would be permanently stale.
        _manualModeService.ManualModeChanged += OnManualModeChanged;
        _youTubeService.StatusChanged += OnStreamStatusChanged;
        UpdateToggleState(_manualModeService.IsManualModeActive);
        UpdateYouTubeSetupEnabled();
        _notifyIcon.Visible = true;
    }

    private void UpdateToggleState(bool isManualModeActive)
    {
        _toggleItem.Text = isManualModeActive ? "Resume Automation" : "Switch to Manual Mode";
        _notifyIcon.Icon = GetIconForMode(isManualModeActive);
    }

    internal Icon GetIconForMode(bool isManualMode) =>
        isManualMode ? _manualIcon : _normalIcon;

    private void OnManualModeChanged(object? sender, bool isActive)
    {
        // ManualModeChanged fires on an ASP.NET Core thread-pool thread.
        // Guard against disposal before posting: unsubscription in Dispose eliminates most
        // races, but a callback may already be in-flight when disposal begins.
        if (_disposed)
        {
            return;
        }

        _invoker.BeginInvoke(() =>
        {
            UpdateToggleState(isActive);
            var title = "PCS Remote";
            var text = isActive
                ? "Manual Mode ON — automation paused"
                : "Manual Mode OFF — automation resumed";
            _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
        });
    }

    private void OnToggleClicked(object? sender, EventArgs e)
    {
        if (_manualModeService.IsManualModeActive)
        {
            _manualModeService.Disable();
        }
        else
        {
            _manualModeService.Enable();
        }
    }

    private async void OnYouTubeSetupClicked(object? sender, EventArgs e)
    {
        _setupInProgress = true;
        UpdateYouTubeSetupEnabled();
        try
        {
            var result = await _youTubeService.RunOAuthSetupAsync().ConfigureAwait(false);
            if (result)
            {
                Log.Information("YouTube OAuth2 setup completed successfully via tray menu");
                ShowBalloon("YouTube Setup", "OAuth2 authorisation complete.", ToolTipIcon.Info);
            }
            else
            {
                Log.Warning("YouTube OAuth2 setup was not completed — check configuration");
                ShowBalloon("YouTube Setup",
                    "YouTube:ClientId and YouTube:ClientSecret must be set in appsettings.json.",
                    ToolTipIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "YouTube OAuth2 setup failed");
            ShowBalloon("YouTube Setup", "OAuth2 setup failed — see log for details.", ToolTipIcon.Error);
        }
        finally
        {
            _setupInProgress = false;
            if (!_disposed)
            {
                _invoker.BeginInvoke(UpdateYouTubeSetupEnabled);
            }
        }
    }

    private void OnStreamStatusChanged(object? sender, StreamStateSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        _invoker.BeginInvoke(UpdateYouTubeSetupEnabled);
    }

    private void UpdateYouTubeSetupEnabled()
    {
        _youTubeSetupItem.Enabled = !_setupInProgress;
    }

    private void OnOpenBrowserClicked(object? sender, EventArgs e)
    {
        // Hoist url so the catch can log what was actually attempted without a second call.
        var url = ResolveApplicationUrl(_configuration);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open browser at {Url}", url);
        }
    }

    private async void OnExitClicked(object? sender, EventArgs e)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _automationService.StopAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Automation service did not stop cleanly during tray exit");
        }
        finally
        {
            Application.Exit();
        }
    }

    /// <summary>
    /// Resolves the application URL from <paramref name="configuration"/> with fallback.
    /// URL precedence: <c>Kestrel:Endpoints:Http:Url</c> → <c>urls</c> → <c>http://localhost:5000</c>.
    /// Exact wildcard bind addresses (<c>0.0.0.0</c> and <c>[::]</c>) are replaced with
    /// <c>localhost</c> so the URL is navigable in a browser. The bare <c>::</c> replacement
    /// is intentionally omitted to avoid corrupting valid IPv6 addresses such as <c>[::1]</c>
    /// that contain <c>::</c> as a substring; Kestrel always emits IPv6 wildcards as <c>[::]</c>.
    /// </summary>
    internal static string ResolveApplicationUrl(IConfiguration configuration)
    {
        var url = configuration["Kestrel:Endpoints:Http:Url"]
               ?? configuration["urls"]
               ?? "http://localhost:5000";

        // Take the first URL if multiple are semicolon-separated.
        url = url.Split(';')[0].Trim();

        return url
            .Replace("0.0.0.0", "localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("[::]", "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static Icon LoadEmbeddedIcon(string resourceName)
    {
        var assembly = typeof(TrayApplicationContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found in {assembly.GetName().Name}");
        return new Icon(stream);
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        if (_disposed)
            return;

        _invoker.BeginInvoke(() => _notifyIcon.ShowBalloonTip(5000, title, text, icon));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;

            // Disposal ordering per spec R-2: unsubscribe → hide → dispose NotifyIcon
            // → dispose ContextMenuStrip → dispose invoker.
            _manualModeService.ManualModeChanged -= OnManualModeChanged;
            _youTubeService.StatusChanged -= OnStreamStatusChanged;
            _hotkeyWindow.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
            _invoker.Dispose();
            _normalIcon.Dispose();
            _manualIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
