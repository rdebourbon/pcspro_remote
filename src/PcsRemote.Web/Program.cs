using Serilog;
using PcsRemote.Core;
using PcsRemote.Web;

// Stage 1: transient bootstrap logger — captures host construction errors before full config is ready.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Stage 2: replace bootstrap logger with fully-configured logger (console + rolling file + debug panel).
    builder.Host.UseSerilog((_, services, loggerConfig) =>
        loggerConfig
            .WriteTo.Console()
            .WriteTo.File(
                path: "logs/pcs-remote-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .WriteTo.Sink(
                new AutomationLogSerilogSink(
                    services.GetRequiredService<IAutomationLogService>()),
                restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning));

    builder.AddPcsRemoteServices();

    var app = builder.Build();

    Log.Information("PCS Remote starting...");

    app.UsePcsRemoteMiddleware();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "PCS Remote terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Expose the implicit Program class for WebApplicationFactory in integration tests.
public partial class Program { }
