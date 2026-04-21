using System.Diagnostics;
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

        // Initialize with a safe default icon; UpdateToggleState corrects it below.
        // NotifyIcon must have a valid icon handle before Visible=true triggers NIM_ADD.
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
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
        _notifyIcon.Icon = isManualModeActive ? SystemIcons.Warning : SystemIcons.Application;
    }

    private void OnManualModeChanged(object? sender, bool isActive)
    {
        // ManualModeChanged fires on an ASP.NET Core thread-pool thread.
        // Guard against disposal before posting: unsubscription in Dispose eliminates most
        // races, but a callback may already be in-flight when disposal begins.
        if (_disposed)
        {
            return;
        }

        _invoker.BeginInvoke(() => UpdateToggleState(isActive));
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
            }
            else
            {
                Log.Warning("YouTube OAuth2 setup was not completed — check configuration");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "YouTube OAuth2 setup failed");
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
        var status = _youTubeService.CurrentStatus;
        _youTubeSetupItem.Enabled = !_setupInProgress
            && status == LiveStreamStatus.Idle;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;

            // Disposal ordering per spec R-2: unsubscribe → hide → dispose NotifyIcon
            // → dispose ContextMenuStrip → dispose invoker.
            _manualModeService.ManualModeChanged -= OnManualModeChanged;
            _youTubeService.StatusChanged -= OnStreamStatusChanged;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
            _invoker.Dispose();
        }

        base.Dispose(disposing);
    }
}
