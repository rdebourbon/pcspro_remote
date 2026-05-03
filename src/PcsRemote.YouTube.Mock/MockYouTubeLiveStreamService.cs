using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PcsRemote.Core;

namespace PcsRemote.YouTube.Mock;

/// <summary>
/// Mock implementation of <see cref="IYouTubeLiveStreamService"/> for development
/// and testing without YouTube credentials.
/// </summary>
public sealed class MockYouTubeLiveStreamService : IYouTubeLiveStreamService
{
    private const string SimulatedFailureMessage = "Simulated start failure";

    private readonly MockYouTubeOptions _options;
    private readonly ILogger<MockYouTubeLiveStreamService> _logger;
    private readonly BroadcastTitleRenderer _titleRenderer;
    private readonly IPcsProAutomationService _automationService;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private LiveStreamStatus _status = LiveStreamStatus.Idle;
    private LiveBroadcastInfo? _currentBroadcast;
    private CancellationTokenSource? _operationCts;

    public MockYouTubeLiveStreamService(
        IOptions<MockYouTubeOptions> options,
        ILogger<MockYouTubeLiveStreamService> logger,
        BroadcastTitleRenderer titleRenderer,
        IPcsProAutomationService automationService)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(titleRenderer);
        ArgumentNullException.ThrowIfNull(automationService);

        _options = options.Value;
        _logger = logger;
        _titleRenderer = titleRenderer;
        _automationService = automationService;
    }

    /// <inheritdoc/>
    public LiveStreamStatus CurrentStatus => _status;

    /// <inheritdoc/>
    public LiveBroadcastInfo? CurrentBroadcast => _currentBroadcast;

    /// <inheritdoc/>
    public YouTubeAvailability Availability => YouTubeAvailability.Ready;

    /// <inheritdoc/>
    public event EventHandler<StreamStateSnapshot>? StatusChanged;

    /// <inheritdoc/>
    public event EventHandler<YouTubeAuthStatusSnapshot>? AuthStatusChanged;

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_options.SimulateActiveOnStartup)
            {
                _logger.LogInformation("Simulating active broadcast found on startup");
                _currentBroadcast = new LiveBroadcastInfo(
                    $"mock-reconciled-{Guid.NewGuid():N}",
                    "Reconciled broadcast",
                    "https://youtube.com/watch?v=mock-reconciled");
                SetStatus(LiveStreamStatus.Live);
            }
            else
            {
                _logger.LogInformation("MockYouTubeLiveStreamService initialized (no active broadcast)");
            }
        }
        finally
        {
            _gate.Release();
        }

        AuthStatusChanged?.Invoke(this,
            new YouTubeAuthStatusSnapshot(YouTubeAvailability.Ready, null));
    }

    /// <inheritdoc/>
    public async Task StartStreamAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        var gateHeld = true;
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

            var title = _titleRenderer.Render(null, match);
            _currentBroadcast = new LiveBroadcastInfo(
                $"mock-broadcast-{Guid.NewGuid():N}",
                title,
                "https://youtube.com/watch?v=mock123");

            SetStatus(LiveStreamStatus.Starting);

            _operationCts?.Dispose();
            _operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var linkedToken = _operationCts.Token;

            // Release gate during delay so StopStreamAsync can interrupt
            _gate.Release();
            gateHeld = false;

            try
            {
                await Task.Delay(_options.StartDelayMs, linkedToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Cancelled by StopStreamAsync — it handles the transition
                return;
            }
            catch (OperationCanceledException)
            {
                // Cancelled by caller — reset to Idle
                await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                gateHeld = true;
                _currentBroadcast = null;
                SetStatus(LiveStreamStatus.Idle);
                _logger.LogInformation("StartStreamAsync cancelled during Starting — reset to Idle");
                return;
            }

            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            gateHeld = true;

            // Check if StopStreamAsync already transitioned us
            if (_status != LiveStreamStatus.Starting)
            {
                return;
            }

            if (_options.SimulateStartFailure)
            {
                _currentBroadcast = null;
                SetStatus(LiveStreamStatus.Error, SimulatedFailureMessage);
                _logger.LogWarning("Simulated start failure — transitioned to Error");
            }
            else
            {
                try
                {
                    await _automationService.StartStreamingAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _currentBroadcast = null;
                    SetStatus(LiveStreamStatus.Error, ex.Message);
                    _logger.LogWarning(ex,
                        "StartStreamingAsync failed — transitioned to Error");
                    return;
                }

                SetStatus(LiveStreamStatus.Live);
                _logger.LogInformation("Stream is now Live: {Title}", _currentBroadcast.Title);
            }
        }
        finally
        {
            if (gateHeld)
            {
                _gate.Release();
            }
        }
    }

    /// <inheritdoc/>
    public async Task StopStreamAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_status is not (LiveStreamStatus.Live or LiveStreamStatus.Starting))
            {
                _logger.LogWarning(
                    "StopStreamAsync called in {Status} state — no-op",
                    _status);
                return;
            }

            // Cancel any in-flight StartStreamAsync
            _operationCts?.Cancel();

            SetStatus(LiveStreamStatus.Stopping);
        }
        finally
        {
            _gate.Release();
        }

        // Use CancellationToken.None: once Stopping begins, always complete to Idle
        await Task.Delay(_options.StopDelayMs, CancellationToken.None).ConfigureAwait(false);

        try
        {
            await _automationService.StopStreamingAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "StopStreamingAsync failed during mock stop — continuing");
        }

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _currentBroadcast = null;
            SetStatus(LiveStreamStatus.Idle);
            _logger.LogInformation("Stream stopped — returned to Idle");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_status != LiveStreamStatus.Error)
            {
                _logger.LogWarning(
                    "ResetAsync called in {Status} state — no-op",
                    _status);
                return;
            }

            _currentBroadcast = null;
            SetStatus(LiveStreamStatus.Idle);
            _logger.LogInformation("Error dismissed — returned to Idle");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public Task<bool> RunOAuthSetupAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("MockYouTubeLiveStreamService: OAuth setup simulated");
        AuthStatusChanged?.Invoke(this,
            new YouTubeAuthStatusSnapshot(YouTubeAvailability.Ready, null));
        return Task.FromResult(true);
    }

    private void SetStatus(LiveStreamStatus newStatus, string? errorMessage = null)
    {
        _status = newStatus;
        var snapshot = new StreamStateSnapshot(newStatus, _currentBroadcast, errorMessage);
        StatusChanged?.Invoke(this, snapshot);
    }
}
