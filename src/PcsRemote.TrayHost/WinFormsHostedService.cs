using Microsoft.Extensions.Hosting;
using Serilog;

namespace PcsRemote.TrayHost;

/// <summary>
/// Hosted service that manages the WinForms STA message pump on a dedicated
/// background thread, bridging the ASP.NET Core host lifecycle with the
/// Windows message loop lifecycle.
/// </summary>
internal sealed class WinFormsHostedService : IHostedService, IDisposable
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly Func<ApplicationContext> _contextFactory;
    private readonly ManualResetEventSlim _readySignal = new(initialState: false);
    private Thread? _staThread;

    public WinFormsHostedService(
        IHostApplicationLifetime lifetime,
        Func<ApplicationContext> contextFactory)
    {
        _lifetime = lifetime;
        _contextFactory = contextFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _staThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "WinForms-STA"
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Waits for the readiness signal (confirming the Win32 message queue exists), then
    /// calls <see cref="Application.Exit"/> so the pump receives WM_QUIT and joins the
    /// STA thread to allow the context to dispose cleanly before returning.
    /// Returns immediately if <see cref="StartAsync"/> was never called, if the readiness
    /// signal times out, or if the <paramref name="cancellationToken"/> fires.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_readySignal.Wait(TimeSpan.FromSeconds(5), cancellationToken))
        {
            return Task.CompletedTask;
        }

        Application.Exit();
        _staThread?.Join(TimeSpan.FromSeconds(5));
        return Task.CompletedTask;
    }

    public void Dispose() => _readySignal.Dispose();

    private void RunMessageLoop()
    {
        try
        {
            ApplicationConfiguration.Initialize();

            // Construct the context before signalling readiness: TrayApplicationContext
            // creates a NotifyIcon, which establishes the Win32 message queue for this thread.
            // StopAsync must not call Application.Exit() until the queue exists.
            var context = _contextFactory();
            _readySignal.Set();

            Application.Run(context);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WinForms STA thread terminated unexpectedly");
        }
        finally
        {
            // When the message loop exits (via Application.Exit(), an unhandled exception,
            // or the user closing the tray), propagate shutdown to the ASP.NET Core host.
            _lifetime.StopApplication();
        }
    }
}
