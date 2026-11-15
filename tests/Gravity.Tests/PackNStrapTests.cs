using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Gear;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using Xunit;

namespace Gravity.Tests;

/// <summary>1.0.7 WTT PackNStrap 벨트: 감지·분류·티어별 벨트 목록·탄창 칸 순서.</summary>
public class PackNStrapTests
{
    private static string Id(int n) => n.ToString("x24");
    private static GearSettings Cfg(string? json = null) => ConfigModel.FromState(json == null ? null : JsonNode.Parse(json), []).Gear!;

    /// <summary>벨트 7개(가격 100~700 → 자동 T1~T7), 일반 완장 1개, 탄창 1개, 의약품 1개.</summary>
    private static GearItems Items(bool belts = true, bool modBelts = false)
    {
        var info = new Dictionary<string, ItemInfo>();
        var pools = new List<(string, string)>();
        if (belts)
            for (int i = 1; i <= 7; i++)
            {
                info[Id(100 + i)] = new ItemInfo(i * 100, 0, 0, false, modBelts) { Section = "armor", Slot = "belt", EquipmentSlots = ["ArmBand"], Source = "WTT-PackNStrap" };
                pools.Add(("armor/belt", Id(100 + i)));
            }
        info[Id(200)] = new ItemInfo(50, 0, 0, false, false) { Section = "armor", Slot = "armband", EquipmentSlots = ["ArmBand"] };
        pools.Add(("ArmBand", Id(200)));
        info[Id(300)] = new ItemInfo(10, 0, 0, false, false) { Section = "parts", Slot = "magazine" };
        info[Id(301)] = new ItemInfo(10, 0, 0, false, false) { Section = "meds", Slot = "meds" };
        info[Id(302)] = new ItemInfo(10, 0, 0, false, false) { Section = "meds", Slot = "food" };
        return new GearItems(info, pools);
    }

    private static GearTable Table(double chance, IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>? pools = null) =>
        new(new Dictionary<string, (int, int)>(), new Dictionary<string, double> { ["belt"] = chance }, new Dictionary<string, double>())
        { Pools = pools ?? new Dictionary<string, IReadOnlyDictionary<string, double>>() };

    private static GearContext Ctx(GearSettings g, GearItems items, string role = "usec", int tier = 0, bool pmc = true) =>
        GearApply.Context(g, new GearPick(role, GearDefaults.GroupOf(role), tier, pmc), items, GearRaid.Pick(items, false, 0, 0, 0, 1), false, new Random(1));

    [Fact]
    public void 폴더나_어셈블리로_감지한다()
    {
        Assert.True(ModDetector.Detect([], ["WTT-PackNStrap"]).PackNStrap);
        Assert.True(ModDetector.Detect(["WTT-PackNStrapServer"], []).PackNStrap);
        Assert.False(ModDetector.Detect([], ["WTT-Armory", "WTT-ContentBackport"]).PackNStrap);
    }

    [Fact]
    public void 벨트는_방어구_벨트_칸_완장_장비로_분류한다()
    {
        var cls = GearCatalog.Classify(b => b == GearBelts.BeltBase, false, "");
        Assert.Equal(("armor", "belt", "gear"), (cls.Section, cls.Slot, cls.Cat));
        Assert.Equal(["ArmBand"], cls.Slots);
    }

    [Fact]
    public void 기본값은_켬_PMC만_못줍게_확률은_티어마다()
    {
        var g = Cfg();
        Assert.Equal(new BeltSettings(true, true, true), g.Belts);
        Assert.Equal(30, g.TableFor("usec", 0).Chance["belt"]);
        Assert.Equal(80, g.TableFor("usec", 6).Chance["belt"]);
        var off = Cfg("""{"gear":{"pns":{"on":false,"pmcOnly":false,"unlootable":false}}}""");
        Assert.Equal(new BeltSettings(false, false, false), off.Belts);
    }

    [Fact]
    public void PackNStrap이_없으면_완장_칸을_건드리지_않는다()
    {
        var items = Items(belts: false);
        Assert.Equal((false, (Dictionary<MongoId, double>?)null), GearBelts.Decide(Ctx(Cfg(), items), Table(100), new Random(1)));
    }

    [Fact]
    public void 자동_목록은_봇_티어_이하_벨트만_가까울수록_많이()
    {
        var items = Items();
        var (managed, pool) = GearBelts.Decide(Ctx(Cfg(), items, tier: 2), Table(100), new Random(1));
        Assert.True(managed);
        Assert.NotNull(pool);
        Assert.Equal([Id(101), Id(102), Id(103)], pool!.Keys.Select(k => k.ToString()).Order());
        Assert.True(pool[Id(103)] > pool[Id(101)]);
        Assert.DoesNotContain((MongoId)Id(200), pool.Keys); // 일반 완장은 벨트 목록에 없음
    }

    [Fact]
    public void 확률에_걸리지_않거나_PMC만인데_PMC가_아니면_안_찬다()
    {
        var items = Items();
        Assert.Equal((true, (Dictionary<MongoId, double>?)null), GearBelts.Decide(Ctx(Cfg(), items), Table(0), new Random(1)));
        Assert.Null(GearBelts.Decide(Ctx(Cfg(), items, "boss_killa", 6, false), Table(100), new Random(1)).Pool);
        var all = Cfg("""{"gear":{"pns":{"pmcOnly":false}}}""");
        Assert.NotNull(GearBelts.Decide(Ctx(all, items, "boss_killa", 6, false), Table(100), new Random(1)).Pool);
        var off = Cfg("""{"gear":{"pns":{"on":false}}}""");
        Assert.Equal((true, (Dictionary<MongoId, double>?)null), GearBelts.Decide(Ctx(off, items), Table(100), new Random(1)));
    }

