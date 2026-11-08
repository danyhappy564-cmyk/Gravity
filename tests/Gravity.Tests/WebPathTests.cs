using Gravity.Web;
using Xunit;
using Path = System.IO.Path;

namespace Gravity.Tests;

public class WebPathTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "gravity-www");

    [Fact]
    public void Resolve_Root_IsIndex() => Assert.Equal(Path.Combine(Path.GetFullPath(Root), "index.html"), GravityWeb.ResolveStatic(Root, ""));

    [Fact]
    public void Resolve_DotDot_Null()
    {
        Assert.Null(GravityWeb.ResolveStatic(Root, "../secret.json"));
        Assert.Null(GravityWeb.ResolveStatic(Root, "a/%2e%2e/%2e%2e/x"));
        Assert.Null(GravityWeb.ResolveStatic(Root, "C:/Windows/win.ini"));
    }

    [Fact]
    public void Resolve_Asset() => Assert.Equal(Path.Combine(Path.GetFullPath(Root), "img", "logo.svg"), GravityWeb.ResolveStatic(Root, "img/logo.svg"));
}
