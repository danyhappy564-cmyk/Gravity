using System.Text.Json.Nodes;
using System.Text.Json;
using Gravity.Client;
using Gravity.Core;
using Gravity.Spawn;
using Gravity.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Xunit;
using Path = System.IO.Path;

namespace Gravity.Tests;

public class CardOverrideTests
{
    private static readonly MapInfo Map = new("bigmap", 16, 20, 2400, ["A"], ["A"]);
    private static GravityConfig Read(string json) => ConfigModel.FromState(JsonNode.Parse(json), []);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EachCard_SelectsCommonOrMapValues(bool enabled)
    {
        var state = JsonNode.Parse("""
            {"spawn":{"startPmcPct":25,"scav":{"refill":50},"protect":{"sec":100},"hotspotShare":60,
              "groupCap":{"on":false}},"near":{"on":false,"interval":180},
             "maps":{"bigmap":{"startPmcPct":75,"scav":{"refill":90},"protect":{"sec":200},
              "hotspotShare":10,"groupCap":{"on":true},"near":{"on":true,"interval":300},
              "ovr":{}}}}
            """)!;
        foreach (var card in new[] { "flow", "scav", "near", "dist", "hotspot", "limits" })
            state["maps"]!["bigmap"]!["ovr"]![card] = enabled;
        var cfg = ConfigModel.FromState(state, []);
        var s = cfg.SpawnFor("bigmap");
        Assert.Equal(enabled ? 75 : 25, s.StartPmcPct);
        Assert.Equal(enabled ? 90 : 50, s.Scav.Refill);
        Assert.Equal(enabled ? 200 : 100, s.Protect.Sec);
        Assert.Equal(enabled ? 10 : 60, s.HotspotShare);
        Assert.Equal(enabled, s.GroupCap.On);
        Assert.Equal(enabled, cfg.NearFor("bigmap").On);
        Assert.Equal(enabled ? 300 : 180, cfg.NearFor("bigmap").Interval);
    }

    [Fact]
    public void LegacyFlowAndIntensityAliases_KeepWorking_ExplicitFalseWins()
    {
        var cfg = Read("""{"spawn":{"interval":9},"maps":{"bigmap":{"override":true,"interval":12,"early":2},"woods":{"override":true,"interval":15,"ovr":{"flow":false}}}}""");
        Assert.True(cfg.MapFor("bigmap").Override);
        Assert.Equal(12, cfg.SpawnFor("bigmap").IntervalMin);
        Assert.Equal(2, cfg.SpawnFor("bigmap").Intensity.Early);
        Assert.Equal(9, cfg.SpawnFor("woods").IntervalMin);
        cfg = Read("""{"maps":{"bigmap":{"ovr":{"flow":true},"intensity":{"early":2.5}}}}""");
        Assert.Equal(2.5, cfg.SpawnFor("bigmap").Intensity.Early);
    }

    [Fact]
    public void OneCard_DoesNotFreezeOtherCardsOrMaps()
    {
        var cfg = Read("""{"spawn":{"interval":9,"scav":{"refill":70}},"maps":{"bigmap":{"ovr":{"scav":true},"interval":15,"scav":{"refill":120}}}}""");
        Assert.Equal(9, cfg.SpawnFor("bigmap").IntervalMin);
        Assert.Equal(120, cfg.SpawnFor("bigmap").Scav.Refill);
        Assert.Equal(70, cfg.SpawnFor("woods").Scav.Refill);
        Assert.Equal(70, cfg.Spawn.Scav.Refill);
    }

