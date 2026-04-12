using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PcsRemote.Automation.Mock;
using PcsRemote.Core;
using PcsRemote.Web;
using PcsRemote.Web.Hubs;
using Radzen;
using System.Net;

namespace PcsRemote.E2E.Tests;

/// <summary>
/// Manages two independent hosts for Playwright E2E testing.
/// <list type="bullet">
///   <item>
///     A WAF <c>TestServer</c> host is returned to <see cref="WebApplicationFactory{TEntryPoint}"/>
///     so the base-class cast in <c>EnsureServer()</c> succeeds.
///   </item>
///   <item>
///     A real Kestrel host is started on a random OS-assigned port.
///     Playwright browsers connect to this host. Services in this container are
///     exposed via <see cref="RealServices"/> so tests can resolve singletons
///     (e.g. <see cref="MockPcsProAutomationService"/>) that Blazor components
///     subscribe to.
///   </item>
/// </list>
/// </summary>
public sealed class PcsProWebApplicationFactory : WebApplicationFactory<Program>
{
    private IHost? _kestrelHost;
    private readonly TaskCompletionSource<string> _serverAddressTcs = new();

    /// <summary>
    /// Completes with the bound HTTP base address (e.g. <c>http://127.0.0.1:5123</c>) once
    /// the Kestrel server has started.
    /// </summary>
    public Task<string> ServerAddressTask => _serverAddressTcs.Task;

    /// <summary>
    /// The DI service provider for the real Kestrel host — the same container that
    /// Blazor circuits, SignalR hubs, and hosted services execute within.
    /// Use this (not <c>factory.Services</c>) to resolve singletons shared with the live app.
    /// </summary>
    public IServiceProvider RealServices => _kestrelHost!.Services;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Configure the WAF TestServer host with the same test settings used by the
        // Kestrel host below. Both hosts must agree on UseMock/AutoLaunch to avoid
        // the WAF host attempting real automation during startup.
        builder.UseSetting("PcsPro:UseMock", "true");
        builder.UseSetting("PcsPro:AutoLaunch", "false");
        builder.UseSetting("PcsPro:Mock:LaunchDelay", "0");
        builder.UseSetting("PcsPro:Mock:LoginDetectedDelay", "0");
        builder.UseSetting("PcsPro:Mock:CredentialsEnteredDelay", "0");
        builder.UseSetting("PcsPro:Mock:ErrorProbability", "0");

        builder.ConfigureServices(services =>
            services.Configure<CircuitOptions>(o =>
                o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromSeconds(1)));
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Build the WAF TestServer host — base.CreateHost satisfies the (TestServer) cast
        // inside WebApplicationFactory.EnsureServer().
        var testHost = base.CreateHost(builder);

        // Build the real Kestrel host that Playwright browsers connect to.
        _kestrelHost = BuildKestrelHost();

        // Register the address callback before Start() so it fires regardless of whether
        // ApplicationStarted has already cancelled (synchronous Register on a cancelled token
        // fires the callback immediately on the calling thread).
        var lifetime = _kestrelHost.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Register(() =>
        {
            var feature = _kestrelHost.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException(
                    "IServerAddressesFeature not available after ApplicationStarted. " +
                    "Kestrel may not have bound an address.");

            _serverAddressTcs.TrySetResult(feature.Addresses.First());
        });

        _kestrelHost.Start();

