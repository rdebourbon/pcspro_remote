using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PcsRemote.Core;

namespace PcsRemote.Automation;

/// <summary>
/// Extension methods for registering the FlaUI-based PCS Pro automation service.
/// </summary>
public static class AutomationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PcsProAutomationService"/> as the singleton implementation of
    /// <see cref="IPcsProAutomationService"/>, and wires <see cref="PcsProOptions"/> and
    /// <see cref="ScoreboardOptions"/> from configuration.
    /// </summary>
    /// <remarks>
    /// Named <c>AddPcsProAutomation</c> (not <c>AddPcsProAutomationService</c>) to avoid
    /// a CS0121 ambiguous-invocation error: <c>PcsRemote.Automation.Mock</c> exports an
    /// extension method with the same base name on <see cref="IServiceCollection"/>, and
    /// the composition root in <c>PcsRemote.Web</c> must import both namespaces.
    /// </remarks>
    public static IServiceCollection AddPcsProAutomation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PcsProOptions>(configuration.GetSection("PcsPro"));
        services.Configure<ScoreboardOptions>(configuration.GetSection("Scoreboard"));
        services.AddSingleton<IProcessManager, SystemProcessManager>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILoginAutomation, FlaUiLoginAutomation>();
        services.AddSingleton<IMatchSelectionAutomation, FlaUiMatchSelectionAutomation>();
        services.AddSingleton<ITeamNamesAutomation, FlaUiTeamNamesAutomation>();
        services.AddSingleton<IScoreboardAutomation, FlaUiScoreboardAutomation>();
        services.AddSingleton<IChangeMatchAutomation, FlaUiChangeMatchAutomation>();
        services.AddSingleton<AutomationDependencies>();
        services.AddSingleton<IPcsProAutomationService, PcsProAutomationService>();
        return services;
    }
}