    [Fact]
    public void PlannerAndClient_UseTheSameMapValues()
    {
        var cfg = Read("""
            {"maps":{"bigmap":{"ovr":{"flow":true,"scav":true,"near":true,"dist":true,"hotspot":true,"limits":true},
              "startPmcPct":100,"waveMin":0,"waveMax":0,"scav":{"startPct":50,"refill":123,"softCap":7,"perPlayer":9},
              "hardStop":{"pmcPct":40,"scavPct":30},"protect":{"sec":222},"pmcDist":66,"scavDist":44,
              "hotspotShare":25,"near":{"on":true,"interval":333},"groupCap":{"on":true,"scope":"alive"}}}}
            """);
        var plan = SpawnPlanner.Plan(cfg, Map, 2400, 0, 7);
        var dto = ClientSettingsBuilder.Build(cfg, Map, plan, true);
        Assert.Equal(16, plan.StartPmcs);
        Assert.Equal(10, plan.ScavWaves.Where(w => w.IsStart).Sum(w => w.SlotsMax));
        Assert.Equal(1440, dto.HardStopPmcSec);
        Assert.Equal(1680, dto.HardStopScavSec);
        Assert.Equal(123, dto.Scav.RefillSec);
        Assert.Equal(7, dto.Scav.SoftCap);
        Assert.Equal(222, dto.ProtectSec);
        Assert.Equal(66, dto.PmcDist);
        Assert.Equal(44, dto.ScavDist);
        Assert.Equal(.25, dto.HotspotShare);
        Assert.Equal(333, dto.Near.Interval);
        Assert.True(dto.GroupCap.On);
    }

    [Fact]
    public void DistanceLegacyValues_AndNewDisabledCard()
    {
        var cfg = Read("""{"spawn":{"pmcDist":150,"scavDist":95},"maps":{"bigmap":{"pmcDist":55,"scavDist":35},"woods":{"pmcDist":55,"ovr":{"dist":false}}}}""");
        Assert.Equal(55, cfg.MapFor("bigmap").PmcDist);
        Assert.Equal(35, cfg.MapFor("bigmap").ScavDist);
        Assert.Equal(150, cfg.MapFor("woods").PmcDist);
        Assert.Equal(40, ConfigModel.Default().MapFor("factory4_day").PmcDist);
    }

    [Fact]
    public void PartialMapCards_FallBackToCommon_AndClampMapValues()
    {
        var clamped = new List<string>();
        var cfg = ConfigModel.FromState(JsonNode.Parse("""
            {"spawn":{"scav":{"softCap":11},"groupCap":{"rules":[{"size":2,"count":4}]},"protect":{"sec":180}},
             "maps":{"bigmap":{"ovr":{"scav":true,"limits":true,"dist":true},"scav":{"refill":99999},"groupCap":{"on":true}}}}
            """), clamped);
        var s = cfg.SpawnFor("bigmap");
        Assert.Equal(11, s.Scav.SoftCap);
        Assert.Equal(3600, s.Scav.Refill);
        Assert.Equal(180, s.Protect.Sec);
        Assert.Equal(2, Assert.Single(s.GroupCap.Rules).Size);
        Assert.Contains("maps.bigmap.scav.refill", clamped);
    }

    [Fact]
    public void SeparateCardStops_DoNotOverwriteEachOther_AndNearRangesClamp()
    {
        var cfg = Read("""
            {"spawn":{"hardStop":{"mode":"time","pmcPct":20,"scavPct":5},"stopBeforeEnd":8},
             "maps":{"bigmap":{"ovr":{"scav":true,"near":true},"hardStop":{"mode":"percent","pmcPct":90,"scavPct":40},
              "near":{"minDist":400,"maxDist":100,"waveMin":9,"waveMax":2}}}}
            """);
        var s = cfg.SpawnFor("bigmap");
        Assert.Equal("time", s.HardStop.Mode);
        Assert.Equal(20, s.HardStop.PmcPct);
        Assert.Equal(8, s.HardStop.BeforeEndMin);
        Assert.Equal(40, s.HardStop.ScavPct);
        Assert.Equal(400, cfg.NearFor("bigmap").MaxDist);
        Assert.Equal(9, cfg.NearFor("bigmap").WaveMax);
    }

    [Fact]
    public void CommonOnlyFields_IgnoreMapValues()
    {
        var cfg = Read("""
            {"spawn":{"usecPct":25,"squad":{"s1":100,"s2":0,"s3":0,"s4":0},"diff":{"easy":100}},
             "maps":{"bigmap":{"ovr":{"flow":true,"scav":true,"common":true},"usecPct":90,"squad":{"s1":0},"diff":{"easy":0}}}}
            """);
        var s = cfg.SpawnFor("bigmap");
        Assert.Equal(25, s.UsecPct);
        Assert.Equal(100, s.Squad[0]);
        Assert.Equal(100, s.Diff[0]);
    }

