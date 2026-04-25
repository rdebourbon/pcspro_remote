using System.Runtime.InteropServices;
using PcsRemote.Core;
using Serilog;

namespace PcsRemote.TrayHost;

/// <summary>
/// Hidden <see cref="NativeWindow"/> that registers Ctrl+Alt+P as a global
/// hotkey via Win32 <c>RegisterHotKey</c>. On receipt of <c>WM_HOTKEY</c>,
/// calls <see cref="IManualModeService.Toggle"/> to pause/resume automation.
/// </summary>
/// <remarks>
/// Must be created on the STA thread that owns the WinForms message loop.
/// Disposal unregisters the hotkey and destroys the hidden window handle.
/// </remarks>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 1;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_ALT = 0x0001;
    private const uint VK_P = 0x50;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IManualModeService _manualModeService;
    private bool _disposed;
    private bool _registered;

    public HotkeyWindow(IManualModeService manualModeService)
    {
        _manualModeService = manualModeService;

        CreateHandle(new CreateParams());

        if (RegisterHotKey(Handle, HotkeyId, MOD_CONTROL | MOD_ALT, VK_P))
        {
            _registered = true;
            Log.Information("Global hotkey Ctrl+Alt+P registered successfully");
        }
        else
        {
            var error = Marshal.GetLastWin32Error();
            Log.Warning(
                "Failed to register global hotkey Ctrl+Alt+P — error code {ErrorCode}. Tray menu remains the only toggle method",
                error);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam == HotkeyId)
        {
            try
            {
                _manualModeService.Toggle();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Manual mode toggle failed from hotkey");
            }

            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_registered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }

        DestroyHandle();
    }
}
