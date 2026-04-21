using FluentAssertions;

namespace PcsRemote.Automation.Tests;

[TestClass]
public sealed class FakeLoginAutomationTests
{
    [TestMethod]
    public void ReadUsername_Default_ReturnsNull()
    {
        var fake = new FakeLoginAutomation();

        fake.ReadUsername().Should().BeNull();
    }

    [TestMethod]
    public void ReadUsername_Configured_ReturnsValue()
    {
        var fake = new FakeLoginAutomation { UsernameValue = "scorer@hhcc.org" };

        fake.ReadUsername().Should().Be("scorer@hhcc.org");
    }

    [TestMethod]
    public void ClickSwitchUser_RecordsCall()
    {
        var fake = new FakeLoginAutomation();

        fake.ClickSwitchUser();

        fake.SwitchUserClicked.Should().BeTrue();
    }

    [TestMethod]
    public void ClickSwitchUser_ThrowOnInteraction_ThrowsInvalidOperationException()
    {
        var fake = new FakeLoginAutomation { ThrowOnInteraction = true };

        var act = () => fake.ClickSwitchUser();

        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void EnterUsername_RecordsValue()
    {
        var fake = new FakeLoginAutomation();

        fake.EnterUsername("scorer@hhcc.org");

        fake.CapturedUsername.Should().Be("scorer@hhcc.org");
    }

    [TestMethod]
    public void EnterUsername_ThrowOnInteraction_ThrowsInvalidOperationException()
    {
        var fake = new FakeLoginAutomation { ThrowOnInteraction = true };

        var act = () => fake.EnterUsername("scorer@hhcc.org");

        act.Should().Throw<InvalidOperationException>();
    }
}
