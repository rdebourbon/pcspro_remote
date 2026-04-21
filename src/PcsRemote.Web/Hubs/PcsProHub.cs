using Microsoft.AspNetCore.SignalR;
using PcsRemote.Core;

namespace PcsRemote.Web.Hubs;

/// <summary>
/// SignalR hub that delivers real-time PCS Pro state updates to all connected browsers.
/// Pushes the current state to each newly connected client (late-joiner support).
/// Connection counting is handled by <see cref="PcsProCircuitHandler"/> — browsers connect
/// via the Blazor <c>/_blazor</c> endpoint, not this hub.
/// </summary>
public sealed class PcsProHub : Hub
{
    private readonly IPcsProAutomationService _automationService;
    private readonly IManualModeService _manualModeService;
    private readonly IOperationCoordinatorService _coordinatorService;

    public PcsProHub(
        IPcsProAutomationService automationService,
        IManualModeService manualModeService,
        IOperationCoordinatorService coordinatorService)
    {
        _automationService = automationService;
        _manualModeService = manualModeService;
        _coordinatorService = coordinatorService;
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync(PcsProHubConstants.ReceiveStateUpdate, _automationService.CurrentState);
        await Clients.Caller.SendAsync(PcsProHubConstants.ReceiveManualModeUpdate, _manualModeService.IsManualModeActive);
        await Clients.Caller.SendAsync(PcsProHubConstants.ReceiveOperationInProgressUpdate, _coordinatorService.IsOperationInProgress);
        await Clients.Caller.SendAsync(PcsProHubConstants.ReceiveOperationDescriptionUpdate, _coordinatorService.CurrentOperationDescription);
        await base.OnConnectedAsync();
    }
}
