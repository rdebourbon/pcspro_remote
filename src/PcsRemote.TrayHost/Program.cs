using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using PcsRemote.Core;
using PcsRemote.TrayHost;
using PcsRemote.Web;

// Stage 1: transient bootstrap logger.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Stage 2: replace with fully-configured logger.
    builder.Host.UseSerilog((_, _, loggerConfig) =>
        loggerConfig
            .WriteTo.Console()
            .WriteTo.File(
                path: "logs/pcs-remote-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7));

    builder.AddPcsRemoteServices();

    // Register the WinForms STA-thread pump as a hosted service.
    // Main thread remains MTA so app.Run() can drive ASP.NET Core's async host machinery
    // without deadlocking on Task continuations inside the host infrastructure.
    builder.Services.AddSingleton<Func<ApplicationContext>>(sp => () => new TrayApplicationContext(
        sp.GetRequiredService<IManualModeService>(),
        sp.GetRequiredService<IPcsProAutomationService>(),
        sp.GetRequiredService<IConfiguration>()));
    builder.Services.AddHostedService<WinFormsHostedService>();

    var app = builder.Build();

    Log.Information("PCS Remote (TrayHost) starting...");

    app.UsePcsRemoteMiddleware();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "PCS Remote (TrayHost) terminated unexpectedly");
    return 1; // Non-zero exit code so Task Scheduler restart triggers on crash
}
finally
{
    Log.CloseAndFlush(); // return 1 above allows this finally block to execute
}

return 0;
