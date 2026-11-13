using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Gear;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Xunit;

namespace Gravity.Tests;

/// <summary>1.0.5 의상 탭: 허용한 모드의 옷만 PMC·스캐브 외형 목록에 넣는다(GearClothing).</summary>
public class ClothingTests
{
    private static string Id(int n) => n.ToString("x24");
    private static GearSettings Cfg(string? json = null) => ConfigModel.FromState(json == null ? null : JsonNode.Parse(json), []).Gear!;

    // 바닐라 몸통 V1(10)·V2(30), 바닐라 다리 F1(6). 모드 A: usec 몸통 A1, bear 몸통 A2, usec 다리 A3, 스캐브 몸통 S1. 모드 B: usec 몸통 B1.
    private static GearItems Items() => new(new Dictionary<string, ItemInfo>(), [])
    {
        Clothing =
        [
            new ClothingItem(Id(101), "Body", ["usec"], "A"), new ClothingItem(Id(102), "Body", ["bear"], "A"),
            new ClothingItem(Id(103), "Feet", ["usec"], "A"), new ClothingItem(Id(104), "Body", ["savage"], "A"),
            new ClothingItem(Id(201), "Body", ["usec", "bear"], "B")
        ]
    };

    private static BotType Bot(Dictionary<MongoId, double>? body = null) => new()
    {
        BotAppearance = new Appearance
        {
            Body = body ?? new() { [Id(1)] = 10, [Id(2)] = 30 },
            Feet = new() { [Id(3)] = 6 },
            Hands = new(), Head = new(), Voice = new()
        }
    };

    private static int Run(BotType bot, string json, string role = "usec", string group = "pmc")
    {
        var g = Cfg(json);
        var items = Items();
        var c = GearApply.Context(g, new GearPick(role, group, 2, group == "pmc"), items, GearRaid.Pick(items, false, 0, 0, 0, 1), false, new Random(1));
        return GearClothing.Apply(bot, c, new Random(1));
    }

    private static Dictionary<string, double> D(Dictionary<MongoId, double> d) => d.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

    [Fact]
    public void 의상_설정이_없으면_외형을_바꾸지_않는다()
    {
        var bot = Bot(new() { [Id(1)] = 10, [Id(201)] = 5 });
        Assert.Equal(0, Run(bot, "{}"));
        Assert.Equal(new Dictionary<string, double> { [Id(1)] = 10, [Id(201)] = 5 }, D(bot.BotAppearance.Body));
        Assert.Null(Cfg("{}").ClothingOn);
    }

    [Fact]
    public void 허용한_모드_옷만_진영에_맞게_더한다()
    {
        var bot = Bot();
        Assert.True(Run(bot, """{"mods":{"clothingOn":["A"]}}""") > 0);
        Assert.Equal(new Dictionary<string, double> { [Id(1)] = 10, [Id(2)] = 30, [Id(101)] = 20 }, D(bot.BotAppearance.Body));
        Assert.Equal(new Dictionary<string, double> { [Id(3)] = 6, [Id(103)] = 6 }, D(bot.BotAppearance.Feet));
        var half = Bot();
        Run(half, """{"mods":{"clothingOn":["A"],"mult":50}}""");
        Assert.Equal(10, half.BotAppearance.Body[Id(101)]);
        var bear = Bot();
        Run(bear, """{"mods":{"clothingOn":["A","B"]}}""", "bear");
        Assert.Equal([Id(1), Id(2), Id(102), Id(201)], D(bear.BotAppearance.Body).Keys.Order());
    }

    [Fact]
    public void 허용_안_한_모드_옷은_뺀다()
    {
        var bot = Bot(new() { [Id(1)] = 10, [Id(2)] = 30, [Id(201)] = 5 });
        Run(bot, """{"mods":{"clothingOn":["A"]}}""");
        Assert.False(bot.BotAppearance.Body.ContainsKey(Id(201)));
        Assert.True(bot.BotAppearance.Body.ContainsKey(Id(101)));
    }

    [Fact]
    public void 빼서_칸이_비면_원래대로()
    {
        var bot = Bot(new() { [Id(201)] = 5 });
        Run(bot, """{"mods":{"clothingOn":[]}}""");
        Assert.Equal(new Dictionary<string, double> { [Id(201)] = 5 }, D(bot.BotAppearance.Body));
    }

    [Fact]
    public void 보스_호위_특수봇은_건드리지_않는다()
    {
        foreach (var (role, group) in new[] { ("boss_killa", "boss"), ("f_glukhar", "follower"), ("cultist", "special"), ("ruaf", "faction"), ("marksman", "scavs") })
        {
            var bot = Bot(new() { [Id(1)] = 10, [Id(201)] = 5 });
            Assert.Equal(0, Run(bot, """{"mods":{"clothingOn":["A"]}}""", role, group));
            Assert.Equal(2, bot.BotAppearance.Body.Count);
        }
    }

    [Fact]
    public void 장비_모듈_꺼짐이면_아무것도_안_한다()
    {
        var bot = Bot();
        Assert.Equal(0, Run(bot, """{"modules":{"gear":false},"mods":{"clothingOn":["A"]}}"""));
        Assert.Equal(2, bot.BotAppearance.Body.Count);
    }

    [Fact]
    public void 목록은_원본에_없고_출처를_찾은_상의_하의만()
    {
        var custom = new (string, string?, IEnumerable<string>?)[]
        {
            (Id(1), "Body", ["Usec"]), (Id(101), "Body", ["Usec", "Bear"]), (Id(102), "Feet", ["Savage"]),
            (Id(103), "Head", ["Usec"]), (Id(104), "Body", ["Usec"]), (Id(105), "Body", ["Other"])
        };
        var list = GearClothing.Build(custom, new HashSet<string> { Id(1) },
            new Dictionary<string, string> { [Id(101)] = "A", [Id(102)] = "A", [Id(103)] = "A", [Id(105)] = "A" });
        Assert.Equal([Id(101), Id(102)], list.Select(x => x.Id));
        Assert.Equal(["usec", "bear"], list[0].Sides);
        Assert.Empty(GearClothing.Build(custom, null, new Dictionary<string, string> { [Id(101)] = "A" }));
    }

    [Fact]
    public void 장비_적용_끝에서_의상도_적용된다()
    {
        var g = Cfg("""{"mods":{"clothingOn":["A"]}}""");
        var items = Items();
        var bot = Bot();
        bot.BotInventory = new BotTypeInventory { Equipment = new(), Ammo = new(), Mods = new(), Items = new ItemPools { Backpack = new(), Pockets = new(), TacticalVest = new(), SpecialLoot = new(), SecuredContainer = new() } };
        bot.BotChances = new Chances { EquipmentChances = new(), WeaponModsChances = new(), EquipmentModsChances = new() };
        bot.BotGeneration = new Generation { Items = new GenerationWeightingItems() };
        GearApply.Apply(bot, g, new GearPick("usec", "pmc", 2, true), items, GearRaid.Pick(items, false, 0, 0, 0, 1), false, new Random(1));
        Assert.True(bot.BotAppearance.Body.ContainsKey(Id(101)));
    }

    [Fact]
    public void 스캐브는_스캐브_옷만()
    {
        var bot = Bot();
        Run(bot, """{"mods":{"clothingOn":["A","B"]}}""", "scav", "scavs");
        Assert.Equal([Id(1), Id(2), Id(104)], D(bot.BotAppearance.Body).Keys.Order());
        Assert.Equal([Id(3)], D(bot.BotAppearance.Feet).Keys);
    }
}
