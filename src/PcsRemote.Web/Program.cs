using Serilog;
using PcsRemote.Automation.Mock;
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
    builder.Services.AddRazorPages();
    builder.Services.AddServerSideBlazor();
    builder.Services.AddRadzenComponents();

    var app = builder.Build();

    Log.Information("PCS Remote starting...");

    app.UseStaticFiles();
    app.UseRouting();
    app.MapBlazorHub();
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
