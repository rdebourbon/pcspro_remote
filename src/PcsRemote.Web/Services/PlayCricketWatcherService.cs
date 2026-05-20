using PcsRemote.Core;

namespace PcsRemote.Web.Services;

/// <summary>
/// Thread-safe singleton implementation of <see cref="IPlayCricketWatcherService"/>.
/// All state mutations are guarded by <see cref="_stateLock"/>; event handlers are
/// captured under the lock and invoked after releasing to prevent deadlocks.
/// </summary>
public sealed class PlayCricketWatcherService : IPlayCricketWatcherService, IDisposable
{
    private readonly object _stateLock = new();
    private readonly IPcsProAutomationService _automationService;

    private bool _isEnabled;
    private int? _currentFixtureId;
    private TimeSpan? _countdownRemaining;
    private bool _t60Eligible;
    private readonly HashSet<string> _autoLoadSuppressions = new();
    private readonly HashSet<int> _dismissedFixtures = new();

    public event EventHandler<AutoWatchEnabledChangedSnapshot>? AutoWatchEnabledChanged;
    public event EventHandler<CountdownStartedSnapshot>? CountdownStarted;
    public event EventHandler? AutoCloseT60Warning;
    public event EventHandler? CountdownExpired;
    public event EventHandler? CountdownCancelled;
    public event EventHandler<FixtureIdChangedSnapshot>? FixtureIdChanged;
    public event EventHandler<AutoCloseFiredSnapshot>? AutoCloseFired;

    public PlayCricketWatcherService(IPcsProAutomationService automationService)
    {
        _automationService = automationService;
        _automationService.StateChanged += OnStateChanged;
    }

    /// <inheritdoc/>
    public bool IsEnabled
    {
        get { lock (_stateLock) return _isEnabled; }
    }

    /// <inheritdoc/>
    public int? CurrentFixtureId
    {
        get { lock (_stateLock) return _currentFixtureId; }
    }

    /// <inheritdoc/>
    public TimeSpan? CountdownRemaining
    {
        get { lock (_stateLock) return _countdownRemaining; }
    }

    private void OnStateChanged(object? sender, PcsProState newState)
    {
        if (newState != PcsProState.MatchLoaded)
            SetCurrentFixtureId(null);
    }

    /// <inheritdoc/>
    public void Enable()
    {
        EventHandler<AutoWatchEnabledChangedSnapshot>? handler;
        lock (_stateLock)
        {
            if (_isEnabled) return;
            _isEnabled = true;
            _dismissedFixtures.Clear();
            handler = AutoWatchEnabledChanged;
        }
        handler?.Invoke(this, new AutoWatchEnabledChangedSnapshot(true));
    }

    /// <inheritdoc/>
    public void Disable()
    {
        bool countdownWasActive;
        EventHandler? cancelledHandler;
        EventHandler<AutoWatchEnabledChangedSnapshot>? enabledChangedHandler;
        lock (_stateLock)
        {
            if (!_isEnabled) return;
            (countdownWasActive, cancelledHandler) = CancelCountdownCore();
            _isEnabled = false;
            enabledChangedHandler = AutoWatchEnabledChanged;
        }
        if (countdownWasActive)
            cancelledHandler?.Invoke(this, EventArgs.Empty);
        enabledChangedHandler?.Invoke(this, new AutoWatchEnabledChangedSnapshot(false));
    }

    /// <inheritdoc/>
    public void SetCurrentFixtureId(int? fixtureId)
    {
        EventHandler<FixtureIdChangedSnapshot>? handler;
        lock (_stateLock)
        {
            if (_currentFixtureId == fixtureId) return;
            _currentFixtureId = fixtureId;
            handler = FixtureIdChanged;
        }
        handler?.Invoke(this, new FixtureIdChangedSnapshot(fixtureId));
    }

    /// <inheritdoc/>
    public void StartCountdown(TimeSpan duration)
    {
        EventHandler<CountdownStartedSnapshot>? handler;
        CountdownStartedSnapshot snapshot;
        lock (_stateLock)
        {
            if (_countdownRemaining != null) return;
            _t60Eligible = duration > TimeSpan.FromSeconds(60);
            _countdownRemaining = duration;
            snapshot = new CountdownStartedSnapshot(duration, _currentFixtureId);
            handler = CountdownStarted;
        }
        handler?.Invoke(this, snapshot);
    }

    /// <inheritdoc/>
    public bool TickCountdown()
    {
        EventHandler? t60Handler = null;
        EventHandler? expiredHandler = null;
        lock (_stateLock)
        {
            if (_countdownRemaining == null) return false;

            var previous = _countdownRemaining.Value;
            _countdownRemaining = previous - TimeSpan.FromSeconds(1);

            if (_t60Eligible
                && previous > TimeSpan.FromSeconds(60)
                && _countdownRemaining <= TimeSpan.FromSeconds(60))
            {
                t60Handler = AutoCloseT60Warning;
            }

            if (_countdownRemaining <= TimeSpan.Zero)
            {
                _countdownRemaining = null;
                _t60Eligible = false;
                expiredHandler = CountdownExpired;
            }
        }
        t60Handler?.Invoke(this, EventArgs.Empty);
        expiredHandler?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <inheritdoc/>
    public void CancelCountdown()
    {
        bool wasActive;
        EventHandler? handler;
        lock (_stateLock)
        {
            (wasActive, handler) = CancelCountdownCore();
        }
        if (wasActive)
            handler?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Internal cancellation logic. Must be called under <see cref="_stateLock"/>.
    /// Returns whether a countdown was active and the captured <see cref="CountdownCancelled"/> handler.
    /// If no countdown was active, takes no action and returns (false, null) — fixture dismissal
    /// does not occur when there was no countdown.
    /// </summary>
    private (bool WasActive, EventHandler? Handler) CancelCountdownCore()
    {
        if (_countdownRemaining == null) return (false, null);
        _countdownRemaining = null;
        _t60Eligible = false;
        if (_currentFixtureId.HasValue)
            _dismissedFixtures.Add(_currentFixtureId.Value);
        return (true, CountdownCancelled);
    }

    /// <inheritdoc/>
    public void RecordAutoLoadSuppression(string matchId)
    {
        lock (_stateLock) _autoLoadSuppressions.Add(matchId);
    }

    /// <inheritdoc/>
    public bool IsAutoLoadSuppressed(string matchId)
    {
        lock (_stateLock) return _autoLoadSuppressions.Contains(matchId);
    }

    /// <inheritdoc/>
    public void ClearAutoLoadSuppressions()
    {
        lock (_stateLock) _autoLoadSuppressions.Clear();
    }

    /// <inheritdoc/>
    public void DismissFixture(int fixtureId)
    {
        lock (_stateLock) _dismissedFixtures.Add(fixtureId);
    }

    /// <inheritdoc/>
    public bool IsFixtureDismissed(int fixtureId)
    {
        lock (_stateLock) return _dismissedFixtures.Contains(fixtureId);
    }

    /// <inheritdoc/>
    public void RaiseAutoCloseFired(AutoCloseFiredSnapshot snapshot)
    {
        EventHandler<AutoCloseFiredSnapshot>? handler;
        lock (_stateLock) handler = AutoCloseFired;
        handler?.Invoke(this, snapshot);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _automationService.StateChanged -= OnStateChanged;
    }
}
