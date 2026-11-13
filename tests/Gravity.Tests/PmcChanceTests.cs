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
