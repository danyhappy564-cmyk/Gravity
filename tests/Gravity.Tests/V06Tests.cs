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

/// <summary>0.6: 맵 변형·ORBIT 연동·프리셋 저장소·진단.</summary>
public class V06Tests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "gravity-06-" + Guid.NewGuid().ToString("N"));

    private static LocationBase Loc(string id = "interchange") => new()
    {
        Id = id, EscapeTimeLimit = 40, BotMax = 20,
        BossLocationSpawn = [new BossLocationSpawn { BossName = "pmcUSEC", BossChance = 100, Time = 30 }, new BossLocationSpawn { BossName = "bossKilla", BossChance = 100 }],
        Waves = [new Wave { WildSpawnType = WildSpawnType.assault, SlotsMin = 1, SlotsMax = 2, SpawnPoints = "ZoneOld" }]
    };

    private static (GravityRuntime rt, GravityConfigStore store) Runtime(string state = "{}", string[]? folders = null, string id = "interchange")
    {
        var dir = TempDir();
        var store = new GravityConfigStore(Path.Combine(dir, "g.json"), Path.Combine(dir, "b"));
        store.Save(JsonNode.Parse(state)!);
        var cat = new MapCatalog();
        cat.AddForTest(new MapInfo(id, 15, 20, 2400, ["ZoneA", "ZoneB"], ["ZoneS"]));
        var det = ModDetector.Detect([], folders ?? []);
        return (new GravityRuntime(store, cat, det, _ => { }, _ => { }) { MaxBotCap = new() }, store);
    }

    // ---------- 설정 ----------

    [Fact]
    public void Config_Defaults_OrbitDiagVariant()
    {
        var c = ConfigModel.Default();
        Assert.Equal(new OrbitSettings(true, false, true), c.OrbitOrDefault);
        Assert.Equal(new DiagSettings("summary", true, false), c.DiagOrDefault);
        Assert.True(c.MapFor("interchange").Variant);
        Assert.False(c.MapFor("lighthouse").Variant); // 웹 기본값과 같음: 등대 변형은 끔
    }

    [Fact]
    public void Config_ReadsOrbitDiagVariant()
    {
        var c = ConfigModel.FromState(JsonNode.Parse("""
            {"orbit":{"refillAfterExtract":false,"nearAsGhost":true,"respectTakeover":false},
             "diag":{"log":"verbose","report":false,"botLog":true},
             "maps":{"lighthouse":{"variant":true},"interchange":{"variant":false}}}
            """), []);
        Assert.Equal(new OrbitSettings(false, true, false), c.OrbitOrDefault);
        Assert.Equal(new DiagSettings("verbose", false, true), c.DiagOrDefault);
        Assert.True(c.MapFor("lighthouse").Variant);
        Assert.False(c.MapFor("interchange").Variant);
    }

    [Fact]
    public void Config_BadLogLevel_FallsBack()
    {
        var c = ConfigModel.FromState(JsonNode.Parse("""{"diag":{"log":"loud"}}"""), []);
        Assert.Equal("summary", c.DiagOrDefault.Log);
    }

    // ---------- 감지 ----------

    [Fact]
    public void Detect_VariantMods_MapToIds()
    {
        var d = ModDetector.Detect([], ["ManimalInterchange", "ManimalLighthouse", "LennoxP90-FactoryClassic", "LennoxP90-MapVariants", "ORBIT", "Blackout"]);
        Assert.Equal(["Manimal"], d.VariantMaps["interchange"]);
        Assert.Equal(["Manimal"], d.VariantMaps["lighthouse"]);
        Assert.Equal(["FactoryClassic"], d.VariantMaps["factory4_day"]);
        Assert.Equal(["FactoryClassic"], d.VariantMaps["factory4_night"]);
        Assert.False(d.VariantMaps.ContainsKey("bigmap"));
        Assert.True(d.MapVariants);
        Assert.True(d.Blackout);
        Assert.Equal("ORBIT", d.OrbitFolder);
    }

    [Fact]
    public void Detect_QuestingBots_WarnsByDefault()
    {
        var d = ModDetector.Detect([], ["SPT-QuestingBots"]);
        Assert.False(d.SpawnLocked);
        Assert.Null(d.SpawnLockReason);
        Assert.Null(ModDetector.Detect([], []).SpawnLockReason);
    }

    // ---------- ORBIT ----------

    private const string OrbitJson = """
        {"ai_limiter":{"enabled":true,"sleep_distance":260,"wake_distance":210,"min_awake_bots":6},
         "loot":{"extract_pmc":true},"player_scav":{"enabled":false},
         "factions":{"take_over_ruaf":true,"take_over_black_division":false,"take_over_isb":true,"vanilla_raiders":true,"vanilla_scavs":false}}
        """;

    [Fact]
    public void Orbit_Parse_ReadsValues()
    {
        var o = OrbitInfo.Parse(true, "3.1.0", OrbitJson);
        Assert.True(o.ConfigRead);
        Assert.Equal(210, o.WakeDistance);
        Assert.Equal(260, o.SleepDistance);
        Assert.False(o.PlayerScav);
        Assert.True(o.TakesOver("ruaf"));
        Assert.False(o.TakesOver("blackdiv"));
        Assert.Contains("isb", o.FactionTakeover);
        // vanilla_* = true면 바닐라 유지: 설정 scavs=false·raiders=true, 나머지는 ORBIT 기본(goons·cultists·bloodhounds false, rogues true)
        Assert.Equal(["scavs", "goons", "cultists", "bloodhounds"], o.VanillaTakeover);
        Assert.True(o.FullSleep);
        Assert.Equal(0, o.EffectiveMinAwake);
    }

    [Fact]
    public void Orbit_Parse_BrokenOrMissing_UsesDefaults()
    {
        Assert.Same(OrbitInfo.None, OrbitInfo.Parse(false, null, OrbitJson));
        var o = OrbitInfo.Parse(true, null, "{not json");
        Assert.False(o.ConfigRead);
        Assert.Equal(200, o.WakeDistance);
        Assert.True(o.Limiter);
    }

    [Fact]
    public void Orbit_NearRange_OnlyWhenGhostOn()
    {
        var o = OrbitInfo.Parse(true, null, OrbitJson);
        Assert.Equal((200d, 300d), o.NearRange(200, 300, false));
        Assert.Equal(270, o.GhostDistance); // max(잠드는 260, 깨우는 210) + 10
        Assert.Equal((270d, 310d), o.NearRange(200, 300, true));
        Assert.Equal((270d, 310d), o.NearRange(100, 150, true));
        Assert.Equal((280d, 400d), o.NearRange(280, 400, true));
        Assert.Equal((100d, 150d), OrbitInfo.None.NearRange(100, 150, true));
        var off = OrbitInfo.Parse(true, null, """{"ai_limiter":{"enabled":false}}""");
        Assert.Equal((100d, 150d), off.NearRange(100, 150, true));
    }

    [Fact]
    public void ClientSettings_CarryOrbitNearAndFlags()
    {
        var cfg = ConfigModel.FromState(JsonNode.Parse("""{"orbit":{"nearAsGhost":true,"refillAfterExtract":false},"diag":{"log":"off"},"near":{"minDist":100,"maxDist":150}}"""), []);
        var info = new MapInfo("bigmap", 16, 20, 2400, ["ZoneA"], ["ZoneS"]);
        var plan = SpawnPlanner.Plan(cfg, info, 2400, 0, 3);
        var s = ClientSettingsBuilder.Build(cfg, info, plan, true, orbit: OrbitInfo.Parse(true, null, OrbitJson));
        Assert.Equal(270, s.Near.MinDist);
        Assert.Equal(310, s.Near.MaxDist);
        Assert.False(s.RefillAfterExtract);
        Assert.Equal("off", s.LogLevel);
        var plain = ClientSettingsBuilder.Build(cfg, info, plan, true);
        Assert.Equal(100, plain.Near.MinDist);
        // 좁은 맵은 ORBIT 수면 거리 보정을 하지 않는다(공장 판 근처 보충이 전부 지점 없음으로 버려짐)
        var fac = new MapInfo("factory4_day", 6, 10, 1200, ["BotZone"], ["BotZone"]);
        var fs = ClientSettingsBuilder.Build(cfg, fac, SpawnPlanner.Plan(cfg, fac, 1200, 0, 3), true, orbit: OrbitInfo.Parse(true, null, OrbitJson));
        Assert.Equal(100, fs.Near.MinDist);
    }

    [Fact]
    public void EffectiveConfig_WebManagementWinsOverTakeover()
    {
        var (rt, store) = Runtime("""{"factions":{"ruaf":{"managed":true},"blackdiv":{"managed":true}},"orbit":{"respectTakeover":false}}""");
        rt.Orbit = OrbitInfo.Parse(true, null, OrbitJson);
        var c = rt.EffectiveConfig(store.Current);
        Assert.True(c.Ruaf!.Managed);
        Assert.True(c.BlackDiv!.Managed); // ORBIT이 블랙 디비전은 장악하지 않음
        store.Save(JsonNode.Parse("""{"factions":{"ruaf":{"managed":true}},"orbit":{"respectTakeover":true}}""")!);
        Assert.True(rt.EffectiveConfig(store.Current).Ruaf!.Managed);
    }

    // ---------- 맵 변형 ----------

    [Fact]
    public void Variant_SameBaseAsLoad_IsVariant_OtherBaseIsOriginal()
    {
        var (rt, _) = Runtime(folders: ["ManimalInterchange"]);
        var loaded = Loc();
        rt.Catalog.SetLoadedForTest("interchange", loaded);
        Assert.True(rt.IsVariant("interchange", loaded));
        Assert.False(rt.IsVariant("interchange", Loc()));
        // 변형 모드가 없는 맵은 늘 원본
        rt.Catalog.SetLoadedForTest("bigmap", loaded);
        Assert.False(rt.IsVariant("bigmap", loaded));
    }

    [Fact]
    public void Variant_Off_SkipsRaid_OriginalStillApplies()
    {
        var (rt, _) = Runtime("""{"maps":{"interchange":{"variant":false}}}""", ["ManimalInterchange"]);
        var loaded = Loc();
        rt.Catalog.SetLoadedForTest("interchange", loaded);
        LocationBase current = loaded;
        rt.CurrentBase = _ => current;
        var raid = Loc();
        Assert.False(rt.TryApplyToRaid("interchange", raid, null, null, 4));
        Assert.Contains("변형 맵", rt.Last!.Summary);
        Assert.Equal("variant", rt.LastLayout["interchange"]);
        Assert.Contains(raid.BossLocationSpawn, e => e.BossName == "pmcUSEC" && e.SptId != "gravity"); // 손대지 않음

        current = Loc(); // MapVariants가 원본을 고름 = 다른 객체
        current.SpawnPointParams = [new SpawnPointParam { BotZoneName = "ZoneOrig", Categories = ["BotPmc"] }];
        var raid2 = Loc();
        Assert.True(rt.TryApplyToRaid("interchange", raid2, null, null, 4));
        Assert.Equal("original", rt.LastLayout["interchange"]);
    }

    [Fact]
    public void Variant_OriginalOff_SkipsOriginalOnly()
    {
        var (rt, _) = Runtime("""{"maps":{"interchange":{"original":false}}}""", ["ManimalInterchange"]);
        var loaded = Loc();
        rt.Catalog.SetLoadedForTest("interchange", loaded);
        rt.CurrentBase = _ => loaded;
        Assert.True(rt.TryApplyToRaid("interchange", Loc(), null, null, 4)); // 변형 판은 적용
        rt.CurrentBase = _ => Loc();
        Assert.False(rt.TryApplyToRaid("interchange", Loc(), null, null, 4)); // 원본 판은 건너뜀
    }

    [Fact]
    public void InfoFor_RecomputesZonesForOtherBase_AndCaches()
    {
        var (rt, _) = Runtime();
        var loaded = Loc();
        rt.Catalog.SetLoadedForTest("interchange", loaded);
        Assert.Equal(["ZoneA", "ZoneB"], rt.Catalog.InfoFor("interchange", loaded)!.PmcZones);
        var other = Loc();
        other.SpawnPointParams =
        [
            new SpawnPointParam { BotZoneName = "ZoneP", Categories = ["BotPmc"] },
            new SpawnPointParam { BotZoneName = "ZoneNew", Categories = ["Bot"], Sides = ["Savage"] }
        ];
        other.OpenZones = "ZoneNew,ZoneX";
        var i1 = rt.Catalog.InfoFor("interchange", other)!;
        Assert.Equal(["ZoneP"], i1.PmcZones);
        Assert.Equal(["ZoneNew"], i1.ScavZones); // 웨이브 구역 ZoneOld는 지점이 없어 OpenZones ∩ 지점으로
        Assert.Same(i1, rt.Catalog.InfoFor("interchange", other));
        Assert.Null(rt.Catalog.InfoFor("woods", other));
    }

    [Fact]
    public void ScavZonesOf_KeepsWaveZonesWithPoints()
    {
        var b = new LocationBase
        {
            Id = "x",
            SpawnPointParams =
            [
                new SpawnPointParam { BotZoneName = "Z1", Categories = ["Bot"], Sides = ["Savage"] },
                new SpawnPointParam { BotZoneName = "Z2", Categories = ["Bot"], Sides = ["All"] },
                new SpawnPointParam { BotZoneName = "Z3", Categories = ["Player"], Sides = ["Pmc"] }
            ]
        };
        Assert.Equal(["Z1", "Z2"], MapCatalog.ScavZonesOf(b, ["Z1", "Z2", "Z3"]));
        Assert.Equal(["Z9"], MapCatalog.ScavZonesOf(new LocationBase { Id = "x" }, ["Z9"])); // 지점 정보 없음 = 그대로
        Assert.Equal(["Z3"], MapCatalog.ScavZonesOf(b, ["Z3"])); // 겹침 없고 OpenZones 없음 = 그대로
    }

    [Fact]
    public void Variant_InstalledButInactive_IsOriginal()
    {
        var (rt, _) = Runtime(folders: ["ManimalLighthouse"], id: "lighthouse");
        var loaded = Loc("lighthouse");
        rt.Catalog.SetLoadedForTest("lighthouse", loaded);
        Assert.True(rt.IsVariant("lighthouse", loaded));
        rt.ActiveVariants = []; // 서버 시작 때 SPT 원본과 같은 배치였음
        Assert.False(rt.IsVariant("lighthouse", loaded));
    }

    [Fact]
    public void SameSpawnPoints_ComparesIds()
    {
        var b = new LocationBase { Id = "x", SpawnPointParams = [new SpawnPointParam { Id = "a" }, new SpawnPointParam { Id = "b" }] };
        Assert.True(MapCatalog.SameSpawnPoints(b, """{"SpawnPointParams":[{"Id":"b"},{"Id":"a"}]}"""));
        Assert.False(MapCatalog.SameSpawnPoints(b, """{"SpawnPointParams":[{"Id":"a"},{"Id":"c"}]}"""));
        Assert.Null(MapCatalog.SameSpawnPoints(b, null));
        Assert.Null(MapCatalog.SameSpawnPoints(b, "{bad"));
    }

    [Fact]
    public void SkipReason_SharedBySimulate()
    {
        var (rt, store) = Runtime("""{"maps":{"interchange":{"variant":false}}}""", ["ManimalInterchange"]);
        var loaded = Loc();
        rt.Catalog.SetLoadedForTest("interchange", loaded);
        rt.CurrentBase = _ => loaded;
        rt.CloneBase = _ => Loc();
        Assert.Contains("변형 맵", rt.SkipReason(store.Current, "interchange", loaded));
        Assert.Contains("변형 맵", rt.Simulate("interchange", 1, 1)!.Skip);
        Assert.Equal("스폰 모듈 꺼짐", rt.SkipReason(ConfigModel.FromState(JsonNode.Parse("""{"modules":{"spawn":false}}"""), []), "interchange", loaded));
    }

    // ---------- 시뮬레이션 ----------

    [Fact]
    public void Simulate_UsesClone_DoesNotTouchState()
    {
        var (rt, _) = Runtime("""{"bosses":{"killa":{"chance":100,"maps":{"interchange":true}}}}""");
        rt.CloneBase = _ => Loc();
        var sim = rt.Simulate("interchange", 5, 7)!;
        Assert.Null(sim.Skip);
        Assert.False(sim.Client);
        var rows = sim.Rows;
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.True(r.Pmc > 0));
        Assert.All(rows, r => Assert.Contains("killa", r.Bosses));
        Assert.Null(rt.Last); // 레이드 기록 안 바뀜
        Assert.Null(rt.Simulate("nomap", 3, 1));
        var plan = rt.PlanPreview("interchange", 9);
        Assert.NotNull(plan);
        Assert.Null(rt.Last);
    }

    // ---------- 프리셋 저장소 ----------

    [Fact]
    public void Library_SaveLoad_RoundTrip_AndBackups()
    {
        var dir = TempDir();
        var lib = new PresetLibrary(Path.Combine(dir, "presets.json"), Path.Combine(dir, "backup"));
        lib.Load();
        Assert.Empty(lib.Current["raid"]!.AsArray());
        for (int i = 0; i < 7; i++)
            Assert.True(lib.Save(JsonNode.Parse($$$"""{"raid":[{"id":"p{{{i}}}","name":"내 프리셋","snap":{}}],"gear":[],"spawn":[{"id":"sp1","kind":"map","data":{}}]}""")).Ok);
        var again = new PresetLibrary(Path.Combine(dir, "presets.json"), Path.Combine(dir, "backup"));
        again.Load();
        Assert.Equal("p6", again.Current["raid"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("내 프리셋", again.Current["raid"]![0]!["name"]!.GetValue<string>());
        Assert.Single(again.Current["spawn"]!.AsArray());
        Assert.Equal(5, Directory.GetFiles(Path.Combine(dir, "backup"), "presets-2*.json").Length);
    }

    [Fact]
    public void Library_Rejects_BadShapes()
    {
        Assert.False(PresetLibrary.Validate(JsonNode.Parse("[]")).Ok);
        Assert.False(PresetLibrary.Validate(JsonNode.Parse("""{"raid":{}}""")).Ok);
        Assert.False(PresetLibrary.Validate(JsonNode.Parse("""{"raid":[{"name":"no id"}]}""")).Ok);
        var many = new JsonArray();
        for (int i = 0; i <= PresetLibrary.MaxPerList; i++) many.Add(new JsonObject { ["id"] = "x" + i });
        Assert.False(PresetLibrary.Validate(new JsonObject { ["gear"] = many }).Ok);
        Assert.False(PresetLibrary.Validate(JsonNode.Parse("""{"gear":[{"id":"a"}]}""")).Ok); // 웹이 못 그리는 장비 프리셋
        Assert.False(PresetLibrary.Validate(JsonNode.Parse("""{"spawn":[{"id":"a","kind":"x","data":{}}]}""")).Ok);
        Assert.True(PresetLibrary.Validate(JsonNode.Parse("""{"gear":[{"id":"g","tiers":[{},{},{},{},{},{},{}]}]}""")).Ok);
        var ok = PresetLibrary.Validate(JsonNode.Parse("""{"raid":[{"id":"a","snap":{}}],"junk":1}"""));
        Assert.True(ok.Ok);
        Assert.Null(ok.Lib!["junk"]);
        Assert.Empty(ok.Lib!["spawn"]!.AsArray());
    }

    [Fact]
    public void Library_BrokenFile_StartsEmpty_AndMovesFile()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, "presets.json");
        File.WriteAllText(f, "{broken");
        var lib = new PresetLibrary(f, Path.Combine(dir, "backup"));
        lib.Load();
        Assert.NotNull(lib.LoadWarning);
        Assert.False(File.Exists(f));
        Assert.Single(Directory.GetFiles(Path.Combine(dir, "backup"), "presets-broken-*.json"));
    }

    // ---------- 진단 ----------

    [Fact]
    public void DiagLog_Levels_AndRecentLimit()
    {
        var sink = new List<string>();
        var level = "off";
        var oldLevel = DiagLog.Level;
        try
        {
            DiagLog.Level = () => level;
            var info = DiagLog.Info(sink.Add);
            var verbose = DiagLog.Verbose(sink.Add);
            var warn = DiagLog.Warn(sink.Add);
            info("a"); verbose("b"); warn("c");
            Assert.Equal(["c"], sink);
            level = "verbose";
            info("d"); verbose("e");
            Assert.Equal(["c", "d", "e"], sink);
            for (int i = 0; i < 40; i++) info("n" + i);
            var recent = DiagLog.RecentEntries();
            Assert.Equal(DiagLog.RecentMax, recent.Count);
            Assert.Equal("n39", recent[0].Text);
        }
        finally { DiagLog.Level = oldLevel; }
    }

    [Fact]
    public void DiagLog_SaveReport_KeepsLatest()
    {
        var old = DiagLog.ReportsDir;
        var dir = TempDir();
        try
        {
            DiagLog.ReportsDir = dir;
            var t = new DateTime(2026, 11, 9, 10, 0, 0);
            for (int i = 0; i < DiagLog.ReportsKept + 3; i++)
                Assert.NotNull(DiagLog.SaveReport("bigmap", new { n = i }, t.AddMinutes(i)));
            var files = DiagLog.ReportFiles();
            Assert.Equal(DiagLog.ReportsKept, files.Length);
            Assert.Equal("20261109-102200-bigmap.json", files[0]);
            // 파일 이름에 맵 id의 경로 문자는 안 들어감
            Assert.EndsWith("-x.json", DiagLog.SaveReport("../x", new { }, t.AddHours(1)));
        }
        finally { DiagLog.ReportsDir = old; }
    }

    [Fact]
    public void Report_Dto_ExtractedOptional()
    {
        var r = new RaidReport("bigmap", 1, new Dictionary<string, int>(), new Dictionary<string, int>(), 0, []);
        Assert.Null(r.Extracted);
        Assert.Equal(0, r.ExtractRefill);
    }
}
