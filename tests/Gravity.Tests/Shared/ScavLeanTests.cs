using System.Linq;
using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Shared;
using Gravity.Spawn;
using Xunit;

namespace Gravity.Tests.Shared;

/// <summary>1.0.8 스캐브 웨이브 몰림·인원 몰림(INBOX #58): 클라 보충 빈도·살아 있는 스캐브 상한, 서버 웨이브 시각·인원.</summary>
public class ScavLeanTests
{
    private static ScavDto Dto(double wave, double bots, double[]? curve = null, string style = "infinite") =>
        new(true, 3, 7, 1, 3, 30, 10, 10, 100, 0, 0, style, 0, 0, curve, 1800, 0, null, 0, wave, bots);

    [Fact]
    public void Zero_lean_is_the_same_as_curve_only()
    {
        for (var t = 0; t <= 1800; t += 100)
        {
            Assert.Equal(ScavSchedule.CurveAt(Dto(0, 0, [0.5, 1, 1.5]), t), ScavSchedule.RateAt(Dto(0, 0, [0.5, 1, 1.5]), t));
            Assert.Equal(ScavSchedule.CurveAt(Dto(0, 0, [0.5, 1, 1.5]), t), ScavSchedule.CapMulAt(Dto(0, 0, [0.5, 1, 1.5]), t));
        }
        var a = new ScavSchedule(Dto(0, 0), null, 99999, new System.Random(7));
        var b = new ScavSchedule(new ScavDto(true, 3, 7, 1, 3, 30, 10, 10, 100, 0, 0, "infinite", 0, 0, null, 1800, 0), null, 99999, new System.Random(7));
        for (var t = 0f; t < 1800; t += 2)
            Assert.Equal(a.Tick(t, 30, 5, 5, 0, 0, 1, out _), b.Tick(t, 30, 5, 5, 0, 0, 1, out _));
    }

    [Fact]
    public void Front_lean_refills_more_often_early_and_back_lean_later()
    {
        Assert.True(ScavSchedule.RateAt(Dto(-100, 0), 60) > 1.5);
        Assert.True(ScavSchedule.RateAt(Dto(-100, 0), 1740) < 0.5);
        Assert.True(ScavSchedule.RateAt(Dto(100, 0), 60) < 0.5);
        Assert.True(ScavSchedule.RateAt(Dto(100, 0), 1740) > 1.5);
        Assert.True(ScavSchedule.CapMulAt(Dto(0, -50), 60) > 1.3);
        Assert.Equal(1, ScavSchedule.RateAt(Dto(0, -50), 60)); // 인원 몰림은 빈도를 바꾸지 않음
    }

    [Fact]
    public void Combined_multiplier_is_clamped()
    {
        Assert.Equal(3, ScavSchedule.RateAt(Dto(-100, 0, [3, 3, 3]), 0));
        Assert.Equal(0.2, ScavSchedule.CapMulAt(Dto(0, -100, [0.2, 0.2, 0.2]), 1800));
    }

    [Fact]
    public void Soft_cap_follows_bots_lean()
    {
        // softCap 10, 인원 몰림 앞(-50): 초반 ×약 1.47 → 살아 있는 스캐브 12명이어도 더 넣음, 후반 ×약 0.53 → 6명에서 멈춤.
        var early = new ScavSchedule(Dto(0, -50), null, 99999, new System.Random(1));
        Assert.Equal(1, early.Tick(30, 40, 12, 12, 0, 0, 1, out _));
        var late = new ScavSchedule(Dto(0, -50), null, 99999, new System.Random(1));
        Assert.Equal(0, late.Tick(1750, 40, 6, 6, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.SoftCap, late.LastBlock);
    }

    [Fact]
    public void Config_reads_lean_and_old_config_is_zero()
    {
        var cfg = ConfigModel.FromState(JsonNode.Parse("""{"spawn":{"scav":{"dist":{"wave":-40,"bots":30}}}}"""), []);
        Assert.Equal(-40, cfg.Spawn.Scav.LeanWave);
        Assert.Equal(30, cfg.Spawn.Scav.LeanBots);
        var old = ConfigModel.FromState(JsonNode.Parse("{}"), []);
        Assert.Equal(0, old.Spawn.Scav.LeanWave);
        Assert.Equal(0, old.Spawn.Scav.LeanBots);
        Assert.Equal(100, ConfigModel.FromState(JsonNode.Parse("""{"spawn":{"scav":{"dist":{"wave":500}}}}"""), []).Spawn.Scav.LeanWave);
    }
}

public class ScavLeanPlanTests
{
    private static readonly MapInfo Customs = new("bigmap", 16, 20, 40 * 60, ["ZoneA", "ZoneB"], ["ZoneS"]);
    private static GravityConfig Cfg(string json) => ConfigModel.FromState(JsonNode.Parse(json), []);

