using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Gear;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using Xunit;

namespace Gravity.Tests;

public class GearTests
{
    private static string Id(int n) => n.ToString("x24");

    private static GearSettings Cfg(string? json = null) =>
        ConfigModel.FromState(json == null ? null : JsonNode.Parse(json), []).Gear!;

    [Fact]
    public void 기본값이_웹과_같다()
    {
        var g = Cfg();
        Assert.True(g.Module);
        Assert.Equal("mixed", g.TierMode);
        Assert.Equal(7, g.Tiers.Count);
        Assert.Equal(20, g.Tiers[0].Pen);
        Assert.Equal(65, g.Tiers[6].Pen);
        Assert.Equal(55, g.Tiers[0].WMin);
        Assert.Equal(30, g.Tiers[0].HelmetAcc);
        Assert.True(g.RoleFor("usec").On);
        Assert.Equal("follow", g.RoleFor("bear").Mode);
        Assert.False(g.RoleFor("scav").On);
        Assert.Equal(2, g.RoleFor("scav").Tier);
        Assert.Equal(6, g.RoleFor("boss_killa").Tier);
        // 표 기본식: PMC T1 탄창 1~3, T7 3~5, 스캐브는 두 단계 아래
        Assert.Equal((1, 3), g.TableFor("usec", 0).Gen["magazines"]);
        Assert.Equal((3, 5), g.TableFor("usec", 6).Gen["magazines"]);
        Assert.Equal(g.TableFor("usec", 0).Gen["magazines"], g.TableFor("scav", 2).Gen["magazines"]);
        Assert.Equal(50, g.TableFor("usec", 0).Chance["headwear"]);
        // 블랙리스트 칸이 없으면 웹 기본 3개, 빈 배열이면 비움
        Assert.Equal(3, g.Blacklist.Count);
        Assert.Contains(g.Blacklist, e => e.Id == "59e690b686f7746c9f75e848" && e.Bots.Contains("scavs"));
        Assert.Empty(Cfg("""{"blacklist":{"entries":[]}}""").Blacklist);
    }

    [Fact]
    public void 위쪽_폭은_음수도_된다()
    {
        var g = Cfg("""{"gear":{"lv":{"pick":"flat","below":10,"above":-10}}}""");
        Assert.Equal((25, 25), GearTier.LevelRange(g, 35, false));
    }

    [Fact]
    public void 부품_거르기는_막힌_것과_열화상을_되살리지_않는다()
    {
        var items = Items(out _);
        var g = Cfg($$$"""{"blacklist":{"entries":[{"id":"{{{Id(52)}}}","bots":["all"]}]}}""");
        var c = GearApply.Context(g, new GearPick("usec", "pmc", 3, true), items, GearRaid.Pick(items, false, 0, 0, 0, 1), false, new Random(1));
        var blocked = new HashSet<MongoId> { Id(52) };
        GearApply.FilterModSet(blocked, "mod_scope", c);
        Assert.Empty(blocked);
        var thermalOnly = new HashSet<MongoId> { Id(51) };
        GearApply.FilterModSet(thermalOnly, "mod_scope_000", c);
        Assert.Empty(thermalOnly); // T4·낮: 열화상 못 씀 → 비움
        var nvg = new HashSet<MongoId> { Id(51), Id(53) };
        GearApply.FilterModSet(nvg, "mod_nvg", c);
        Assert.Equal([Id(53)], nvg.Select(x => x.ToString()));
    }