    [Fact]
    public void InitialScavs_CanBeDisabledOrZero_AndInfinitePmcKeepsTheInitialShare()
    {
        foreach (var fragment in new[] { "\"startOn\":false", "\"startPct\":0" })
        {
            var raw = JsonNode.Parse("""{"maps":{"bigmap":{"ovr":{"scav":true},"scavStyle":"start"}}}""")!;
            raw["maps"]!["bigmap"]!["scav"] = JsonNode.Parse("{" + fragment + "}");
            var cfg = ConfigModel.FromState(raw, []);
            Assert.Empty(SpawnPlanner.Plan(cfg, Map, 2400, 0, 7).ScavWaves);
        }
        var infinite = Read("""{"maps":{"bigmap":{"style":"infinite","ovr":{"scav":true},"scav":{"startPct":50}}}}""");
        Assert.Equal(10, SpawnPlanner.Plan(infinite, Map, 2400, 0, 7).ScavWaves.Where(w => w.IsStart).Sum(w => w.SlotsMax));
    }

    [Theory]
    [InlineData("waves", 17)]
    [InlineData("start", 10)]
    public void SimulationAndPreview_UseMapCardValues(string style, int expectedScavs)
    {
        var dir = Path.Combine(Path.GetTempPath(), "gravity-cards-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new GravityConfigStore(Path.Combine(dir, "state.json"), Path.Combine(dir, "backup"));
            var raw = JsonNode.Parse("""
                {"spawn":{"startPmcPct":0},"maps":{"bigmap":{"ovr":{"flow":true,"scav":true},
                 "startPmcPct":100,"waveMin":0,"waveMax":0,"scav":{"startPct":50,"softCap":7}}}}
                """)!;
            raw["maps"]!["bigmap"]!["scavStyle"] = style;
            Assert.True(store.Save(raw).Ok);
            var catalog = new MapCatalog();
            catalog.AddForTest(Map);
            var rt = new GravityRuntime(store, catalog, ModDetector.Detect([], []), _ => { }, _ => { });
            rt.CloneBase = _ => new LocationBase { Id = "bigmap", EscapeTimeLimit = 40, BossLocationSpawn = [], Waves = [] };
            rt.OnHello("cards", new HelloRequest(GravityVersion.Current, false));
            var sim = rt.Simulate("bigmap", 2, 7)!;
            Assert.True(sim.Client);
            Assert.All(sim.Rows, row => { Assert.Equal(16, row.Pmc); Assert.Equal(expectedScavs, row.Scav); });
            var preview = JsonSerializer.SerializeToNode(rt.PlanPreview("bigmap", 7))!;
            Assert.Equal(16, preview["pmc"]!.AsArray().Sum(g => g!["Size"]!.GetValue<int>()));
            Assert.Equal(10, preview["scavWaves"]!.AsArray().Sum(w => w!["SlotsMax"]!.GetValue<int>()));
            Assert.Null(rt.Last);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public void 맵별_시작_PMC_인원_범위와_플레이어_지점_스위치()
    {
        var cfg = Read("""{"spawn":{"startPmcPct":35},"maps":{"bigmap":{"ovr":{"flow":true},"startMode":"count","startPmcMin":9,"startPmcMax":12,"startPlayerOnly":true}}}""");
        Assert.Equal("count", cfg.SpawnFor("bigmap").StartMode);
        Assert.False(cfg.SpawnFor("woods").StartPlayerOnly);
        var seen = new HashSet<int>();
        for (int seed = 0; seed < 60; seed++)
        {
            var plan = SpawnPlanner.Plan(cfg, Map, 2400, 0, seed);
            Assert.InRange(plan.StartPmcs, 9, 12);
            seen.Add(plan.StartPmcs);
            var cs = ClientSettingsBuilder.Build(cfg, Map, plan, true);
            Assert.True(cs.StartPlayerOnly);
            Assert.Equal(plan.Groups.Select((g, k) => (g, k)).Where(x => x.g.IsStart).Select(x => x.k), cs.StartGroups!);
        }
        Assert.True(seen.Count > 1);
        // 공통 탭은 그대로 비율(바닐라 16명 × 35% ≈ 6)
        Assert.Equal(6, SpawnPlanner.Plan(Read("{}"), Map, 2400, 0, 1).StartPmcs);
    }
}
