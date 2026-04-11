using Microsoft.AspNetCore.SignalR;
using PcsRemote.Core;

namespace PcsRemote.Web.Hubs;

/// <summary>
/// SignalR hub that delivers real-time PCS Pro state updates to all connected browsers.
/// Tracks the live connection count via <see cref="IConnectionTracker"/> and pushes
/// the current state to each newly connected client (late-joiner support).
/// </summary>
public sealed class PcsProHub : Hub
{
    private readonly IConnectionTracker _connectionTracker;
    private readonly IPcsProAutomationService _automationService;

    public PcsProHub(IConnectionTracker connectionTracker, IPcsProAutomationService automationService)
    {
        _connectionTracker = connectionTracker;
        _automationService = automationService;
    }

    public override async Task OnConnectedAsync()
    {
        _connectionTracker.Increment();
        await Clients.Caller.SendAsync(PcsProHubConstants.ReceiveStateUpdate, _automationService.CurrentState);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connectionTracker.Decrement();
        await base.OnDisconnectedAsync(exception);
    }
}
