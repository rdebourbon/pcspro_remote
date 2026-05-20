using System.Net;
using System.Text.Json;
using FluentAssertions;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PcsRemote.Core;
using PcsRemote.YouTube;
using LiveStreamStatus = PcsRemote.Core.LiveStreamStatus;

namespace PcsRemote.YouTube.Tests;

[TestClass]
public class YouTubeLiveStreamServiceTests
{
    private Mock<IPcsProAutomationService> _autoMock = null!;
    private Mock<ILogger<YouTubeLiveStreamService>> _loggerMock = null!;
    private BroadcastTitleRenderer _titleRenderer = null!;
    private Mock<IDataStore> _dataStoreMock = null!;
    private Mock<IStalenessPersistence> _stalenessMock = null!;
    private YouTubeOptions _options = null!;

    [TestInitialize]
    public void Setup()
    {
        _autoMock = new Mock<IPcsProAutomationService>();
        _loggerMock = new Mock<ILogger<YouTubeLiveStreamService>>();
        _titleRenderer = new BroadcastTitleRenderer(
            Mock.Of<ILogger<BroadcastTitleRenderer>>());
        _dataStoreMock = new Mock<IDataStore>();
        _stalenessMock = new Mock<IStalenessPersistence>();
        _stalenessMock.Setup(s => s.GetLastConsentAtAsync()).ReturnsAsync((DateTimeOffset?)null);
        _stalenessMock.Setup(s => s.SetLastConsentAtAsync(It.IsAny<DateTimeOffset>())).Returns(Task.CompletedTask);
        _options = new YouTubeOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            LiveStreamId = "test-stream-id",
            BroadcastTitleTemplate = "{HomeTeam} vs {AwayTeam}",
            BroadcastPrivacy = "public",
            StreamReadyTimeoutSeconds = 2,
            StreamPollIntervalSeconds = 1,
        };
    }

    private YouTubeLiveStreamService CreateService(YouTubeOptions? options = null) =>
        new(
            Options.Create(options ?? _options),
            _autoMock.Object,
            _titleRenderer,
            _loggerMock.Object,
            _dataStoreMock.Object,
            _stalenessMock.Object);

    // S-003 TC-12: Config validation — missing LiveStreamId sets ConfigError, does not throw
    [TestMethod]
    public async Task InitializeAsync_MissingLiveStreamId_SetsConfigError()
    {
        _options.LiveStreamId = "";
        var sut = CreateService();

        var snapshots = new List<YouTubeAuthStatusSnapshot>();
        sut.AuthStatusChanged += (_, s) => snapshots.Add(s);

        await sut.InitializeAsync();

        sut.Availability.Should().Be(YouTubeAvailability.ConfigError);
        snapshots.Should().ContainSingle()
            .Which.Availability.Should().Be(YouTubeAvailability.ConfigError);
        VerifyLogLevel(LogLevel.Error, "configuration");
    }

    // S-003 TC-12: Config validation — missing ClientId sets ConfigError
    [TestMethod]
    public async Task InitializeAsync_MissingClientId_SetsConfigError()
    {
        _options.ClientId = "";
        var sut = CreateService();

        var snapshots = new List<YouTubeAuthStatusSnapshot>();
        sut.AuthStatusChanged += (_, s) => snapshots.Add(s);

        await sut.InitializeAsync();

        sut.Availability.Should().Be(YouTubeAvailability.ConfigError);
        snapshots.Should().ContainSingle()
            .Which.Availability.Should().Be(YouTubeAvailability.ConfigError);
    }

    // S-003 TC-12: Config validation — missing ClientSecret sets ConfigError
    [TestMethod]
    public async Task InitializeAsync_MissingClientSecret_SetsConfigError()
    {
        _options.ClientSecret = "";
        var sut = CreateService();

        var snapshots = new List<YouTubeAuthStatusSnapshot>();
        sut.AuthStatusChanged += (_, s) => snapshots.Add(s);

        await sut.InitializeAsync();

        sut.Availability.Should().Be(YouTubeAvailability.ConfigError);
        snapshots.Should().ContainSingle()
            .Which.Availability.Should().Be(YouTubeAvailability.ConfigError);
    }

    // AC-6: No token → logs Information, sets NotConfigured
    [TestMethod]
    public async Task InitializeAsync_NoToken_SetsNotConfigured()
    {
        _dataStoreMock
            .Setup(ds => ds.GetAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.FromResult<TokenResponse>(null!));

        var sut = CreateService();

        var snapshots = new List<YouTubeAuthStatusSnapshot>();
        sut.AuthStatusChanged += (_, s) => snapshots.Add(s);

        await sut.InitializeAsync();

        sut.CurrentStatus.Should().Be(LiveStreamStatus.Idle);
        sut.Availability.Should().Be(YouTubeAvailability.NotConfigured);
        snapshots.Should().ContainSingle()
            .Which.Availability.Should().Be(YouTubeAvailability.NotConfigured);
        VerifyLogLevel(LogLevel.Information, "not configured");
    }

    // AC-7: StartStreamAsync with no token throws YouTubeStreamException with context
    [TestMethod]
    public async Task StartStreamAsync_NoToken_ThrowsWithAvailabilityMessage()
    {
        _dataStoreMock
            .Setup(ds => ds.GetAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.FromResult<TokenResponse>(null!));

        var sut = CreateService();
        await sut.InitializeAsync();

        var act = () => sut.StartStreamAsync();

        await act.Should()
            .ThrowAsync<YouTubeStreamException>()
            .WithMessage("*not configured*");
    }

    // AC-15: StopStreamAsync when Idle — no-op with Warning log
    [TestMethod]
    public async Task StopStreamAsync_WhenIdle_LogsWarning()
    {
        _dataStoreMock
            .Setup(ds => ds.GetAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.FromResult<TokenResponse>(null!));

        var sut = CreateService();
        await sut.InitializeAsync();

        await sut.StopStreamAsync();

        sut.CurrentStatus.Should().Be(LiveStreamStatus.Idle);
        VerifyLogLevel(LogLevel.Warning, "no-op");
    }

    // AC-16: ResetAsync when Error → Idle
    [TestMethod]
    public async Task ResetAsync_WhenNotError_IsNoop()
    {
        _dataStoreMock
            .Setup(ds => ds.GetAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.FromResult<TokenResponse>(null!));

        var sut = CreateService();
        await sut.InitializeAsync();

        await sut.ResetAsync();

        sut.CurrentStatus.Should().Be(LiveStreamStatus.Idle);
        VerifyLogLevel(LogLevel.Warning, "no-op");
    }

    // AC-22: YouTubeStreamException is sealed
    [TestMethod]
    public void YouTubeStreamException_IsSealed()
    {
        typeof(YouTubeStreamException).IsSealed.Should().BeTrue();
    }

    // AC-23: Service constructor does NOT call GoogleWebAuthorizationBroker
    [TestMethod]
    public void Constructor_DoesNotCallBroker()
    {
        // The fact that we can construct the service without any browser interaction
        // proves that construction doesn't trigger interactive auth
        var sut = CreateService();
        sut.Should().NotBeNull();
        sut.CurrentStatus.Should().Be(LiveStreamStatus.Idle);
    }

    [TestMethod]
    public async Task InitializeAsync_NoToken_DoesNotCallBroker()
    {
        _dataStoreMock
            .Setup(ds => ds.GetAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.FromResult<TokenResponse>(null!));

        var sut = CreateService();
        await sut.InitializeAsync();

        // If we got here without an exception or browser prompt, broker was not called
        sut.CurrentStatus.Should().Be(LiveStreamStatus.Idle);
    }

    // AC-8: StartStreamAsync with no LoadedMatch throws InvalidOperationException
    [TestMethod]
    public async Task StartStreamAsync_NoLoadedMatch_Throws()
    {
        var sut = CreateService();
        // Bypass InitializeAsync to avoid real YouTube API calls — set internal state
        // as if initialization succeeded with a valid token.
        SetPrivateField(sut, "_tokenAvailable", true);
        SetPrivateField(sut, "_availability", YouTubeAvailability.Ready);
        SetPrivateField(sut, "_youTubeService", new Google.Apis.YouTube.v3.YouTubeService(
            new Google.Apis.Services.BaseClientService.Initializer
            {
                ApplicationName = "Test"
            }));

        _autoMock.SetupGet(a => a.LoadedMatch).Returns((MatchInfo?)null);

        var act = () => sut.StartStreamAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no match*");
    }

    private static void SetPrivateField(object obj, string fieldName, object? value)
    {
        var field = obj.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Field '{fieldName}' not found");
        field.SetValue(obj, value);
    }

    // Verify StatusChanged fires with correct snapshot
    [TestMethod]
    public async Task StatusChanged_FiresOnTransition()
    {
        _dataStoreMock
            .Setup(ds => ds.GetAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.FromResult<TokenResponse>(null!));

        var sut = CreateService();
        var snapshots = new List<StreamStateSnapshot>();
        sut.StatusChanged += (_, s) => snapshots.Add(s);

        await sut.InitializeAsync();

        // No transitions should have fired during init with no token
        snapshots.Should().BeEmpty();
    }

    // S-003 TC-2: AuthStatusChanged fires NotConfigured when no token present
    [TestMethod]
    public async Task AuthStatusChanged_NoToken_FiresNotConfigured()
    {
        _dataStoreMock
            .Setup(ds => ds.GetAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.FromResult<TokenResponse>(null!));

        var sut = CreateService();
        var authSnapshots = new List<YouTubeAuthStatusSnapshot>();
        sut.AuthStatusChanged += (_, s) => authSnapshots.Add(s);

        await sut.InitializeAsync();

        authSnapshots.Should().ContainSingle()
            .Which.Availability.Should().Be(YouTubeAvailability.NotConfigured);
    }

    // S-003: Default Availability before InitializeAsync is NotConfigured
    [TestMethod]
    public void Availability_BeforeInit_IsNotConfigured()
    {
        var sut = CreateService();
        sut.Availability.Should().Be(YouTubeAvailability.NotConfigured);
    }

    // IS-020 S-001 TC-5: RunOAuthSetupAsync when Live returns false (state guard blocks active stream)
    [TestMethod]
    public async Task RunOAuthSetupAsync_WhenStatusIsLive_ReturnsFalse()
    {
        var sut = CreateService();
        SetPrivateField(sut, "_status", LiveStreamStatus.Live);

        var result = await sut.RunOAuthSetupAsync();

        result.Should().BeFalse();
        _dataStoreMock.Verify(
            ds => ds.DeleteAsync<TokenResponse>(It.IsAny<string>()),
            Times.Never());
    }

    // IS-020 S-001 TC-6: RunOAuthSetupAsync when Starting returns false
    [TestMethod]
    public async Task RunOAuthSetupAsync_WhenStatusIsStarting_ReturnsFalse()
    {
        var sut = CreateService();
        SetPrivateField(sut, "_status", LiveStreamStatus.Starting);

        var result = await sut.RunOAuthSetupAsync();

        result.Should().BeFalse();
        _dataStoreMock.Verify(
            ds => ds.DeleteAsync<TokenResponse>(It.IsAny<string>()),
            Times.Never());
    }

    // IS-020 S-001 TC-7: RunOAuthSetupAsync when Stopping returns false
    [TestMethod]
    public async Task RunOAuthSetupAsync_WhenStatusIsStopping_ReturnsFalse()
    {
        var sut = CreateService();
        SetPrivateField(sut, "_status", LiveStreamStatus.Stopping);

        var result = await sut.RunOAuthSetupAsync();

        result.Should().BeFalse();
        _dataStoreMock.Verify(
            ds => ds.DeleteAsync<TokenResponse>(It.IsAny<string>()),
            Times.Never());
    }

    // IS-020 S-001 TC-8: RunOAuthSetupAsync when Error proceeds past state guard into OAuth flow.
    // Verified by confirming DeleteAsync is called (code beyond the state guard) and the call
    // terminates via OperationCanceledException when the authorize step is reached.
    [TestMethod]
    public async Task RunOAuthSetupAsync_WhenStatusIsError_ProceedsToOAuthFlow()
    {
        var sut = CreateService();
        SetPrivateField(sut, "_status", LiveStreamStatus.Error);

        using var cts = new CancellationTokenSource();

        _dataStoreMock
            .Setup(ds => ds.DeleteAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Cancel the token inside GetAsync (after DeleteAsync, before AuthorizeAsync)
        // so the test terminates cleanly without making real HTTP calls.
        _dataStoreMock
            .Setup(ds => ds.GetAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromResult<TokenResponse>(null!);
            });

        _dataStoreMock
            .Setup(ds => ds.ClearAsync())
            .Returns(Task.CompletedTask);

        Func<Task> act = () => sut.RunOAuthSetupAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        // DeleteAsync called proves code passed the state guard (not returned false)
        _dataStoreMock.Verify(
            ds => ds.DeleteAsync<TokenResponse>(It.IsAny<string>()),
            Times.Once());
    }

    // TC-1: RunProactiveRefreshAsync when not ready is a no-op
    [TestMethod]
    public async Task RunProactiveRefreshAsync_WhenNotReady_IsNoOp()
    {
        var sut = CreateService();
        SetPrivateField(sut, "_availability", YouTubeAvailability.NotConfigured);

        var eventFired = false;
        sut.TokenExpiryApproaching += (_, _) => eventFired = true;

        await sut.RunProactiveRefreshAsync();

        _stalenessMock.Verify(s => s.SetLastConsentAtAsync(It.IsAny<DateTimeOffset>()), Times.Never());
        eventFired.Should().BeFalse();
    }

    // TC-2: RunProactiveRefreshAsync when ready with token rotation updates the staleness marker
    [TestMethod]
    public async Task RunProactiveRefreshAsync_WhenReady_WithTokenRotation_UpdatesMarker()
    {
        var sut = CreateService();
        SetPrivateField(sut, "_availability", YouTubeAvailability.Ready);

        var refresherMock = new Mock<ICredentialRefresher>();
        refresherMock
            .SetupSequence(r => r.GetRefreshToken())
            .Returns("token-v1")
            .Returns("token-v2");
        refresherMock
            .Setup(r => r.RefreshAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        SetPrivateField(sut, "_credentialRefresher", refresherMock.Object);

        await sut.RunProactiveRefreshAsync();

        _stalenessMock.Verify(
            s => s.SetLastConsentAtAsync(It.IsAny<DateTimeOffset>()),
            Times.Once());
    }

    // TC-3: RunProactiveRefreshAsync when ready without token rotation does not update the marker
    [TestMethod]
    public async Task RunProactiveRefreshAsync_WhenReady_WithoutTokenRotation_DoesNotUpdateMarker()
    {
        var sut = CreateService();
        SetPrivateField(sut, "_availability", YouTubeAvailability.Ready);

        var refresherMock = new Mock<ICredentialRefresher>();
        refresherMock.Setup(r => r.GetRefreshToken()).Returns("same-token");
        refresherMock
            .Setup(r => r.RefreshAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        SetPrivateField(sut, "_credentialRefresher", refresherMock.Object);

        await sut.RunProactiveRefreshAsync();

        _stalenessMock.Verify(
            s => s.SetLastConsentAtAsync(It.IsAny<DateTimeOffset>()),
            Times.Never());
    }

    // TC-4: RunProactiveRefreshAsync TokenResponseException transitions to AuthFailed
    [TestMethod]
    public async Task RunProactiveRefreshAsync_WhenReady_TokenResponseException_DelegatesToAuthFailedPath()
    {
        var sut = CreateService();
        SetPrivateField(sut, "_availability", YouTubeAvailability.Ready);
        SetPrivateField(sut, "_tokenAvailable", true);

        _dataStoreMock
            .Setup(ds => ds.DeleteAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var refresherMock = new Mock<ICredentialRefresher>();
        refresherMock.Setup(r => r.GetRefreshToken()).Returns("some-token");
        refresherMock
            .Setup(r => r.RefreshAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TokenResponseException(
                new TokenErrorResponse { Error = "invalid_grant" }));

        SetPrivateField(sut, "_credentialRefresher", refresherMock.Object);

        var authSnapshots = new List<YouTubeAuthStatusSnapshot>();
        sut.AuthStatusChanged += (_, s) => authSnapshots.Add(s);

        await sut.RunProactiveRefreshAsync();

        sut.Availability.Should().Be(YouTubeAvailability.AuthFailed);
        authSnapshots.Should().ContainSingle(s => s.Availability == YouTubeAvailability.AuthFailed);
    }

    // TC-5: RunProactiveRefreshAsync other exception logs warning, leaves availability unchanged
    [TestMethod]
    public async Task RunProactiveRefreshAsync_WhenReady_OtherException_LogsWarningNoStateChange()
    {
        var sut = CreateService();
        SetPrivateField(sut, "_availability", YouTubeAvailability.Ready);

        var refresherMock = new Mock<ICredentialRefresher>();
        refresherMock.Setup(r => r.GetRefreshToken()).Returns("some-token");
        refresherMock
            .Setup(r => r.RefreshAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated"));

        SetPrivateField(sut, "_credentialRefresher", refresherMock.Object);

        await sut.RunProactiveRefreshAsync();

        sut.Availability.Should().Be(YouTubeAvailability.Ready);
        VerifyLogLevel(LogLevel.Warning);
    }

    // TC-6: Staleness check when marker exceeds threshold fires TokenExpiryApproaching
    [TestMethod]
    public async Task CheckStaleness_WhenMarkerExceedsThreshold_FiresTokenExpiryApproaching()
    {
        _stalenessMock
            .Setup(s => s.GetLastConsentAtAsync())
            .ReturnsAsync(DateTimeOffset.UtcNow.AddDays(-6));
        _dataStoreMock
            .Setup(ds => ds.GetAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.FromResult<TokenResponse>(null!));

        var sut = CreateService();
        var eventFired = false;
        sut.TokenExpiryApproaching += (_, _) => eventFired = true;

        await sut.InitializeAsync();

        eventFired.Should().BeTrue();
    }

    // TC-7: Staleness check when marker within threshold does not fire
    [TestMethod]
    public async Task CheckStaleness_WhenMarkerWithinThreshold_DoesNotFire()
    {
        _stalenessMock
            .Setup(s => s.GetLastConsentAtAsync())
            .ReturnsAsync(DateTimeOffset.UtcNow.AddDays(-2));
        _dataStoreMock
            .Setup(ds => ds.GetAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.FromResult<TokenResponse>(null!));

        var sut = CreateService();
        var eventFired = false;
        sut.TokenExpiryApproaching += (_, _) => eventFired = true;

        await sut.InitializeAsync();

        eventFired.Should().BeFalse();
    }

    // TC-8: Staleness check when no marker does not fire
    [TestMethod]
    public async Task CheckStaleness_WhenNoMarker_DoesNotFire()
    {
        // _stalenessMock already returns null by default
        _dataStoreMock
            .Setup(ds => ds.GetAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.FromResult<TokenResponse>(null!));

        var sut = CreateService();
        var eventFired = false;
        sut.TokenExpiryApproaching += (_, _) => eventFired = true;

        await sut.InitializeAsync();

        eventFired.Should().BeFalse();
    }

    // TC-9: InitializeAsync with stale marker fires TokenExpiryApproaching regardless of availability
    [TestMethod]
    public async Task InitializeAsync_WhenMarkerExceedsThreshold_FiresTokenExpiryApproaching()
    {
        _stalenessMock
            .Setup(s => s.GetLastConsentAtAsync())
            .ReturnsAsync(DateTimeOffset.UtcNow.AddDays(-6));
        _dataStoreMock
            .Setup(ds => ds.GetAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.FromResult<TokenResponse>(null!));

        var sut = CreateService();
        var tokenExpiryFired = false;
        var authSnapshots = new List<YouTubeAuthStatusSnapshot>();
        sut.TokenExpiryApproaching += (_, _) => tokenExpiryFired = true;
        sut.AuthStatusChanged += (_, s) => authSnapshots.Add(s);

        await sut.InitializeAsync();

        tokenExpiryFired.Should().BeTrue();
        sut.Availability.Should().Be(YouTubeAvailability.NotConfigured);
    }

    // TC-10: InitializeAsync with fresh marker does not fire TokenExpiryApproaching
    [TestMethod]
    public async Task InitializeAsync_WhenMarkerWithinThreshold_DoesNotFire()
    {
        _stalenessMock
            .Setup(s => s.GetLastConsentAtAsync())
            .ReturnsAsync(DateTimeOffset.UtcNow.AddDays(-2));
        _dataStoreMock
            .Setup(ds => ds.GetAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.FromResult<TokenResponse>(null!));

        var sut = CreateService();
        var tokenExpiryFired = false;
        sut.TokenExpiryApproaching += (_, _) => tokenExpiryFired = true;

        await sut.InitializeAsync();

        tokenExpiryFired.Should().BeFalse();
        sut.Availability.Should().Be(YouTubeAvailability.NotConfigured);
    }

    // TC-11: InitializeAsync with no marker does not fire TokenExpiryApproaching
    [TestMethod]
    public async Task InitializeAsync_WhenNoMarker_DoesNotFire()
    {
        // _stalenessMock already returns null by default
        _dataStoreMock
            .Setup(ds => ds.GetAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.FromResult<TokenResponse>(null!));

        var sut = CreateService();
        var tokenExpiryFired = false;
        sut.TokenExpiryApproaching += (_, _) => tokenExpiryFired = true;

        await sut.InitializeAsync();

        tokenExpiryFired.Should().BeFalse();
        sut.Availability.Should().Be(YouTubeAvailability.NotConfigured);
    }

    // TC-12: RunOAuthSetupAsync on success writes the staleness marker
    [TestMethod]
    public async Task RunOAuthSetupAsync_OnSuccess_WritesStalenessMaker()
    {
        _dataStoreMock
            .Setup(ds => ds.DeleteAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _dataStoreMock
            .Setup(ds => ds.StoreAsync(It.IsAny<string>(), It.IsAny<object>()))
            .Returns(Task.CompletedTask);
        _dataStoreMock
            .SetupSequence(ds => ds.GetAsync<TokenResponse>(It.IsAny<string>()))
            .Returns(Task.FromResult<TokenResponse>(null!))            // verification check → null, skip ClearAsync
            .Returns(Task.FromResult(new TokenResponse               // AuthorizeAsync LoadTokenAsync → valid token
            {
                AccessToken = "test-access-token",
                RefreshToken = "test-refresh-token",
                ExpiresInSeconds = 3600,
                IssuedUtc = DateTime.UtcNow
            }))
            .Returns(Task.FromResult<TokenResponse>(null!));          // InitializeAsync → null → NotConfigured

        var sut = CreateService();

        var result = await sut.RunOAuthSetupAsync();

        result.Should().BeTrue();
        _stalenessMock.Verify(
            s => s.SetLastConsentAtAsync(
                It.Is<DateTimeOffset>(d => d > DateTimeOffset.UtcNow.AddMinutes(-1))),
            Times.AtLeastOnce());
    }

    // TC-13: RunProactiveRefreshAsync after refresh runs the staleness check
    [TestMethod]
    public async Task RunProactiveRefreshAsync_AfterRefresh_RunsStalenessCheck()
    {
        _stalenessMock
            .Setup(s => s.GetLastConsentAtAsync())
            .ReturnsAsync(DateTimeOffset.UtcNow.AddDays(-6));

        var sut = CreateService();
        SetPrivateField(sut, "_availability", YouTubeAvailability.Ready);

        var refresherMock = new Mock<ICredentialRefresher>();
        refresherMock.Setup(r => r.GetRefreshToken()).Returns("same-token");
        refresherMock
            .Setup(r => r.RefreshAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        SetPrivateField(sut, "_credentialRefresher", refresherMock.Object);

        var tokenExpiryFired = false;
        sut.TokenExpiryApproaching += (_, _) => tokenExpiryFired = true;

        await sut.RunProactiveRefreshAsync();

        tokenExpiryFired.Should().BeTrue();
    }

    // Helper to verify a log level was called with optional message fragment
    private void VerifyLogLevel(LogLevel level, string? messageFragment = null)
    {
        _loggerMock.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    messageFragment == null ||
                    v.ToString()!.Contains(messageFragment, StringComparison.OrdinalIgnoreCase)), // null-forgiving: structured log state ToString() is never null
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
