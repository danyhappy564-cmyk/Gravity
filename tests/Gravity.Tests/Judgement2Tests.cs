using System.Text.Json;
using System.Text.Json.Nodes;
using Gravity.Client;
using Gravity.Core;
using Gravity.Gear;
using Gravity.Shared;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Xunit;
using Path = System.IO.Path;

namespace Gravity.Tests;

public class Judgement2Tests
{
    private static GravityConfig Cfg(string s = "{}") => ConfigModel.FromState(JsonNode.Parse(s), []);
    private const string A = "000000000000000000000001", B = "000000000000000000000002";

    [Fact]
    public void 수동_티어가_가격보다_우선하며_0은_자동입니다()
    {
        var items = new GearItems(new Dictionary<string, ItemInfo>
        { [A] = new(1, 0, 0, false, true), [B] = new(1000, 0, 0, false, true) }, [("Headwear", A), ("Headwear", B)]);
        var g = Cfg($$$$"""{"gear":{"itemTier":{"{{{{A}}}}":7,"{{{{B}}}}":0,"invalid":2}}}""").Gear!;
        var c = GearApply.Context(g, new("usec", "pmc", 3, true), items, new(null, 0, 0, 0), false, new(1));
        Assert.Equal(6, c.TierOf("Headwear", A));
        Assert.Equal(3, c.TierOf("Headwear", B));
        Assert.Single(g.ItemTier);
        Assert.Equal(0, items.TierOf("Headwear", A)); // 전역 가격 자료는 보존
        var bot = new BotType
        {
            BotInventory = new BotTypeInventory
            {
                Equipment = new() { [SPTarkov.Server.Core.Models.Enums.EquipmentSlots.Headwear] = new() { [A] = 10, [B] = 10 } }
            }
        };
        GearApply.Apply(bot, c, false, new(1));
        Assert.Equal([B], bot.BotInventory.Equipment[SPTarkov.Server.Core.Models.Enums.EquipmentSlots.Headwear].Keys.Select(x => x.ToString()));
    }

    [Fact]
    public void 열화상_Remove가_Add와_자동판별보다_우선합니다()
    {
        var items = new GearItems(new Dictionary<string, ItemInfo>
        { [A] = new(1, 0, 0, false, false), [B] = new(1, 0, 0, false, false, Thermal: true) }, []);
        var g = Cfg($$$$"""{"gear":{"thermalAdd":["{{{{A}}}}","{{{{B}}}}"],"thermalRemove":["{{{{B}}}}"]}}""").Gear!;
        var c = GearApply.Context(g, new("usec", "pmc", 0, true), items, new(null, 0, 0, 0), false, new(1));
        Assert.True(c.Info(A)!.Thermal);
        Assert.False(c.Info(B)!.Thermal);
        Assert.False(items.Info[A].Thermal);
        Assert.True(items.Info[B].Thermal);
        var mods = new HashSet<SPTarkov.Server.Core.Models.Common.MongoId> { A, B };
        GearApply.FilterModSet(mods, "mod_scope", c);
        Assert.Equal([B], mods.Select(x => x.ToString()));
    }

    [Theory]
    [InlineData("{}", "infinite", 20)]
    [InlineData("{\"orbit\":{\"refillAfterExtract\":false}}", "off", 20)]
    [InlineData("{\"orbit\":{\"refillAfterExtract\":false,\"extractRefillMode\":\"all\",\"extractRefillCap\":120}}", "all", 100)]
    [InlineData("{\"orbit\":{\"extractRefillMode\":\"bad\",\"extractRefillCap\":-5}}", "infinite", 0)]
    public void 탈출_설정이_옛_bool과_범위를_처리합니다(string json, string mode, int cap)
    {
        var cfg = Cfg(json);
        var map = new MapInfo("bigmap", 16, 20, 2400, ["P"], ["S"]);
        var dto = ClientSettingsBuilder.Build(cfg, map, SpawnPlanner.Plan(cfg, map, 2400, 0, 1), true);
        Assert.Equal(mode, dto.ExtractRefillMode);
        Assert.Equal(cap, dto.ExtractRefillCap);
        Assert.Equal(mode != "off" && cap > 0, dto.RefillAfterExtract);
    }

