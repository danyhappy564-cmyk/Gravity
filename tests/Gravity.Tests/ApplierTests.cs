using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Location;
using Xunit;
using Path = System.IO.Path;

namespace Gravity.Tests;

public class ApplierTests
{
    private static LocationBase Loc() => new()
    {
        Id = "bigmap", EscapeTimeLimit = 40, BotMax = 20,
        BossLocationSpawn =
        [
            new BossLocationSpawn { BossName = "pmcUSEC", BossChance = 100, Time = 30 },
            new BossLocationSpawn { BossName = "bossBully", BossChance = 39 },
            new BossLocationSpawn { BossName = "exUsec", BossChance = 50 },
            new BossLocationSpawn { BossName = "ruafPatrol", BossChance = 45 }
        ],
        Waves =
        [
            new Wave { WildSpawnType = WildSpawnType.assault, SlotsMin = 1, SlotsMax = 2 },
            new Wave { WildSpawnType = WildSpawnType.marksman, SlotsMin = 1, SlotsMax = 1 }
        ]
    };

    private static readonly MapInfo Info = new("bigmap", 16, 20, 2400, ["ZoneA", "ZoneB"], ["ZoneS1", "ZoneS2"]);

    private static (GravityRuntime rt, GravityConfigStore store) Runtime(string state = "{}", bool abps = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "gravity-apply-" + Guid.NewGuid().ToString("N"));
        var store = new GravityConfigStore(Path.Combine(dir, "g.json"), Path.Combine(dir, "b"));
        store.Save(JsonNode.Parse(state)!);
        var cat = new MapCatalog();
        cat.AddForTest(Info);
        var det = ModDetector.Detect(abps ? ["acidphantasm-botplacementsystem"] : [], []);
        return (new GravityRuntime(store, cat, det, _ => { }, _ => { }) { MaxBotCap = new() }, store);
    }

    [Fact]
    public void Apply_RemovesPmcAndAssault_KeepsBossesAndFactions()
    {
        var loc = Loc();
        var (rt, _) = Runtime();
        Assert.True(rt.TryApplyToRaid("bigmap", loc, null, 5));
        Assert.Contains(loc.BossLocationSpawn, e => e.BossName == "bossBully");
        Assert.Contains(loc.BossLocationSpawn, e => e.BossName == "exUsec");
        Assert.Contains(loc.BossLocationSpawn, e => e.BossName == "ruafPatrol");
        Assert.DoesNotContain(loc.BossLocationSpawn, e => e.BossName == "pmcUSEC" && e.SptId != "gravity");
        Assert.Contains(loc.Waves, w => w.WildSpawnType == WildSpawnType.marksman);
        Assert.All(loc.Waves.Where(w => w.WildSpawnType == WildSpawnType.assault), w => Assert.Equal("gravity", w.SptId));
        Assert.Equal(Enumerable.Range(0, loc.Waves.Count), loc.Waves.Select(w => w.Number ?? -1));
    }

    [Fact]
    public void Apply_WritesGroupFields()
    {
        var loc = Loc();
        var (rt, _) = Runtime("""{"spawn":{"ignoreCap":{"start":true,"waves":false}}}""");
        rt.TryApplyToRaid("bigmap", loc, null, 5);
        var pmcs = loc.BossLocationSpawn.Where(MapCatalog.IsPmc).ToList();
        Assert.NotEmpty(pmcs);
        var plan = rt.Last!.Plan!;
        Assert.Equal(plan.Groups.Count, pmcs.Count);
        Assert.All(pmcs, e =>
        {
            Assert.Equal(100, e.BossChance);
            Assert.Equal(e.BossName, e.BossEscortType);
            Assert.InRange(int.Parse(e.BossEscortAmount!), 0, 4);
            Assert.Equal("ZoneA,ZoneB", e.BossZone);
            Assert.Contains("pve", e.SpawnMode);
        });
        var startCount = plan.Groups.Count(g => g.IsStart);
        Assert.Equal(startCount, pmcs.Count(e => e.IgnoreMaxBots == true));
    }

    [Fact]
    public void Apply_SetsBotMaxAndCap()
    {
        var loc = Loc();
        var (rt, _) = Runtime("""{"maps":{"bigmap":{"cap":31}}}""");
        rt.TryApplyToRaid("bigmap", loc, null, 5);
        Assert.Equal(31, loc.BotMax);
        Assert.Equal(31, rt.MaxBotCap!["bigmap"]);
    }

    [Fact]
    public void Apply_ZoneOff_Excluded()
    {
        var loc = Loc();
        var (rt, _) = Runtime("""{"maps":{"bigmap":{"zones":{"ZoneA":{"on":false}}}}}""");
        rt.TryApplyToRaid("bigmap", loc, null, 5);
        Assert.All(loc.BossLocationSpawn.Where(MapCatalog.IsPmc), e => Assert.Equal("ZoneB", e.BossZone));
    }

    [Fact]
    public void ApplyBosses_OffMap_ChanceZero()
    {
        var loc = Loc();
        var (rt, _) = Runtime("""{"bosses":{"reshala":{"on":true,"chance":80,"maps":{"bigmap":false}}}}""");
        rt.TryApplyToRaid("bigmap", loc, null, 5);
        Assert.Equal(0, loc.BossLocationSpawn.First(e => e.BossName == "bossBully").BossChance);
        Assert.Equal(50, loc.BossLocationSpawn.First(e => e.BossName == "exUsec").BossChance);
    }

    [Fact]
    public void ApplyBosses_ChanceSet()
    {
        var loc = Loc();
        var (rt, _) = Runtime("""{"bosses":{"reshala":{"chance":80}}}""");
        rt.TryApplyToRaid("bigmap", loc, null, 5);
        Assert.Equal(80, loc.BossLocationSpawn.First(e => e.BossName == "bossBully").BossChance);
    }

    [Fact]
    public void UnknownMap_LeavesLocationUntouched()
    {
        var loc = Loc();
        loc.Id = "custom_map";
        var (rt, _) = Runtime();
        Assert.False(rt.TryApplyToRaid("custom_map", loc, null, 5));
        Assert.Contains(loc.BossLocationSpawn, e => e.BossName == "pmcUSEC" && e.SptId == null);
        Assert.Equal(20, loc.BotMax);
    }

    [Fact]
    public void Abps_Locked_Untouched()
    {
        var loc = Loc();
        var (rt, _) = Runtime(abps: true);
        Assert.False(rt.TryApplyToRaid("bigmap", loc, null, 5));
        Assert.Equal(2, loc.Waves.Count);
    }

    [Fact]
    public void MapOriginalOff_Untouched()
    {
        var loc = Loc();
        var (rt, _) = Runtime("""{"maps":{"bigmap":{"original":false}}}""");
        Assert.False(rt.TryApplyToRaid("bigmap", loc, null, 5));
        Assert.Equal(20, loc.BotMax);
    }

    [Fact]
    public void ScavRaid_SimStartUsedAndRaidTimeFromClone()
    {
        var loc = Loc();
        loc.EscapeTimeLimit = 25;
        var (rt, _) = Runtime();
        rt.TryApplyToRaid("bigmap", loc, new RaidChanges { SimulatedRaidStartSeconds = 600, RaidTimeMinutes = 25 }, 5);
        var plan = rt.Last!.Plan!;
        Assert.Equal(1500, plan.RaidSec);
        Assert.Equal(600, plan.SimStartSec);
    }
}
