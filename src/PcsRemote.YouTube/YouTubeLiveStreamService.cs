using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PcsRemote.Core;
using LiveStreamStatus = PcsRemote.Core.LiveStreamStatus;

namespace PcsRemote.YouTube;

/// <summary>
/// Real YouTube live stream service implementing broadcast lifecycle via the YouTube Data API v3.
/// </summary>
public sealed class YouTubeLiveStreamService : IYouTubeLiveStreamService, IAsyncDisposable
{
    private readonly YouTubeOptions _options;
    private readonly IPcsProAutomationService _automationService;
    private readonly BroadcastTitleRenderer _titleRenderer;
    private readonly ILogger<YouTubeLiveStreamService> _logger;
    private readonly IDataStore _dataStore;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();

    private LiveStreamStatus _status = LiveStreamStatus.Idle;
    private LiveBroadcastInfo? _currentBroadcast;
    private YouTubeService? _youTubeService;
    private bool _tokenAvailable;
    private CancellationTokenSource? _startCts;

    public YouTubeLiveStreamService(
        IOptions<YouTubeOptions> options,
        IPcsProAutomationService automationService,
        BroadcastTitleRenderer titleRenderer,
        ILogger<YouTubeLiveStreamService> logger,
        IDataStore dataStore)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(automationService);
        ArgumentNullException.ThrowIfNull(titleRenderer);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dataStore);

        _options = options.Value;
        _automationService = automationService;
        _titleRenderer = titleRenderer;
        _logger = logger;
        _dataStore = dataStore;
    }

    /// <inheritdoc/>
    public LiveStreamStatus CurrentStatus
    {
        get { lock (_stateLock) return _status; }
    }

    /// <inheritdoc/>
    public LiveBroadcastInfo? CurrentBroadcast
    {
        get { lock (_stateLock) return _currentBroadcast; }
    }

    /// <inheritdoc/>
    public event EventHandler<StreamStateSnapshot>? StatusChanged;

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ValidateConfiguration();

        var tokenResponse = await _dataStore
            .GetAsync<TokenResponse>(TokenKey)
            .ConfigureAwait(false);

        if (tokenResponse is null)
        {
            _logger.LogInformation(
                "YouTube not configured. Run --setup-youtube on the garage PC to authorize");
            _tokenAvailable = false;
            return;
        }

        var credential = BuildCredentialFromStoredToken(tokenResponse);
        _youTubeService = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "PCS Remote"
        });
        _tokenAvailable = true;

        await ValidateLiveStreamIdAsync(ct).ConfigureAwait(false);
        await ReconcileActiveBroadcastsAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task StartStreamAsync(CancellationToken ct = default)
    {
        ValidateTokenAvailable();

        // Validation and state transition — exceptions propagate directly to caller
        var (title, linkedToken) = await BeginStartAsync(ct).ConfigureAwait(false);

        string? broadcastId = null;
        try
        {
            broadcastId = await CreateAndBindBroadcastAsync(title, linkedToken)
                .ConfigureAwait(false);
            await WaitForStreamAndGoLiveAsync(broadcastId, title, linkedToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await CleanupCancelledStartAsync(broadcastId, cancelledByStop: true)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await CleanupCancelledStartAsync(broadcastId, cancelledByStop: false)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await CleanupFailedStartAsync(broadcastId, ex).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task StopStreamAsync(CancellationToken ct = default)
    {
        StreamStateSnapshot? pendingEvent = null;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_status is not (LiveStreamStatus.Live or LiveStreamStatus.Starting))
            {
                _logger.LogWarning(
                    "StopStreamAsync called in {Status} state — no-op", _status);
                return;
            }

            if (_status == LiveStreamStatus.Starting)
            {
                _startCts?.Cancel();
                return;
            }

            pendingEvent = SetStatus(LiveStreamStatus.Stopping);
        }
        finally
        {
            _gate.Release();
        }

        FireStatusChanged(pendingEvent);

        // Stop PCS Pro streaming before completing broadcast (C-3 sequencing)
        // Use CancellationToken.None: once Stopping begins, PCS Pro shutdown must complete
        try
        {
            await _automationService.StopStreamingAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "StopStreamingAsync failed during StopStreamAsync — continuing with broadcast completion");
        }

        // Transition broadcast to complete (outside gate)
        await TryCompleteBroadcastAsync(ct).ConfigureAwait(false);

        // Finalize to Idle
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            SetBroadcast(null);
            pendingEvent = SetStatus(LiveStreamStatus.Idle);
            _logger.LogInformation("Stream stopped — returned to Idle");
        }
        finally
        {
            _gate.Release();
        }

        FireStatusChanged(pendingEvent);
    }

    /// <inheritdoc/>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        StreamStateSnapshot? pendingEvent = null;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_status != LiveStreamStatus.Error)
            {
                _logger.LogWarning(
                    "ResetAsync called in {Status} state — no-op", _status);
                return;
            }

            SetBroadcast(null);
            pendingEvent = SetStatus(LiveStreamStatus.Idle);
            _logger.LogInformation("Error dismissed — returned to Idle");
        }
        finally
        {
            _gate.Release();
        }

        FireStatusChanged(pendingEvent);
    }

    /// <inheritdoc/>
    public async Task<bool> RunOAuthSetupAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) ||
            string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            _logger.LogError(
                "YouTube:ClientId and YouTube:ClientSecret must be configured before running OAuth setup");
            return false;
        }

        // Hold the gate for the entire OAuth flow to prevent concurrent StartStreamAsync.
        // The OAuth flow is a blocking user interaction (browser consent) — no other
        // stream operations should proceed while it is in progress.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_status != LiveStreamStatus.Idle)
            {
                _logger.LogWarning(
                    "RunOAuthSetupAsync rejected — current status is {Status}, expected Idle",
                    _status);
                return false;
            }

            _logger.LogInformation("YouTube OAuth2 setup starting...");

            await GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets
                {
                    ClientId = _options.ClientId,
                    ClientSecret = _options.ClientSecret
                },
                new[] { YouTubeService.Scope.Youtube },
                "user",
                ct,
                _dataStore).ConfigureAwait(false);

            _logger.LogInformation("YouTube OAuth2 consent complete — re-initialising service");
        }
        finally
        {
            _gate.Release();
        }

        // Re-initialise outside the gate — InitializeAsync does not use the gate
        // and may make YouTube API calls that should not block other callers.
        try
        {
            await InitializeAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Token stored but YouTube configuration incomplete — resolve configuration and restart");
        }

        return true;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _startCts?.Dispose();
        _youTubeService?.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    // ─── StartStreamAsync helpers ────────────────────────────────────────

    private void ValidateTokenAvailable()
    {
        if (!_tokenAvailable || _youTubeService is null)
        {
            throw new YouTubeStreamException(
                "YouTube streaming is not configured. Run the setup command first.");
        }
    }

    /// <summary>
    /// Acquires the gate, validates preconditions, transitions to Starting,
    /// creates a linked CTS, and releases the gate.
    /// </summary>
    private async Task<(string Title, CancellationToken LinkedToken)> BeginStartAsync(
        CancellationToken ct)
    {
        StreamStateSnapshot? pendingEvent = null;
        string title;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_status != LiveStreamStatus.Idle)
            {
                throw new InvalidOperationException(
                    $"Cannot start stream: current status is {_status}, expected Idle");
            }

            var match = _automationService.LoadedMatch
                ?? throw new InvalidOperationException(
                    "Cannot start stream: no match is currently loaded");

            title = _titleRenderer.Render(_options.BroadcastTitleTemplate, match);
            SetBroadcast(new LiveBroadcastInfo("", title, ""));
            pendingEvent = SetStatus(LiveStreamStatus.Starting);

            _startCts?.Dispose();
            _startCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }
        finally
        {
            _gate.Release();
        }

        FireStatusChanged(pendingEvent);
        return (title, _startCts!.Token);
    }

    /// <summary>
    /// Creates a YouTube broadcast and binds it to the configured stream.
    /// Returns the broadcast ID.
    /// </summary>
    private async Task<string> CreateAndBindBroadcastAsync(
        string title, CancellationToken ct)
    {
        var broadcast = await CreateBroadcastAsync(title, ct).ConfigureAwait(false);
        var broadcastId = broadcast.Id;

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_status != LiveStreamStatus.Starting)
            {
                return broadcastId;
            }

            SetBroadcast(new LiveBroadcastInfo(broadcastId, title, ""));
        }
        finally
        {
            _gate.Release();
        }

        await BindBroadcastToStreamAsync(broadcastId, ct).ConfigureAwait(false);
        return broadcastId;
    }

    /// <summary>
    /// Polls for PCS Pro stream readiness, transitions the broadcast to live,
    /// and updates state to Live.
    /// </summary>
    private async Task WaitForStreamAndGoLiveAsync(
        string broadcastId, string title, CancellationToken ct)
    {
        await _automationService.StartStreamingAsync(ct).ConfigureAwait(false);
        await PollStreamReadyAsync(ct).ConfigureAwait(false);
        await TransitionToLiveAsync(broadcastId, ct).ConfigureAwait(false);

        StreamStateSnapshot? pendingEvent = null;

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_status != LiveStreamStatus.Starting)
            {
                return;
            }

            var watchUrl = $"https://youtube.com/watch?v={broadcastId}";
            SetBroadcast(new LiveBroadcastInfo(broadcastId, title, watchUrl));
            pendingEvent = SetStatus(LiveStreamStatus.Live);
            _logger.LogInformation(
                "Stream is now Live: {Title} ({WatchUrl})", title, watchUrl);
        }
        finally
        {
            _gate.Release();
        }

        FireStatusChanged(pendingEvent);
    }

    /// <summary>
    /// Handles cancellation during start — cleans up and transitions to Idle.
    /// S-YT-11: caller cancellation NEVER routes through Error.
    /// </summary>
    private async Task CleanupCancelledStartAsync(string? broadcastId, bool cancelledByStop)
    {
        if (cancelledByStop)
        {
            _logger.LogInformation(
                "StartStreamAsync cancelled by StopStreamAsync — cleaning up");
        }

        try
        {
            await _automationService.StopStreamingAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "StopStreamingAsync failed during cancel cleanup — continuing");
        }

        await TryDeleteBroadcastAsync(broadcastId).ConfigureAwait(false);

        StreamStateSnapshot? pendingEvent = null;

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            SetBroadcast(null);
            pendingEvent = SetStatus(LiveStreamStatus.Idle);
            _logger.LogInformation(
                "Cancelled start cleaned up — broadcast {BroadcastId} deleted, reset to Idle",
                broadcastId ?? "(none)");
        }
        finally
        {
            _gate.Release();
        }

        FireStatusChanged(pendingEvent);
    }

    /// <summary>
    /// Handles unexpected errors during start. Transitions to Error state.
    /// </summary>
    private async Task CleanupFailedStartAsync(string? broadcastId, Exception ex)
    {
        try
        {
            await _automationService.StopStreamingAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception stopEx)
        {
            _logger.LogWarning(stopEx,
                "StopStreamingAsync failed during error cleanup — continuing");
        }

        await TryDeleteBroadcastAsync(broadcastId).ConfigureAwait(false);

        StreamStateSnapshot? pendingEvent = null;

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            SetBroadcast(null);
            pendingEvent = SetStatus(LiveStreamStatus.Error, ex.Message);
            _logger.LogError(ex, "StartStreamAsync failed — transitioned to Error");
        }
        finally
        {
            _gate.Release();
        }

        FireStatusChanged(pendingEvent);
    }

    // ─── State management ────────────────────────────────────────────────

    /// <summary>
    /// Sets the status and returns a snapshot for deferred event firing.
    /// Must be called while holding <see cref="_gate"/> to ensure transition ordering.
    /// Thread-safe for reads via <see cref="_stateLock"/>.
    /// </summary>
    private StreamStateSnapshot SetStatus(LiveStreamStatus newStatus, string? errorMessage = null)
    {
        lock (_stateLock)
        {
            _status = newStatus;
            return new StreamStateSnapshot(newStatus, _currentBroadcast, errorMessage);
        }
    }

    /// <summary>
    /// Sets the current broadcast info. Thread-safe via <see cref="_stateLock"/>.
    /// </summary>
    private void SetBroadcast(LiveBroadcastInfo? broadcast)
    {
        lock (_stateLock)
        {
            _currentBroadcast = broadcast;
        }
    }

    /// <summary>
    /// Fires the StatusChanged event. Must be called OUTSIDE the gate to prevent
    /// deadlock when subscribers call back into the service.
    /// </summary>
    private void FireStatusChanged(StreamStateSnapshot? snapshot)
    {
        if (snapshot is not null)
        {
            StatusChanged?.Invoke(this, snapshot);
        }
    }

    // ─── Configuration and token helpers ─────────────────────────────────

    private const string TokenKey =
        "Google.Apis.Auth.OAuth2.Responses.TokenResponse-user";

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.LiveStreamId))
        {
            _logger.LogCritical("YouTube:LiveStreamId is not configured");
            throw new InvalidOperationException(
                "YouTube:LiveStreamId is required. See the setup guide.");
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            _logger.LogCritical("YouTube:ClientId is not configured");
            throw new InvalidOperationException(
                "YouTube:ClientId is required. See the setup guide.");
        }

        if (string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            _logger.LogCritical("YouTube:ClientSecret is not configured");
            throw new InvalidOperationException(
                "YouTube:ClientSecret is required. See the setup guide.");
        }
    }

    private UserCredential BuildCredentialFromStoredToken(TokenResponse tokenResponse)
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _options.ClientId,
                ClientSecret = _options.ClientSecret
            },
            DataStore = _dataStore,
            Scopes = new[] { YouTubeService.Scope.Youtube }
        });

        return new UserCredential(flow, "user", tokenResponse);
    }

    // ─── YouTube API helpers ─────────────────────────────────────────────

    private async Task ValidateLiveStreamIdAsync(CancellationToken ct)
    {
        var request = _youTubeService!.LiveStreams.List("id,status");
        request.Id = _options.LiveStreamId;

        var response = await request.ExecuteAsync(ct).ConfigureAwait(false);

        if (response.Items is null || response.Items.Count == 0)
        {
            _logger.LogCritical(
                "YouTube LiveStreamId {LiveStreamId} not found — verify PCS Pro stream key is configured and " +
                "the stream key matches. See the setup guide (§7 Step 6)",
                _options.LiveStreamId);
            throw new YouTubeStreamException(
                $"YouTube LiveStreamId '{_options.LiveStreamId}' not found. " +
                "Ensure PCS Pro has connected at least once and the ID is correct.");
        }

        _logger.LogInformation(
            "Validated YouTube LiveStreamId {LiveStreamId}", _options.LiveStreamId);
    }

    private async Task ReconcileActiveBroadcastsAsync(CancellationToken ct)
    {
        var activeRequest = _youTubeService!.LiveBroadcasts.List("id,snippet,status");
        activeRequest.BroadcastStatus =
            LiveBroadcastsResource.ListRequest.BroadcastStatusEnum.Active;
        activeRequest.Mine = true;

        var activeResponse = await activeRequest.ExecuteAsync(ct).ConfigureAwait(false);

        if (activeResponse.Items is { Count: > 0 })
        {
            if (activeResponse.Items.Count > 1)
            {
                _logger.LogWarning(
                    "Multiple active broadcasts found ({Count}) — using most recent",
                    activeResponse.Items.Count);
            }

            var broadcast = activeResponse.Items
                .OrderByDescending(b => b.Snippet.ActualStartTimeDateTimeOffset)
                .First();

            SetBroadcast(new LiveBroadcastInfo(
                broadcast.Id,
                broadcast.Snippet.Title,
                $"https://youtube.com/watch?v={broadcast.Id}"));

            StreamStateSnapshot? pendingEvent = null;
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                pendingEvent = SetStatus(LiveStreamStatus.Live);
            }
            finally
            {
                _gate.Release();
            }

            FireStatusChanged(pendingEvent);

            _logger.LogWarning(
                "Restored Live state from active broadcast {BroadcastId}: {Title}",
                broadcast.Id,
                broadcast.Snippet.Title);
            return;
        }

        var readyRequest = _youTubeService.LiveBroadcasts.List("id,snippet");
        readyRequest.BroadcastStatus =
            LiveBroadcastsResource.ListRequest.BroadcastStatusEnum.Upcoming;
        readyRequest.Mine = true;

        var readyResponse = await readyRequest.ExecuteAsync(ct).ConfigureAwait(false);

        if (readyResponse.Items is { Count: > 0 })
        {
            foreach (var orphan in readyResponse.Items)
            {
                _logger.LogWarning(
                    "Orphaned broadcast found: {BroadcastId} — {Title}. " +
                    "Delete via YouTube Studio if no longer needed",
                    orphan.Id,
                    orphan.Snippet.Title);
            }
        }

        _logger.LogInformation("No active broadcasts found — starting in Idle state");
    }

    private async Task<LiveBroadcast> CreateBroadcastAsync(string title, CancellationToken ct)
    {
        var broadcast = new LiveBroadcast
        {
            Snippet = new LiveBroadcastSnippet
            {
                Title = title,
                ScheduledStartTimeDateTimeOffset = DateTimeOffset.UtcNow
            },
            Status = new LiveBroadcastStatus
            {
                PrivacyStatus = _options.BroadcastPrivacy
            },
            ContentDetails = new LiveBroadcastContentDetails
            {
                EnableAutoStart = false,
                EnableAutoStop = false
            }
        };

        var request = _youTubeService!.LiveBroadcasts.Insert(broadcast, "snippet,status,contentDetails");
        var created = await request.ExecuteAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Created broadcast {BroadcastId}: {Title}", created.Id, title);

        return created;
    }

    private async Task BindBroadcastToStreamAsync(string broadcastId, CancellationToken ct)
    {
        var request = _youTubeService!.LiveBroadcasts.Bind(broadcastId, "id,contentDetails");
        request.StreamId = _options.LiveStreamId;

        await request.ExecuteAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Bound broadcast {BroadcastId} to stream {LiveStreamId}",
            broadcastId, _options.LiveStreamId);
    }

    private async Task PollStreamReadyAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(_options.StreamReadyTimeoutSeconds);
        var pollInterval = TimeSpan.FromSeconds(_options.StreamPollIntervalSeconds);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var request = _youTubeService!.LiveStreams.List("id,status");
            request.Id = _options.LiveStreamId;

            var response = await request.ExecuteAsync(ct).ConfigureAwait(false);

            if (response.Items is { Count: > 0 })
            {
                var streamStatus = response.Items[0].Status?.StreamStatus;
                if (string.Equals(streamStatus, "active", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "Stream {LiveStreamId} is active — ready to go live",
                        _options.LiveStreamId);
                    return;
                }

                _logger.LogDebug(
                    "Stream {LiveStreamId} status: {StreamStatus} — waiting",
                    _options.LiveStreamId, streamStatus);
            }

            await Task.Delay(pollInterval, ct).ConfigureAwait(false);
        }

        throw new YouTubeStreamException(
            $"PCS Pro is not streaming. Waited {_options.StreamReadyTimeoutSeconds}s for " +
            $"stream '{_options.LiveStreamId}' to become active. " +
            "Check that PCS Pro is running and streaming to YouTube.");
    }

    private async Task TransitionToLiveAsync(string broadcastId, CancellationToken ct)
    {
        var request = _youTubeService!.LiveBroadcasts.Transition(
            LiveBroadcastsResource.TransitionRequest.BroadcastStatusEnum.Live,
            broadcastId,
            "status");

        await request.ExecuteAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Broadcast {BroadcastId} transitioned to Live", broadcastId);
    }

    /// <summary>
    /// Best-effort transition of an active broadcast to complete status.
    /// Logs a warning on failure — startup reconciliation catches orphaned broadcasts.
    /// </summary>
    private async Task TryCompleteBroadcastAsync(CancellationToken ct)
    {
        if (_youTubeService is null)
        {
            return;
        }

        string? id;
        lock (_stateLock)
        {
            id = _currentBroadcast?.BroadcastId;
        }

        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        try
        {
            var request = _youTubeService.LiveBroadcasts.Transition(
                LiveBroadcastsResource.TransitionRequest.BroadcastStatusEnum.Complete,
                id,
                "status");
            await request.ExecuteAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Failed to transition broadcast {BroadcastId} to complete", id);
        }
    }

    private async Task TryDeleteBroadcastAsync(string? broadcastId)
    {
        if (string.IsNullOrEmpty(broadcastId) || _youTubeService is null)
        {
            return;
        }

        try
        {
            var request = _youTubeService.LiveBroadcasts.Delete(broadcastId);
            await request.ExecuteAsync().ConfigureAwait(false);
            _logger.LogInformation("Deleted broadcast {BroadcastId}", broadcastId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Best-effort delete of broadcast {BroadcastId} failed", broadcastId);
        }
    }
}