    [Theory]
    [InlineData("all", false, true)]
    [InlineData("infinite", false, false)]
    [InlineData("infinite", true, true)]
    [InlineData("off", true, false)]
    public void 탈출_보충은_방식_상한_종료시각을_지킵니다(string mode, bool infinite, bool expected)
    {
        var s = ClientSettingsBuilder.Unmanaged("bigmap") with
        { Managed = true, ExtractRefillMode = mode, ExtractRefillCap = 2, HardStopPmcSec = 100,
          Inf = infinite ? new(45, 2, 0, 0) : null };
        Assert.Equal(expected, ExtractRefillRule.CanQueue(s, true, 0, 10));
        Assert.False(ExtractRefillRule.CanQueue(s, false, 0, 10));
        Assert.False(ExtractRefillRule.CanQueue(s, true, 2, 10));
        Assert.False(ExtractRefillRule.CanQueue(s with { ExtractRefillCap = 0 }, true, 0, 10));
        Assert.False(ExtractRefillRule.CanQueue(s, true, 0, 101));
        Assert.False(ExtractRefillRule.CanQueue(s with { MapId = "labyrinth" }, true, 0, 10));
    }

    [Fact]
    public void 원본_배치는_별도_구역을_선택하며_옛_가중치를_읽습니다()
    {
        var cfg = Cfg("""{"maps":{"bigmap":{"zones":{"P":{"on":false,"wPmc":20,"wScav":30}},"zonesAlt":{"P":{"on":true,"w":160,"wPmc":70}}}}}""");
        var original = cfg.ForLayout("bigmap", false, true).MapFor("bigmap");
        Assert.True(original.Zones["P"]);
        Assert.Equal(70, original.ZoneWeights["P"]);
        Assert.Equal(160, original.ScavZoneWeights!["P"]);
        Assert.False(cfg.ForLayout("bigmap", true, true).MapFor("bigmap").Zones["P"]);
        Assert.False(cfg.ForLayout("bigmap", false, false).MapFor("bigmap").Zones["P"]);
        var info = new MapInfo("bigmap", 16, 20, 2400, ["P"], ["P"]);
        var variant = ClientSettingsBuilder.Build(cfg, info, SpawnPlanner.Plan(cfg, info, 2400, 0, 1), true);
        Assert.Contains("P", variant.DisabledZones!);
        var origCfg = cfg.ForLayout("bigmap", false, true);
        var orig = ClientSettingsBuilder.Build(origCfg, info, SpawnPlanner.Plan(origCfg, info, 2400, 0, 1), true);
        Assert.Empty(orig.DisabledZones!);
        Assert.Equal(70, orig.ZoneWeights["P"]);
        Assert.Equal(160, orig.ScavZoneWeights!["P"]);
    }

    [Fact]
    public void 미등록_모드도_스폰_id가_다르면_변형이며_두_배치_구역을_보냅니다()
    {
        var loaded = new LocationBase { Id = "bigmap", SpawnPointParams = [new SpawnPointParam { Id = "new", BotZoneName = "New", Categories = ["BotPmc"] }] };
        var original = new LocationBase { Id = "bigmap", SpawnPointParams = [new SpawnPointParam { Id = "old", BotZoneName = "Old", Categories = ["BotPmc"] }] };
        var cat = new MapCatalog();
        cat.AddForTest(new MapInfo("bigmap", 16, 20, 2400, ["New"], []));
        cat.SetLoadedForTest("bigmap", loaded);
        var active = cat.DiscoverVariants(ModDetector.Detect([], ["UnknownRework"]),
            _ => """{"SpawnPointParams":[{"Id":"old"}]}""", _ => original);
        Assert.Contains("bigmap", active);
        Assert.Contains("bigmap", cat.AutomaticVariants);
        Assert.Equal(["Old"], cat.OriginalInfoFor("bigmap")!.PmcZones);
        var runtime = new GravityRuntime(new GravityConfigStore("unused.json", "unused-backup"), cat,
            ModDetector.Detect([], ["UnknownRework"]), _ => { }, _ => { }) { ActiveVariants = active };
        Assert.True(runtime.IsVariant("bigmap", loaded));
        Assert.False(runtime.IsVariant("bigmap", original));
    }