    [Fact]
    public void 필수_칸은_다_막혀도_비우지_않는다()
    {
        // 1.0 인게임: 묶음 밖 모드 방어구 내장 부품이 다 막혀 필수 칸이 비면 SPT가 봇 생성 전체를 실패했다(Nullable object must have a value)
        var items = Items(out var info);
        info[Id(70)] = new ItemInfo(1, 4, 0, false, true, RequiredSlots: new HashSet<string> { "soft_armor_front" });
        info[Id(71)] = new ItemInfo(1, 2, 0, false, true);
        items = new GearItems(info, [("mods", Id(71)), ("ArmorVest", Id(70))]);
        var g = Cfg($$$"""{"blacklist":{"entries":[{"id":"{{{Id(72)}}}","bots":["all"]}]}}""");
        info[Id(72)] = new ItemInfo(1, 2, 0, false, false);
        var raid = GearRaid.Pick(items, true, 0, 0, 0, 1);
        var c = GearApply.Context(g, new GearPick("usec", "pmc", 3, true), items, raid, false, new Random(1));
        Assert.True(c.Blocked(Id(71)));
        Assert.True(c.Required(Id(70), "Soft_armor_front"));
        Assert.False(c.Required(Id(70), "mod_scope"));

        var optional = new HashSet<MongoId> { Id(71) };
        GearApply.FilterModSet(optional, "soft_armor_front", c);
        Assert.Empty(optional); // 필수 아님: 예전처럼 비움

        var req = new HashSet<MongoId> { Id(71), Id(72) };
        var (_, restored) = GearApply.FilterModSet(req, "Soft_armor_front", c, c.Required(Id(70), "Soft_armor_front"));
        Assert.True(restored);
        Assert.Equal([Id(71)], req.Select(x => x.ToString())); // 사용자 블랙리스트(72)는 되살리지 않는다

        var onlyBlack = new HashSet<MongoId> { Id(72) };
        GearApply.FilterModSet(onlyBlack, "soft_armor_front", c, true);
        Assert.Single(onlyBlack); // 그것뿐이면 원래 목록(봇 생성 실패보다 낫다)
    }

    [Fact]
    public void 설정을_읽고_범위를_자른다()
    {
        var g = Cfg("""
        {"modules":{"gear":false},"gear":{"tierMode":"fixed","fixedTier":9,"tiers":[{"pen":33,"fMin":5,"fMax":3}]},
         "roles":{"scav":{"on":true,"mode":"offset","offset":2}},
         "tierOwn":{"usec":true},
         "tierData":{"pmc":[{"gen":{"magazines":{"min":4,"max":2}},"chance":{"headwear":12}}],"usec":[{"mods":{"scope":77}}]},
         "blacklist":{"entries":[{"id":"m995","bots":["all"]},{"id":"5C0D5E4486F77478390952FE","bots":["pmc"]}]}}
        """);
        Assert.False(g.Module);
        Assert.Equal("fixed", g.TierMode);
        Assert.Equal(7, g.FixedTier);
        Assert.Equal(33, g.Tiers[0].Pen);
        Assert.Equal(5, g.Tiers[0].FMax); // fMax는 fMin 이상
        Assert.True(g.RoleFor("scav").On);
        Assert.Equal(2, g.RoleFor("scav").Offset);
        Assert.Equal(77, g.TableFor("usec", 0).Mods["scope"]);  // 전용 표
        Assert.Equal(12, g.TableFor("bear", 0).Chance["headwear"]); // 묶음 표
        Assert.Equal((4, 4), g.TableFor("bear", 0).Gen["magazines"]);
        var e = Assert.Single(g.Blacklist); // 예시 id는 건너뜀
        Assert.Equal("5c0d5e4486f77478390952fe", e.Id);
    }

    [Fact]
    public void 레벨_구간과_티어()
    {
        var g = Cfg();
        Assert.Equal(0, GearTier.TierOfLevel(g.Tiers, 1));
        Assert.Equal(3, GearTier.TierOfLevel(g.Tiers, 35));
        Assert.Equal(6, GearTier.TierOfLevel(g.Tiers, 99));
        Assert.Equal((20, 43), GearTier.LevelRange(g, 35, false)); // T4: −15 +8
        Assert.Equal((17, 35), GearTier.LevelRange(g, 35, true));  // 스캐브 T4: −18 +0
        var flat = Cfg("""{"gear":{"lv":{"pick":"flat","below":3,"above":2}}}""");
        Assert.Equal((32, 37), GearTier.LevelRange(flat, 35, false));
        var fix = Cfg("""{"gear":{"lv":{"pick":"fixed","fixMin":20,"fixMax":10}}}""");
        Assert.Equal((20, 20), GearTier.LevelRange(fix, 50, false));
    }

