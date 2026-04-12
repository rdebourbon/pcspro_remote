using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Moq;
using PcsRemote.TrayHost;

namespace PcsRemote.TrayHost.Tests;

[TestClass]
[DoNotParallelize]
public class WinFormsHostedServiceTests
{
    // TC-1: StartAsync creates and starts an STA background thread.
    [TestMethod]
    public async Task StartAsync_WhenCalled_CreatesStaBackgroundThread()
    {
        var lifetimeMock = new Mock<IHostApplicationLifetime>();
        var apartmentStateCaptured = ApartmentState.Unknown;
        var loopExited = new ManualResetEventSlim(initialState: false);
        lifetimeMock.Setup(l => l.StopApplication()).Callback(() => loopExited.Set());

        var svc = new WinFormsHostedService(lifetimeMock.Object, () =>
        {
            apartmentStateCaptured = Thread.CurrentThread.GetApartmentState();
            // Use the first Application.Idle event (fired from inside the running loop)
            // to call Application.Exit(). This guarantees the thread is registered in
            // Application's internal context map before Exit() is called.
            EventHandler? handler = null;
            handler = (_, _) =>
            {
                Application.Idle -= handler;
                Application.Exit();
            };
            Application.Idle += handler;
            return new ApplicationContext();
        });

        await svc.StartAsync(CancellationToken.None);

        // Wait for the loop to exit and StopApplication to be called before asserting,
        // to avoid leaking a running STA thread that would interfere with subsequent tests.
        loopExited.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
            "the STA thread must complete within the timeout");

        apartmentStateCaptured.Should().Be(ApartmentState.STA,
            "WinFormsHostedService must set apartment state to STA before starting the thread");
    }

    // TC-2: StopAsync on an unstarted service returns without throwing.
    [TestMethod]
    public async Task StopAsync_WhenNeverStarted_ReturnsCleanly()
    {
        var lifetimeMock = new Mock<IHostApplicationLifetime>();
        var svc = new WinFormsHostedService(lifetimeMock.Object, () => new ApplicationContext());

        Func<Task> act = () => svc.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync(
            "StopAsync on an unstarted service must not throw");
    }

    // TC-3: When the message loop exits for any reason, StopApplication is called.
    [TestMethod]
    public async Task StartAsync_WhenLoopExits_CallsStopApplication()
    {
        var stopApplicationCalled = new ManualResetEventSlim(initialState: false);
        var lifetimeMock = new Mock<IHostApplicationLifetime>();
        lifetimeMock
            .Setup(l => l.StopApplication())
            .Callback(() => stopApplicationCalled.Set());

        var svc = new WinFormsHostedService(lifetimeMock.Object, () =>
        {
            // Exit the loop from within the first Idle event (loop is pumping by then).
            EventHandler? handler = null;
            handler = (_, _) =>
            {
                Application.Idle -= handler;
                Application.Exit();
            };
            Application.Idle += handler;
            return new ApplicationContext();
        });

        await svc.StartAsync(CancellationToken.None);

        stopApplicationCalled.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
            "StopApplication must be called after the WinForms message loop exits");

        lifetimeMock.Verify(l => l.StopApplication(), Times.Once);
    }

    // TC-6: StopAsync on a running service causes the STA thread to terminate.
    [TestMethod]
    public async Task StopAsync_OnRunningService_TerminatesStaThread()
    {
        var stopApplicationCalled = new ManualResetEventSlim(initialState: false);
        var lifetimeMock = new Mock<IHostApplicationLifetime>();
        lifetimeMock
            .Setup(l => l.StopApplication())
            .Callback(() => stopApplicationCalled.Set());

        // Signal that the pump is actively processing (Idle fires inside a running loop).
        var pumpRunning = new ManualResetEventSlim(initialState: false);

        var svc = new WinFormsHostedService(lifetimeMock.Object, () =>
        {
            EventHandler? handler = null;
            handler = (_, _) =>
            {
                Application.Idle -= handler;
                pumpRunning.Set();
            };
            Application.Idle += handler;
            return new ApplicationContext();
        });

        await svc.StartAsync(CancellationToken.None);

        // Wait until the pump is confirmed running before calling StopAsync.
        // At this point the STA thread is registered in Application's context map,
        // so Application.Exit() inside StopAsync will reliably reach the loop.
        pumpRunning.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
            "the message pump must start within the timeout");

        await svc.StopAsync(CancellationToken.None);

        stopApplicationCalled.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
            "StopApplication must be called after Application.Exit causes the loop to exit");

        lifetimeMock.Verify(l => l.StopApplication(), Times.Once);
    }

    // TC-7: If the ApplicationContext factory throws, StopApplication is still called.
    [TestMethod]
    public async Task StartAsync_WhenFactoryThrows_CallsStopApplication()
    {
        var stopApplicationCalled = new ManualResetEventSlim(initialState: false);
        var lifetimeMock = new Mock<IHostApplicationLifetime>();
        lifetimeMock
            .Setup(l => l.StopApplication())
            .Callback(() => stopApplicationCalled.Set());

        var svc = new WinFormsHostedService(lifetimeMock.Object,
            () => throw new InvalidOperationException("simulated factory failure"));

        await svc.StartAsync(CancellationToken.None);

        stopApplicationCalled.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
            "StopApplication must be called even when the context factory throws");

        lifetimeMock.Verify(l => l.StopApplication(), Times.Once);
    }
}
