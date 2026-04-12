using Microsoft.AspNetCore.Components.Server.Circuits;
using PcsRemote.Core;

namespace PcsRemote.Web.Hubs;

/// <summary>
/// Blazor circuit handler that tracks connected browser sessions via <see cref="IConnectionTracker"/>.
/// Browsers connect to Blazor Server via the <c>/_blazor</c> endpoint, not the SignalR hub, so
/// connection counting must be done here rather than in <see cref="PcsProHub"/>.
/// </summary>
public sealed class PcsProCircuitHandler : CircuitHandler
{
    private readonly IConnectionTracker _connectionTracker;

    public PcsProCircuitHandler(IConnectionTracker connectionTracker)
    {
        _connectionTracker = connectionTracker;
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _connectionTracker.Increment();
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _connectionTracker.Decrement();
        return Task.CompletedTask;
    }
}