    private static (double avgT, double avgEarlyN, double avgLateN) Shape(string json)
    {
        double t = 0, en = 0, ln = 0; int n = 0, ec = 0, lc = 0;
        for (var seed = 1; seed <= 30; seed++)
        {
            var waves = SpawnPlanner.Plan(Cfg(json), Customs, 2400, 0, seed).ScavWaves.Where(w => !w.IsStart).ToList();
            foreach (var w in waves)
            {
                t += w.TimeMinSec; n++;
                if (w.TimeMinSec < 800) { en += w.SlotsMax; ec++; } else if (w.TimeMinSec > 1600) { ln += w.SlotsMax; lc++; }
            }
        }
        return (t / n, en / System.Math.Max(1, ec), ln / System.Math.Max(1, lc));
    }

    [Fact]
    public void Zero_lean_plan_is_identical_to_old_config()
    {
        var a = SpawnPlanner.Plan(Cfg("{}"), Customs, 2400, 0, 42);
        var b = SpawnPlanner.Plan(Cfg("""{"spawn":{"scav":{"dist":{"wave":0,"bots":0}}}}"""), Customs, 2400, 0, 42);
        Assert.Equal(a.ScavWaves, b.ScavWaves);
    }

    [Fact]
    public void Server_waves_move_with_wave_lean_and_grow_with_bots_lean()
    {
        var flat = Shape("{}");
        Assert.True(Shape("""{"spawn":{"scav":{"dist":{"wave":-80}}}}""").avgT < flat.avgT - 60);
        Assert.True(Shape("""{"spawn":{"scav":{"dist":{"wave":80}}}}""").avgT > flat.avgT + 60);
        var front = Shape("""{"spawn":{"scav":{"dist":{"bots":-80}}}}""");
        Assert.True(front.avgEarlyN > front.avgLateN);
    }
}

public class ScavLeanClientTests
{
    private static readonly MapInfo Big = new("bigmap", 16, 20, 2400, ["ZoneA", "ZoneB"], ["ZoneS1", "ZoneS2"]);

    [Theory]
    [InlineData("waves", -40, 30)]
    [InlineData("list", 0, 0)]
    [InlineData("start", 0, 0)]
    public void Lean_is_sent_only_for_refill_styles(string style, double wave, double bots)
    {
        var cfg = ConfigModel.FromState(JsonNode.Parse("{\"spawn\":{\"scav\":{\"dist\":{\"wave\":-40,\"bots\":30}}},\"maps\":{\"bigmap\":{\"ovr\":{\"scav\":true},\"scavStyle\":\"" + style + "\",\"scav\":{\"dist\":{\"wave\":-40,\"bots\":30}}}}}"), []);
        var s = Gravity.Client.ClientSettingsBuilder.Build(cfg, Big, SpawnPlanner.Plan(cfg, Big, 2400, 0, 1), true);
        Assert.Equal(wave, s.Scav.LeanWave);
        Assert.Equal(bots, s.Scav.LeanBots);
    }
}
