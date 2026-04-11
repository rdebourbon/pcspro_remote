using Microsoft.AspNetCore.SignalR;
using PcsRemote.Core;

namespace PcsRemote.Web.Hubs;

/// <summary>
/// Bridges <see cref="IPcsProAutomationService.StateChanged"/> events to all
/// connected SignalR clients for the lifetime of the application.
/// </summary>
public sealed class PcsProStateBroadcaster : IHostedService
{
    private readonly IPcsProAutomationService _automationService;
    private readonly IHubContext<PcsProHub> _hubContext;

    public PcsProStateBroadcaster(
        IPcsProAutomationService automationService,
        IHubContext<PcsProHub> hubContext)
    {
        _automationService = automationService;
        _hubContext = hubContext;
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

    // async void is required here: EventHandler<T> returns void.
    // Exceptions that escape this handler are unobserved — accepted risk AR-2 (SPEC-S-003 §5).
    private async void OnStateChanged(object? sender, PcsProState newState)
    {
        await _hubContext.Clients.All.SendAsync(PcsProHubConstants.ReceiveStateUpdate, newState);
    }
}
