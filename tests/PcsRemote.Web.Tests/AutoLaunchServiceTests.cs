using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web;

namespace PcsRemote.Web.Tests;

[TestClass]
public sealed class AutoLaunchServiceTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static AutoLaunchService BuildService(
        Mock<IPcsProAutomationService> mockSvc,
        IConfiguration config,
        ILogger<AutoLaunchService>? logger = null) =>
        new(mockSvc.Object, config, logger ?? NullLogger<AutoLaunchService>.Instance);

    // ── Tests ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AutoLaunch_True_CallsLaunchAndLoginAsync()
    {
        // Arrange
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedToken = default;

        var mockSvc = new Mock<IPcsProAutomationService>();
        mockSvc
            .Setup(s => s.LaunchAndLoginAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(ct => { capturedToken = ct; tcs.TrySetResult(true); })
            .Returns(Task.CompletedTask);

        var config = BuildConfig(new Dictionary<string, string?> { ["PcsPro:AutoLaunch"] = "true" });
        var service = BuildService(mockSvc, config);

        // Act
        await service.StartAsync(CancellationToken.None);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — called exactly once
        mockSvc.Verify(s => s.LaunchAndLoginAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Assert — stoppingToken forwarded (R-5): after StopAsync the captured token is cancelled
        await service.StopAsync(CancellationToken.None);
        capturedToken.IsCancellationRequested.Should().BeTrue();

        service.Dispose();
    }

    [TestMethod]
    public async Task AutoLaunch_False_DoesNotCallLaunchAndLoginAsync()
    {
        // Arrange
        var mockSvc = new Mock<IPcsProAutomationService>();
        var config = BuildConfig(new Dictionary<string, string?> { ["PcsPro:AutoLaunch"] = "false" });
        var service = BuildService(mockSvc, config);

        // Act — no await needed; false path has no await so ExecuteAsync is synchronous
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        mockSvc.Verify(s => s.LaunchAndLoginAsync(It.IsAny<CancellationToken>()), Times.Never);

        await service.StopAsync(CancellationToken.None);
        service.Dispose();
    }

    [TestMethod]
    public async Task LaunchThrowsException_IsLoggedAndSwallowed()
    {
        // Arrange
        var mockSvc = new Mock<IPcsProAutomationService>();
        mockSvc
            .Setup(s => s.LaunchAndLoginAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var loggerMock = new Mock<ILogger<AutoLaunchService>>();
        var config = BuildConfig(new Dictionary<string, string?> { ["PcsPro:AutoLaunch"] = "true" });
        var service = BuildService(mockSvc, config, loggerMock.Object);

        // Act
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — logged at Error level with the exception object
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.Is<Exception>(e => e is InvalidOperationException),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Assert — did not propagate (ExecuteTask completed, not faulted)
        service.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();

        await service.StopAsync(CancellationToken.None);
        service.Dispose();
    }

    [TestMethod]
    public async Task LaunchThrowsOperationCancelled_IsSwallowedSilently()
    {
        // Arrange
        var mockSvc = new Mock<IPcsProAutomationService>();
        mockSvc
            .Setup(s => s.LaunchAndLoginAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var loggerMock = new Mock<ILogger<AutoLaunchService>>();
        var config = BuildConfig(new Dictionary<string, string?> { ["PcsPro:AutoLaunch"] = "true" });
        var service = BuildService(mockSvc, config, loggerMock.Object);

        // Act
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — no error logged
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);

        // Assert — did not propagate
        service.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();

        await service.StopAsync(CancellationToken.None);
        service.Dispose();
    }

    [TestMethod]
    public async Task AutoLaunch_KeyAbsent_CallsLaunchAndLoginAsync()
    {
        // Arrange — configuration has no PcsPro:AutoLaunch key → defaults to true
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var mockSvc = new Mock<IPcsProAutomationService>();
        mockSvc
            .Setup(s => s.LaunchAndLoginAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var config = BuildConfig(new Dictionary<string, string?>());
        var service = BuildService(mockSvc, config);

        // Act
        await service.StartAsync(CancellationToken.None);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        mockSvc.Verify(s => s.LaunchAndLoginAsync(It.IsAny<CancellationToken>()), Times.Once);

        await service.StopAsync(CancellationToken.None);
        service.Dispose();
    }

    [TestMethod]
    public async Task Integration_AutoLaunchService_RegisteredAndTriggersLaunch()
    {
        // Arrange
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var mockSvc = new Mock<IPcsProAutomationService>();
        mockSvc.Setup(s => s.CurrentState).Returns(PcsProState.NotRunning);
        mockSvc
            .Setup(s => s.LaunchAndLoginAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("PcsPro:UseMock", "true");
                b.UseSetting("PcsPro:AutoLaunch", "true");
                b.ConfigureTestServices(services =>
                    services.Replace(ServiceDescriptor.Singleton<IPcsProAutomationService>(mockSvc.Object)));
            });

        // Act — creating the client triggers host startup
        await using var _ = factory;
        using var client = factory.CreateClient();
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        mockSvc.Verify(s => s.LaunchAndLoginAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
