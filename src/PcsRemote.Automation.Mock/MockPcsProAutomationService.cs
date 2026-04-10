using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PcsRemote.Core;

namespace PcsRemote.Automation.Mock;

public class MockPcsProAutomationService : IPcsProAutomationService
{
    private readonly MockPcsProOptions _options;
    private readonly ILogger<MockPcsProAutomationService> _logger;
    private PcsProState _currentState = PcsProState.NotRunning;

    public MockPcsProAutomationService(
        IOptions<MockPcsProOptions> options,
        ILogger<MockPcsProAutomationService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    public PcsProState CurrentState => _currentState;

    public event EventHandler<PcsProState>? StateChanged;

    protected virtual void OnStateChanged(PcsProState state) =>
        StateChanged?.Invoke(this, state);

    public Task LaunchAndLoginAsync(CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<MatchInfo>> GetTodaysMatchesAsync(CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task LoadMatchAsync(MatchInfo match, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<MatchTeams> GetTeamNamesAsync(CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task RefreshScoreboardAsync(CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<byte[]> CaptureScoreboardImageAsync(CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task StopAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
}
