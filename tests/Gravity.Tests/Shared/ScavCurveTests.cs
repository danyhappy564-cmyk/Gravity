using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Shared;
using Gravity.Spawn;
using Xunit;

namespace Gravity.Tests.Shared;

/// <summary>1.0.6 스캐브 시간대별 강도(역방향 웨이브): 클라 ScavSchedule 상한·간격, 서버 웨이브 인원.</summary>
public class ScavCurveTests
{
    private static ScavDto Dto(double[]? curve, double raid = 1800, double offset = 0, string style = "infinite") =>
        new(true, 3, 7, 1, 3, 30, 10, 10, 100, 0, 0, style, 0, 0, curve, raid, offset);

    [Fact]
    public void NoCurve_IsOne_EverySecond()
    {
        for (var t = 0; t <= 1800; t += 300) Assert.Equal(1, ScavSchedule.CurveAt(Dto(null), t));
    }

    [Fact]
    public void Reverse_EarlyHalf_LateOneAndHalf_ClampedAndOffset()
    {
        var d = Dto([0.5, 1, 1.5]);
        Assert.Equal(0.5, ScavSchedule.CurveAt(d, 100));
        Assert.Equal(1, ScavSchedule.CurveAt(d, 900));
        Assert.Equal(1.5, ScavSchedule.CurveAt(d, 1500));
        Assert.Equal(1.5, ScavSchedule.CurveAt(Dto([0.5, 1, 1.5], 1800, 1300), 100)); // 스캐브 레이드 지연만큼 뒤 칸
        Assert.Equal(0.2, ScavSchedule.CurveAt(Dto([0, 1, 9]), 10));
        Assert.Equal(3, ScavSchedule.CurveAt(Dto([0, 1, 9]), 1700));
    }

    [Fact]
    public void SoftCap_Follows_Curve_MapCapUnchanged()
    {
        // softCap 10: 초반 ×0.5 → 살아 있는 스캐브 5명에서 멈춤, 후반 ×1.5 → 15명까지(맵 상한 30 안).
        var early = new ScavSchedule(Dto([0.5, 1, 1.5], style: "start"), null, 99999, new Random(1));
        var s = new ScavSchedule(Dto([0.5, 1, 1.5]), null, 99999, new Random(1));
        Assert.Equal(0, s.Tick(100, 30, 5, 5, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.SoftCap, s.LastBlock);
        var late = new ScavSchedule(Dto([0.5, 1, 1.5]), null, 99999, new Random(1));
        Assert.Equal(1, late.Tick(1500, 30, 12, 12, 0, 0, 1, out _));
        // 맵 상한은 곡선과 상관없이 막는다
        var full = new ScavSchedule(Dto([0.5, 1, 3]), null, 99999, new Random(1));
        Assert.Equal(0, full.Tick(1500, 20, 20, 12, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.Cap, full.LastBlock);
        Assert.Equal(0, early.Tick(1500, 30, 0, 0, 0, 0, 1, out _));
    }

    [Fact]
    public void NoCurve_SameResultsAsBefore()
    {
        var a = new ScavSchedule(Dto(null), null, 99999, new Random(7));
        var b = new ScavSchedule(Dto([1, 1, 1]), null, 99999, new Random(7));
        for (var t = 0f; t < 1800; t += 2)
            Assert.Equal(a.Tick(t, 30, 5, 5, 0, 0, 1, out _), b.Tick(t, 30, 5, 5, 0, 0, 1, out _));
    }

    [Fact]
    public void Config_And_ServerWaves()
    {
        var cfg = ConfigModel.FromState(JsonNode.Parse("""{"spawn":{"scav":{"curve":{"early":0.5,"mid":1,"late":1.5}}}}"""), []);
        Assert.Equal([0.5, 1, 1.5], cfg.Spawn.Scav.Curve!);
        Assert.Null(ConfigModel.FromState(JsonNode.Parse("{}"), []).Spawn.Scav.Curve);
        Assert.Equal(0.5, SpawnPlanner.ScavCurveAt([0.5, 1, 1.5], 5, 40));
        Assert.Equal(1.5, SpawnPlanner.ScavCurveAt([0.5, 1, 1.5], 35, 40));
        Assert.Equal(1, SpawnPlanner.ScavCurveAt(null, 35, 40));
    }
}
