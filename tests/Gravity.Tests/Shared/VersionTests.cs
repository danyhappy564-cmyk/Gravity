using Gravity.Shared;
using Xunit;

namespace Gravity.Tests.Shared;

public class VersionTests
{
    [Theory]
    [InlineData("0.2.0", "0.2.5", true)]
    [InlineData("0.2.0", "0.1.0", false)]   // Compatible_MajorMinorMismatch_False
    [InlineData("0.2.0", "1.2.0", false)]
    [InlineData("0.2.0", "garbage", false)]
    [InlineData("", "0.2.0", false)]
    public void Compatible_Cases(string a, string b, bool expected) =>
        Assert.Equal(expected, GravityVersion.Compatible(a, b));

    [Fact]
    public void Current_IsCompatibleWithItself() =>
        Assert.True(GravityVersion.Compatible(GravityVersion.Current, GravityVersion.Current));
}
