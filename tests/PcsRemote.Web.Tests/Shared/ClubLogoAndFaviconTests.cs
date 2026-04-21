using FluentAssertions;

namespace PcsRemote.Web.Tests.Shared;

/// <summary>
/// Verifies that the club logo and favicon assets are correctly wired into
/// the Web UI layout and host page. Per SPEC-S-006-ClubLogoFavicon.md.
/// </summary>
[TestClass]
public class ClubLogoAndFaviconTests
{
    private static readonly string WebProjectRoot = FindWebProjectRoot();

    // TC-1: MainLayout.razor contains an img element referencing hhcc-logo.svg
    [TestMethod]
    public void MainLayout_ContainsHhccLogoImage()
    {
        var layoutPath = Path.Combine(WebProjectRoot, "Shared", "MainLayout.razor");
        File.Exists(layoutPath).Should().BeTrue("MainLayout.razor must exist");

        var content = File.ReadAllText(layoutPath);
        content.Should().Contain("hhcc-logo.svg", "MainLayout must render the HHCC logo");
        content.Should().Contain("hhcc-logo", "MainLayout must apply the hhcc-logo CSS class");
    }

    // TC-2: _Host.cshtml contains favicon link tags
    [TestMethod]
    public void HostPage_ContainsFaviconLinkTags()
    {
        var hostPath = Path.Combine(WebProjectRoot, "Pages", "_Host.cshtml");
        File.Exists(hostPath).Should().BeTrue("_Host.cshtml must exist");

        var content = File.ReadAllText(hostPath);
        content.Should().Contain("rel=\"icon\"", "_Host.cshtml must contain favicon link tag");
        content.Should().Contain("rel=\"apple-touch-icon\"", "_Host.cshtml must contain apple touch icon link tag");
        content.Should().Contain("favicon-16x16.png", "_Host.cshtml must reference 16x16 favicon");
        content.Should().Contain("favicon-32x32.png", "_Host.cshtml must reference 32x32 favicon");
        content.Should().Contain("apple-touch-icon.png", "_Host.cshtml must reference apple touch icon");
    }

    // TC-3: Logo SVG asset exists in wwwroot
    [TestMethod]
    public void LogoSvg_ExistsInWwwroot()
    {
        var svgPath = Path.Combine(WebProjectRoot, "wwwroot", "images", "hhcc-logo.svg");
        File.Exists(svgPath).Should().BeTrue("HHCC logo SVG must exist in wwwroot/images/");
        new FileInfo(svgPath).Length.Should().BeGreaterThan(0, "SVG file must not be empty");
    }

    // TC-4: Favicon PNG assets exist in wwwroot
    [TestMethod]
    [DataRow("favicon-16x16.png")]
    [DataRow("favicon-32x32.png")]
    [DataRow("apple-touch-icon.png")]
    public void FaviconPng_ExistsInWwwroot(string fileName)
    {
        var pngPath = Path.Combine(WebProjectRoot, "wwwroot", "favicon", fileName);
        File.Exists(pngPath).Should().BeTrue($"{fileName} must exist in wwwroot/favicon/");
        new FileInfo(pngPath).Length.Should().BeGreaterThan(0, $"{fileName} must not be empty");
    }

    private static string FindWebProjectRoot()
    {
        // Walk up from test assembly location to find the solution root,
        // then navigate to the Web project.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null
            && !File.Exists(Path.Combine(dir.FullName, "PcsRemote.sln"))
            && !File.Exists(Path.Combine(dir.FullName, "PCS_Remote.slnx")))
        {
            dir = dir.Parent;
        }

        return dir is not null
            ? Path.Combine(dir.FullName, "src", "PcsRemote.Web")
            : throw new InvalidOperationException("Could not find solution root");
    }
}
