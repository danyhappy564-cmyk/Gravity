using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Events;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using Xunit;

namespace Gravity.Tests;

/// <summary>0.3 시즌 이벤트(EventControl·EventRaid), 스캐브 레이드 남는 PMC, 설정 기본값.</summary>
public class EventTests
{
    private static GravityConfig Cfg(string json) => ConfigModel.FromState(JsonNode.Parse(json), []);

    private static SeasonalEventConfig Seasonal() => new()
    {
        EnableSeasonalEventDetection = true,
        EventGear = new() { [SeasonalEventType.Halloween] = new(), [SeasonalEventType.Christmas] = new() },
        EventLoot = new(), EventBotMapping = new(), EventBossSpawns = new(), EventWaves = new(), GifterSettings = [],
        HostilitySettingsForEvent = new(), KhorovodEventTransitWhitelist = new(), ChristmasContainerIds = [], BotAppearanceChanges = new(),
        Events =
        [
            new SeasonalEvent
            {
                Name = "halloween", Type = SeasonalEventType.Halloween, Enabled = true, StartMonth = 10, StartDay = 28, EndMonth = 11, EndDay = 9,
                Settings = new SeasonalEventSettings { EnableSummoning = true, ZombieSettings = new ZombieSettings { Enabled = true } }
            },
            new SeasonalEvent
            {
                Name = "christmas", Type = SeasonalEventType.Christmas, Enabled = true, StartMonth = 12, StartDay = 13, EndMonth = 12, EndDay = 31,
                Settings = new SeasonalEventSettings { EnableSanta = true, EnableChristmasHideout = true, AdjustBotAppearances = true }
            },
            new SeasonalEvent { Name = "April fools", Type = SeasonalEventType.AprilFools, Enabled = true, StartMonth = 4, StartDay = 1, EndMonth = 4, EndDay = 1 }
        ]
    };

    [Theory]
    [InlineData("auto", false, null, "gravity")]
    [InlineData("auto", true, null, "ppes")]
    [InlineData("auto", false, true, "svm")]
    [InlineData("auto", false, false, "gravity")]
    [InlineData("spt", true, true, "spt")]
    [InlineData("gravity", true, true, "gravity")]
    public void ResolveOwner(string owner, bool ppes, bool? svmTouches, string expected)
    {
        var svm = svmTouches == null ? null : new SvmEvents(svmTouches.Value, false, false);
        Assert.Equal(expected, EventControl.ResolveOwner(owner, ppes, svm));
    }

    [Fact]
    public void Defaults_KeepSptDates_AprilFoolsOff()
    {
        var s = Seasonal();
        EventControl.Apply(s, Cfg("{}").Events!);
        Assert.True(s.EnableSeasonalEventDetection);
        var h = s.Events[0];
        Assert.True(h.Enabled);
        Assert.Equal(10, h.StartMonth);
        Assert.False(s.Events[2].Enabled); // 웹 기본 만우절 off
        Assert.Equal(2, s.EventGear.Count);
    }

    [Fact]
    public void ForceOn_WholeYear_AndDetectionOn_EvenIfDetectOff()
    {
        var s = Seasonal();
        EventControl.Apply(s, Cfg("""{"events":{"detect":false,"halloween":{"mode":"on","summon":false,"zombies":0,"gear":false},"christmas":{"mode":"off"}}}""").Events!);
        Assert.True(s.EnableSeasonalEventDetection);
        var h = s.Events[0];
        Assert.True(h.Enabled);
        Assert.Equal((1, 1, 12, 31), (h.StartMonth, h.StartDay, h.EndMonth, h.EndDay));
        Assert.False(h.Settings!.EnableSummoning);
        Assert.False(h.Settings.ZombieSettings!.Enabled);
        Assert.False(s.EventGear.ContainsKey(SeasonalEventType.Halloween));
        Assert.False(s.Events[1].Enabled);
    }

    [Fact]
    public void DetectOff_NoForced_DetectionOff()
    {
        var s = Seasonal();
        EventControl.Apply(s, Cfg("""{"events":{"detect":false}}""").Events!);
        Assert.False(s.EnableSeasonalEventDetection);
    }

    [Fact]
    public void Christmas_Settings()
    {
        var s = Seasonal();
        EventControl.Apply(s, Cfg("""{"events":{"christmas":{"santa":0,"gifts":false,"snow":false,"gear":false}}}""").Events!);
        var c = s.Events[1].Settings!;
        Assert.False(c.EnableSanta);
        Assert.False(c.EnableChristmasHideout);
        Assert.False(c.AdjustBotAppearances);
        Assert.False(s.EventGear.ContainsKey(SeasonalEventType.Christmas));
    }

