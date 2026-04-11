using PcsRemote.Core;

namespace PcsRemote.Web;

/// <summary>
/// On host startup, triggers <see cref="IPcsProAutomationService.LaunchAndLoginAsync"/>
/// when the <c>PcsPro:AutoLaunch</c> configuration key is <c>true</c> (or absent).
/// </summary>
public sealed class AutoLaunchService : BackgroundService
{
    private readonly IPcsProAutomationService _automationService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AutoLaunchService> _logger;

    public AutoLaunchService(
        IPcsProAutomationService automationService,
        IConfiguration configuration,
        ILogger<AutoLaunchService> logger)
    {
        _automationService = automationService;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var autoLaunch = _configuration.GetValue("PcsPro:AutoLaunch", defaultValue: true);
        if (!autoLaunch)
            return;

        try
        {
            await _automationService.LaunchAndLoginAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Application is shutting down — exit silently.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AutoLaunchService: LaunchAndLoginAsync failed.");
        }
    }
}