        return testHost;
    }

    /// <summary>
    /// Builds a standalone Kestrel host that mirrors the production <c>Program.cs</c> pipeline.
    /// <para>
    /// <strong>Sync invariant — keep in step with <c>Program.cs</c>:</strong>
    /// Any service registration or middleware added to <c>Program.cs</c> that affects Blazor
    /// circuit behaviour, SignalR hubs, or state broadcasting must also be added here.
    /// </para>
    /// <para>
    /// <strong>Deliberate omissions (do not add):</strong>
    /// <list type="bullet">
    ///   <item><c>AutoLaunchService</c> — suppressed by <c>PcsPro:AutoLaunch=false</c>; omitted to avoid an unnecessary hosted service.</item>
    ///   <item><c>UseSerilog</c> — test output captured by MSTest; Serilog file sink not needed.</item>
    /// </list>
    /// </para>
    /// </summary>
    private static IHost BuildKestrelHost()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // Set ApplicationName to the web assembly so Razor Pages and Blazor
            // components are discovered from PcsRemote.Web.dll.
            ApplicationName = typeof(Program).Assembly.GetName().Name!
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PcsPro:UseMock"] = "true",
            ["PcsPro:AutoLaunch"] = "false",
            ["PcsPro:Mock:LaunchDelay"] = "0",
            ["PcsPro:Mock:LoginDetectedDelay"] = "0",
            ["PcsPro:Mock:CredentialsEnteredDelay"] = "0",
            ["PcsPro:Mock:ErrorProbability"] = "0",
            ["PcsPro:Mock:ImageVariationProbability"] = "1",
            ["Scoreboard:CaptureIntervalSeconds"] = "1",
            // Provide the static web assets manifest so Radzen.Blazor.js and other RCL assets
            // are served correctly. StaticWebAssetsStartupFilter reads this key and adds the
            // Radzen NuGet package staticwebassets/ directory to the WebRootFileProvider,
            // enabling _content/Radzen.Blazor/Radzen.Blazor.js to be served. Without this,
            // RadzenDialog.Close's JSRuntime.InvokeAsync("Radzen.closeDialog") hangs and
            // the dialog never re-renders as hidden.
            ["StaticWebAssets"] = Path.Combine(AppContext.BaseDirectory, "PcsRemote.Web.staticwebassets.runtime.json")
        });

        builder.Services.AddPcsProAutomationService(builder.Configuration);
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IConnectionTracker, ConnectionTracker>();
        builder.Services.AddServerSideBlazor();
        builder.Services.AddScoped<CircuitHandler, PcsProCircuitHandler>();
        builder.Services.AddHostedService<PcsProStateBroadcaster>();
        // AutoLaunchService is intentionally omitted — PcsPro:AutoLaunch=false would
        // prevent it from acting, but omitting it avoids an unnecessary hosted service.
        builder.Services.AddRazorPages();
        builder.Services.AddRadzenComponents();
        builder.Services.AddSingleton<IScoreboardService, ScoreboardService>();
        builder.Services.AddHostedService<ScoreboardPollingService>();
        builder.Services.AddScoped<IConfirmDialogService, RadzenConfirmDialogService>();
        builder.Services.Configure<CircuitOptions>(o =>
            o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromSeconds(1));

        // Ensure Razor Class Library static assets (e.g. _content/Radzen.Blazor/Radzen.Blazor.js)
        // are served. StaticWebAssetsStartupFilter reads the StaticWebAssets config key and
        // updates the WebRootFileProvider, but calling UseStaticWebAssets() explicitly on the
        // web host builder guarantees the asset manifest is applied regardless of environment.
        builder.WebHost.UseStaticWebAssets();

        // Programmatic Listen() takes highest priority — overrides UseUrls and any
        // Kestrel:Endpoints from appsettings. Port 0 means OS assigns a free port.
        builder.WebHost.ConfigureKestrel(serverOptions =>
            serverOptions.Listen(IPAddress.Loopback, 0));

        var app = builder.Build();

        app.UseStaticFiles();
        app.UseRouting();
        app.MapBlazorHub();
        app.MapHub<PcsProHub>("/hubs/pcspro");
        app.MapFallbackToPage("/_Host");

        return app;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_kestrelHost is not null)
        {
            await _kestrelHost.StopAsync();
            if (_kestrelHost is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else
                _kestrelHost.Dispose();
        }

        await base.DisposeAsync();
    }
}
