using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using PcsRemote.Core;
using PcsRemote.TrayHost;
using PcsRemote.Web;
using PcsRemote.YouTube;

// DPI awareness must be set before any window creation or UI framework initialisation.
// Without this, the garage PC's 150% DPI scaling produces incorrect scoreboard captures.
SetProcessDPIAware();

// Stage 1: transient bootstrap logger.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    // --setup-youtube: one-time OAuth consent flow, then exit
    if (args.Contains("--setup-youtube", StringComparer.OrdinalIgnoreCase))
    {
        return await RunYouTubeSetupAsync(args);
    }

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        // Pin content root to the directory containing the executable so appsettings.json
        // and static files are found regardless of the working directory the user launches from.
        ContentRootPath = AppContext.BaseDirectory
    });

    // Stage 2: replace with fully-configured logger.
    builder.Host.UseSerilog((_, _, loggerConfig) =>
        loggerConfig
            .WriteTo.Console()
            .WriteTo.File(
                // Absolute path so logs land next to the exe, not the working directory.
                path: Path.Combine(AppContext.BaseDirectory, "logs", "pcs-remote-.log"),
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

static async Task<int> RunYouTubeSetupAsync(string[] args)
{
    try
    {
        Log.Information("YouTube OAuth2 setup starting...");

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new YouTubeOptions();
        config.GetSection("YouTube").Bind(options);

        if (string.IsNullOrWhiteSpace(options.ClientId) ||
            string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            Log.Error(
                "YouTube:ClientId and YouTube:ClientSecret must be configured " +
                "before running --setup-youtube. See the setup guide (§7 Step 4)");
            return 1;
        }

        var tokenStorePath = options.GetEffectiveTokenStorePath();
        var serilogLogger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateLogger();

        using var loggerFactory = new Serilog.Extensions.Logging.SerilogLoggerFactory(serilogLogger);
        var storeLogger = loggerFactory.CreateLogger("PcsRemote.YouTube.DpapiFileDataStore");
        var typedLogger = new Microsoft.Extensions.Logging.Logger<DpapiFileDataStore>(
            loggerFactory);
        var dataStore = new DpapiFileDataStore(tokenStorePath, typedLogger);

        var credential = await Google.Apis.Auth.OAuth2.GoogleWebAuthorizationBroker.AuthorizeAsync(
            new Google.Apis.Auth.OAuth2.ClientSecrets
            {
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret
            },
            new[] { Google.Apis.YouTube.v3.YouTubeService.Scope.Youtube },
            "user",
            CancellationToken.None,
            dataStore);

        Log.Information(
            "YouTube OAuth2 setup complete. Token stored at {TokenStorePath}", tokenStorePath);
        return 0;
    }
    catch (Exception ex)
    {
        Log.Error(ex, "YouTube OAuth2 setup failed");
        return 1;
    }
    finally
    {
        Log.CloseAndFlush();
    }
}

[DllImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SetProcessDPIAware();
