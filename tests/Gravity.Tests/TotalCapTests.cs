using System.Linq;
using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Spawn;
using Xunit;

namespace Gravity.Tests;

/// <summary>1.0.8 INBOX #61: 레이드 전체 봇 총량(spawn.totalCap).</summary>
public class TotalCapTests
{
    private static readonly MapInfo Big = new("bigmap", 16, 20, 2400, ["ZoneA", "ZoneB"], ["ZoneS1", "ZoneS2"]);
    private static GravityConfig Cfg(string json) => ConfigModel.FromState(JsonNode.Parse(json), []);

    [Fact]
    public void Off_by_default_and_plan_is_unchanged()
    {
        Assert.Equal(0, Cfg("{}").Spawn.TotalCap);
        var a = SpawnPlanner.Plan(Cfg("{}"), Big, 2400, 0, 5);
        var b = SpawnPlanner.Plan(Cfg("""{"spawn":{"totalCap":0}}"""), Big, 2400, 0, 5);
        Assert.Equal(a.Groups, b.Groups);
        Assert.Equal(a.ScavWaves, b.ScavWaves);
    }

    [Fact]
    public void Plan_is_trimmed_to_total_starting_from_start_bots()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            var full = SpawnPlanner.Plan(Cfg("{}"), Big, 2400, 0, seed);
            var cut = SpawnPlanner.Plan(Cfg("""{"spawn":{"totalCap":20}}"""), Big, 2400, 0, seed);
            int sum = cut.Groups.Sum(g => g.Size) + cut.ScavWaves.Sum(w => w.SlotsMax);
            Assert.True(sum <= 20, $"seed {seed}: {sum}");
            // 시작 PMC는 먼저 들어간다(뒤 웨이브부터 빠짐)
            int startFull = full.Groups.Where(g => g.IsStart).Sum(g => g.Size);
            if (startFull <= 20) Assert.Equal(startFull, cut.Groups.Where(g => g.IsStart).Sum(g => g.Size));
            Assert.True(cut.DroppedPmcs >= full.DroppedPmcs);
        }
    }

    [Fact]
    public void Map_limits_card_overrides_total_and_client_gets_it()
    {
        var cfg = Cfg("""{"spawn":{"totalCap":40},"maps":{"laboratory":{"ovr":{"limits":true},"totalCap":30}}}""");
        Assert.Equal(30, cfg.SpawnFor("laboratory").TotalCap);
        Assert.Equal(40, cfg.SpawnFor("bigmap").TotalCap);
        var s = Gravity.Client.ClientSettingsBuilder.Build(cfg, Big, SpawnPlanner.Plan(cfg, Big, 2400, 0, 1), true);
        Assert.Equal(40, s.TotalCap);
        Assert.Equal(500, Cfg("""{"spawn":{"totalCap":9999}}""").Spawn.TotalCap);
    }
}
