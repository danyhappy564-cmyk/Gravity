using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Spawn;
using Xunit;

namespace Gravity.Tests;

public class SpawnPlannerTests
{
    private static readonly MapInfo Customs = new("bigmap", 16, 20, 40 * 60, ["ZoneA", "ZoneB"], ["ZoneS"]);
    private static readonly MapInfo Busy = new("bigmap", 46, 20, 40 * 60, ["ZoneA"], ["ZoneS"]);

    private static GravityConfig Cfg(string json = "{}") => ConfigModel.FromState(JsonNode.Parse(json), []);

    [Fact]
    public void SameSeed_SamePlan()
    {
        var a = SpawnPlanner.Plan(Cfg(), Customs, 2400, 0, 42);
        var b = SpawnPlanner.Plan(Cfg(), Customs, 2400, 0, 42);
        Assert.Equal(a.Groups, b.Groups);
        Assert.Equal(a.ScavWaves, b.ScavWaves);
    }

    [Fact]
    public void NoGroupAfterHardStop()
    {
        var p = SpawnPlanner.Plan(Cfg("""{"spawn":{"hardStop":{"mode":"percent","pmcPct":25}}}"""), Customs, 2400, 0, 1);
        Assert.NotEmpty(p.Groups);
        Assert.All(p.Groups, g => Assert.True(g.TimeSec <= 2400 * 0.75 + 1));
    }

    [Fact]
    public void SimStart_RemovesEarlyAndShifts()
    {
        var full = SpawnPlanner.Plan(Cfg(), Customs, 2400, 0, 7);
        var late = SpawnPlanner.Plan(Cfg(), Customs, 2400, 900, 7);
        Assert.Equal(full.Groups.Count(g => g.TimeSec >= 900), late.Groups.Count);
        Assert.All(late.Groups, g => Assert.True(g.TimeSec >= 0));
        Assert.DoesNotContain(late.Groups, g => g.IsStart);
    }

    [Fact]
    public void ShortRaid_NoNegativeTimes()
    {
        var p = SpawnPlanner.Plan(Cfg(), Customs, 300, 280, 3);
        Assert.All(p.Groups, g => Assert.True(g.TimeSec >= 0));
        Assert.All(p.ScavWaves, w => Assert.True(w.TimeMinSec >= 0 && w.TimeMaxSec >= w.TimeMinSec));
    }

    [Fact]
    public void GroupCap_Split_LimitsCountsKeepsPlayers()
    {
        var cfg = Cfg("""{"spawn":{"startPmcPct":100,"squad":{"s1":20,"s2":20,"s3":20,"s4":20,"s5":20},"groupCap":{"on":true,"over":"split","rules":[{"size":5,"cmp":"eq","count":1},{"size":3,"cmp":"eq","count":1}]}}}""");
        var noCap = SpawnPlanner.Plan(Cfg("""{"spawn":{"startPmcPct":100,"squad":{"s1":20,"s2":20,"s3":20,"s4":20,"s5":20}}}"""), Busy, 2400, 0, 11);
        var p = SpawnPlanner.Plan(cfg, Busy, 2400, 0, 11);
        Assert.True(p.Groups.Count(g => g.Size == 5) <= 1);
        Assert.True(p.Groups.Count(g => g.Size == 3) <= 1);
        Assert.Equal(0, p.DroppedPmcs);
        Assert.True(p.PmcTotal > 0);
        Assert.Equal(noCap.PmcTotal, p.PmcTotal);
        for (var seed = 0; seed < 30; seed++)
        {
            var q = SpawnPlanner.Plan(cfg, Busy, 2400, 0, seed);
            Assert.True(q.Groups.Count(g => g.Size == 5) <= 1 && q.Groups.Count(g => g.Size == 3) <= 1);
        }
    }

    [Fact]
    public void GroupCap_Drop_LosesPlayers()
    {
        var cfg = Cfg("""{"spawn":{"waveMin":5,"waveMax":5,"squad":{"s1":0,"s2":0,"s3":0,"s4":0,"s5":100},"groupCap":{"on":true,"over":"drop","rules":[{"size":5,"cmp":"eq","count":1}]}}}""");
        var p = SpawnPlanner.Plan(cfg, Busy, 2400, 0, 5);
        Assert.True(p.Groups.Count(g => g.Size == 5) <= 1);
        Assert.True(p.DroppedPmcs > 0);
    }

    [Fact]
    public void GroupCap_AllForbidden_Terminates()
    {
        var cfg = Cfg("""{"spawn":{"squad":{"s5":100,"s1":0,"s2":0,"s3":0,"s4":0},"groupCap":{"on":true,"over":"split","rules":[{"size":1,"cmp":"ge","count":0}]}}}""");
        var p = SpawnPlanner.Plan(cfg, Customs, 2400, 0, 5);
        Assert.Empty(p.Groups);
        Assert.True(p.DroppedPmcs > 0);
    }

    [Fact]
    public void Infinite_StyleProducesWavesUntilEnd_Max400()
    {
        var cfg = Cfg("""{"maps":{"bigmap":{"style":"infinite","inf":{"delay":10,"batch":2,"pmcShare":50,"jitter":0}}}}""");
        var p = SpawnPlanner.Plan(cfg, Customs, 6 * 3600, 0, 9);
        Assert.True(p.ScavWaves.Count <= 400);
        Assert.True(p.Groups.Count(g => !g.IsStart) > 50);
        Assert.True(p.Groups.Max(g => g.TimeSec) > 6 * 3600 * 0.8);
    }

    [Fact]
    public void UsecPct100_AllUsec()
    {
        var p = SpawnPlanner.Plan(Cfg("""{"spawn":{"usecPct":100}}"""), Customs, 2400, 0, 2);
        Assert.All(p.Groups, g => Assert.True(g.Usec));
    }

    [Fact]
    public void GroupSizesBetween1And5()
    {
        for (var seed = 0; seed < 20; seed++)
            Assert.All(SpawnPlanner.Plan(Cfg(), Customs, 2400, 0, seed).Groups, g => Assert.InRange(g.Size, 1, 5));
    }

    [Fact]
    public void StartStyle_NoWaves()
    {
        var p = SpawnPlanner.Plan(Cfg("""{"maps":{"bigmap":{"style":"start"}}}"""), Customs, 2400, 0, 2);
        Assert.All(p.Groups, g => Assert.True(g.IsStart));
        Assert.True(p.StartPmcs >= (int)Math.Round(16 * 0.6) - 0);
    }

    [Fact]
    public void LongerRaid_MorePmcs()
    {
        var shortP = SpawnPlanner.Plan(Cfg(), Customs, 2400, 0, 4);
        var longP = SpawnPlanner.Plan(Cfg(), Customs, 6000, 0, 4);
        Assert.True(longP.Groups.Count(g => !g.IsStart) > shortP.Groups.Count(g => !g.IsStart));
    }
}
