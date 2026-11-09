using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Factions;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Eft.Common;
using Xunit;

namespace Gravity.Tests;

/// <summary>0.4 팩션 관리(FactionRaid)·설정 파일 읽기(FactionData)·다른 모드 PMC 구분(RaidSpawnApplier).</summary>
public class FactionTests
{
    private static GravityConfig Cfg(string json) => ConfigModel.FromState(JsonNode.Parse(json), []);

    private static readonly FactionData Data = new()
    {
        Ruaf = new Dictionary<string, RuafMap>
        {
            ["bigmap"] = new(["ZoneBlockPost", "ZoneCrossRoad", "ZoneTankSquare"], ["ZoneWade", "ZoneBlockPost"], null),
            ["lighthouse"] = new(["Zone_DestroyedHouse"], [], "Zone_DestroyedHouse")
        },
        BlackDivMaps = ["bigmap", "factory4_day"],
        LabGateVanilla = new Dictionary<string, List<BossLocationSpawn>>
        {
            ["autoId_00632_EXFIL"] =
            [
                new BossLocationSpawn { BossName = "pmcBot", BossZone = "BotZoneFloor1", BossChance = 35, TriggerId = "autoId_00632_EXFIL" },
                new BossLocationSpawn { BossName = "pmcBot", BossZone = "BotZoneGate1", BossChance = 45, TriggerId = "autoId_00632_EXFIL" }
            ],
            ["autoId_00014_EXFIL"] =
            [
                new BossLocationSpawn { BossName = "pmcBot", BossZone = "BotZoneGate2", BossChance = 35, TriggerId = "autoId_00014_EXFIL" }
            ]
        }
    };

    private static LocationBase Bigmap() => new()
    {
        Id = "bigmap",
        BossLocationSpawn =
        [
            new BossLocationSpawn { BossName = "ruafRiflemanSenior", BossChance = 50, BossZone = "ZoneBlockPost", Time = 400 },
            new BossLocationSpawn { BossName = "remnantRifleman", BossChance = 15, BossZone = "ZoneWade", TriggerId = "hunt" },
            new BossLocationSpawn { BossName = "exUsec", BossChance = 30, BossZone = "ZoneCustoms", TriggerId = "hunt" },
            new BossLocationSpawn { BossName = "blackDivAssault", BossChance = 25, BossZone = "", TriggerId = "hunt", Time = 900 },
            new BossLocationSpawn { BossName = "bossBully", BossChance = 38, BossZone = "ZoneDormitory" }
        ]
    };

    private static string? Run(LocationBase l, string json, string map = "bigmap", int seed = 3, int raidSec = 2400, bool ruaf = true, bool bd = true) =>
        FactionRaid.Apply(l, Cfg(json), map, seed, raidSec, Data, ruaf, bd, ["ZonePmcA"]);

    [Fact]
    public void NotManaged_Untouched()
    {
        var l = Bigmap();
        var before = l.BossLocationSpawn.ToList();
        Assert.Null(Run(l, "{}"));
        Assert.Equal(before, l.BossLocationSpawn);
    }

    [Fact]
    public void NotInstalled_Untouched_EvenIfManaged()
    {
        var l = Bigmap();
        Assert.Null(Run(l, """{"factions":{"ruaf":{"managed":true},"blackdiv":{"managed":true}}}""", ruaf: false, bd: false));
        Assert.Equal(5, l.BossLocationSpawn.Count);
    }

    [Fact]
    public void Ruaf_Managed_RebuildsPatrolsCheckpointsHunt_KeepsOtherHunts()
    {
        var l = Bigmap();
        var note = Run(l, """{"factions":{"ruaf":{"managed":true,"patrol":{"chance":100,"count":3,"min":4,"max":4,"tMin":5,"tMax":6},"checkpoint":{"count":1,"min":2,"max":2},"hunt":{"min":3,"max":3}}}}""");
        Assert.Contains("순찰 3·검문소 1·사냥조 1", note);
        var ruaf = l.BossLocationSpawn.Where(FactionRaid.IsRuaf).ToList();
        Assert.Equal(5, ruaf.Count);
        Assert.All(ruaf, e => Assert.Equal("gravity", e.SptId));
        var patrols = ruaf.Where(e => e.TriggerId == "" && e.Time > 0).ToList();
        Assert.Equal(3, patrols.Count);
        Assert.All(patrols, p => Assert.InRange(p.Time!.Value, 300, 360));
        Assert.Equal(3, patrols.Select(p => p.BossZone).Distinct().Count()); // 구역 겹치지 않음
        var cp = ruaf.Single(e => e.Time == -1);
        Assert.Contains(cp.BossZone, new[] { "ZoneWade", "ZoneBlockPost" });
        Assert.Contains(l.BossLocationSpawn, e => e.BossName == "remnantRifleman");
        Assert.Contains(l.BossLocationSpawn, e => e.BossName == "exUsec");
        Assert.Contains(l.BossLocationSpawn, e => e.BossName == "bossBully");
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(3, 1, 1)]
    [InlineData(6, 2, 3)]
    public void RuafGroup_Composition(int size, int specialists, int riflemen)
    {
        var g = FactionRaid.RuafGroup(size, 50, "Z", 100, new Random(1), true);
        var sup = g.Supports!.ToList();
        Assert.Equal(specialists, sup.Count(s => s.BossEscortType != "ruafRifleman"));
        Assert.Equal(riflemen.ToString(), sup.Single(s => s.BossEscortType == "ruafRifleman").BossEscortAmount);
        Assert.Equal(size - 1, sup.Sum(s => int.Parse(s.BossEscortAmount!)));
        Assert.True(g.IgnoreMaxBots);
    }

