using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PcsRemote.Core;

namespace PcsRemote.Web.Hubs;

/// <summary>
/// Bridges <see cref="IOperationCoordinatorService.OperationInProgressChanged"/> events to all
/// connected SignalR clients for the lifetime of the application.
/// </summary>
public sealed class OperationInProgressBroadcaster : IHostedService
{
    private readonly IOperationCoordinatorService _coordinatorService;
    private readonly IHubContext<PcsProHub> _hubContext;
    private readonly ILogger<OperationInProgressBroadcaster> _logger;

    public OperationInProgressBroadcaster(
        IOperationCoordinatorService coordinatorService,
        IHubContext<PcsProHub> hubContext,
        ILogger<OperationInProgressBroadcaster> logger)
    {
        _coordinatorService = coordinatorService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _coordinatorService.OperationInProgressChanged += OnOperationInProgressChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _coordinatorService.OperationInProgressChanged -= OnOperationInProgressChanged;
        return Task.CompletedTask;
    }

    // async void is required here: EventHandler<T> returns void.
    // Top-level catch prevents unobserved exceptions from crashing the process.
    private async void OnOperationInProgressChanged(object? sender, bool isInProgress)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(
                PcsProHubConstants.ReceiveOperationInProgressUpdate, isInProgress);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to broadcast operation in-progress update {IsInProgress} to hub clients",
                isInProgress);
        }
    }
}
