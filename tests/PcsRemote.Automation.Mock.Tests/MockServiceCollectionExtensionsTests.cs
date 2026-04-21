using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PcsRemote.Automation.Mock;
using PcsRemote.Core;

namespace PcsRemote.Automation.Mock.Tests;

[TestClass]
public sealed class MockServiceCollectionExtensionsTests
{
    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(IEnumerable<KeyValuePair<string, string?>> pairs)
        => new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();

    // ──────────────────────────────────────────────────────────────────────
    // UseMock=true → MockPcsProAutomationService resolved
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AddPcsProAutomationService_UseMockTrue_ResolvesMockService()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["PcsPro:UseMock"] = "true"
        });

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<MockPcsProAutomationService>>(
            NullLogger<MockPcsProAutomationService>.Instance);
        services.AddSingleton<IAutomationLogService>(new NullAutomationLogService());
        services.AddPcsProAutomationService(config);

        using var sp = services.BuildServiceProvider();
        var service = sp.GetRequiredService<IPcsProAutomationService>();

        service.Should().BeOfType<MockPcsProAutomationService>(
            "UseMock=true must resolve the mock implementation");
    }
}
