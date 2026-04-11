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
    // S-007 AC-7: UseMock=true → MockPcsProAutomationService resolved
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
        services.AddPcsProAutomationService(config);

        using var sp = services.BuildServiceProvider();
        var service = sp.GetRequiredService<IPcsProAutomationService>();

        service.Should().BeOfType<MockPcsProAutomationService>(
            "UseMock=true must resolve the mock implementation");
    }

    // ──────────────────────────────────────────────────────────────────────
    // S-007 AC-8: UseMock=false → NotSupportedException on any call
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AddPcsProAutomationService_UseMockFalse_ResolvesPlaceholderThatThrows()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["PcsPro:UseMock"] = "false"
        });

        var services = new ServiceCollection();
        services.AddPcsProAutomationService(config);

        using var sp = services.BuildServiceProvider();
        var service = sp.GetRequiredService<IPcsProAutomationService>();

        await service.Invoking(s => s.LaunchAndLoginAsync())
            .Should().ThrowAsync<NotSupportedException>(
                "the placeholder service must throw NotSupportedException for all operations");
    }
}