    [Fact]
    public void 티어_고르기()
    {
        var rnd = new Random(1);
        var g = Cfg("""{"gear":{"tierMode":"level"}}""");
        var usec = g.RoleFor("usec") with { Poverty = 0 };
        Assert.Equal(3, GearTier.Pick(g, usec, "pmc", 35, 35, rnd));
        Assert.Equal(1, GearTier.Pick(g, g.RoleFor("scav"), "scavs", 35, null, rnd)); // 고정 T2
        Assert.Equal(5, GearTier.Pick(g, usec with { Mode = "offset", Offset = 2 }, "pmc", 35, 35, rnd));
        var st = Cfg("""{"gear":{"tierMode":"level","startTier":5}}""");
        Assert.Equal(4, GearTier.Pick(st, usec, "pmc", 1, 1, rnd));
        var fx = Cfg("""{"gear":{"tierMode":"fixed","fixedTier":3}}""");
        Assert.Equal(2, GearTier.Pick(fx, usec, "pmc", 70, 70, rnd));
        Assert.Equal(1, GearTier.Pick(g, usec with { Poverty = 100 }, "pmc", 35, 35, rnd)); // 빈곤: 두 단계 아래
        // 섞기: 레벨 티어에서 ±2를 넘지 않음
        var mixed = Cfg();
        for (int i = 0; i < 200; i++)
            Assert.InRange(GearTier.Pick(mixed, usec, "pmc", 35, 35, rnd), 1, 5);
    }

    [Theory]
    [InlineData("assault", false, null, "scav")]
    [InlineData("pmcBEAR", true, "Bear", "bear")]
    [InlineData("pmcUSEC", true, "Usec", "usec")]
    [InlineData("bossKilla", false, null, "boss_killa")]
    [InlineData("followerGluharSnipe", false, null, "f_glukhar")]
    [InlineData("bossKnight", false, null, "boss_goons")]
    [InlineData("ruafRifleman", false, null, "ruaf")]
    [InlineData("blackDivIb", false, null, "wedge")]
    [InlineData("blackDivAssault", false, null, "blackdiv")]
    [InlineData("pmcBot", false, null, "raider")]
    [InlineData("gifter", false, null, null)]
    public void 역할_이름_대응(string role, bool pmc, string? side, string? want) =>
        Assert.Equal(want, GearTier.RoleId(role, pmc, side));

    private static GearItems Items(out Dictionary<string, ItemInfo> info)
    {
        info = new Dictionary<string, ItemInfo>();
        var pools = new List<(string, string)>();
        for (int i = 1; i <= 7; i++) { info[Id(i)] = new ItemInfo(i * 100, 0, 0, false, i == 7, Weapon: true); pools.Add(("FirstPrimaryWeapon", Id(i))); }
        info[Id(20)] = new ItemInfo(100, 2, 0, false, false); info[Id(21)] = new ItemInfo(900, 6, 0, false, false);
        pools.Add(("Headwear", Id(20))); pools.Add(("Headwear", Id(21)));
        info[Id(30)] = new ItemInfo(1, 0, 15, false, false); info[Id(31)] = new ItemInfo(1, 0, 60, false, false);
        info[Id(40)] = new ItemInfo(1, 3, 0, false, false, Plate: true); info[Id(41)] = new ItemInfo(1, 6, 0, false, false, Plate: true);
        info[Id(50)] = new ItemInfo(1, 0, 0, false, false, LongScope: true); info[Id(51)] = new ItemInfo(1, 0, 0, false, false, Thermal: true, LongScope: true);
        info[Id(52)] = new ItemInfo(1, 0, 0, false, false);
        info[Id(53)] = new ItemInfo(1, 0, 0, false, false); // 일반 야투경
        info[Id(60)] = new ItemInfo(1, 0, 0, true, false); info[Id(61)] = new ItemInfo(1, 0, 0, false, false);
        pools.AddRange([("mods", Id(40)), ("mods", Id(41)), ("mods", Id(50)), ("mods", Id(51)), ("mods", Id(52))]);
        return new GearItems(info, pools);
    }

