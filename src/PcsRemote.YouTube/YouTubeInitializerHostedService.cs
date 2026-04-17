using Microsoft.Extensions.Hosting;
using PcsRemote.Core;

namespace PcsRemote.YouTube;

/// <summary>
/// Hosted service that calls <see cref="IYouTubeLiveStreamService.InitializeAsync"/>
/// during application startup, performing config validation, token check, and
/// startup reconciliation.
/// </summary>
public sealed class YouTubeInitializerHostedService : IHostedService
{
    private readonly IYouTubeLiveStreamService _streamService;

    public YouTubeInitializerHostedService(IYouTubeLiveStreamService streamService)
    {
        _streamService = streamService;
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _streamService.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
