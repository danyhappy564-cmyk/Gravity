using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Xunit;

namespace Gravity.Tests;

/// <summary>1.0.6 #39: 원래 무리가 없는 맵에 켠 특수 봇(레이더·로그·컬티스트·블러드하운드)을 다른 맵 항목을 본떠 넣는다.</summary>
public class SpecialNewMapTests
{
    // 인터체인지처럼 특수 봇이 하나도 없는 맵. 보스 구역 2곳 + 저격 구역 1곳.
    private static LocationBase Interchange() => new()
    {
        Id = "interchange", EscapeTimeLimit = 40, BotMax = 20,
        BossLocationSpawn =
        [
            new BossLocationSpawn { BossName = "bossKilla", BossChance = 38, BossZone = "ZoneCenter,ZoneOLI", Time = -1 },
            new BossLocationSpawn { BossName = "marksman", BossChance = 0, BossZone = "ZoneSniperRoof", Time = -1 }
        ]
    };

    private static Dictionary<string, BossLocationSpawn> Templates() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["pmcBot"] = new BossLocationSpawn { BossName = "pmcBot", BossChance = 60, BossZone = "BotZoneFloor2", BossEscortType = "pmcBot", BossEscortAmount = "1,2", Time = 300 },
        ["exUsec"] = new BossLocationSpawn { BossName = "exUsec", BossChance = 80, BossZone = "Zone_Blockpost", BossEscortType = "exUsec", BossEscortAmount = "1", Time = -1 },
        ["sectantPriest"] = new BossLocationSpawn { BossName = "sectantPriest", BossChance = 10, BossZone = "ZoneCult", BossEscortType = "sectantWarrior", BossEscortAmount = "2,3", Time = -1 },
        ["arenaFighterEvent"] = new BossLocationSpawn { BossName = "arenaFighterEvent", BossChance = 5, BossZone = "ZoneHound", BossEscortAmount = "1", Time = -1 }
    };

    private static GravityConfig Cfg(string json) => ConfigModel.FromState(JsonNode.Parse(json), []);
    private static List<BossLocationSpawn> Of(LocationBase l, string role) => l.BossLocationSpawn.Where(e => e.BossName == role).ToList();

    [Fact]
    public void RaidersExtra_OnMapWithoutRaiders_ClonesTemplate_BossZonesWithoutSniper()
    {
        var l = Interchange();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"specials":{"raiders":{"extra":{"interchange":25},"extraMin":2,"extraMax":4}}}"""), "interchange", 3, 2400, Templates());
        var r = Assert.Single(Of(l, "pmcBot"));
        Assert.Equal(25, r.BossChance);
        Assert.Equal("ZoneCenter,ZoneOLI", r.BossZone);
        Assert.Equal("", r.TriggerName);
        Assert.Equal("gravity", r.SptId);
        Assert.False(r.IgnoreMaxBots);
        Assert.Equal("1,2,3", r.BossEscortAmount);
    }

    [Fact]
    public void RoguesExtra_OnMapWithoutRogues_Added()
    {
        var l = Interchange();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"specials":{"rogues":{"extra":{"interchange":100},"extraMin":3,"extraMax":3}}}"""), "interchange", 3, 2400, Templates());
        var r = Assert.Single(Of(l, "exUsec"));
        Assert.Equal(100, r.BossChance);
        Assert.DoesNotContain("ZoneSniper", r.BossZone);
    }

    [Fact]
    public void ZeroChance_OrNoTemplates_AddsNothing()
    {
        var l = Interchange();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"specials":{"raiders":{"extra":{"interchange":0},"extraMax":3},"cultists":{"chance":{"interchange":0}}}}"""), "interchange", 3, 2400, Templates());
        Assert.Empty(Of(l, "pmcBot"));
        Assert.Empty(Of(l, "sectantPriest"));

        var l2 = Interchange();
        RaidSpawnApplier.ApplyBosses(l2, Cfg("""{"specials":{"raiders":{"extra":{"interchange":50},"extraMax":3},"cultists":{"chance":{"interchange":50}}}}"""), "interchange", 3, 2400);
        Assert.Empty(Of(l2, "pmcBot"));
        Assert.Empty(Of(l2, "sectantPriest"));
    }

    [Fact]
    public void Cultists_And_Bloodhounds_ChanceOnNewMap_Added_WithGroupSize()
    {
        var l = Interchange();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"specials":{"cultists":{"chance":{"interchange":40},"min":3,"max":5},"bloodhounds":{"chance":{"interchange":15}}}}"""), "interchange", 3, 2400, Templates());
        var c = Assert.Single(Of(l, "sectantPriest"));
        Assert.Equal(40, c.BossChance);
        Assert.Equal("2,3,4", c.BossEscortAmount);
        Assert.Equal("sectantWarrior", c.BossEscortType);
        Assert.Equal("ZoneCenter,ZoneOLI", c.BossZone);
        var h = Assert.Single(Of(l, "arenaFighterEvent"));
        Assert.Equal(15, h.BossChance);
        Assert.Equal("1", h.BossEscortAmount);
    }

    [Fact]
    public void SpecialOff_AddsNothing()
    {
        var l = Interchange();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"specials":{"cultists":{"on":false,"chance":{"interchange":40}},"raiders":{"on":false,"extra":{"interchange":40},"extraMax":3}}}"""), "interchange", 3, 2400, Templates());
        Assert.Empty(Of(l, "sectantPriest"));
        Assert.Empty(Of(l, "pmcBot"));
    }

    [Fact]
    public void MapWithOriginal_UsesOwnEntries_NotTemplate()
    {
        var l = new LocationBase
        {
            Id = "bigmap", EscapeTimeLimit = 40, BotMax = 20,
            BossLocationSpawn = [new BossLocationSpawn { BossName = "sectantPriest", BossChance = 10, BossZone = "ZoneCustomsCult", BossEscortAmount = "2", Time = -1 }]
        };
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"specials":{"cultists":{"chance":{"bigmap":30}}}}"""), "bigmap", 3, 2400, Templates());
        var c = Assert.Single(Of(l, "sectantPriest"));
        Assert.Equal(30, c.BossChance);
        Assert.Equal("ZoneCustomsCult", c.BossZone);
        Assert.NotEqual("gravity", c.SptId);
    }

    [Fact]
    public void CatalogTemplates_SpecialRoles_OnlyWithoutTrigger()
    {
        Assert.True(MapCatalog.IsTemplateRole(new BossLocationSpawn { BossName = "pmcBot" }, "pmcBot"));
        Assert.False(MapCatalog.IsTemplateRole(new BossLocationSpawn { BossName = "pmcBot", TriggerName = "interactObject", TriggerId = "autoId_00632_EXFIL" }, "pmcBot"));
        Assert.True(MapCatalog.IsTemplateRole(new BossLocationSpawn { BossName = "bossKilla", TriggerName = "botEvent" }, "bossKilla"));
        Assert.False(MapCatalog.IsTemplateRole(new BossLocationSpawn { BossName = "pmcUSEC" }, "pmcUSEC"));
    }
}
