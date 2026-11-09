using System.Text.Json;
using System.Text.Json.Nodes;
using Gravity.Client;
using Gravity.Core;
using Gravity.Shared;
using Gravity.Spawn;
using Xunit;

namespace Gravity.Tests;

public class UxSettingsTests
{
    private static readonly MapInfo Map = new("bigmap", 16, 20, 2400, ["A"], ["A"]);
    private static GravityConfig Read(string json) => ConfigModel.FromState(JsonNode.Parse(json), []);

    [Fact]
    public void OldState_DefaultsToWaveStyles()
    {
        var cfg = Read("{}");
        Assert.Equal("waves", cfg.MapFor("bigmap").ScavStyle);
        Assert.Equal("waves", cfg.Ruaf!.Style);
        Assert.Equal("waves", cfg.BlackDiv!.Style);
    }

    [Theory]
    [InlineData("waves")]
    [InlineData("infinite")]
    [InlineData("start")]
    public void ScavStyle_ReachesClient(string style)
    {
        var cfg = Read($$$$"""{"maps":{"bigmap":{"scavStyle":"{{{{style}}}}"}}}""");
        var dto = ClientSettingsBuilder.Build(cfg, Map, SpawnPlanner.Plan(cfg, Map, 2400, 0, 1), true);
        Assert.Equal(style, cfg.MapFor("bigmap").ScavStyle);
        Assert.Equal(style, dto.Scav.Style);
        Assert.True(dto.Scav.Managed);
    }

    [Fact]
    public void InvalidStyles_FallBackToWaves()
    {
        var cfg = Read("""{"maps":{"bigmap":{"scavStyle":"bad"}},"factions":{"ruaf":{"style":"infinite"},"blackdiv":{"style":"bad"}}}""");
        Assert.Equal("waves", cfg.MapFor("bigmap").ScavStyle);
        Assert.Equal("waves", cfg.Ruaf!.Style);
        Assert.Equal("waves", cfg.BlackDiv!.Style);
    }

    [Fact]
    public void ZoneWeights_LegacyAndIndependentOverrides()
    {
        var cfg = Read("""{"maps":{"bigmap":{"zones":{"A":{"w":140},"B":{"w":80,"wPmc":20},"C":{"w":60,"wScav":190},"D":{"wPmc":0,"wScav":200},"E":{"on":false,"w":160}}}}}""");
        var dto = ClientSettingsBuilder.Build(cfg, Map, SpawnPlanner.Plan(cfg, Map, 2400, 0, 1), true);
        Assert.Equal(140, dto.ZoneWeights["A"]);
        Assert.Equal(140, dto.ScavZoneWeights!["A"]);
        Assert.Equal(20, dto.ZoneWeights["B"]);
        Assert.Equal(80, dto.ScavZoneWeights["B"]);
        Assert.Equal(60, dto.ZoneWeights["C"]);
        Assert.Equal(190, dto.ScavZoneWeights["C"]);
        Assert.Equal(0, dto.ZoneWeights["D"]);
        Assert.Equal(200, dto.ScavZoneWeights["D"]);
        Assert.False(dto.ZoneWeights.ContainsKey("E"));
        Assert.False(dto.ScavZoneWeights.ContainsKey("E"));
    }

    [Fact]
    public void ZoneWeights_NewSlidersClampTo200_LegacyWeightRemainsReadable()
    {
        var cfg = Read("""{"maps":{"bigmap":{"zones":{"A":{"wPmc":999,"wScav":-10},"B":{"w":400}}}}}""");
        Assert.Equal(200, cfg.MapFor("bigmap").ZoneWeights["A"]);
        Assert.Equal(0, cfg.MapFor("bigmap").ScavZoneWeights!["A"]);
        Assert.Equal(400, cfg.MapFor("bigmap").ZoneWeights["B"]);
        Assert.Equal(400, cfg.MapFor("bigmap").ScavZoneWeights!["B"]);
    }

    [Fact]
    public void Dto_OldJsonDefaultsAndNewFieldsRoundTrip()
    {
        var opt = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var dto = ClientSettingsBuilder.Unmanaged("bigmap");
        var node = JsonSerializer.SerializeToNode(dto, opt)!;
        node.AsObject().Remove("scavZoneWeights");
        node["scav"]!.AsObject().Remove("style");
        var old = node.Deserialize<ClientRaidSettings>(opt)!;
        Assert.Equal("waves", old.Scav.Style);
        Assert.Null(old.ScavZoneWeights);
        var cfg = Read("""{"maps":{"bigmap":{"scavStyle":"start","zones":{"A":{"wPmc":30,"wScav":170}}}}}""");
        var current = ClientSettingsBuilder.Build(cfg, Map, SpawnPlanner.Plan(cfg, Map, 2400, 0, 1), true);
        var back = JsonSerializer.Deserialize<ClientRaidSettings>(JsonSerializer.Serialize(current, opt), opt)!;
        Assert.Equal("start", back.Scav.Style);
        Assert.Equal(30, back.ZoneWeights["A"]);
        Assert.Equal(170, back.ScavZoneWeights!["A"]);
    }

    [Theory]
    [InlineData("waves")]
    [InlineData("infinite")]
    [InlineData("start")]
    public void ScavStartOnly_HasInitialWavesRegardlessOfPmcStyle(string pmcStyle)
    {
        var cfg = Read($$$$"""{"maps":{"bigmap":{"style":"{{{{pmcStyle}}}}","scavStyle":"start"}}}""");
        var plan = SpawnPlanner.Plan(cfg, Map, 2400, 0, 7);
        Assert.NotEmpty(plan.ScavWaves);
        Assert.All(plan.ScavWaves, w => Assert.True(w.IsStart));
    }
}
