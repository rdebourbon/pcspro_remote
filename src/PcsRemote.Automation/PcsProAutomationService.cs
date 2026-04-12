using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PcsRemote.Core;

namespace PcsRemote.Automation;

/// <summary>
/// FlaUI-based implementation of <see cref="IPcsProAutomationService"/>.
/// All automation methods are scaffolded and throw <see cref="NotImplementedException"/>
/// until implemented in IS-006 steps S-002 through S-007.
/// </summary>
internal sealed class PcsProAutomationService : IPcsProAutomationService
{
    private readonly PcsProOptions _options;
    private readonly ScoreboardOptions _scoreboardOptions;
    private readonly ILogger<PcsProAutomationService> _logger;

    public PcsProAutomationService(
        IOptions<PcsProOptions> options,
        IOptions<ScoreboardOptions> scoreboardOptions,
        ILogger<PcsProAutomationService> logger)
    {
        _options = options.Value;
        _scoreboardOptions = scoreboardOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public PcsProState CurrentState => PcsProState.NotRunning;

    /// <inheritdoc/>
    public string? LastErrorReason => null;

    /// <inheritdoc/>
    public event EventHandler<PcsProState> StateChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public Task LaunchAndLoginAsync(CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(LaunchAndLoginAsync)} is not yet implemented.");

    /// <inheritdoc/>
    public Task<IReadOnlyList<MatchInfo>> GetTodaysMatchesAsync(CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(GetTodaysMatchesAsync)} is not yet implemented.");

    /// <inheritdoc/>
    public Task LoadMatchAsync(MatchInfo match, CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(LoadMatchAsync)} is not yet implemented.");

    /// <inheritdoc/>
    public Task<MatchTeams> GetTeamNamesAsync(CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(GetTeamNamesAsync)} is not yet implemented.");

    /// <inheritdoc/>
    public Task RefreshScoreboardAsync(CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(RefreshScoreboardAsync)} is not yet implemented.");

    /// <inheritdoc/>
    public Task<byte[]> CaptureScoreboardImageAsync(CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(CaptureScoreboardImageAsync)} is not yet implemented.");

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(StopAsync)} is not yet implemented.");

    /// <inheritdoc/>
    public Task ChangeMatchAsync(CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(ChangeMatchAsync)} is not yet implemented.");

    /// <inheritdoc/>
    public Task RetryAsync(CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(RetryAsync)} is not yet implemented.");
}
