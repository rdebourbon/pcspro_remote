namespace PcsRemote.TrayHost;

/// <summary>
/// WinForms <see cref="ApplicationContext"/> that owns the system tray icon for
/// PCS Remote. Hides and disposes the icon on disposal to avoid ghost tray icons
/// on process exit.
/// </summary>
/// <remarks>
/// S-006 uses a system placeholder icon. S-007 will replace it with mode-specific assets
/// and wire up the context menu.
/// </remarks>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;

    public TrayApplicationContext()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "PCS Remote",
            Visible = true
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
