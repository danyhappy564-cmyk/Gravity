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
        // 컬티스트·블러드하운드는 예전처럼 원래 항목의 확률·인원을 바꾼다. 레이더는 옛 chance·min·max를 무시(원래 항목 그대로).
        var l = Loc();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"specials":{"raiders":{"on":true,"chance":{"bigmap":70},"min":3,"max":5},"cultists":{"on":true,"chance":{"bigmap":70},"min":3,"max":5}}}"""), "bigmap", 1, 2400);
        Assert.Equal(40, Boss(l, "pmcBot").BossChance);
        Assert.Equal("1,2", Boss(l, "pmcBot").BossEscortAmount);
        Assert.Equal(70, Boss(l, "sectantPriest").BossChance);
        Assert.Equal("2,3,4", Boss(l, "sectantPriest").BossEscortAmount);
        var off = Loc();
        RaidSpawnApplier.ApplyBosses(off, Cfg("""{"specials":{"cultists":{"on":false}}}"""), "bigmap", 1, 2400);
        Assert.Equal(0, Boss(off, "sectantPriest").BossChance);
    }

    [Fact]
    public void Raiders_VanillaAndLeverUntouched_ExtraSpawnSeparate()
    {
        // 사용자 결정 11-10(ABPS식): 레버 레이더는 30%·2~4명·트리거·상한 무시 그대로, 추가 스폰만 따로(상한 지킴, 트리거 없음)
        var l = Loc();
        l.BossLocationSpawn.Add(new BossLocationSpawn { BossName = "pmcBot", BossChance = 30, BossZone = "ZoneSubCommand", BossEscortAmount = "2,2,3",
            TriggerId = "autoId_00000_D2_LEVER", TriggerName = "interactObject", Time = -1, IgnoreMaxBots = true });
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"specials":{"raiders":{"extra":{"bigmap":25},"extraMin":2,"extraMax":3}}}"""), "bigmap", 1, 2400);
        var raiders = l.BossLocationSpawn.Where(e => e.BossName == "pmcBot").ToList();
        Assert.Equal(3, raiders.Count);
        Assert.Equal(40, raiders[0].BossChance);
        Assert.Equal("1,2", raiders[0].BossEscortAmount);
        var lever = raiders.Single(e => e.TriggerName == "interactObject");
        Assert.Equal(30, lever.BossChance);
        Assert.Equal("2,2,3", lever.BossEscortAmount);
        Assert.Equal("autoId_00000_D2_LEVER", lever.TriggerId);
        Assert.True(lever.IgnoreMaxBots);
        var extra = raiders.Single(e => e.SptId == "gravity");
        Assert.Equal(25, extra.BossChance);
        Assert.Equal(RaidSpawnApplier.Range(1, 2), extra.BossEscortAmount);
        Assert.Equal("ZoneRaiders,ZoneSubCommand", extra.BossZone);
        Assert.True(Gravity.Shared.WaveMark.IsExtraSpecial(extra.TriggerId, extra.TriggerName));
        Assert.Equal("", extra.TriggerName);
        Assert.False(extra.IgnoreMaxBots);
        Assert.Equal(-1, extra.Time);
    }

    [Fact]
    public void Rogues_ZeroStaysZero_VanillaSwitchAndOff()
    {
        LocationBase Light() => new()
        {
            Id = "lighthouse", EscapeTimeLimit = 40, BotMax = 20,
            BossLocationSpawn =
            [
                new BossLocationSpawn { BossName = "exUsec", BossChance = 80, BossZone = "Zone_RoofRocks", BossEscortAmount = "0", Time = -1, IgnoreMaxBots = true },
                new BossLocationSpawn { BossName = "exUsec", BossChance = 0, BossZone = "Zone_Island", BossEscortAmount = "2", Time = -1, IgnoreMaxBots = true }
            ]
        };
        // 웹 기본값 그대로(옛 chance 75 저장돼 있어도): 원래 꺼진 섬 로그 0%, 1인 자리 1명 그대로
        var a = Light();
        RaidSpawnApplier.ApplyBosses(a, Cfg("""{"specials":{"rogues":{"chance":{"lighthouse":75},"min":3,"max":6}}}"""), "lighthouse", 1, 2400);
        Assert.Equal([80.0, 0.0], a.BossLocationSpawn.Select(e => e.BossChance ?? -1).ToArray());
        Assert.Equal("0", a.BossLocationSpawn[0].BossEscortAmount);
        // 원래 스폰 유지 끔 → 원래 항목 0%, 추가만
        var b = Light();
        RaidSpawnApplier.ApplyBosses(b, Cfg("""{"specials":{"rogues":{"vanilla":false,"extra":{"lighthouse":50}}}}"""), "lighthouse", 1, 2400);
        Assert.All(b.BossLocationSpawn.Where(e => e.SptId != "gravity"), e => Assert.Equal(0, e.BossChance));
        Assert.Equal(50, b.BossLocationSpawn.Single(e => e.SptId == "gravity").BossChance);
        // 끄기 → 원래도 추가도 없음
        var c = Light();
        RaidSpawnApplier.ApplyBosses(c, Cfg("""{"specials":{"rogues":{"on":false,"extra":{"lighthouse":50}}}}"""), "lighthouse", 1, 2400);
        Assert.All(c.BossLocationSpawn, e => Assert.Equal(0, e.BossChance));
        Assert.DoesNotContain(c.BossLocationSpawn, e => e.SptId == "gravity");
        // 원래 무리가 없는 맵에는 추가하지 않는다(구역을 모름)
        var d = Loc();
        RaidSpawnApplier.ApplyBosses(d, Cfg("""{"specials":{"rogues":{"extra":{"bigmap":50}}}}"""), "bigmap", 1, 2400);
        Assert.DoesNotContain(d.BossLocationSpawn, e => e.BossName == "exUsec");
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

    [Fact]
    public void Specials_TimeWindow_OnlyUntriggered_ExtraZeroSkips_SniperWaveTime()
    {
        // 1.0 수정점 23: 특수 봇 시각 창(분), 레버 항목은 시각 그대로, 추가 스폰 0명이면 안 넣음, 저격 웨이브 time_min/max
        var l = Loc();
        l.BossLocationSpawn.RemoveAll(e => e.BossName is "pmcBot" or "sectantPriest");
        l.BossLocationSpawn.Add(new BossLocationSpawn { BossName = "pmcBot", BossChance = 30, BossZone = "ZoneSubCommand", BossEscortAmount = "2",
            TriggerId = "lever", TriggerName = "interactObject", Time = -1 });
        l.BossLocationSpawn.Add(new BossLocationSpawn { BossName = "pmcBot", BossChance = 30, BossZone = "ZoneRaiders", BossEscortAmount = "2", Time = -1 });
        l.BossLocationSpawn.Add(new BossLocationSpawn { BossName = "sectantPriest", BossChance = 10, BossZone = "ZoneScavBase", BossEscortAmount = "3", Time = -1 });
        l.Waves = [new Wave { WildSpawnType = WildSpawnType.marksman, SlotsMin = 1, SlotsMax = 1, TimeMin = 60, TimeMax = 120 }];
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"specials":{"raiders":{"tFrom":5,"tTo":10,"extra":{"bigmap":100},"extraMin":0,"extraMax":0},"cultists":{"tFrom":20,"tTo":20},"snipers":{"tFrom":2,"tTo":4}}}"""), "bigmap", 1, 2400);
        var raiders = l.BossLocationSpawn.Where(e => e.BossName == "pmcBot").ToList();
        Assert.Equal(2, raiders.Count); // 추가 스폰 0명 → 안 넣음
        Assert.Equal(-1, raiders.Single(e => e.TriggerName == "interactObject").Time);
        var plain = raiders.Single(e => string.IsNullOrEmpty(e.TriggerName));
        Assert.InRange(plain.Time ?? -1, 300, 600);
        Assert.Equal(1200, l.BossLocationSpawn.Single(e => e.BossName == "sectantPriest").Time);
        var w = l.Waves.Single();
        Assert.Equal(120, w.TimeMin);
        Assert.Equal(240, w.TimeMax);
    }

    [Theory]
    [InlineData("""{}""", 24)]                                                         // 공통 0 → 맵 기본(세관 24)
    [InlineData("""{"spawn":{"botCap":30}}""", 30)]                                   // 공통 값
    [InlineData("""{"spawn":{"botCap":30},"maps":{"bigmap":{"cap":12,"capOn":true}}}""", 12)]   // 이 맵만 따로
    [InlineData("""{"spawn":{"botCap":30},"maps":{"bigmap":{"cap":12,"capOn":false}}}""", 30)]  // 따로 끔 → 공통
    [InlineData("""{"spawn":{"botCap":30},"maps":{"bigmap":{"cap":18}}}""", 18)]      // 옛 설정: 기본과 다르면 따로 정한 것
    [InlineData("""{"spawn":{"botCap":30},"maps":{"bigmap":{"cap":24}}}""", 30)]      // 옛 설정: 기본과 같으면 공통
    public void BotCap_SharedThenMapOverride(string json, int expected)
    {
        var cfg = Cfg(json);
        Assert.Equal(expected, cfg.Maps["bigmap"].Cap);
    }

    // 1.0.5: 보스 맵별 등장 확률 — 맵별 값 > 보스 공통 > 보스 기본. 0이면 그 맵에서 안 나옴.
    [Fact]
    public void MapChance_Overrides_Common_And_Base()
    {
        var cfg = Cfg("""{"bossCommon":{"on":true,"chance":35},"bosses":{"reshala":{"chance":39,"mapChance":{"bigmap":100}}}}""");
        var l = Loc();
        RaidSpawnApplier.ApplyBosses(l, cfg, "bigmap", 1, 2400);
        Assert.Equal(100, Boss(l, "bossBully").BossChance);
        Assert.Equal(35, RaidSpawnApplier.BossChanceFor(cfg, "reshala", cfg.Bosses["reshala"], "woods"));
        var noCommon = Cfg("""{"bosses":{"reshala":{"chance":39,"mapChance":{"bigmap":80}}}}""");
        Assert.Equal(39, RaidSpawnApplier.BossChanceFor(noCommon, "reshala", noCommon.Bosses["reshala"], "woods"));
        Assert.Equal(80, RaidSpawnApplier.BossChanceFor(noCommon, "reshala", noCommon.Bosses["reshala"], "bigmap"));
    }

    [Fact]
    public void MapChance_Zero_Disables_OnThatMap()
    {
        var l = Loc();
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"bosses":{"reshala":{"mapChance":{"bigmap":0}}}}"""), "bigmap", 1, 2400);
        Assert.Equal(0, Boss(l, "bossBully").BossChance);
        // 원래 없던 맵에 템플릿으로 새로 넣는 경로도 맵별 값을 따른다.
        var tpl = new Dictionary<string, BossLocationSpawn> { ["bossBully"] = Boss(Loc(), "bossBully") };
        var w0 = new LocationBase { Id = "woods", EscapeTimeLimit = 40, BotMax = 20, BossLocationSpawn = [new BossLocationSpawn { BossName = "pmcUSEC", BossChance = 100, BossZone = "ZoneA" }] };
        RaidSpawnApplier.ApplyBosses(w0, Cfg("""{"bosses":{"reshala":{"maps":{"woods":true},"mapChance":{"woods":0}}}}"""), "woods", 1, 2400, tpl);
        Assert.DoesNotContain(w0.BossLocationSpawn, e => e.BossName == "bossBully");
        var w1 = new LocationBase { Id = "woods", EscapeTimeLimit = 40, BotMax = 20, BossLocationSpawn = [new BossLocationSpawn { BossName = "pmcUSEC", BossChance = 100, BossZone = "ZoneA" }] };
        RaidSpawnApplier.ApplyBosses(w1, Cfg("""{"bossCommon":{"on":true,"chance":20},"bosses":{"reshala":{"maps":{"woods":true},"mapChance":{"woods":70}}}}"""), "woods", 1, 2400, tpl);
        Assert.Equal(70, Boss(w1, "bossBully").BossChance);
    }

    [Fact]
    public void MapChance_Clamped_And_MapOff_Wins()
    {
        var cfg = Cfg("""{"bosses":{"reshala":{"mapChance":{"bigmap":150,"woods":-5}},"killa":{"maps":{"interchange":false},"mapChance":{"interchange":90}}}}""");
        Assert.Equal(100, RaidSpawnApplier.BossChanceFor(cfg, "reshala", cfg.Bosses["reshala"], "bigmap"));
        Assert.Equal(0, RaidSpawnApplier.BossChanceFor(cfg, "reshala", cfg.Bosses["reshala"], "woods"));
        Assert.Equal(0, RaidSpawnApplier.BossChanceFor(cfg, "killa", cfg.Bosses["killa"], "interchange"));
        var off = Cfg("""{"bosses":{"reshala":{"on":false,"mapChance":{"bigmap":100}}}}""");
        Assert.Equal(0, RaidSpawnApplier.BossChanceFor(off, "reshala", off.Bosses["reshala"], "bigmap"));
    }
}
