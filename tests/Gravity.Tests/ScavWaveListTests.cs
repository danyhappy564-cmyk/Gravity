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

/// <summary>1.0.6 스캐브 웨이브 목록: 서버 계획(시작 스캐브만)·클라 설정(옛 클라 보호 Style=start + List).</summary>
public class ScavListPlanTests
{
    private static readonly Gravity.Spawn.MapInfo Customs = new("bigmap", 16, 20, 2400, ["A"], ["S1", "S2"]);
    private static readonly Gravity.Spawn.MapInfo Factory = new("factory4_day", 6, 10, 1200, ["A"], ["S1"]);
    private static GravityConfig Cfg(string json) => ConfigModel.FromState(JsonNode.Parse(json), []);
    private const string List3 = """{"maps":{"bigmap":{"scavStyle":"list","scavListJitter":15,"scavList":[{"t":2,"min":2,"max":4},{"t":10,"min":3,"max":3},{"t":25,"min":1,"max":5}]}}}""";

    private static Gravity.Shared.ClientRaidSettings Dto(GravityConfig cfg, Gravity.Spawn.MapInfo map, int raid) =>
        Gravity.Client.ClientSettingsBuilder.Build(cfg, map, Gravity.Spawn.SpawnPlanner.Plan(cfg, map, raid, 0, 5, true), true);

    [Fact]
    public void List_PlanHasOnlyStartScavs()
    {
        var p = Gravity.Spawn.SpawnPlanner.Plan(Cfg(List3), Customs, 2400, 0, 5, true);
        Assert.All(p.ScavWaves, w => Assert.True(w.IsStart));
        var waves = Gravity.Spawn.SpawnPlanner.Plan(Cfg("{}"), Customs, 2400, 0, 5, true);
        Assert.Contains(waves.ScavWaves, w => !w.IsStart);
    }

    [Fact]
    public void List_Dto_StyleStart_WithList()
    {
        var s = Dto(Cfg(List3), Customs, 2400).Scav;
        Assert.Equal("start", s.Style);
        Assert.NotNull(s.List);
        Assert.Equal(new[] { 120.0, 600, 1500 }, s.List!.Select(w => w.TimeSec).ToArray());
        Assert.Equal(2, s.List[0].Min); Assert.Equal(4, s.List[0].Max);
        Assert.Equal(15, s.ListJitterSec);
    }

    [Fact]
    public void List_Dto_DropsBeyondRaid()
    {
        var s = Dto(Cfg("""{"maps":{"factory4_day":{"scavStyle":"list","scavList":[{"t":5,"min":2,"max":2},{"t":30,"min":2,"max":2}]}}}"""), Factory, 1200).Scav;
        Assert.Single(s.List!);
    }

    [Fact]
    public void List_Empty_NoError()
    {
        var s = Dto(Cfg("""{"maps":{"bigmap":{"scavStyle":"list"}}}"""), Customs, 2400).Scav;
        Assert.Equal("start", s.Style);
        Assert.NotNull(s.List);
        Assert.Empty(s.List!);
    }

    [Fact]
    public void Waves_Default_Unchanged()
    {
        var s = Dto(Cfg("{}"), Customs, 2400).Scav;
        Assert.Equal("waves", s.Style);
        Assert.Null(s.List);
        Assert.Equal(0, s.ListJitterSec);
    }
}
