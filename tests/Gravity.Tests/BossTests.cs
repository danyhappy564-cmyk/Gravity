using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Xunit;

namespace Gravity.Tests;

/// <summary>0.3 보스 세부·특수 봇(RaidSpawnApplier.ApplyBosses).</summary>
public class BossTests
{
    private static LocationBase Loc() => new()
    {
        Id = "bigmap", EscapeTimeLimit = 40, BotMax = 20,
        BossLocationSpawn =
        [
            new BossLocationSpawn { BossName = "pmcUSEC", BossChance = 100, Time = 30, BossZone = "ZoneA" },
            new BossLocationSpawn { BossName = "bossBully", BossChance = 39, BossZone = "ZoneDormitory,ZoneGasStation", BossEscortAmount = "2,3", Time = -1, BossDifficulty = "normal" },
            new BossLocationSpawn { BossName = "pmcBot", BossChance = 40, BossZone = "ZoneRaiders", BossEscortAmount = "1,2" },
            new BossLocationSpawn { BossName = "sectantPriest", BossChance = 10, BossZone = "ZoneCult", BossEscortAmount = "2,3,4" }
        ]
    };

    private static GravityConfig Cfg(string json) => ConfigModel.FromState(JsonNode.Parse(json), []);
    private static BossLocationSpawn Boss(LocationBase l, string name) => l.BossLocationSpawn.First(e => e.BossName == name);

    [Fact]
    public void Defaults_KeepVanillaDetail()
    {
        var l = Loc();
        RaidSpawnApplier.ApplyBosses(l, Cfg("{}"), "bigmap", 1, 2400);
        var b = Boss(l, "bossBully");
        Assert.Equal("2,3", b.BossEscortAmount);
        Assert.Equal(-1, b.Time);
        Assert.Equal("ZoneDormitory,ZoneGasStation", b.BossZone);
        Assert.Equal("normal", b.BossDifficulty);
        Assert.Equal(4, l.BossLocationSpawn.Count);
        Assert.Equal(40, Boss(l, "pmcBot").BossChance);
    }

