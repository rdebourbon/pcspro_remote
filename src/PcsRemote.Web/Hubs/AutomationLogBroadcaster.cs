using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PcsRemote.Core;

namespace PcsRemote.Web.Hubs;

/// <summary>
/// Bridges <see cref="IAutomationLogService.EntryAdded"/> events to all connected
/// SignalR clients via <see cref="PcsProHubConstants.ReceiveAutomationLogEntry"/>.
/// </summary>
public sealed class AutomationLogBroadcaster : IHostedService
{
    private readonly IAutomationLogService _logService;
    private readonly IHubContext<PcsProHub> _hubContext;
    private readonly ILogger<AutomationLogBroadcaster> _logger;

    public AutomationLogBroadcaster(
        IAutomationLogService logService,
        IHubContext<PcsProHub> hubContext,
        ILogger<AutomationLogBroadcaster> logger)
    {
        _logService = logService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logService.EntryAdded += OnEntryAdded;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logService.EntryAdded -= OnEntryAdded;
        return Task.CompletedTask;
    }

    private async void OnEntryAdded(object? sender, AutomationLogEntry entry)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(
                PcsProHubConstants.ReceiveAutomationLogEntry, entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to broadcast automation log entry {Action} to hub clients",
                entry.Action);
        }
    }
}
