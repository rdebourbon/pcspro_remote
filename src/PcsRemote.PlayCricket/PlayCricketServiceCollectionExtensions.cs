using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PcsRemote.Core;

namespace PcsRemote.PlayCricket;

/// <summary>
/// DI registration helpers for the real Play-Cricket API client.
/// Must only be called when <c>PlayCricket:UseMock</c> is <c>false</c>;
/// the composition root in <c>PcsRemote.Web</c> is responsible for the branching decision.
/// </summary>
public static class PlayCricketServiceCollectionExtensions
{
    /// <summary>
    /// Registers the real Play-Cricket HTTP API client and its supporting services.
    /// </summary>
    public static IServiceCollection AddPlayCricketApiClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PlayCricketOptions>(configuration.GetSection("PlayCricket"));

        services.AddHttpClient("PlayCricket", client =>
        {
            client.BaseAddress = new Uri("https://play-cricket.com/api/v2/");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddSingleton<IPlayCricketApiClient, PlayCricketApiClient>();

        return services;
    }
}