    [Fact]
    public void Ruaf_MapOff_RemovesAllRuafModEntries()
    {
        var l = Bigmap();
        Run(l, """{"factions":{"ruaf":{"managed":true,"maps":{"bigmap":false}}}}""");
        Assert.DoesNotContain(l.BossLocationSpawn, e => FactionRaid.IsRuaf(e) || FactionRaid.IsRuafOtherHunt(e));
        Assert.Contains(l.BossLocationSpawn, e => e.BossName == "bossBully");
    }

    [Fact]
    public void Ruaf_NewMap_UsesFallbackZones_NoCheckpoint_ShareCap()
    {
        var l = new LocationBase { Id = "rezervbase", BossLocationSpawn = [] };
        var note = Run(l, """{"factions":{"ruaf":{"managed":true,"shareCap":true,"maps":{"rezervbase":true},"patrol":{"count":1}}}}""", "rezervbase");
        Assert.Contains("검문소 좌표 없는 맵", note);
        var p = l.BossLocationSpawn.Single(e => e.TriggerId == "");
        Assert.Equal("ZonePmcA", p.BossZone);
        Assert.False(p.IgnoreMaxBots);
        Assert.Equal("ZonePmcA", l.BossLocationSpawn.Single(e => e.TriggerId == "hunt").BossZone);
    }

    [Fact]
    public void Ruaf_DefaultMapOff_WhenNotInModConfig()
    {
        var l = new LocationBase { Id = "rezervbase", BossLocationSpawn = [] };
        Assert.Contains("이 맵 끔", Run(l, """{"factions":{"ruaf":{"managed":true}}}""", "rezervbase"));
        Assert.Empty(l.BossLocationSpawn);
    }

    [Fact]
    public void BlackDiv_Hunt_TimeWindowAndSize()
    {
        var l = Bigmap();
        Run(l, """{"factions":{"blackdiv":{"managed":true,"bd":{"chance":100,"tMin":50,"tMax":60,"huntMin":4,"huntMax":4}}}}""", raidSec: 1000);
        var h = l.BossLocationSpawn.Single(e => e.BossName == "blackDivAssault");
        Assert.InRange(h.Time!.Value, 500, 600);
        Assert.Equal("3", h.BossEscortAmount);
        Assert.Equal(100, h.BossChance);
        Assert.Equal("hunt", h.TriggerId);
    }

    [Fact]
    public void BlackDiv_MapOff_And_NotDefaultMap()
    {
        var a = Bigmap();
        Run(a, """{"factions":{"blackdiv":{"managed":true,"maps":{"bigmap":false}}}}""");
        Assert.DoesNotContain(a.BossLocationSpawn, FactionRaid.IsBlackDiv);
        var b = new LocationBase { Id = "woods", BossLocationSpawn = [] };
        Run(b, """{"factions":{"blackdiv":{"managed":true}}}""", "woods");
        Assert.Empty(b.BossLocationSpawn);
    }

    private static LocationBase Lab() => new()
    {
        Id = "laboratory",
        BossLocationSpawn =
        [
            new BossLocationSpawn { BossName = "bossWedge", BossChance = 15, TriggerId = "hunt", Time = -1 },
            // 모드가 Gate2를 차지해 그 트리거의 바닐라 항목을 지운 판
            new BossLocationSpawn { BossName = "blackDivAssault", BossZone = "BotZoneGate2", BossChance = 100, TriggerId = "autoId_00014_EXFIL" },
            new BossLocationSpawn { BossName = "pmcBot", BossZone = "BotZoneFloor1", BossChance = 35, TriggerId = "autoId_00632_EXFIL" },
            new BossLocationSpawn { BossName = "pmcBot", BossZone = "BotZoneGate1", BossChance = 45, TriggerId = "autoId_00632_EXFIL" }
        ]
    };

