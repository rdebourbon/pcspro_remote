using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PcsRemote.Core;

namespace PcsRemote.Automation.Mock;

public class MockPcsProAutomationService : IPcsProAutomationService
{
    private readonly MockPcsProOptions _options;
    private readonly ILogger<MockPcsProAutomationService> _logger;
    private readonly IAutomationLogService _logService;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Random _rng;
    private PcsProState _currentState = PcsProState.NotRunning;
    private MatchInfo? _loadedMatch;
    private byte[]? _lastImageBytes;
    private int _imageGenCounter;
    private bool _isStreaming;

    public MockPcsProAutomationService(
        IOptions<MockPcsProOptions> options,
        ILogger<MockPcsProAutomationService> logger,
        IAutomationLogService logService)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(logService);
        _options = options.Value;
        _logger = logger;
        _logService = logService;
        _rng = _options.RngSeed.HasValue ? new Random(_options.RngSeed.Value) : Random.Shared;
    }

    public PcsProState CurrentState => _currentState;

    public string? LastErrorReason { get; private set; }

    public MatchInfo? LoadedMatch => _loadedMatch;

    /// <summary>
    /// Gets whether PCS Pro's RTMP streaming is currently active.
    /// This state is tracked by the mock only — it is not part of
    /// <see cref="IPcsProAutomationService"/>.
    /// </summary>
    public bool IsStreaming => _isStreaming;

    public event EventHandler<PcsProState>? StateChanged;

    /// <inheritdoc/>
    public event EventHandler<HealthAlertEventArgs>? HealthAlert;

    protected virtual void OnStateChanged(PcsProState state) =>
        StateChanged?.Invoke(this, state);

    protected virtual void OnHealthAlert(HealthAlertEventArgs args) =>
        HealthAlert?.Invoke(this, args);

    public async Task LaunchAndLoginAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("LaunchAndLoginAsync starting");
        _logService.AddEntry("Launching PCS Pro\u2026", AutomationLogOutcome.Info);

        if (!_semaphore.Wait(0))
            throw new InvalidOperationException("A lifecycle operation is already in progress.");

        try
        {
            if (_currentState != PcsProState.NotRunning)
                throw new InvalidOperationException(
                    $"LaunchAndLoginAsync requires NotRunning state; current state is {_currentState}.");

            await Task.Delay(_options.LaunchDelay, ct);
            if (ShouldInjectErrorAt(MockForcedErrorMode.Probabilistic))
            {
                TransitionToError("Failed to start PCS Pro process");
                return;
            }
            Transition(PcsProState.Launching);
            _logger.LogDebug("Reached {State}", PcsProState.Launching);

            await Task.Delay(_options.LoginDetectedDelay, ct);
            if (ShouldInjectErrorAt(MockForcedErrorMode.LaunchingToLoginScreen))
            {
                TransitionToError("Timed out waiting for login screen to appear after launch");
                return;
            }
            Transition(PcsProState.LoginScreen);
            _logger.LogDebug("Reached {State}", PcsProState.LoginScreen);

            _logService.AddEntry("Entering credentials\u2026", AutomationLogOutcome.Info);
            await Task.Delay(_options.CredentialsEnteredDelay, ct);
            if (ShouldInjectErrorAt(MockForcedErrorMode.LoginScreenToMatchSelection))
            {
                TransitionToError("Timed out waiting for match selection screen after login");
                return;
            }
            if (ShouldInjectErrorAt(MockForcedErrorMode.UnexpectedDialog))
            {
                TransitionToError("Unexpected dialog interrupted automation after login");
                return;
            }

            Transition(PcsProState.MatchSelection);
            _logService.AddEntry("Launch and login complete", AutomationLogOutcome.Success);
            _logger.LogInformation("LaunchAndLoginAsync complete — reached {State}", PcsProState.MatchSelection);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task LoadMatchAsync(MatchInfo match, CancellationToken ct = default)
    {
        _logger.LogInformation("LoadMatchAsync starting for match {MatchId}", match?.MatchId);
        _logService.AddEntry("Loading match\u2026", AutomationLogOutcome.Info);

        if (!_semaphore.Wait(0))
            throw new InvalidOperationException("A lifecycle operation is already in progress.");

        try
        {
            if (_currentState != PcsProState.MatchSelection && _currentState != PcsProState.MatchLoaded)
                throw new InvalidOperationException(
                    $"LoadMatchAsync requires MatchSelection or MatchLoaded state; current state is {_currentState}.");

            if (_currentState == PcsProState.MatchLoaded)
            {
                await Task.Delay(_options.ChangeMatchDelay, ct);
                if (ShouldInjectErrorAt(MockForcedErrorMode.Probabilistic))
                {
                    TransitionToError("Failed to return to match selection when changing match");
                    return;
                }
                Transition(PcsProState.MatchSelection);
                _logger.LogDebug("ChangeMatch — reached {State}", PcsProState.MatchSelection);
            }

            await Task.Delay(_options.SearchTriggeredDelay, ct);
            if (ShouldInjectErrorAt(MockForcedErrorMode.Probabilistic))
            {
                TransitionToError("Failed to trigger match search");
                return;
            }
            Transition(PcsProState.MatchSelectionSearching);
            _logger.LogDebug("Reached {State}", PcsProState.MatchSelectionSearching);

            await Task.Delay(_options.SpinnerGoneDelay, ct);
            if (ShouldInjectErrorAt(MockForcedErrorMode.MatchSelectionSearchingToReady))
            {
                TransitionToError("Timed out waiting for match search results");
                return;
            }
            Transition(PcsProState.MatchSelectionReady);
            _logger.LogDebug("Reached {State}", PcsProState.MatchSelectionReady);

            await Task.Delay(_options.MatchOpenedDelay, ct);
            if (ShouldInjectErrorAt(MockForcedErrorMode.MatchSelectionToLoaded))
            {
                TransitionToError("Timed out opening selected match");
                return;
            }
            // match is non-nullable but the null-conditional on line 117 narrows
            // the compiler's flow analysis; the parameter contract guarantees non-null here.
            _loadedMatch = FormatMatchForTitle(match!);
            Transition(PcsProState.MatchLoaded);
            _logService.AddEntry("Match loaded", AutomationLogOutcome.Success);
            _logger.LogInformation("LoadMatchAsync complete — reached {State}", PcsProState.MatchLoaded);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("StopAsync starting from {State}", _currentState);
        _logService.AddEntry("Stopping PCS Pro\u2026", AutomationLogOutcome.Info);

        if (!_semaphore.Wait(0))
            throw new InvalidOperationException("A lifecycle operation is already in progress.");

        try
        {
            if (_currentState == PcsProState.NotRunning)
                return;

            await Task.Delay(_options.StopDelay, ct);
            LastErrorReason = null;
            Transition(PcsProState.NotRunning);
            _logService.AddEntry("PCS Pro stopped", AutomationLogOutcome.Success);
            _logger.LogInformation("StopAsync complete — reached {State}", PcsProState.NotRunning);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ChangeMatchAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("ChangeMatchAsync starting from {State}", _currentState);
        _logService.AddEntry("Changing match\u2026", AutomationLogOutcome.Info);

        if (!_semaphore.Wait(0))
            throw new InvalidOperationException("A lifecycle operation is already in progress.");

        try
        {
            if (_currentState != PcsProState.MatchLoaded)
                throw new InvalidOperationException(
                    $"ChangeMatchAsync requires MatchLoaded state; current state is {_currentState}.");

            await Task.Delay(_options.ChangeMatchDelay, ct);
            Transition(PcsProState.MatchSelection);
            _logService.AddEntry("Returned to match selection", AutomationLogOutcome.Success);
            _logger.LogInformation("ChangeMatchAsync complete — reached {State}", PcsProState.MatchSelection);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task StartStreamingAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("StartStreamingAsync starting from {State}", _currentState);
        _logService.AddEntry("Starting streaming\u2026", AutomationLogOutcome.Info);

        if (!_semaphore.Wait(0))
            throw new InvalidOperationException("A lifecycle operation is already in progress.");

        try
        {
            if (_currentState != PcsProState.MatchLoaded)
                throw new InvalidOperationException(
                    $"StartStreamingAsync requires MatchLoaded state; current state is {_currentState}.");

            if (_isStreaming)
            {
                _logger.LogDebug("StartStreamingAsync called while already streaming — no-op");
                return;
            }

            await Task.Delay(_options.StartStreamingDelay, ct);
            _isStreaming = true;
            _logService.AddEntry("Streaming started", AutomationLogOutcome.Success);
            _logger.LogInformation("StartStreamingAsync complete — streaming is now active");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task StopStreamingAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("StopStreamingAsync starting from {State}", _currentState);
        _logService.AddEntry("Stopping streaming\u2026", AutomationLogOutcome.Info);

        if (!_semaphore.Wait(0))
            throw new InvalidOperationException("A lifecycle operation is already in progress.");

        try
        {
            if (_currentState != PcsProState.MatchLoaded)
                throw new InvalidOperationException(
                    $"StopStreamingAsync requires MatchLoaded state; current state is {_currentState}.");

            if (!_isStreaming)
            {
                _logger.LogDebug("StopStreamingAsync called while not streaming — no-op");
                return;
            }

            await Task.Delay(_options.StopStreamingDelay, ct);
            _isStreaming = false;
            _logService.AddEntry("Streaming stopped", AutomationLogOutcome.Success);
            _logger.LogInformation("StopStreamingAsync complete — streaming is now inactive");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task DismissAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("DismissAsync starting from {State}", _currentState);
        _logService.AddEntry("Dismissing error\u2026", AutomationLogOutcome.Info);

        if (!_semaphore.Wait(0))
            throw new InvalidOperationException("A lifecycle operation is already in progress.");

        try
        {
            if (_currentState != PcsProState.Error)
                throw new InvalidOperationException(
                    $"DismissAsync requires Error state; current state is {_currentState}.");

            LastErrorReason = null;
            Transition(PcsProState.NotRunning);
            _logService.AddEntry("Error dismissed", AutomationLogOutcome.Success);
            _logger.LogInformation("DismissAsync complete — transitioned to {State}", PcsProState.NotRunning);
        }
        finally
        {
            _semaphore.Release();
        }

        return Task.CompletedTask;
    }

    public async Task RetryAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("RetryAsync starting from {State}", _currentState);
        _logService.AddEntry("Retrying automation\u2026", AutomationLogOutcome.Info);

        if (!_semaphore.Wait(0))
            throw new InvalidOperationException("A lifecycle operation is already in progress.");

        try
        {
            if (_currentState != PcsProState.Error)
                throw new InvalidOperationException(
                    $"RetryAsync requires Error state; current state is {_currentState}.");

            LastErrorReason = null;
            Transition(PcsProState.NotRunning);
            _logger.LogInformation("RetryAsync — transitioned to {State}, releasing semaphore before re-launch", PcsProState.NotRunning);
        }
        finally
        {
            _semaphore.Release();
        }

        // Semaphore released before calling LaunchAndLoginAsync to avoid re-entrancy deadlock.
        await LaunchAndLoginAsync(ct);
    }

    public Task<MatchTeams> UseCurrentMatchAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("UseCurrentMatchAsync starting from {State}", _currentState);
        _logService.AddEntry("Attaching to current match\u2026", AutomationLogOutcome.Info);

        if (!_semaphore.Wait(0))
            throw new InvalidOperationException("A lifecycle operation is already in progress.");

        try
        {
            if (_currentState != PcsProState.NotRunning)
                throw new InvalidOperationException(
                    $"UseCurrentMatchAsync requires NotRunning state; current state is {_currentState}.");

            // Mock: transition directly to MatchLoaded. LoadedMatch remains null (AC-8).
            Transition(PcsProState.MatchLoaded);
            _logService.AddEntry("Attached to current match", AutomationLogOutcome.Success);
            _logger.LogInformation("UseCurrentMatchAsync complete — reached {State}", PcsProState.MatchLoaded);
            return Task.FromResult(new MatchTeams(
                new TeamNameInfo("Home CC", "Home XI"),
                new TeamNameInfo("Away CC", "Away XI")));
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task<IReadOnlyList<MatchInfo>> GetTodaysMatchesAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var matches = Enumerable.Range(1, _options.FakeMatchCount)
            .Select(i => new MatchInfo(
                MatchId: $"match-{i}",
                HomeTeam: FakeTeams[(i * 2 - 2) % FakeTeams.Length],
                AwayTeam: FakeTeams[(i * 2 - 1) % FakeTeams.Length],
                MatchType: FakeMatchTypes[(i - 1) % FakeMatchTypes.Length],
                MatchDate: today))
            .ToList();
        return Task.FromResult<IReadOnlyList<MatchInfo>>(matches);
    }

    private static readonly string[] FakeTeams =
    [
        "Riverside CC", "Oakwood XI", "Hillcrest CC", "Valley Hawks",
        "Northgate CC", "Southfield XI", "Westbrook CC", "Eastside XI"
    ];

    private static readonly string[] FakeMatchTypes =
    [
        "Club T20", "20 overs", "Club Limited", "Friendly", "League"
    ];

    public Task<MatchTeams> GetTeamNamesAsync(CancellationToken ct = default)
        => Task.FromResult(new MatchTeams(
            new TeamNameInfo("Home CC", "Home XI"),
            new TeamNameInfo("Away CC", "Away XI")));

    public Task RefreshScoreboardAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<byte[]> CaptureScoreboardImageAsync(CancellationToken ct = default)
    {
        var variationTriggered = _rng.NextDouble() < _options.ImageVariationProbability;

        if (!variationTriggered && _lastImageBytes != null)
            return Task.FromResult(_lastImageBytes);

        _imageGenCounter++;
        _lastImageBytes = GenerateScoreboardJpeg(_imageGenCounter);
        return Task.FromResult(_lastImageBytes);
    }

    private static byte[] GenerateScoreboardJpeg(int counter)
    {
        using var bitmap = new Bitmap(320, 120);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(30, 60, 30 + (counter * 17 % 180)));
        using var font = new Font("Arial", 12f, FontStyle.Bold);
        graphics.DrawString(
            $"PCS Remote \u2014 Mock Scoreboard #{counter}",
            font,
            Brushes.White,
            new PointF(10f, 10f));
        using var ms = new System.IO.MemoryStream();
        bitmap.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }

    private bool ShouldInjectErrorAt(MockForcedErrorMode site)
    {
        if (_options.ForcedErrorMode != MockForcedErrorMode.None)
            return _options.ForcedErrorMode == site;

        return _rng.NextDouble() < _options.ErrorProbability;
    }

    private void TransitionToError(string reason)
    {
        LastErrorReason = reason;
        Transition(PcsProState.Error);
        _logService.AddEntry(reason, AutomationLogOutcome.Failure);
        _logger.LogWarning("Error injected during lifecycle: {Reason}", reason);
    }

    private void Transition(PcsProState newState)
    {
        if (newState != PcsProState.MatchLoaded)
        {
            _loadedMatch = null;
            _isStreaming = false;
        }

        _currentState = newState;
        OnStateChanged(newState);
    }

    private MatchInfo FormatMatchForTitle(MatchInfo match)
    {
        var clubName = _options.ClubName;
        if (string.IsNullOrEmpty(clubName))
        {
            return match;
        }

        var (first, second) = TeamNameFormatter.FormatForTitle(
            match.HomeTeam, match.AwayTeam, clubName);

        return match with { HomeTeam = first, AwayTeam = second };
    }
}

