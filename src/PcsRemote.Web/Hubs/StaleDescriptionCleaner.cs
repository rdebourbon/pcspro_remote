using PcsRemote.Core;

namespace PcsRemote.Web.Hubs;

/// <summary>
/// Clears stale operation descriptions on state transitions when the coordinator is idle.
/// Replaces the safety-net side-effect previously hosted in <c>OperationInProgressBroadcaster</c>.
/// </summary>
public sealed class StaleDescriptionCleaner : IHostedService
{
    private readonly IPcsProAutomationService _automationService;
    private readonly IOperationCoordinatorService _coordinatorService;

    public StaleDescriptionCleaner(
        IPcsProAutomationService automationService,
        IOperationCoordinatorService coordinatorService)
    {
        _automationService = automationService;
        _coordinatorService = coordinatorService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _automationService.StateChanged += OnStateChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _automationService.StateChanged -= OnStateChanged;
        return Task.CompletedTask;
    }

    private void OnStateChanged(object? sender, PcsProState newState)
    {
        if (!_coordinatorService.IsOperationInProgress)
        {
            _coordinatorService.ClearStaleDescription();
        }
    }
}