    private static BotType Bot() => new()
    {
        BotInventory = new BotTypeInventory
        {
            Equipment = new Dictionary<EquipmentSlots, Dictionary<MongoId, double>>
            {
                [EquipmentSlots.FirstPrimaryWeapon] = Enumerable.Range(1, 7).ToDictionary(i => (MongoId)Id(i), _ => 10.0),
                [EquipmentSlots.Headwear] = new() { [Id(20)] = 10, [Id(21)] = 10 }
            },
            Ammo = new Dictionary<string, Dictionary<MongoId, double>> { ["c"] = new() { [Id(30)] = 10, [Id(31)] = 10 } },
            Mods = new Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>>
            {
                [Id(99)] = new()
                {
                    ["front_plate"] = [Id(40), Id(41)], ["left_side_plate"] = [Id(41)],
                    ["mod_scope"] = [Id(50), Id(51), Id(52)]
                }
            },
            Items = new ItemPools { Backpack = new() { [Id(60)] = 1, [Id(61)] = 1 }, Pockets = new(), TacticalVest = new(), SpecialLoot = new(), SecuredContainer = new() }
        },
        BotChances = new Chances { EquipmentChances = new(), WeaponModsChances = new() { ["mod_scope_000"] = 1 }, EquipmentModsChances = new() },
        BotGeneration = new Generation
        {
            Items = new GenerationWeightingItems
            {
                Magazines = new GenerationData { Weights = new() { [9] = 1 } }, Healing = new GenerationData { Weights = new() },
                Grenades = new GenerationData { Weights = new() }, BackpackLoot = new GenerationData { Weights = new() { [5] = 1 } }
            }
        }
    };

    [Fact]
    public void 목록을_티어에_맞게_고친다()
    {
        var items = Items(out _);
        var g = Cfg("""{"gear":{"thermalsT7":true},"mods":{"subset":false,"pmcOnly":true}}""");
        var bot = Bot();
        var pick = new GearPick("scav", "scavs", 1, false);
        var r = GearApply.Apply(bot, g, pick, items, GearRaid.Pick(items, false, 0, 0, 0, 1), false, new Random(3));
        var w = bot.BotInventory.Equipment[EquipmentSlots.FirstPrimaryWeapon];
        Assert.Equal(2, w.Count); // 티어 0·1만
        Assert.Equal(10.0, w[Id(2)]); // 거리 0 → 그대로
        Assert.Equal(10 * 24 / 30.0, w[Id(1)], 6);
        // 헬멧: 가격 티어로 비싼 것(티어 6)은 빠짐
        Assert.Equal([Id(20)], bot.BotInventory.Equipment[EquipmentSlots.Headwear].Keys.Select(k => k.ToString()));
        // 탄약: 스캐브 T2 관통 상한 28 → 60은 빠짐
        Assert.Equal([Id(30)], bot.BotInventory.Ammo["c"].Keys.Select(k => k.ToString()));
        var mods = bot.BotInventory.Mods[Id(99)];
        Assert.Equal([Id(40)], mods["front_plate"].Select(k => k.ToString())); // T2 판 3~3
        Assert.Equal([Id(41)], mods["left_side_plate"].Select(k => k.ToString())); // 맞는 판 없음 → 가장 가까운 것
        Assert.DoesNotContain((MongoId)Id(51), mods["mod_scope"]); // 열화상은 T7만
        Assert.DoesNotContain((MongoId)Id(60), bot.BotInventory.Items.Backpack.Keys); // 퀘스트 템
        Assert.True(r.Removed > 0);
        // 확률·개수
        Assert.Equal(g.RoleFor("scav").FrontPlate, bot.BotChances.EquipmentModsChances["front_plate"]);
        Assert.Equal(100, bot.BotChances.WeaponModsChances["mod_scope_000"]); // 조준경 항상
        Assert.Equal([1.0, 2.0, 3.0], bot.BotGeneration.Items.Magazines.Weights.Keys.OrderBy(x => x));
    }

