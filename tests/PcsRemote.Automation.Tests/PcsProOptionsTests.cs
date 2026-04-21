using FluentAssertions;

namespace PcsRemote.Automation.Tests;

[TestClass]
public sealed class PcsProOptionsTests
{
    [TestMethod]
    public void ExpectedUsername_SetValue_ReturnsValue()
    {
        var options = new PcsProOptions { ExpectedUsername = "testuser@example.com" };

        options.ExpectedUsername.Should().Be("testuser@example.com");
    }

    [TestMethod]
    public void ExpectedUsername_Default_IsEmptyString()
    {
        var options = new PcsProOptions();

        options.ExpectedUsername.Should().BeEmpty();
    }
}
