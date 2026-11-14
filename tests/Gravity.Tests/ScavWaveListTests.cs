using System.Linq;
using System.Text.Json.Nodes;
using Gravity.Core;
using Xunit;

namespace Gravity.Tests;

/// <summary>1.0.6 스캐브 웨이브 목록 설정 읽기(maps.&lt;id&gt;.scavStyle=list · scavList · scavListJitter).</summary>
public class ScavListConfigTests
{
    private static GravityConfig Cfg(string json) => ConfigModel.FromState(JsonNode.Parse(json), []);

    [Fact]
    public void Default_IsWavesAndEmptyList()
    {
        var m = ConfigModel.Default().MapFor("bigmap");
        Assert.Equal("waves", m.ScavStyle);
        Assert.Empty(m.ScavList);
        Assert.Equal(20, m.ScavListJitterSec);
    }

    [Fact]
    public void ListStyle_Accepted()
    {
        Assert.Equal("list", Cfg("""{"maps":{"bigmap":{"scavStyle":"list"}}}""").MapFor("bigmap").ScavStyle);
    }

    [Fact]
    public void List_SortedClampedCapped()
    {
        var m = Cfg("""{"maps":{"bigmap":{"scavStyle":"list","scavList":[{"t":20,"min":30,"max":40},{"t":-5,"min":3,"max":1},{"t":10,"min":2,"max":4}]}}}""").MapFor("bigmap");
        Assert.Equal(new[] { 0.0, 10, 20 }, m.ScavList.Select(w => w.TMin).ToArray());
        Assert.Equal(3, m.ScavList[0].Min); Assert.Equal(3, m.ScavList[0].Max);   // max<min → max=min
        Assert.Equal(20, m.ScavList[2].Min); Assert.Equal(20, m.ScavList[2].Max); // 30·40 → 20
        Assert.Equal(2, m.ScavList[1].Min); Assert.Equal(4, m.ScavList[1].Max);

        var many = string.Join(",", Enumerable.Range(0, 70).Select(i => $"{{\"t\":{i},\"min\":1,\"max\":2}}"));
        var m2 = Cfg("{\"maps\":{\"bigmap\":{\"scavList\":[" + many + "]}}}").MapFor("bigmap");
        Assert.Equal(60, m2.ScavList.Count);
    }

    [Fact]
    public void BadEntries_Ignored()
    {
        var m = Cfg("""{"maps":{"bigmap":{"scavList":[{"t":"x","min":1,"max":2},5,null,{"t":3,"min":1,"max":2},{"min":1}]}}}""").MapFor("bigmap");
        Assert.Single(m.ScavList);
        Assert.Equal(3, m.ScavList[0].TMin);
        Assert.Empty(Cfg("""{"maps":{"bigmap":{"scavList":{"0":{"t":1}}}}}""").MapFor("bigmap").ScavList);
    }

    [Fact]
    public void Jitter_Clamped()
    {
        Assert.Equal(120, Cfg("""{"maps":{"bigmap":{"scavListJitter":500}}}""").MapFor("bigmap").ScavListJitterSec);
        Assert.Equal(0, Cfg("""{"maps":{"bigmap":{"scavListJitter":-3}}}""").MapFor("bigmap").ScavListJitterSec);
    }
}