    [Fact]
    public void 직접_정한_목록과_블랙리스트를_따른다()
    {
        var items = Items(modBelts: true);
        var own = new Dictionary<string, IReadOnlyDictionary<string, double>> { ["armor/belt"] = new Dictionary<string, double> { [Id(107)] = 5, [Id(101)] = 0 } };
        // 직접 넣은 T7 벨트는 T1 봇에게도 나온다(직접 티어를 정하지 않았으면 티어로 막지 않음). 무게 0은 뺀다.
        var pool = GearBelts.Decide(Ctx(Cfg(), items), Table(100, own), new Random(1)).Pool;
        Assert.Equal([Id(107)], pool!.Keys.Select(k => k.ToString()));
        var black = Cfg($$$"""{"blacklist":{"entries":[{"id":"{{{Id(101)}}}","bots":["all"]}]}}""");
        Assert.DoesNotContain((MongoId)Id(101), GearBelts.Decide(Ctx(black, items, tier: 6), Table(100), new Random(1)).Pool!.Keys);
        // 모드 출처를 끄면 벨트도 안 나온다. 판마다 고르는 모드 묶음(subset)은 벨트를 막지 않는다.
        var src = Cfg("""{"mods":{"disabledSources":["WTT-PackNStrap"]}}""");
        Assert.Null(GearBelts.Decide(Ctx(src, items, tier: 6), Table(100), new Random(1)).Pool);
        var subset = GearApply.Context(Cfg(), new GearPick("usec", "pmc", 6, true), items, GearRaid.Pick(items, true, 0, 0, 0, 1), false, new Random(1));
        Assert.Equal(7, GearBelts.Decide(subset, Table(100), new Random(1)).Pool!.Count);
    }

    [Fact]
    public void 벨트는_판마다_고르는_모드_장비_묶음에_넣지_않는다()
    {
        var items = Items(modBelts: true);
        Assert.DoesNotContain(items.ModEquipment, id => id.StartsWith(Id(100)[..20]) && GearBelts.IsBelt(items, id));
    }

    [Fact]
    public void 탄창은_벨트_먼저_의약품은_마지막_그_밖은_그대로()
    {
        var items = Items();
        HashSet<EquipmentSlots> mag = [EquipmentSlots.TacticalVest, EquipmentSlots.Pockets];
        Assert.Equal([EquipmentSlots.ArmBand, EquipmentSlots.TacticalVest, EquipmentSlots.Pockets], GearBelts.SlotsFor(mag, items.Info[Id(300)])!.ToArray());
        HashSet<EquipmentSlots> loot = [EquipmentSlots.Pockets, EquipmentSlots.TacticalVest, EquipmentSlots.Backpack];
        Assert.Equal(EquipmentSlots.ArmBand, GearBelts.SlotsFor(loot, items.Info[Id(301)])!.Last());
        Assert.Null(GearBelts.SlotsFor(loot, items.Info[Id(302)])); // 음식은 벨트에 넣지 않음
        Assert.Null(GearBelts.SlotsFor([EquipmentSlots.Backpack], items.Info[Id(300)])); // 가방 전용 칸은 그대로
        Assert.Null(GearBelts.SlotsFor([EquipmentSlots.ArmBand, EquipmentSlots.Pockets], items.Info[Id(300)]));
    }

    [Fact]
    public void 장비_적용이_벨트_목록을_정하고_완장_칸을_만든다()
    {
        var items = Items();
        var g = Cfg();
        var bot = new BotType
        {
            BotInventory = new BotTypeInventory
            {
                Equipment = new Dictionary<EquipmentSlots, Dictionary<MongoId, double>>(),
                Ammo = new Dictionary<string, Dictionary<MongoId, double>>(),
                Mods = new Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>>(),
                Items = new ItemPools { Backpack = new(), Pockets = new(), TacticalVest = new(), SpecialLoot = new(), SecuredContainer = new() }
            },
            BotChances = new Chances { EquipmentChances = new(), WeaponModsChances = new(), EquipmentModsChances = new() },
            BotGeneration = new Generation { Items = new GenerationWeightingItems() }
        };
        // T7 PMC 기본 확률 80%: 시드를 바꿔 가며 한 번은 차고 한 번은 안 차는 것을 본다.
        bool worn = false, bare = false;
        for (int seed = 0; seed < 40 && !(worn && bare); seed++)
        {
            var b = new BotType { BotInventory = new BotTypeInventory { Equipment = new(), Ammo = new(), Mods = new(), Items = bot.BotInventory.Items }, BotChances = bot.BotChances, BotGeneration = bot.BotGeneration };
            var c = GearApply.Context(g, new GearPick("usec", "pmc", 6, true), items, GearRaid.Pick(items, false, 0, 0, 0, 1), false, new Random(seed));
            GearApply.Apply(b, c, false, new Random(seed));
            Assert.True(c.BeltManaged);
            if (c.Belts != null) { worn = true; Assert.True(b.BotInventory.Equipment.ContainsKey(EquipmentSlots.ArmBand)); Assert.Equal(7, c.Belts.Count); }
            else bare = true;
        }
        Assert.True(worn && bare);
    }
}