    [Fact]
    public void 다_빠지면_가장_낮은_티어를_남기고_블랙리스트와_모드_묶음을_지킨다()
    {
        var items = Items(out _);
        var g = Cfg($$$"""{"blacklist":{"entries":[{"id":"{{{Id(1)}}}","bots":["scavs"]}]},"mods":{"pmcOnly":false}}""");
        var bot = Bot();
        GearApply.Apply(bot, g, new GearPick("scav", "scavs", 0, false), items, GearRaid.Pick(items, true, 0, 0, 0, 1), false, new Random(1));
        var w = bot.BotInventory.Equipment[EquipmentSlots.FirstPrimaryWeapon];
        Assert.Single(w); // 티어 0은 블랙리스트로 빠짐 → 남은 것 중 가장 낮은 티어
        Assert.True(w.ContainsKey(Id(2)));

        // PMC: 모드 아이템(7번)은 이번 판 묶음에 있어야만
        var pmc = new GearPick("usec", "pmc", 6, true);
        var b1 = Bot();
        GearApply.Apply(b1, g, pmc, items, GearRaid.Pick(items, true, 0, 0, 0, 1), false, new Random(1));
        Assert.False(b1.BotInventory.Equipment[EquipmentSlots.FirstPrimaryWeapon].ContainsKey(Id(7)));
        var b2 = Bot();
        GearApply.Apply(b2, g, pmc, items, GearRaid.Pick(items, true, 5, 0, 0, 1), false, new Random(1));
        Assert.True(b2.BotInventory.Equipment[EquipmentSlots.FirstPrimaryWeapon].ContainsKey(Id(7)));
    }

    [Fact]
    public void 아이템_티어와_모드_묶음()
    {
        var items = Items(out _);
        Assert.Equal(0, items.TierOf("FirstPrimaryWeapon", Id(1)));
        Assert.Equal(6, items.TierOf("FirstPrimaryWeapon", Id(7)));
        Assert.Equal([Id(7)], items.ModWeapons);
        var a = GearRaid.Pick(items, true, 1, 1, 1, 42);
        Assert.Equal(1, a.Weapons);
        Assert.True(a.ModAllowed(Id(7)));
        Assert.Null(GearRaid.Pick(items, false, 0, 0, 0, 1).Allowed);
    }

    [Fact]
    public void 무게_탄약_티어_내구도()
    {
        Assert.Equal(1.0, GearApply.Factor(0));
        Assert.Equal(2 / 30.0, GearApply.Factor(6), 6);
        var g = Cfg();
        Assert.Equal(0, GearApply.AmmoTier(g.Tiers, 15));
        Assert.Equal(3, GearApply.AmmoTier(g.Tiers, 40));
        Assert.Equal(6, GearApply.AmmoTier(g.Tiers, 80));
        Assert.Equal((75.0, 85.0), GearApply.Durability(60, 85, 75, 100));
        Assert.Equal((60.0, 85.0), GearApply.Durability(60, 85, 90, 100)); // 안 겹치면 티어
        Assert.Equal([0.0], GearApply.Uniform(3, 0).Keys); // 상한 0
    }
}
