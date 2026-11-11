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
    [InlineData("1.0.3", "1.0.2", false)]
    [InlineData("1.0.2", "1.0.3", false)]
    [InlineData("1.0.0", "1.0.3", false)]
    [InlineData("1.0", "1.0.3", false)]
    [InlineData("1.0.3", "1.0.3", true)]
    [InlineData("1.0.3", "1.0.7", true)]
    [InlineData("1.0.3", "1.1.0", false)]
    public void Compatible_Cases(string a, string b, bool expected) =>
        Assert.Equal(expected, GravityVersion.Compatible(a, b));

    [Fact]
    public void Current_IsCompatibleWithItself() =>
        Assert.True(GravityVersion.Compatible(GravityVersion.Current, GravityVersion.Current));
    [Theory]
    [InlineData("1.0.2",true,false)]
    [InlineData("1.0.3",true,true)]
    [InlineData("1.0.3",false,false)]
    [InlineData("1.1.0",true,false)]
    [InlineData("1.0.7",true,true)]
    public void ClientAlsoChecksVersionWhenOldServerClaimsCompatibility(string server,bool accepted,bool expected)
    {
        var response=new HelloResponse(server,accepted,true);
        var checkedResponse=GravityVersion.ValidateHello(response);
        Assert.Equal(expected,checkedResponse.Compatible);
        Assert.Equal(server,checkedResponse.ServerVersion);
        Assert.True(checkedResponse.SpawnLocked);
        Assert.Equal(accepted,response.Compatible);
    }
}
