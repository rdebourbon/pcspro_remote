using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using PcsRemote.Core;

namespace PcsRemote.Web.Hubs;

/// <summary>
/// Bridges <see cref="IManualModeService.ManualModeChanged"/> events to all
/// connected SignalR clients for the lifetime of the application.
/// </summary>
public sealed class ManualModeBroadcaster : IHostedService
{
    private readonly IManualModeService _manualModeService;
    private readonly IHubContext<PcsProHub> _hubContext;
    private readonly ILogger<ManualModeBroadcaster> _logger;

    public ManualModeBroadcaster(
        IManualModeService manualModeService,
        IHubContext<PcsProHub> hubContext,
        ILogger<ManualModeBroadcaster> logger)
    {
        _manualModeService = manualModeService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _manualModeService.ManualModeChanged += OnManualModeChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _manualModeService.ManualModeChanged -= OnManualModeChanged;
        return Task.CompletedTask;
    }

    // async void is required here: EventHandler<T> returns void.
    // Top-level catch prevents unobserved exceptions from crashing the process.
    private async void OnManualModeChanged(object? sender, bool isActive)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(PcsProHubConstants.ReceiveManualModeUpdate, isActive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast manual mode update {IsActive} to hub clients", isActive);
        }
    }
}
