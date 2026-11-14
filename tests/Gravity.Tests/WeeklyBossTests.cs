using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using Xunit;

namespace Gravity.Tests;

/// <summary>1.0.6 주간 보스 토글(weeklyBoss): 켜면 이번 주 보스 100%, 끄면 Gravity 확률·해골 표시 끔.</summary>
public class WeeklyBossTests
{
    private static GravityConfig Cfg(string json) => ConfigModel.FromState(JsonNode.Parse(json), []);
    private static readonly WeeklyBoss.Pick Killa = new("bossKilla", "interchange");

    private static LocationBase Interchange() => new()
    {
        Id = "interchange",
        BossLocationSpawn =
        [
            new BossLocationSpawn { BossName = "bossKilla", BossChance = 100, BossZone = "ZoneCenter", Time = -1, ShowOnTarkovMap = true, ShowOnTarkovMapPvE = true },
            new BossLocationSpawn { BossName = "pmcBot", BossChance = 30, BossZone = "ZoneCenter" }
        ]
    };

    [Fact]
    public void Pick_SameAsSptFormula()
    {
        List<WildSpawnType> pool = [WildSpawnType.bossBully, WildSpawnType.bossKilla, WildSpawnType.bossGluhar, WildSpawnType.bossKnight];
        var sat = new DateTime(2026, 11, 14);           // 토요일
        var mon = new DateTime(2026, 11, 9);            // 그 전 월요일(초기화 요일)
        var expected = pool[new Random(mon.Year * 1009 + mon.DayOfYear).Next(0, pool.Count)];
        Assert.Equal(expected.ToString(), WeeklyBoss.Choose(pool, DayOfWeek.Monday, sat));
        Assert.Equal(expected.ToString(), WeeklyBoss.Choose(pool, DayOfWeek.Monday, mon)); // 초기화 당일 포함
        Assert.Equal("interchange", WeeklyBoss.MapOf("bossKilla"));
        Assert.Null(WeeklyBoss.MapOf("bossZryachiy"));
    }

    [Fact]
    public void Default_IsOn() => Assert.True(ConfigModel.Default().WeeklyBoss);

    [Fact]
    public void On_WeeklyBossMap_Chance100()
    {
        var loc = Interchange();
        RaidSpawnApplier.ApplyBosses(loc, Cfg("{}"), "interchange", weekly: Killa);
        var k = loc.BossLocationSpawn.Single(e => e.BossName == "bossKilla");
        Assert.Equal(100, k.BossChance);
        Assert.True(k.ShowOnTarkovMap);
    }

    [Fact]
    public void On_BossOff_StaysZero()
    {
        var loc = Interchange();
        RaidSpawnApplier.ApplyBosses(loc, Cfg("""{"bosses":{"killa":{"on":false}}}"""), "interchange", weekly: Killa);
        Assert.Equal(0, loc.BossLocationSpawn.Single(e => e.BossName == "bossKilla").BossChance);
    }

    [Fact]
    public void Off_UsesGravityChance_AndHidesSkull()
    {
        var loc = Interchange();
        RaidSpawnApplier.ApplyBosses(loc, Cfg("""{"weeklyBoss":false}"""), "interchange", weekly: Killa);
        var k = loc.BossLocationSpawn.Single(e => e.BossName == "bossKilla");
        Assert.Equal(ConfigModel.BossDefaultChance["killa"], k.BossChance);
        Assert.False(k.ShowOnTarkovMap);
        Assert.False(k.ShowOnTarkovMapPvE);
    }

    [Fact]
    public void OtherMap_Unchanged()
    {
        var loc = Interchange();
        RaidSpawnApplier.ApplyBosses(loc, Cfg("{}"), "interchange", weekly: new WeeklyBoss.Pick("bossBully", "bigmap"));
        Assert.Equal(ConfigModel.BossDefaultChance["killa"], loc.BossLocationSpawn.Single(e => e.BossName == "bossKilla").BossChance);
    }

    [Fact]
    public void DbFlag_FollowsToggle()
    {
        var b = Interchange();
        Assert.True(WeeklyBoss.SetFlag(b, Killa, false));
        Assert.False(b.BossLocationSpawn[0].ShowOnTarkovMap);
        Assert.False(b.BossLocationSpawn[0].ShowOnTarkovMapPvE);
        Assert.True(WeeklyBoss.SetFlag(b, Killa, true));
        Assert.True(b.BossLocationSpawn[0].ShowOnTarkovMap);
        Assert.False(WeeklyBoss.SetFlag(b, new WeeklyBoss.Pick("bossBully", "interchange"), false));
    }

    [Fact]
    public void SptWeeklyDisabled_NoPick()
    {
        Assert.Null(WeeklyBoss.Detect(false, [WildSpawnType.bossKilla], DayOfWeek.Monday, DateTime.Today, _ => Interchange()));
        Assert.Equal(Killa, WeeklyBoss.Detect(true, [WildSpawnType.bossKilla], DayOfWeek.Monday, DateTime.Today, _ => Interchange()));
        Assert.Null(WeeklyBoss.Detect(true, [WildSpawnType.bossKilla], DayOfWeek.Monday, DateTime.Today, _ => null));
    }
}
