using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PcsRemote.Core;
using PcsRemote.Web;
using PcsRemote.Web.Services;

namespace PcsRemote.TrayHost.Tests;

[TestClass]
public class WebApplicationExtensionsTests
{
    // TC-5: AddPcsRemoteServices registers the expected key services.
    [TestMethod]
    public void AddPcsRemoteServices_RegistersKeyServices()
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());

        builder.AddPcsRemoteServices();

        // Inspect registered service descriptors without resolving them
        // to avoid triggering hosted-service startup logic.
        var registeredTypes = builder.Services
            .Select(d => d.ServiceType)
            .ToHashSet();

        registeredTypes.Should().Contain(typeof(IManualModeService),
            "AddPcsRemoteServices must register IManualModeService");

        registeredTypes.Should().Contain(typeof(IOperationCoordinatorService),
            "AddPcsRemoteServices must register IOperationCoordinatorService");

        registeredTypes.Should().Contain(typeof(IScoreboardService),
            "AddPcsRemoteServices must register IScoreboardService");

        registeredTypes.Should().Contain(typeof(IPcsProAutomationService),
            "AddPcsRemoteServices must register IPcsProAutomationService");
    }
}
