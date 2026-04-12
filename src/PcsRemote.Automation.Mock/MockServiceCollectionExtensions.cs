using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PcsRemote.Core;

namespace PcsRemote.Automation.Mock;

public static class MockServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IPcsProAutomationService"/> using the mock implementation.
    /// This method must only be called when <c>PcsPro:UseMock</c> is <c>true</c>;
    /// the composition root in <c>PcsRemote.Web</c> is responsible for the branching decision.
    /// </summary>
    public static IServiceCollection AddPcsProAutomationService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MockPcsProOptions>(configuration.GetSection("PcsPro:Mock"));
        services.AddSingleton<IPcsProAutomationService, MockPcsProAutomationService>();
        return services;
    }
}
