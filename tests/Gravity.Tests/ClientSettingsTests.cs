using System.Text.Json;
using System.Text.Json.Nodes;
using Gravity.Client;
using Gravity.Core;
using Gravity.Shared;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Xunit;
using Path = System.IO.Path;

namespace Gravity.Tests;

public class ClientSettingsTests
{
    private static readonly MapInfo Factory = new("factory4_day", 6, 10, 1200, ["BotZone"], ["BotZone"]);
    private static readonly MapInfo Big = new("bigmap", 16, 20, 2400, ["ZoneA", "ZoneB"], ["ZoneS1", "ZoneS2"]);
    private static readonly MapInfo Lab = new("labyrinth", 4, 6, 2400, ["L1"], ["L1"]);

    private static (GravityRuntime rt, List<string> log) Runtime(string state = "{}", bool abps = false, params MapInfo[] maps)
    {
        var dir = Path.Combine(Path.GetTempPath(), "gravity-client-" + Guid.NewGuid().ToString("N"));
        var store = new GravityConfigStore(Path.Combine(dir, "g.json"), Path.Combine(dir, "b"));
        store.Save(JsonNode.Parse(state)!);
        var cat = new MapCatalog();
        foreach (var m in maps.Length > 0 ? maps : [Big]) cat.AddForTest(m);
        var det = ModDetector.Detect(abps ? ["acidphantasm-botplacementsystem"] : [], []);
        var log = new List<string>();
        return (new GravityRuntime(store, cat, det, log.Add, log.Add) { MaxBotCap = new() }, log);
    }

    private static LocationBase Loc(string id, double minutes) => new()
    {
        Id = id, EscapeTimeLimit = minutes, BotMax = 20,
        BossLocationSpawn = [new BossLocationSpawn { BossName = "pmcUSEC", BossChance = 100, Time = 30 }],
        Waves =
        [
            new Wave { WildSpawnType = WildSpawnType.assault, SlotsMin = 1, SlotsMax = 2 },
            new Wave { WildSpawnType = WildSpawnType.marksman, SlotsMin = 1, SlotsMax = 1 }
        ]
    };

    private static void Hello(GravityRuntime rt, string session, bool abps = false) =>
        rt.OnHello(session, new HelloRequest(GravityVersion.Current, abps));

    [Fact]
    public void Build_Factory_PmcDistFromMap()
    {
        var cfg = ConfigModel.Default();
        var plan = SpawnPlanner.Plan(cfg, Factory, 1200, 0, 1);
        var s = ClientSettingsBuilder.Build(cfg, Factory, plan, true);
        Assert.Equal(40, s.PmcDist);
        Assert.Equal(30, s.ScavDist);
        Assert.Equal(14, s.Cap);
        Assert.Equal(80, s.SpacingPlayer);
        Assert.True(s.ProtectOn);
        Assert.Equal(90, s.ProtectSec);
        Assert.Null(s.Inf);
    }

    [Fact]
    public void Build_HardStopSec_FromPlan()
    {
        var cfg = ConfigModel.Default(); // hardStop percent, pmcPct 15, scavPct 5
        var plan = SpawnPlanner.Plan(cfg, Factory, 1200, 0, 1);
        var s = ClientSettingsBuilder.Build(cfg, Factory, plan, true);
        Assert.Equal(1200, s.RaidSec);
        Assert.Equal(1020, s.HardStopPmcSec);
        Assert.Equal(1140, s.HardStopScavSec);
    }

    [Fact]
    public void Build_InfiniteStyle_HasRefillGroups()
    {
        var cfg = ConfigModel.Default();
        var plan = SpawnPlanner.Plan(cfg, Lab, 2400, 0, 3);
        var s = ClientSettingsBuilder.Build(cfg, Lab, plan, true);
        Assert.NotNull(s.Inf);
        Assert.NotEmpty(s.RefillGroups);
        Assert.All(s.RefillGroups, g => Assert.InRange(g.Size, 1, 5));
        Assert.Equal((int)Math.Ceiling(2400 / 45.0), s.RefillGroups.Count);
        // 같은 시드 → 같은 목록
        var again = ClientSettingsBuilder.Build(cfg, Lab, plan, true);
        Assert.Equal(s.RefillGroups, again.RefillGroups);
    }

    [Fact]
    public void Build_RefillGroups_CappedAt200()
    {
        var cfg = ConfigModel.FromState(JsonNode.Parse("""{"maps":{"labyrinth":{"inf":{"delay":10}}}}"""), []);
        var plan = SpawnPlanner.Plan(cfg, Lab, 36000, 0, 3);
        Assert.Equal(200, ClientSettingsBuilder.Build(cfg, Lab, plan, true).RefillGroups.Count);
    }

