using Microsoft.AspNetCore.Components.Server.Circuits;
using Serilog;
using PcsRemote.Automation.Mock;
using PcsRemote.Core;
using PcsRemote.Web;
using PcsRemote.Web.Hubs;
using PcsRemote.Web.Services;
using Radzen;

// Stage 1: transient bootstrap logger — captures host construction errors before full config is ready.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Stage 2: replace bootstrap logger with fully-configured logger (console + rolling file).
    builder.Host.UseSerilog((_, _, loggerConfig) =>
        loggerConfig
            .WriteTo.Console()
            .WriteTo.File(
                path: "logs/pcs-remote-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7));

    builder.Services.AddPcsProAutomationService(builder.Configuration);
    builder.Services.AddSignalR();
    builder.Services.AddSingleton<IConnectionTracker, ConnectionTracker>();
    builder.Services.AddSingleton<IManualModeService, ManualModeService>();
    builder.Services.AddSingleton<IOperationCoordinatorService, OperationCoordinatorService>();
    builder.Services.AddServerSideBlazor();
    builder.Services.AddScoped<CircuitHandler, PcsProCircuitHandler>();
    builder.Services.AddSingleton<IScoreboardService, ScoreboardService>();
    builder.Services.AddHostedService<PcsProStateBroadcaster>();
    builder.Services.AddHostedService<ManualModeBroadcaster>();
    builder.Services.AddHostedService<OperationInProgressBroadcaster>();
    builder.Services.AddHostedService<ScoreboardPollingService>();
    builder.Services.AddHostedService<AutoLaunchService>();
    builder.Services.AddRazorPages();
    builder.Services.AddRadzenComponents();
    builder.Services.AddScoped<IConfirmDialogService, RadzenConfirmDialogService>();

    var app = builder.Build();

    Log.Information("PCS Remote starting...");

    app.UseStaticFiles();
    app.UseRouting();
    app.MapBlazorHub();
    app.MapHub<PcsProHub>("/hubs/pcspro");
    app.MapFallbackToPage("/_Host");

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
