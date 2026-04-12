using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PcsRemote.Core;

namespace PcsRemote.Automation.Mock;

public static class MockServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IPcsProAutomationService"/> using either the mock or the
    /// (not-yet-implemented) real service, based on <c>PcsPro:UseMock</c> in configuration.
    /// </summary>
    public static IServiceCollection AddPcsProAutomationService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("PcsPro:UseMock"))
        {
            services.Configure<MockPcsProOptions>(configuration.GetSection("PcsPro:Mock"));
            services.AddSingleton<IPcsProAutomationService, MockPcsProAutomationService>();
        }
        else
        {
            services.AddSingleton<IPcsProAutomationService, NotSupportedPcsProAutomationService>();
        }

        return services;
    }
}

/// <summary>
/// Placeholder service registered when <c>PcsPro:UseMock</c> is <c>false</c>.
/// Throws <see cref="NotSupportedException"/> for all operations until the real
/// FlaUI-based implementation is delivered in HLPS-006.
/// </summary>
internal sealed class NotSupportedPcsProAutomationService : IPcsProAutomationService
{
    private const string NotImplementedMessage =
        "Real PCS Pro automation service is not yet implemented.";

    public PcsProState CurrentState => PcsProState.NotRunning;

    public string? LastErrorReason => null;

    public event EventHandler<PcsProState> StateChanged
    {
        add { }
        remove { }
    }

    public Task LaunchAndLoginAsync(CancellationToken ct = default) =>
        throw new NotSupportedException(NotImplementedMessage);

    public Task<IReadOnlyList<MatchInfo>> GetTodaysMatchesAsync(CancellationToken ct = default) =>
        throw new NotSupportedException(NotImplementedMessage);

    public Task LoadMatchAsync(MatchInfo match, CancellationToken ct = default) =>
        throw new NotSupportedException(NotImplementedMessage);

    public Task<MatchTeams> GetTeamNamesAsync(CancellationToken ct = default) =>
        throw new NotSupportedException(NotImplementedMessage);

    public Task RefreshScoreboardAsync(CancellationToken ct = default) =>
        throw new NotSupportedException(NotImplementedMessage);

    public Task<byte[]> CaptureScoreboardImageAsync(CancellationToken ct = default) =>
        throw new NotSupportedException(NotImplementedMessage);

    public Task StopAsync(CancellationToken ct = default) =>
        throw new NotSupportedException(NotImplementedMessage);

    public Task ChangeMatchAsync(CancellationToken ct = default) =>
        throw new NotSupportedException(NotImplementedMessage);

    public Task RetryAsync(CancellationToken ct = default) =>
        throw new NotSupportedException(NotImplementedMessage);
}
