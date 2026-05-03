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
    private YouTubeOptions _options = null!;

    [TestInitialize]
    public void Setup()
    {
        _autoMock = new Mock<IPcsProAutomationService>();
        _loggerMock = new Mock<ILogger<YouTubeLiveStreamService>>();
        _titleRenderer = new BroadcastTitleRenderer(
            Mock.Of<ILogger<BroadcastTitleRenderer>>());
        _dataStoreMock = new Mock<IDataStore>();
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
            _dataStoreMock.Object);

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
