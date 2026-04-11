using Bunit;
using FluentAssertions;
using Radzen;
using IndexPage = PcsRemote.Web.Pages.Index;

namespace PcsRemote.Web.Tests.Pages;

[TestClass]
public class IndexTests
{
    [TestMethod]
    public void IndexPage_RendersExpectedHeading()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddRadzenComponents();

        var cut = ctx.Render<IndexPage>();

        cut.Find("h1").TextContent.Should().Contain("PCS Remote");
    }
}