    [Fact]
    public void Lab_GateChance0_RestoresVanilla_WedgeOff()
    {
        var l = Lab();
        Run(l, """{"factions":{"blackdiv":{"managed":true,"bd":{"labsGate":0,"wedge":false}},"lab":{"blackoutLink":"always"}}}""", "laboratory");
        Assert.DoesNotContain(l.BossLocationSpawn, FactionRaid.IsBlackDiv);
        Assert.Contains(l.BossLocationSpawn, e => e.BossZone == "BotZoneGate2" && e.BossName == "pmcBot");
        Assert.Equal(3, l.BossLocationSpawn.Count(e => e.BossName == "pmcBot"));
    }

    [Fact]
    public void Lab_GateChance100_TakesOnlyGateZones()
    {
        var l = Lab();
        Run(l, """{"factions":{"blackdiv":{"managed":true,"bd":{"labsGate":100,"wedgeChance":40}}},"lab":{"blackoutLink":"always"}}""", "laboratory");
        Assert.Equal(2, l.BossLocationSpawn.Count(e => e.BossName == "blackDivAssault"));
        Assert.Contains(l.BossLocationSpawn, e => e.BossName == "pmcBot" && e.BossZone == "BotZoneFloor1"); // 층 레이더는 남김
        Assert.Equal(40, l.BossLocationSpawn.Single(e => e.BossName == "bossWedge").BossChance);
    }

    [Fact]
    public void Lab_BlackoutLink_OnlyAndBoost()
    {
        FactionData With(bool? blackout) => new()
        {
            LabGateVanilla = Data.LabGateVanilla, BlackDivMaps = Data.BlackDivMaps, BlackoutNow = () => blackout
        };
        var a = Lab();
        FactionRaid.Apply(a, Cfg("""{"factions":{"blackdiv":{"managed":true,"bd":{"labsGate":100}}},"lab":{"blackoutLink":"only"}}"""), "laboratory", 1, 2400, With(false), false, true, []);
        Assert.DoesNotContain(a.BossLocationSpawn, FactionRaid.IsBlackDiv);

        var b = Lab();
        var note = FactionRaid.Apply(b, Cfg("""{"factions":{"blackdiv":{"managed":true,"bd":{"wedgeChance":20}}},"lab":{"blackoutLink":"boost","blackoutMult":3}}"""), "laboratory", 1, 2400, With(true), false, true, []);
        Assert.Equal(60, b.BossLocationSpawn.Single(e => e.BossName == "bossWedge").BossChance);
        Assert.Contains("정전 판", note);

        var c = Lab();
        FactionRaid.Apply(c, Cfg("""{"factions":{"blackdiv":{"managed":true,"bd":{"wedgeChance":20}}},"lab":{"blackoutLink":"only"}}"""), "laboratory", 1, 2400, With(null), false, true, []);
        Assert.Equal(20, c.BossLocationSpawn.Single(e => e.BossName == "bossWedge").BossChance); // 모르면 "상관없이"
    }

    [Fact]
    public void ReadConfigs_Jsonc()
    {
        var ruaf = FactionData.ReadRuaf("""
            { // 주석
              "locations": {
                "Woods": { "patrol": { "patrolZones": ["ZoneHouse", "ZoneMiniHouse"], }, "checkpoint": { "checkpointZones": [ { "checkpointZone": "ZoneRoad", "x": 1 } ] },
                           "hunt": { "hunts": { "remnant": { "huntZones": "A" } } } },
                "lighthouse": { "patrol": { "patrolZones": [] }, "checkpoint": { "checkpointZones": [] }, "hunt": { "hunts": { "ruaf": { "huntZones": "Zone_DestroyedHouse" } } } }
              }
            }
            """);
        Assert.Equal(["ZoneHouse", "ZoneMiniHouse"], ruaf["woods"].PatrolZones);
        Assert.Equal(["ZoneRoad"], ruaf["woods"].CheckpointZones);
        Assert.Null(ruaf["woods"].HuntZones);
        Assert.Equal("Zone_DestroyedHouse", ruaf["lighthouse"].HuntZones);
        Assert.Equal(["bigmap", "labyrinth"], FactionData.ReadBlackDivMaps("""{"spawns":{"huntMaps":["bigmap","Labyrinth"]}}"""));
        Assert.Equal(100, FactionData.ReadBlackoutChance("""{"blackoutChance": 100}"""));
    }

