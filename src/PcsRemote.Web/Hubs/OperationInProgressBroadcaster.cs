using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PcsRemote.Core;

namespace PcsRemote.Web.Hubs;

/// <summary>
/// Bridges <see cref="IOperationCoordinatorService.OperationInProgressChanged"/> and
/// <see cref="IOperationCoordinatorService.OperationDescriptionChanged"/> events to all
/// connected SignalR clients. Also implements the safety-net clearing mechanism: on any
/// <see cref="IPcsProAutomationService.StateChanged"/> transition while the coordinator
/// is idle, calls <see cref="IOperationCoordinatorService.ClearStaleDescription"/>.
/// </summary>
public sealed class OperationInProgressBroadcaster : IHostedService
{
    private readonly IOperationCoordinatorService _coordinatorService;
    private readonly IPcsProAutomationService _automationService;
    private readonly IHubContext<PcsProHub> _hubContext;
    private readonly ILogger<OperationInProgressBroadcaster> _logger;

    public OperationInProgressBroadcaster(
        IOperationCoordinatorService coordinatorService,
        IPcsProAutomationService automationService,
        IHubContext<PcsProHub> hubContext,
        ILogger<OperationInProgressBroadcaster> logger)
    {
        _coordinatorService = coordinatorService;
        _automationService = automationService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _coordinatorService.OperationInProgressChanged += OnOperationInProgressChanged;
        _coordinatorService.OperationDescriptionChanged += OnOperationDescriptionChanged;
        _automationService.StateChanged += OnStateChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _coordinatorService.OperationInProgressChanged -= OnOperationInProgressChanged;
        _coordinatorService.OperationDescriptionChanged -= OnOperationDescriptionChanged;
        _automationService.StateChanged -= OnStateChanged;
        return Task.CompletedTask;
    }

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

    private async void OnOperationDescriptionChanged(object? sender, string? description)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(
                PcsProHubConstants.ReceiveOperationDescriptionUpdate, description);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to broadcast operation description update to hub clients");
        }
    }

    // Safety-net: clear stale description when state transitions while coordinator is idle.
    private void OnStateChanged(object? sender, PcsProState newState)
    {
        if (!_coordinatorService.IsOperationInProgress)
        {
            _coordinatorService.ClearStaleDescription();
        }
    }
}