    private static LocationBase HalloweenLoc(double infection) => new()
    {
        Id = "factory4_day",
        Events = new LocationEvents { Halloween2024 = new Halloween2024 { InfectionPercentage = infection } },
        BossLocationSpawn =
        [
            new BossLocationSpawn { BossName = "infectedAssault", BossChance = 7000 },
            new BossLocationSpawn { BossName = "sectantPredvestnik", BossChance = 40 },
            new BossLocationSpawn { BossName = "gifter", BossChance = 30 },
            new BossLocationSpawn { BossName = "bossKilla", BossChance = 30 }
        ]
    };

    private static double Chance(LocationBase l, string name) => l.BossLocationSpawn.First(b => b.BossName == name).BossChance ?? -1;

    [Fact]
    public void Infected_Chance0_ClearsInfection_100_Keeps()
    {
        var a = HalloweenLoc(70);
        EventRaid.Apply(a, Cfg("""{"events":{"halloween":{"zombies":0}}}"""), "factory4_day", 1);
        Assert.Equal(0, a.Events!.Halloween2024!.InfectionPercentage);
        Assert.Equal(0, Chance(a, "infectedAssault"));
        Assert.Equal(30, Chance(a, "bossKilla"));

        var b = HalloweenLoc(70);
        EventRaid.Apply(b, Cfg("""{"events":{"halloween":{"zombies":100}}}"""), "factory4_day", 1);
        Assert.Equal(70, b.Events!.Halloween2024!.InfectionPercentage);
        Assert.Equal(7000, Chance(b, "infectedAssault"));
    }

    [Fact]
    public void Infected_SpecialsOn_UsesMapChance()
    {
        var a = HalloweenLoc(70);
        EventRaid.Apply(a, Cfg("""{"events":{"halloween":{"zombies":100}},"specials":{"infected":{"on":true,"chance":{"factory4_day":0}}}}"""), "factory4_day", 1);
        Assert.Equal(0, a.Events!.Halloween2024!.InfectionPercentage);
    }

    [Fact]
    public void NoInfection_Untouched_CultAndSantaSet()
    {
        var a = HalloweenLoc(0);
        var off = HalloweenLoc(0);
        Assert.Null(EventRaid.Apply(off, Cfg("""{"events":{"christmas":{"santa":5}}}"""), "factory4_day", 1));
        Assert.Equal(30, Chance(off, "gifter")); // 이벤트가 꺼져 있으면 산타·밤 컬티스트를 건드리지 않는다
        Assert.Equal(40, Chance(off, "sectantPredvestnik"));

        var note = EventRaid.Apply(a, Cfg("""{"events":{"halloween":{"nightCult":15},"christmas":{"santa":5}}}"""), "factory4_day", 1, halloween: true, christmas: true);
        Assert.Equal(7000, Chance(a, "infectedAssault"));
        Assert.Equal(15, Chance(a, "sectantPredvestnik"));
        Assert.Equal(5, Chance(a, "gifter"));
        Assert.Contains("밤 컬티스트", note);

        var plain = new LocationBase { Id = "x", BossLocationSpawn = [new BossLocationSpawn { BossName = "bossKilla", BossChance = 30 }] };
        Assert.Null(EventRaid.Apply(plain, Cfg("{}"), "x", 1));
    }

    [Theory]
    [InlineData(10, 60, 6)]
    [InlineData(10, 0, 0)]
    [InlineData(10, 100, 10)]
    [InlineData(5, 50, 3)]
    public void KeepForScavRaid_Count(int n, double pct, int expected)
    {
        var groups = Enumerable.Range(0, n).Select(i => new PmcGroup(i * 60, true, 1, "normal", i < 2)).ToList();
        var keep = GravityRuntime.KeepForScavRaid(groups, pct, 7);
        Assert.Equal(expected, keep.Count);
        Assert.Equal(keep.OrderBy(g => g.TimeSec).ToList(), keep); // 원래 순서 유지
    }

    [Fact]
    public void Config_Defaults()
    {
        var c = Cfg("{}");
        Assert.Equal("auto", c.Events!.Owner);
        Assert.Equal(60, c.Spawn.Scav.PlayerScavRemain);
        Assert.True(c.Near.UseDefault);
        Assert.False(c.Specials!["infected"].On);
        Assert.True(c.Specials["raiders"].On);
    }
}
