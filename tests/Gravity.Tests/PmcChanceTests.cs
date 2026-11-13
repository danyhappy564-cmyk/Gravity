using System.Text.Json.Nodes;
using Gravity.Core;
using Xunit;

namespace Gravity.Tests;

/// <summary>1.0.5: PMC 방탄복·헤드셋 T1 하한과 앞뒤판 확률을 SPT 원본(usec/bear) 수준으로. 옛 기본값 그대로 저장된 설정도 새 기본으로 읽는다.</summary>
public class PmcChanceTests
{
    private static GearSettings Cfg(string? json = null) => ConfigModel.FromState(json == null ? null : JsonNode.Parse(json), []).Gear!;

    [Fact]
    public void PMC_방탄복_헤드셋_하한이_원본_수준()
    {
        var g = Cfg();
        Assert.Equal(75, g.TableFor("usec", 0).Chance["armorVest"]);
        Assert.Equal(50, g.TableFor("usec", 0).Chance["earpiece"]);
        Assert.Equal(95, g.TableFor("bear", 6).Chance["armorVest"]);
        Assert.Equal(85, g.TableFor("bear", 6).Chance["earpiece"]);
        Assert.Equal(40, g.TableFor("scav", 2).Chance["armorVest"]);
        Assert.Equal(15, g.TableFor("scav", 2).Chance["earpiece"]);
        Assert.Equal(50, g.TableFor("usec", 0).Chance["headwear"]);
        Assert.Equal(100, g.RoleFor("usec").FrontPlate);
        Assert.Equal(100, g.RoleFor("bear").FrontPlate);
        Assert.Equal(35, g.RoleFor("usec").SidePlate);
        Assert.Equal(40, g.RoleFor("scav").FrontPlate);
    }

    // 1.0.5 Task 6: 판이 빠지는 원인이 등급 상한인지 확인. 상한 밖 판만 있으면 가장 가까운 등급을 되살려 칸이 비지 않아야 한다.
    [Fact]
    public void T1_PMC_조끼에_맞는_판이_전부_등급_밖이면_빈칸이_되지_않는다()
    {
        string Id(int n) => n.ToString("x24");
        var g = Cfg();
        var info = new Dictionary<string, Gravity.Gear.ItemInfo>
        {
            [Id(40)] = new(1, 4, 0, false, false, Plate: true), [Id(41)] = new(1, 5, 0, false, false, Plate: true),
            [Id(42)] = new(1, 5, 0, false, false, Plate: true)
        };
        var items = new Gravity.Gear.GearItems(info, [("mods", Id(40)), ("mods", Id(41)), ("mods", Id(42))]);
        var c = Gravity.Gear.GearApply.Context(g, new Gravity.Gear.GearPick("usec", "pmc", 0, true), items, Gravity.Gear.GearRaid.Pick(items, false, 0, 0, 0, 1), false, new Random(1));
        Assert.True(c.Caps.FMax < 4, "T1 앞판 상한이 4급 미만이어야 재현 조건");
        foreach (var slot in new[] { "front_plate", "back_plate", "left_side_plate" })
        {
            var set = new HashSet<SPTarkov.Server.Core.Models.Common.MongoId> { Id(40), Id(41), Id(42) };
            Gravity.Gear.GearApply.FilterModSet(set, slot, c);
            Assert.NotEmpty(set);
            Assert.All(set, m => Assert.Equal(4, info[m.ToString()].ArmorClass));
        }
    }

    private static string PmcTable(Func<int, string> chance) =>
        "[" + string.Join(",", Enumerable.Range(0, 7).Select(t => "{\"chance\":" + chance(t) + "}")) + "]";

    private static int Old(double lo, double hi, int t) => (int)Math.Round(lo + (hi - lo) * t / 6.0, MidpointRounding.AwayFromZero);

    [Fact]
    public void 옛_기본값_그대로_저장된_PMC_표만_새_기본으로_읽는다()
    {
        var tbl = PmcTable(t => $"{{\"armorVest\":{(t == 3 ? 33 : Old(40, 95, t))},\"earpiece\":{Old(15, 85, t)},\"headwear\":{Old(50, 95, t)}}}");
        var g = Cfg("{\"tierData\":{\"pmc\":" + tbl + "},\"roles\":{\"usec\":{\"frontPlate\":90},\"bear\":{\"frontPlate\":80}}}");
        Assert.Equal(75, g.TableFor("usec", 0).Chance["armorVest"]);
        Assert.Equal(33, g.TableFor("usec", 3).Chance["armorVest"]);
        Assert.Equal(50, g.TableFor("usec", 0).Chance["earpiece"]);
        Assert.Equal(Old(50, 95, 0), g.TableFor("usec", 0).Chance["headwear"]);
        Assert.Equal(100, g.RoleFor("usec").FrontPlate);
        Assert.Equal(80, g.RoleFor("bear").FrontPlate);
        // 새 판(chanceVer 2)으로 저장된 값은 그대로 — 사용자가 일부러 40·90을 고른 것.
        var v2 = Cfg("{\"gear\":{\"chanceVer\":2},\"tierData\":{\"pmc\":" + tbl + "},\"roles\":{\"usec\":{\"frontPlate\":90}}}");
        Assert.Equal(40, v2.TableFor("usec", 0).Chance["armorVest"]);
        Assert.Equal(90, v2.RoleFor("usec").FrontPlate);
    }
}
