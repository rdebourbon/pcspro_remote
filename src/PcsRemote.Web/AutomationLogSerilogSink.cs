using PcsRemote.Core;
using Serilog.Core;
using Serilog.Events;

namespace PcsRemote.Web;

/// <summary>
/// Serilog sink that routes Warning-and-above log events into
/// <see cref="IAutomationLogService"/> for display in the debug panel.
/// </summary>
public sealed class AutomationLogSerilogSink : ILogEventSink
{
    private readonly IAutomationLogService _logService;

    public AutomationLogSerilogSink(IAutomationLogService logService)
    {
        _logService = logService;
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Warning)
        {
            return;
        }

        try
        {
            var message = logEvent.RenderMessage();

            if (logEvent.Exception is not null)
            {
                message += $" [{logEvent.Exception.GetType().Name}: {logEvent.Exception.Message}]";
            }

            var outcome = logEvent.Level switch
            {
                LogEventLevel.Warning => AutomationLogOutcome.Warning,
                _ => AutomationLogOutcome.Failure,
            };

            _logService.AddEntry(message, outcome);
        }
        catch
        {
            // Sink must never throw — swallow to avoid disrupting the logging pipeline.
        }
    }
}
