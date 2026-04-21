using Google.Apis.Util.Store;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Options;
using PcsRemote.Automation;
using PcsRemote.Automation.Mock;
using PcsRemote.Core;
using PcsRemote.Web.Hubs;
using PcsRemote.Web.Services;
using PcsRemote.YouTube;
using PcsRemote.YouTube.Mock;
using Radzen;

namespace PcsRemote.Web;

/// <summary>
/// Shared registration helpers consumed by both the Web standalone entry point and
/// the TrayHost combined entry point, eliminating service/middleware duplication.
/// </summary>
public static class WebApplicationBuilderExtensions
{
    /// <summary>
    /// Registers all PCS Remote services onto the builder, including
    /// <see cref="AutoLaunchService"/> so both entry points auto-initiate
    /// the automation service on startup.
    /// </summary>
    public static WebApplicationBuilder AddPcsRemoteServices(this WebApplicationBuilder builder)
    {
        if (builder.Configuration.GetValue<bool>("PcsPro:UseMock"))
            builder.Services.AddPcsProAutomationService(builder.Configuration);
        else
            builder.Services.AddPcsProAutomation(builder.Configuration);
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IConnectionTracker, ConnectionTracker>();
        builder.Services.AddSingleton<IManualModeService, ManualModeService>();
        builder.Services.AddSingleton<IOperationCoordinatorService, OperationCoordinatorService>();
        builder.Services.AddSingleton<IAutomationLogService, AutomationLogService>();
        builder.Services.Configure<DebugSectionOptions>(
            builder.Configuration.GetSection("DebugSection"));
        builder.Services.AddServerSideBlazor();
        builder.Services.AddScoped<CircuitHandler, PcsProCircuitHandler>();
        builder.Services.AddSingleton<IScoreboardService, ScoreboardService>();
        builder.Services.AddHostedService<PcsProStateBroadcaster>();
        builder.Services.AddHostedService<ManualModeBroadcaster>();
        builder.Services.AddHostedService<OperationInProgressBroadcaster>();
        builder.Services.AddHostedService<AutomationLogBroadcaster>();
        builder.Services.AddHostedService<ScoreboardPollingService>();
        builder.Services.AddHostedService<AutoLaunchService>();
        // AddApplicationPart ensures _Host.cshtml and Blazor components in PcsRemote.Web
        // are discoverable when a different assembly (e.g. TrayHost) is the entry point.
        builder.Services.AddRazorPages()
            .AddApplicationPart(typeof(WebApplicationBuilderExtensions).Assembly);
        builder.Services.AddRadzenComponents();
        builder.Services.AddScoped<IConfirmDialogService, RadzenConfirmDialogService>();
        builder.Services.AddSingleton<BroadcastTitleRenderer>();
        if (builder.Configuration.GetValue<bool>("YouTube:UseMock"))
        {
            builder.Services.Configure<MockYouTubeOptions>(
                builder.Configuration.GetSection("YouTube:Mock"));
            builder.Services.AddSingleton<IYouTubeLiveStreamService, MockYouTubeLiveStreamService>();
        }
        else
        {
            builder.Services.Configure<YouTubeOptions>(
                builder.Configuration.GetSection("YouTube"));
            builder.Services.AddSingleton<IDataStore>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<YouTubeOptions>>().Value;
                var logger = sp.GetRequiredService<ILogger<DpapiFileDataStore>>();
                return new DpapiFileDataStore(opts.GetEffectiveTokenStorePath(), logger);
            });
            builder.Services.AddSingleton<IYouTubeLiveStreamService, YouTubeLiveStreamService>();
            builder.Services.AddHostedService<YouTubeInitializerHostedService>();
        }
        return builder;
    }

    /// <summary>
    /// Applies the PCS Remote middleware pipeline and endpoint routing.
    /// </summary>
    public static WebApplication UsePcsRemoteMiddleware(this WebApplication app)
    {
        app.UseStaticFiles();
        app.UseRouting();
        app.MapBlazorHub();
        app.MapHub<PcsProHub>("/hubs/pcspro");
        app.MapFallbackToPage("/_Host");
        return app;
    }
}