    // ----- 다른 모드 PMC 구분 -----

    private static (LocationBase Loc, Dictionary<string, int> Spt) PmcLoc()
    {
        var spt = new Dictionary<string, int>();
        FactionData.AddPmcSigs(spt, JsonNode.Parse("""
            [ {"BossName":"pmcUSEC","BossZone":"ZoneA","BossChance":100,"BossEscortAmount":"0","Time":30,"TriggerId":""},
              {"BossName":"pmcUSEC","BossZone":"ZoneA","BossChance":100,"BossEscortAmount":"0","Time":30,"TriggerId":""},
              {"BossName":"bossKilla","BossZone":"ZoneA"} ]
            """));
        var loc = new LocationBase
        {
            Id = "bigmap", BossLocationSpawn =
            [
                new BossLocationSpawn { BossName = "pmcUSEC", BossZone = "ZoneA", BossChance = 100, BossEscortAmount = "0", Time = 5, TriggerId = "" },
                new BossLocationSpawn { BossName = "pmcUSEC", BossZone = "ZoneA", BossChance = 100, BossEscortAmount = "0", Time = 30, TriggerId = "" },
                new BossLocationSpawn { BossName = "pmcBEAR", BossZone = "ZoneModded", BossChance = 70, BossEscortAmount = "2", Time = 600, TriggerId = "" },
                new BossLocationSpawn { BossName = "bossKilla", BossZone = "ZoneA" }
            ]
        };
        return (loc, spt);
    }

    private static readonly MapInfo Info = new("bigmap", 10, 10, 2400, ["ZoneA"], ["ZoneS"]);
    private static readonly SpawnPlan Empty = new("bigmap", 2400, 0, 1, "waves", [], [], 20, 0);

    [Fact]
    public void OtherPmc_KeepByDefault_TimeIgnored()
    {
        var (loc, spt) = PmcLoc();
        Assert.Single(spt);
        var kept = RaidSpawnApplier.Apply(loc, Empty, Cfg("{}"), Info, false, spt);
        Assert.Equal(1, kept);
        Assert.Single(loc.BossLocationSpawn, e => MapCatalog.IsPmc(e));
        Assert.Equal("ZoneModded", loc.BossLocationSpawn.Single(e => MapCatalog.IsPmc(e)).BossZone);
    }

    [Fact]
    public void OtherPmc_Remove_Or_NoData_RemovesAll()
    {
        var (a, spt) = PmcLoc();
        Assert.Equal(0, RaidSpawnApplier.Apply(a, Empty, Cfg("""{"compat":{"otherPmc":"remove"}}"""), Info, false, spt));
        Assert.DoesNotContain(a.BossLocationSpawn, e => MapCatalog.IsPmc(e));
        var (b, _) = PmcLoc();
        Assert.Equal(0, RaidSpawnApplier.Apply(b, Empty, Cfg("{}"), Info));
        Assert.DoesNotContain(b.BossLocationSpawn, e => MapCatalog.IsPmc(e));
    }

    [Fact]
    public void Config_FactionDefaults()
    {
        var c = Cfg("{}");
        Assert.False(c.Ruaf!.Managed);
        Assert.Equal(2, c.Ruaf.Patrol.Count);
        Assert.Empty(c.Ruaf.Maps);
        Assert.Equal("boost", c.BlackDiv!.BlackoutLink);
        Assert.Equal(15, c.BlackDiv.WedgeChance);
        Assert.Equal("keep", c.OtherPmc);
    }

    [Fact]
    public void CapExclude_OnlyManagedAndNotSharing()
    {
        Assert.Null(FactionRaid.CapExcludeRoles(Cfg("{}"), true, true));
        var l = FactionRaid.CapExcludeRoles(Cfg("""{"factions":{"ruaf":{"managed":true},"blackdiv":{"managed":true,"shareCap":true}}}"""), true, true)!;
        Assert.Contains("ruafRifleman", l);
        Assert.Contains("848400", l);
        Assert.DoesNotContain("blackDivAssault", l);
        Assert.Null(FactionRaid.CapExcludeRoles(Cfg("""{"factions":{"ruaf":{"managed":true}}}"""), false, true));
        var plan = SpawnPlanner.Plan(Cfg("""{"factions":{"blackdiv":{"managed":true}}}"""), Info, 2400, 0, 1);
        var s = Gravity.Client.ClientSettingsBuilder.Build(Cfg("""{"factions":{"blackdiv":{"managed":true}}}"""), Info, plan, true, false, true);
        Assert.Contains("bossWedge", s.CapExclude!);
    }
}
