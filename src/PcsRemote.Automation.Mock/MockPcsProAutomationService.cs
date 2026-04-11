using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PcsRemote.Core;

namespace PcsRemote.Automation.Mock;

public class MockPcsProAutomationService : IPcsProAutomationService
{
    private readonly MockPcsProOptions _options;
    private readonly ILogger<MockPcsProAutomationService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Random _rng;
    private PcsProState _currentState = PcsProState.NotRunning;

    public MockPcsProAutomationService(
        IOptions<MockPcsProOptions> options,
        ILogger<MockPcsProAutomationService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
        _rng = _options.RngSeed.HasValue ? new Random(_options.RngSeed.Value) : Random.Shared;
    }

    public PcsProState CurrentState => _currentState;

    public string? LastErrorReason { get; private set; }

    public event EventHandler<PcsProState>? StateChanged;

    protected virtual void OnStateChanged(PcsProState state) =>
        StateChanged?.Invoke(this, state);

    public async Task LaunchAndLoginAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("LaunchAndLoginAsync starting");

        if (!_semaphore.Wait(0))
            throw new InvalidOperationException("A lifecycle operation is already in progress.");

        try
        {
            if (_currentState != PcsProState.NotRunning)
                throw new InvalidOperationException(
                    $"LaunchAndLoginAsync requires NotRunning state; current state is {_currentState}.");

            await Task.Delay(_options.LaunchDelay, ct);
            if (_rng.NextDouble() < _options.ErrorProbability)
            {
                TransitionToError("Error before Launching transition");
                return;
            }
            Transition(PcsProState.Launching);
            _logger.LogDebug("Reached {State}", PcsProState.Launching);

            await Task.Delay(_options.LoginDetectedDelay, ct);
            if (_rng.NextDouble() < _options.ErrorProbability)
            {
                TransitionToError("Error before LoginScreen transition");
                return;
            }
            Transition(PcsProState.LoginScreen);
            _logger.LogDebug("Reached {State}", PcsProState.LoginScreen);

            await Task.Delay(_options.CredentialsEnteredDelay, ct);
            if (_rng.NextDouble() < _options.ErrorProbability)
            {
                TransitionToError("Error before MatchSelection transition");
                return;
            }
            Transition(PcsProState.MatchSelection);
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
                if (_rng.NextDouble() < _options.ErrorProbability)
                {
                    TransitionToError("Error before ChangeMatch MatchSelection transition");
                    return;
                }
                Transition(PcsProState.MatchSelection);
                _logger.LogDebug("ChangeMatch — reached {State}", PcsProState.MatchSelection);
            }

            await Task.Delay(_options.SearchTriggeredDelay, ct);
            if (_rng.NextDouble() < _options.ErrorProbability)
            {
                TransitionToError("Error before MatchSelectionSearching transition");
                return;
            }
            Transition(PcsProState.MatchSelectionSearching);
            _logger.LogDebug("Reached {State}", PcsProState.MatchSelectionSearching);

            await Task.Delay(_options.SpinnerGoneDelay, ct);
            if (_rng.NextDouble() < _options.ErrorProbability)
            {
                TransitionToError("Error before MatchSelectionReady transition");
                return;
            }
            Transition(PcsProState.MatchSelectionReady);
            _logger.LogDebug("Reached {State}", PcsProState.MatchSelectionReady);

            await Task.Delay(_options.MatchOpenedDelay, ct);
            if (_rng.NextDouble() < _options.ErrorProbability)
            {
                TransitionToError("Error before MatchLoaded transition");
                return;
            }
            Transition(PcsProState.MatchLoaded);
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

        if (!_semaphore.Wait(0))
            throw new InvalidOperationException("A lifecycle operation is already in progress.");

        try
        {
            if (_currentState == PcsProState.NotRunning)
                return;

            await Task.Delay(_options.StopDelay, ct);
            Transition(PcsProState.NotRunning);
            LastErrorReason = null;
            _logger.LogInformation("StopAsync complete — reached {State}", PcsProState.NotRunning);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task<IReadOnlyList<MatchInfo>> GetTodaysMatchesAsync(CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<MatchTeams> GetTeamNamesAsync(CancellationToken ct = default)
        => Task.FromResult(new MatchTeams("Home XI", "Away XI"));

    public Task RefreshScoreboardAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<byte[]> CaptureScoreboardImageAsync(CancellationToken ct = default)
        => throw new NotImplementedException();

    private void TransitionToError(string reason)
    {
        LastErrorReason = reason;
        Transition(PcsProState.Error);
        _logger.LogWarning("Error injected during lifecycle: {Reason}", reason);
    }

    private void Transition(PcsProState newState)
    {
        _currentState = newState;
        OnStateChanged(newState);
    }
}