    [Fact]
    public void Build_PercentsBecomeFractions_ZoneWeightsOnlyEnabled()
    {
        var cfg = ConfigModel.FromState(JsonNode.Parse(
            """{"spawn":{"hotspotShare":60,"scav":{"pscavChance":10}},"maps":{"bigmap":{"zones":{"ZoneA":{"on":true,"w":140},"ZoneB":{"on":false,"w":80}}}}}"""), []);
        var s = ClientSettingsBuilder.Build(cfg, Big, SpawnPlanner.Plan(cfg, Big, 2400, 0, 1), true);
        Assert.Equal(0.6, s.HotspotShare, 6);
        Assert.Equal(0.1, s.Scav.PscavChance, 6);
        Assert.Equal(140, s.ZoneWeights["ZoneA"]);
        Assert.False(s.ZoneWeights.ContainsKey("ZoneB"));
    }

    [Fact]
    public void Config_NewFields_DefaultsAndClamp()
    {
        var d = ConfigModel.Default();
        Assert.Equal(new ProtectSettings(true, 90, 120), d.Spawn.Protect);
        Assert.Equal(new SpacingSettings(80, 25), d.Spawn.Spacing);
        Assert.Equal("mixed", d.Spawn.Placement);
        Assert.Equal(120, d.MapFor("bigmap").PmcDist);
        Assert.Equal(80, d.MapFor("bigmap").ScavDist);
        Assert.Equal(40, d.MapFor("factory4_night").PmcDist);
        Assert.True(d.MapFor("bigmap").StartScav);
        Assert.Equal(3, d.Spawn.Scav.Pending);
        Assert.False(d.Near.On);
        Assert.Equal(200, d.Near.MinDist);

        var clamped = new List<string>();
        var c = ConfigModel.FromState(JsonNode.Parse(
            """{"spawn":{"protect":{"sec":99999,"radius":1},"scav":{"pending":99}},"maps":{"bigmap":{"pmcDist":5000}}}"""), clamped);
        Assert.Equal(3600, c.Spawn.Protect.Sec);
        Assert.Equal(5, c.Spawn.Protect.Radius);
        Assert.Equal(20, c.Spawn.Scav.Pending);
        Assert.Equal(1000, c.MapFor("bigmap").PmcDist);
        Assert.Contains("spawn.scav.pending", clamped);
    }

    [Fact]
    public void Apply_NoClient_ScavUntouched_FactoryPmcSkipped()
    {
        var (rt, log) = Runtime(maps: Factory);
        var loc = Loc("factory4_day", 20);
        var waves = loc.Waves.ToList();
        Assert.True(rt.TryApplyToRaid("factory4_day", loc, null, "s1", 5));
        Assert.DoesNotContain(loc.BossLocationSpawn, MapCatalog.IsPmc);
        Assert.Equal(waves, loc.Waves);
        Assert.Contains("클라 플러그인 없음", rt.Last!.Summary);
        Assert.Contains(log, l => l.Contains("클라 플러그인 없음"));
        Assert.False(rt.LastClientSettings.ContainsKey("factory4_day"));
    }

    [Fact]
    public void Apply_NoClient_Labyrinth_PmcSkipped()
    {
        var (rt, _) = Runtime(maps: Lab);
        var loc = Loc("labyrinth", 40);
        rt.TryApplyToRaid("labyrinth", loc, null, "s1", 5);
        Assert.DoesNotContain(loc.BossLocationSpawn, MapCatalog.IsPmc);
    }

    [Fact]
    public void Apply_WithClient_Factory_AddsGravityPmc_AndStoresSettings()
    {
        var (rt, _) = Runtime(maps: Factory);
        Hello(rt, "s1");
        var loc = Loc("factory4_day", 20);
        Assert.True(rt.TryApplyToRaid("factory4_day", loc, null, "s1", 5));
        Assert.Contains(loc.BossLocationSpawn, e => MapCatalog.IsPmc(e) && e.SptId == "gravity");
        var st = rt.ClientRaidFor("factory4_day");
        Assert.True(st.Managed);
        Assert.Equal(40, st.PmcDist);
        Assert.Equal(5, st.Seed);
    }

    [Fact]
    public void Apply_WithClient_Labyrinth_PmcOnlyWhenAllowed()
    {
        var (rt, _) = Runtime(maps: Lab);
        Hello(rt, "s1");
        var loc = Loc("labyrinth", 40);
        rt.TryApplyToRaid("labyrinth", loc, null, "s1", 5);
        Assert.DoesNotContain(loc.BossLocationSpawn, MapCatalog.IsPmc);

        var (rt2, _) = Runtime("""{"spawn":{"allowPmcLabyrinth":true}}""", maps: Lab);
        Hello(rt2, "s1");
        var loc2 = Loc("labyrinth", 40);
        rt2.TryApplyToRaid("labyrinth", loc2, null, "s1", 5);
        Assert.Contains(loc2.BossLocationSpawn, e => MapCatalog.IsPmc(e) && e.SptId == "gravity");
    }

    [Fact]
    public void Apply_WithClient_StartScavWavesReplaceAssault()
    {
        var (rt, _) = Runtime();
        Hello(rt, "s1");
        var loc = Loc("bigmap", 40);
        Assert.True(rt.TryApplyToRaid("bigmap", loc, null, "s1", 5));
        Assert.DoesNotContain(loc.Waves, w => w.WildSpawnType == WildSpawnType.assault && w.SptId == null);
        var mine = loc.Waves.Where(w => w.WildSpawnType == WildSpawnType.assault && w.SptId == "gravity").ToList();
        Assert.NotEmpty(mine);
        Assert.All(rt.Last!.Plan!.ScavWaves, w => Assert.True(w.IsStart));
        Assert.Equal(rt.Last.Plan.ScavWaves.Count, mine.Count);
        Assert.Contains(loc.Waves, w => w.WildSpawnType == WildSpawnType.marksman);
    }

