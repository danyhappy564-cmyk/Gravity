using System.Collections.Generic;
using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Shared;
using Gravity.Spawn;
using Xunit;

namespace Gravity.Tests;

/// <summary>1.0.8 INBOX #60: 웨이브 PMC 맵 전체 배치(spawn.wavePoints).</summary>
public class WavePointsTests
{
    private static readonly MapInfo Big = new("bigmap", 16, 20, 2400, ["ZoneA", "ZoneB"], ["ZoneS1", "ZoneS2"]);
    private static readonly List<Candidate> Cands = new() { new(1, new Vec3(0, 0, 0), true), new(2, new Vec3(500, 0, 0), false), new(3, new Vec3(900, 0, 0), false) };
    private static PickRules Rules(bool playerFirst) => new(PmcDist: 80, BotDist: 20, ReserveDist: 80, ProtectActive: false, ProtectRadius: 0, PlayerFirst: playerFirst);

    [Fact]
    public void Default_rules_still_pick_player_start_point_first()
    {
        for (int seed = 0; seed < 30; seed++)
            Assert.Equal(1, PointPicker.PickSquad(Cands, new List<Actor>(), new List<Vec3>(), 1, Rules(true), new System.Random(seed)).Ids[0]);
    }

    [Fact]
    public void Spread_rules_do_not_prefer_player_start_points()
    {
        var seen = new HashSet<int>();
        for (int seed = 0; seed < 60; seed++)
            seen.Add(PointPicker.PickSquad(Cands, new List<Actor>(), new List<Vec3>(), 1, Rules(false), new System.Random(seed)).Ids[0]);
        Assert.Contains(2, seen);
        Assert.Contains(3, seen);
    }

    [Fact]
    public void Config_default_is_pmc_and_spread_reaches_client()
    {
        Assert.Equal("pmc", ConfigModel.FromState(JsonNode.Parse("{}"), []).Spawn.WavePoints);
        Assert.Equal("pmc", ConfigModel.FromState(JsonNode.Parse("""{"spawn":{"wavePoints":"weird"}}"""), []).Spawn.WavePoints);
        var cfg = ConfigModel.FromState(JsonNode.Parse("""{"spawn":{"wavePoints":"spread"}}"""), []);
        Assert.Equal("spread", cfg.Spawn.WavePoints);
        var s = Gravity.Client.ClientSettingsBuilder.Build(cfg, Big, SpawnPlanner.Plan(cfg, Big, 2400, 0, 1), true);
        Assert.Equal("spread", s.WavePoints);
        var d = Gravity.Client.ClientSettingsBuilder.Build(ConfigModel.Default(), Big, SpawnPlanner.Plan(ConfigModel.Default(), Big, 2400, 0, 1), true);
        Assert.Equal("pmc", d.WavePoints);
    }

    [Fact]
    public void Map_hotspot_card_overrides_wave_points()
    {
        var cfg = ConfigModel.FromState(JsonNode.Parse("""{"maps":{"bigmap":{"ovr":{"hotspot":true},"wavePoints":"spread"}}}"""), []);
        Assert.Equal("spread", cfg.SpawnFor("bigmap").WavePoints);
        Assert.Equal("pmc", cfg.SpawnFor("factory4_day").WavePoints);
    }
}
