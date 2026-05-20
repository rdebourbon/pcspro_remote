using System.Reflection;
using FluentAssertions;
using PcsRemote.Core;

namespace PcsRemote.Core.Tests;

[TestClass]
public sealed class PlayCricketContractTests
{
    // TC-2: IPlayCricketApiClient is declared in PcsRemote.Core; method uses only BCL types.
    [TestMethod]
    public void IPlayCricketApiClient_IsInCore_HasNoExternalDependencies()
    {
        var type = typeof(IPlayCricketApiClient);

        type.Assembly.GetName().Name.Should().Be("PcsRemote.Core",
            "IPlayCricketApiClient must be declared in PcsRemote.Core");

        var method = type.GetMethod("GetFixturesAsync");
        method.Should().NotBeNull("GetFixturesAsync must exist on IPlayCricketApiClient");

        // Return type is Task<IReadOnlyList<PlayCricketFixture>>
        method!.ReturnType.Should().Be(
            typeof(Task<IReadOnlyList<PlayCricketFixture>>),
            "return type must be Task<IReadOnlyList<PlayCricketFixture>>");

        // Parameters are (int siteId, CancellationToken ct)
        var parameters = method.GetParameters();
        parameters.Should().HaveCount(2);
        parameters[0].ParameterType.Should().Be(typeof(int));
        parameters[1].ParameterType.Should().Be(typeof(CancellationToken));
        parameters[1].HasDefaultValue.Should().BeTrue("ct must be optional");

        // All method parameter and return types must belong to BCL assemblies
        var bcl = typeof(object).Assembly;
        var systemRuntime = typeof(CancellationToken).Assembly;
        var systemCollections = typeof(IReadOnlyList<>).Assembly;
        var systemThreadingTasks = typeof(Task).Assembly;
        var allowedAssemblies = new[] { bcl, systemRuntime, systemCollections, systemThreadingTasks,
            typeof(PlayCricketFixture).Assembly };

        foreach (var param in parameters)
        {
            allowedAssemblies.Should().Contain(param.ParameterType.Assembly,
                $"parameter type {param.ParameterType.Name} must come from a BCL or Core assembly");
        }
    }

    // TC-3: PlayCricketFixture is an immutable record with required FixtureId and string defaults.
    [TestMethod]
    public void PlayCricketFixture_IsImmutableRecord_WithRequiredFixtureId()
    {
        // Record type check
        typeof(PlayCricketFixture).IsValueType.Should().BeFalse();
        typeof(PlayCricketFixture).GetMethod("<Clone>$").Should().NotBeNull(
            "PlayCricketFixture must be a record type");

        // Primary constructor with FixtureId = 42; all other args default
        var fixture = new PlayCricketFixture(42);

        fixture.FixtureId.Should().Be(42);
        fixture.HomeTeam.Should().Be("", "HomeTeam must default to empty string");
        fixture.AwayTeam.Should().Be("", "AwayTeam must default to empty string");
        fixture.Status.Should().Be("", "Status must default to empty string");
        fixture.MatchDate.Should().Be(DateOnly.MinValue, "MatchDate must default to DateOnly.MinValue");
    }
}
