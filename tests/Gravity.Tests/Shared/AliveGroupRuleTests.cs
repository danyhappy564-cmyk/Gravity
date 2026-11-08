using Gravity.Shared;
using Xunit;

namespace Gravity.Tests.Shared;

public class AliveGroupRuleTests
{
    private static GroupCapDto Cap(string cmp, int size, bool on = true, string scope = "all") =>
        new(on, scope, "split", new[] { new GroupCapRuleDto(size, cmp, 1) });

    [Fact]
    public void AliveRule_EqRule_WaitsAtLimit()
    {
        var cap = Cap("eq", 5);
        Assert.Equal(CapVerdict.Wait, AliveGroupRule.Check(new[] { 5 }, 5, cap));
        Assert.Equal(CapVerdict.Allow, AliveGroupRule.Check(new[] { 5 }, 4, cap));
    }

    [Fact]
    public void AliveRule_GteRule()
    {
        Assert.Equal(CapVerdict.Wait, AliveGroupRule.Check(new[] { 4 }, 3, Cap("gte", 3)));
    }

    [Fact]
    public void AliveRule_Off_AlwaysAllow()
    {
        Assert.Equal(CapVerdict.Allow, AliveGroupRule.Check(new[] { 5 }, 5, Cap("eq", 5, on: false)));
        Assert.Equal(CapVerdict.Allow, AliveGroupRule.Check(new[] { 5 }, 5, Cap("eq", 5, scope: "raid")));
    }
}