    [Fact]
    public void Escorts_Range_And_FollowersOff()
    {
        var l = Loc();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"bosses":{"reshala":{"escMin":2,"escMax":4}}}"""), "bigmap", 1, 2400);
        Assert.Equal("2,3,4", Boss(l, "bossBully").BossEscortAmount);

        var l2 = Loc();
        RaidSpawnApplier.ApplyBosses(l2, Cfg("""{"bosses":{"reshala":{"escMin":2,"escMax":4,"followers":false}}}"""), "bigmap", 1, 2400);
        Assert.Equal("0", Boss(l2, "bossBully").BossEscortAmount);
    }

    [Fact]
    public void Time_InWindow_ClampedToRaid()
    {
        var l = Loc();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"bosses":{"reshala":{"tFrom":5,"tTo":10}}}"""), "bigmap", 7, 2400);
        var t = Boss(l, "bossBully").Time!.Value;
        Assert.InRange(t, 300, 600);

        var l2 = Loc();
        RaidSpawnApplier.ApplyBosses(l2, Cfg("""{"bosses":{"reshala":{"tFrom":50,"tTo":60}}}"""), "bigmap", 7, 600);
        Assert.Equal(540, Boss(l2, "bossBully").Time);
    }

    [Fact]
    public void Difficulty_SetForBossAndEscort()
    {
        var l = Loc();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"bosses":{"reshala":{"diff":"hard"}}}"""), "bigmap", 1, 2400);
        Assert.Equal("hard", Boss(l, "bossBully").BossDifficulty);
        Assert.Equal("hard", Boss(l, "bossBully").BossEscortDifficulty);
    }

    [Fact]
    public void Zones_KeepOnlyRealNames_ElseVanilla()
    {
        var l = Loc();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"bosses":{"reshala":{"zones":{"bigmap":["ZoneGasStation","dorms"]}}}}"""), "bigmap", 1, 2400);
        Assert.Equal("ZoneGasStation", Boss(l, "bossBully").BossZone);

        var l2 = Loc();
        RaidSpawnApplier.ApplyBosses(l2, Cfg("""{"bosses":{"reshala":{"zones":{"bigmap":["dorms","newgas"]}}}}"""), "bigmap", 1, 2400);
        Assert.Equal("ZoneDormitory,ZoneGasStation", Boss(l2, "bossBully").BossZone);
    }

    [Fact]
    public void Extra_AddsGravityClone_OnlyWhenChancePositive()
    {
        var l = Loc();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"bosses":{"reshala":{"extra":true,"chance":60}}}"""), "bigmap", 1, 2400);
        var all = l.BossLocationSpawn.Where(e => e.BossName == "bossBully").ToList();
        Assert.Equal(2, all.Count);
        Assert.Single(all, e => e.SptId == "gravity");
        Assert.All(all, e => Assert.Equal(60, e.BossChance));

        var l2 = Loc();
        RaidSpawnApplier.ApplyBosses(l2, Cfg("""{"bosses":{"reshala":{"extra":true,"on":false}}}"""), "bigmap", 1, 2400);
        Assert.Single(l2.BossLocationSpawn, e => e.BossName == "bossBully");
    }

    [Fact]
    public void Specials_OffChanceSize()
    {
        var l = Loc();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"specials":{"raiders":{"on":true,"chance":{"bigmap":70},"min":3,"max":5},"cultists":{"on":false}}}"""), "bigmap", 1, 2400);
        Assert.Equal(70, Boss(l, "pmcBot").BossChance);
        Assert.Equal("2,3,4", Boss(l, "pmcBot").BossEscortAmount);
        Assert.Equal(0, Boss(l, "sectantPriest").BossChance);
        Assert.Equal("2,3,4", Boss(l, "sectantPriest").BossEscortAmount);
    }

    [Fact]
    public void Specials_ChanceForOtherMapOnly_KeepsVanilla()
    {
        var l = Loc();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"specials":{"raiders":{"chance":{"laboratory":65}}}}"""), "bigmap", 1, 2400);
        Assert.Equal(40, Boss(l, "pmcBot").BossChance);
        Assert.Equal("1,2", Boss(l, "pmcBot").BossEscortAmount);
    }

    [Fact]
    public void Range_Format() => Assert.Equal("0", RaidSpawnApplier.Range(0, 0));

    // ----- 0.4 -----

    private static readonly Dictionary<string, BossLocationSpawn> Tpl = new()
    {
        ["bossKilla"] = new BossLocationSpawn { BossName = "bossKilla", BossChance = 38, BossZone = "ZoneCenter", BossEscortAmount = "0", TriggerId = "x", TriggerName = "y", Time = -1 }
    };

    [Fact]
    public void NewMap_BossFromTemplate_WhenMapTurnedOn()
    {
        var l = Loc();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"bosses":{"killa":{"chance":50,"maps":{"bigmap":true},"zones":{"bigmap":["ZoneDormitory"]}}}}"""), "bigmap", 1, 2400, Tpl);
        var k = Boss(l, "bossKilla");
        Assert.Equal(50, k.BossChance);
        Assert.Equal("ZoneDormitory", k.BossZone);
        Assert.Equal("gravity", k.SptId);
        Assert.Equal("", k.TriggerId);
    }

    [Fact]
    public void NewMap_NoZoneChosen_UsesMapZones_AndOffMapNotAdded()
    {
        var l = Loc();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"bosses":{"killa":{"maps":{"bigmap":true}}}}"""), "bigmap", 1, 2400, Tpl);
        Assert.Contains("ZoneDormitory", Boss(l, "bossKilla").BossZone);

        var l2 = Loc();
        RaidSpawnApplier.ApplyBosses(l2, Cfg("{}"), "bigmap", 1, 2400, Tpl);
        Assert.DoesNotContain(l2.BossLocationSpawn, e => e.BossName == "bossKilla");
    }

    [Fact]
    public void Snipers_OffChanceSize()
    {
        LocationBase L() { var l = Loc(); l.Waves = [new Wave { WildSpawnType = WildSpawnType.marksman, SlotsMin = 1, SlotsMax = 1 }, new Wave { WildSpawnType = WildSpawnType.assault, SlotsMin = 1, SlotsMax = 2 }]; return l; }
        var a = L();
        RaidSpawnApplier.ApplyBosses(a, Cfg("""{"specials":{"snipers":{"on":false}}}"""), "bigmap", 1, 2400);
        Assert.DoesNotContain(a.Waves, w => w.WildSpawnType == WildSpawnType.marksman);
        Assert.Contains(a.Waves, w => w.WildSpawnType == WildSpawnType.assault);

        var b = L();
        RaidSpawnApplier.ApplyBosses(b, Cfg("""{"specials":{"snipers":{"chance":{"bigmap":100},"min":2,"max":3}}}"""), "bigmap", 1, 2400);
        var m = b.Waves.Single(w => w.WildSpawnType == WildSpawnType.marksman);
        Assert.Equal(2, m.SlotsMin);
        Assert.Equal(3, m.SlotsMax);

        var c = L();
        RaidSpawnApplier.ApplyBosses(c, Cfg("""{"specials":{"snipers":{"chance":{"bigmap":0}}}}"""), "bigmap", 1, 2400);
        Assert.DoesNotContain(c.Waves, w => w.WildSpawnType == WildSpawnType.marksman);
    }
}