    [Fact]
    public void 사용자_변형_폴더목록은_설치된_폴더만_감지합니다()
    {
        var c = Cfg("""{"compat":{"variantMods":["MyRework","Absent"],"lockWithQuestingBots":true}}""");
        var d = ModDetector.Detect([], ["MyRework", "SPT-QuestingBots"], c.CompatOrDefault);
        Assert.Contains("MyRework", d.VariantMaps["bigmap"]);
        Assert.DoesNotContain("Absent", d.VariantMaps["bigmap"]);
        Assert.True(d.SpawnLocked);
        Assert.Equal("Questing Bots", d.SpawnLockReason);
        Assert.False(ModDetector.Detect([], ["SPT-QuestingBots"]).SpawnLocked);
    }

    [Fact]
    public void 탈출_보고가_서버_요청_변환에서_유실되지_않습니다()
    {
        var dto = new ReportRequestData { Extracted = new() { ["pmcUSEC"] = 3 }, ExtractRefill = 2 }.ToReport();
        Assert.Equal(3, dto.Extracted!["pmcUSEC"]);
        Assert.Equal(2, dto.ExtractRefill);
    }

    [Fact]
    public void 원본_구역의_새_비중은_200까지이며_옛_비중은_보존합니다()
    {
        var m = Cfg("""{"maps":{"bigmap":{"zonesAlt":{"P":{"wPmc":999,"wScav":-2},"S":{"w":400}}}}}""").MapFor("bigmap");
        Assert.Equal(200, m.ZonesAlt!.Pmc["P"]);
        Assert.Equal(0, m.ZonesAlt.Scav["P"]);
        Assert.Equal(400, m.ZonesAlt.Pmc["S"]);
    }

    [Fact]
    public void 옛_DTO는_bool과_기본_20명으로_동작합니다()
    {
        var opt = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var dto = ClientSettingsBuilder.Unmanaged("bigmap") with
        { Managed = true, RefillAfterExtract = true, HardStopPmcSec = 100, Inf = new(45, 2, 0.5, 0) };
        var json = System.Text.Json.JsonSerializer.SerializeToNode(dto, opt)!.AsObject();
        json.Remove("extractRefillMode");
        json.Remove("extractRefillCap");
        var old = json.Deserialize<ClientRaidSettings>(opt)!;
        Assert.Equal(20, ExtractRefillRule.Cap(old));
        Assert.True(ExtractRefillRule.CanQueue(old, true, 19, 10));
        Assert.False(ExtractRefillRule.CanQueue(old, true, 20, 10));
        Assert.False(ExtractRefillRule.CanQueue(old with { RefillAfterExtract = false }, true, 0, 10));
    }

    [Fact]
    public void QB_잠금은_저장한_현재_설정을_사용하며_ABPS는_항상_잠깁니다()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gravity-judgement2-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new GravityConfigStore(Path.Combine(dir, "state.json"), Path.Combine(dir, "backup"));
            var cat = new MapCatalog();
            cat.AddForTest(new MapInfo("bigmap", 16, 20, 2400, [], []));
            var rt = new GravityRuntime(store, cat, ModDetector.Detect([], ["QuestingBots"]), _ => { }, _ => { });
            Assert.False(rt.OnHello("s", new(GravityVersion.Current, false)).SpawnLocked);
            Assert.Null(rt.SkipReason(store.Current, "bigmap", null));
            Assert.True(store.Save(JsonNode.Parse("""{"compat":{"lockWithQuestingBots":true}}""")!).Ok);
            Assert.True(rt.OnHello("s", new(GravityVersion.Current, false)).SpawnLocked);
            Assert.Contains("Questing Bots", rt.SkipReason(store.Current, "bigmap", null)!);
            Assert.True(store.Save(JsonNode.Parse("{}")!).Ok);
            Assert.False(rt.Detect.SpawnLocked);
            Assert.True(ModDetector.Detect([], ["acidphantasm-botplacementsystem"]).SpawnLocked);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
