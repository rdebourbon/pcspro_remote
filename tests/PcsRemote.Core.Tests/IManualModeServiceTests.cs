using System.Reflection;
using FluentAssertions;
using PcsRemote.Core;

namespace PcsRemote.Core.Tests;

[TestClass]
public sealed class IManualModeServiceTests
{
    private static readonly Type _sut = typeof(IManualModeService);

    // ──────────────────────────────────────────────────────────────────────
    // TC-1  Interface exists in PcsRemote.Core namespace
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetType_IManualModeService_IsInterfaceInCoreAssembly()
    {
        _sut.IsInterface.Should().BeTrue();
        _sut.Namespace.Should().Be("PcsRemote.Core");
        _sut.Assembly.GetName().Name.Should().Be("PcsRemote.Core");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-2  IsManualModeActive — read-only bool property
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetProperty_IsManualModeActive_IsReadOnlyBool()
    {
        var prop = _sut.GetProperty("IsManualModeActive");

        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(bool));
        prop.CanRead.Should().BeTrue();
        prop.CanWrite.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-3  Enable — no-param, void
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetMethod_Enable_ReturnsVoidWithNoParameters()
    {
        var method = _sut.GetMethod("Enable");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(void));
        method.GetParameters().Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-4  Disable — no-param, void
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetMethod_Disable_ReturnsVoidWithNoParameters()
    {
        var method = _sut.GetMethod("Disable");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(void));
        method.GetParameters().Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-5  ManualModeChanged — EventHandler<bool>
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetEvent_ManualModeChanged_IsEventHandlerOfBool()
    {
        var ev = _sut.GetEvent("ManualModeChanged");

        ev.Should().NotBeNull();
        ev!.EventHandlerType.Should().Be(typeof(EventHandler<bool>));
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-6  Toggle — no-param, void
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetMethod_Toggle_ReturnsVoidWithNoParameters()
    {
        var method = _sut.GetMethod("Toggle");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(void));
        method.GetParameters().Should().BeEmpty();
    }
}
