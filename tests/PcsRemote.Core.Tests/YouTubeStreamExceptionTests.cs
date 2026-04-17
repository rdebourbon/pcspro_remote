using FluentAssertions;
using PcsRemote.Core;

namespace PcsRemote.Core.Tests;

[TestClass]
public sealed class YouTubeStreamExceptionTests
{
    [TestMethod]
    public void Constructor_WithMessage_PreservesMessage()
    {
        var sut = new YouTubeStreamException("Broadcast creation failed");

        sut.Message.Should().Be("Broadcast creation failed");
    }

    [TestMethod]
    public void Constructor_WithMessageAndInnerException_PreservesBoth()
    {
        var inner = new InvalidOperationException("inner");
        var sut = new YouTubeStreamException("API error", inner);

        sut.Message.Should().Be("API error");
        sut.InnerException.Should().BeSameAs(inner);
    }
}
