using Microsoft.AspNetCore.Components.Server.Circuits;
using PcsRemote.Automation.Mock;
using PcsRemote.Core;
using PcsRemote.Web.Hubs;
using PcsRemote.Web.Services;
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
