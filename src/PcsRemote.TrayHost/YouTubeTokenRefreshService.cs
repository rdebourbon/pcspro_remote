using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PcsRemote.Core;
using PcsRemote.YouTube;

namespace PcsRemote.TrayHost;

/// <summary>
/// Background service that proactively refreshes the YouTube OAuth token on a configurable
/// interval. Only runs when <see cref="IYouTubeLiveStreamService.Availability"/> is
/// <see cref="YouTubeAvailability.Ready"/>; skips silently otherwise.
/// </summary>
internal sealed class YouTubeTokenRefreshService : BackgroundService
{
    private readonly IYouTubeLiveStreamService _youTubeService;
    private readonly YouTubeOptions _options;
    private readonly ILogger<YouTubeTokenRefreshService> _logger;
    private readonly Func<IPeriodicTimer> _timerFactory;

    /// <summary>Production constructor — timer interval sourced from <see cref="YouTubeOptions"/>.</summary>
    public YouTubeTokenRefreshService(
        IYouTubeLiveStreamService youTubeService,
        IOptions<YouTubeOptions> options,
        ILogger<YouTubeTokenRefreshService> logger)
        : this(youTubeService, options, logger, timerFactory: null)
    {
    }

    /// <summary>Test-only constructor — accepts an injectable timer factory for deterministic control.</summary>
    internal YouTubeTokenRefreshService(
        IYouTubeLiveStreamService youTubeService,
        IOptions<YouTubeOptions> options,
        ILogger<YouTubeTokenRefreshService> logger,
        Func<IPeriodicTimer>? timerFactory)
    {
        _youTubeService = youTubeService;
        _options = options.Value;
        _logger = logger;
        _timerFactory = timerFactory
            ?? (() => new RealPeriodicTimer(TimeSpan.FromHours(_options.ProactiveRefreshIntervalHours)));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var timer = _timerFactory();

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (_youTubeService.Availability != YouTubeAvailability.Ready)
            {
                continue;
            }

            try
            {
                await _youTubeService.RunProactiveRefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Proactive YouTube token refresh failed; will retry on next interval");
            }
        }
    }

    /// <summary>
    /// Exposes <see cref="ExecuteAsync"/> for unit-test access.
    /// Production code must use <see cref="IHostedService.StartAsync"/> instead.
    /// </summary>
    internal Task RunAsync(CancellationToken stoppingToken) => ExecuteAsync(stoppingToken);
}