    [Fact]
    public void Apply_WithClient_StartMarksmanOff_RemovesMarksman()
    {
        var (rt, _) = Runtime("""{"spawn":{"scav":{"startMarksman":false}}}""");
        Hello(rt, "s1");
        var loc = Loc("bigmap", 40);
        rt.TryApplyToRaid("bigmap", loc, null, "s1", 5);
        Assert.DoesNotContain(loc.Waves, w => w.WildSpawnType == WildSpawnType.marksman);
    }

    [Fact]
    public void Apply_WithClient_StartScavOff_NoScavWaves()
    {
        var (rt, _) = Runtime("""{"maps":{"bigmap":{"start":{"scav":false}}}}""");
        Hello(rt, "s1");
        var loc = Loc("bigmap", 40);
        rt.TryApplyToRaid("bigmap", loc, null, "s1", 5);
        Assert.DoesNotContain(loc.Waves, w => w.WildSpawnType == WildSpawnType.assault);
    }

    [Fact]
    public void Apply_OtherSessionWithoutHello_IsNoClient()
    {
        var (rt, _) = Runtime();
        Hello(rt, "s1");
        var loc = Loc("bigmap", 40);
        rt.TryApplyToRaid("bigmap", loc, null, "other", 5);
        Assert.Contains(loc.Waves, w => w.WildSpawnType == WildSpawnType.assault && w.SptId == null);
    }

    [Fact]
    public void Apply_WithAbpsClient_Locked()
    {
        var (rt, _) = Runtime();
        Hello(rt, "s1", abps: true);
        var loc = Loc("bigmap", 40);
        Assert.False(rt.TryApplyToRaid("bigmap", loc, null, "s1", 5));
        Assert.Equal(2, loc.Waves.Count);
        Assert.All(loc.Waves, w => Assert.Null(w.SptId));
        Assert.Contains(loc.BossLocationSpawn, e => MapCatalog.IsPmc(e) && e.SptId == null);
        Assert.False(rt.ClientRaidFor("bigmap").Managed);
    }

    [Fact]
    public void Hello_ReportsCompatibilityAndLock()
    {
        var (rt, _) = Runtime();
        var ok = rt.OnHello("s1", new HelloRequest("0.2.7", false));
        Assert.True(ok.Compatible);
        Assert.False(ok.SpawnLocked);
        Assert.Equal("0.2.7", rt.Sessions.LastVersion);
        var bad = rt.OnHello("s2", new HelloRequest("0.1.0", false));
        Assert.False(bad.Compatible);
        Assert.True(rt.OnHello("s3", new HelloRequest("0.2.0", true)).SpawnLocked);
    }

    [Fact]
    public void ClientRaidFor_UnknownMap_Unmanaged()
    {
        var (rt, _) = Runtime();
        Assert.False(rt.ClientRaidFor("bigmap").Managed);
        Assert.False(rt.ClientRaidFor("nope").Managed);
    }

    [Fact]
    public void Report_IsStored()
    {
        var (rt, log) = Runtime();
        rt.OnReport(new RaidReport("bigmap", 5, new Dictionary<string, int> { ["pmcUSEC"] = 3 }, new Dictionary<string, int>(), 2, [new[] { 1, 2, 3, 5 }]));
        Assert.Equal("bigmap", rt.LastReport!.MapId);
        Assert.Contains(log, l => l.Contains("클라 보고"));
    }

    [Fact]
    public void Wire_IsCamelCase()
    {
        var cfg = ConfigModel.Default();
        var s = ClientSettingsBuilder.Build(cfg, Big, SpawnPlanner.Plan(cfg, Big, 2400, 0, 1), true);
        var json = ClientRoutes.ToWire(s)!.ToJsonString();
        Assert.Contains("\"mapId\":\"bigmap\"", json);
        Assert.Contains("\"hardStopPmcSec\"", json);
        Assert.DoesNotContain("\"MapId\"", json);
        var back = JsonSerializer.Deserialize<ClientRaidSettings>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(s.Cap, back.Cap);
    }

    [Fact]
    public void RequestData_ParsesCamelCase()
    {
        var o = JsonSerializer.Deserialize<HelloRequestData>("""{"clientVersion":"0.2.0","abpsClient":true}""")!;
        Assert.Equal("0.2.0", o.ClientVersion);
        Assert.True(o.AbpsClient);
        var r = JsonSerializer.Deserialize<ReportRequestData>("""{"mapId":"bigmap","seed":4,"spawned":{"near":2},"dropped":{},"sanitized":1,"alivePerMinute":[[0,1,2,3]]}""")!;
        Assert.Equal(2, r.ToReport().Spawned["near"]);
    }
}
