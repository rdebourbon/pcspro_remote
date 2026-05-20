using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PcsRemote.Core;

namespace PcsRemote.PlayCricket.Mock;

/// <summary>
/// DI registration helpers for the mock Play-Cricket API client.
/// Must only be called when <c>PlayCricket:UseMock</c> is <c>true</c>;
/// the composition root in <c>PcsRemote.Web</c> is responsible for the branching decision.
/// </summary>
public static class MockPlayCricketServiceCollectionExtensions
{
    /// <summary>
    /// Registers the mock Play-Cricket API client implementation.
    /// </summary>
    public static IServiceCollection AddMockPlayCricketApiClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MockPlayCricketOptions>(configuration.GetSection("PlayCricket:Mock"));
        services.AddSingleton<IPlayCricketApiClient, MockPlayCricketApiClient>();
        return services;
    }
}
