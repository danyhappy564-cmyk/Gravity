using System.Text.Json.Nodes;
using Gravity.Client;
using Gravity.Core;
using Gravity.Shared;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Xunit;

namespace Gravity.Tests;

public class StartScavRegressionTests
{
    private static readonly MapInfo Map = new("interchange", 20, 40, 2400, ["P1"], ["S1", "S2", "S3"]);
    private static GravityConfig Config(int delay=0)
    {
        var root=JsonNode.Parse("""{"spawn":{"scav":{"startMode":"count","startMin":10,"startMax":10,"delay":0,"refill":45}},"maps":{"interchange":{"scavStyle":"start"}}}""")!;
        root["spawn"]!["scav"]!["delay"]=(double)delay;
        return ConfigModel.FromState(root,[]);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void NativeMinimumCannotInflateManagedScavWaves(bool hasClient, bool keepMarksman)
    {
        var root=JsonNode.Parse("""{"spawn":{"scav":{"startMode":"count","startMin":1,"startMax":1,"delay":0}},"maps":{"interchange":{"scavStyle":"start"}}}""")!;
        root["spawn"]!["scav"]!["startMarksman"]=keepMarksman;
        var cfg=ConfigModel.FromState(root,[]);
        var source=new List<MinMaxBot>{new(){WildSpawnType="assault",Min=3,Max=20},new(){WildSpawnType="marksman",Min=2,Max=10},new(){WildSpawnType="bossBully",Min=1,Max=1}};
        var loc=new LocationBase{Id=Map.Id,BossLocationSpawn=[],Waves=[],MinMaxBots=source};
        RaidSpawnApplier.Apply(loc,SpawnPlanner.Plan(cfg,Map,2400,0,7,hasClient),cfg,Map,hasClient);
        Assert.Equal(hasClient?0:3,loc.MinMaxBots.Single(x=>x.WildSpawnType=="assault").Min);
        Assert.Equal(hasClient&&!keepMarksman?0:2,loc.MinMaxBots.Single(x=>x.WildSpawnType=="marksman").Min);
        Assert.Equal(20,loc.MinMaxBots.Single(x=>x.WildSpawnType=="assault").Max);
        Assert.Equal(1,loc.MinMaxBots.Single(x=>x.WildSpawnType=="bossBully").Min);
        Assert.Equal(3,source[0].Min);Assert.Equal(2,source[1].Min);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void StartingCountIsPreparedTogetherAtConfiguredDelay(int delay)
    {
        var p=SpawnPlanner.Plan(Config(delay),Map,2400,0,1279758348,true);
        var w=Assert.Single(p.ScavWaves,w=>w.IsStart);
        Assert.Equal(10,w.SlotsMin);
        Assert.Equal(10,w.SlotsMax);
        Assert.Equal(delay*60,w.TimeMinSec);
        Assert.Equal(delay*60,w.TimeMaxSec);
    }

    [Fact]
    public void ZeroDelayStartsLoadBeforeRaidAndKeepExplicitCountsAndZones()
    {
        var cfg=Config();var p=SpawnPlanner.Plan(cfg,Map,2400,0,7,true);
        var loc=new LocationBase {Id=Map.Id,BossLocationSpawn=[],Waves=[]};
        RaidSpawnApplier.Apply(loc,p,cfg,Map,true);
        var ws=loc.Waves.Where(w=>w.WildSpawnType==WildSpawnType.assault).ToArray();
        Assert.Equal(10,ws.Length);
        Assert.All(ws,w=>{
            Assert.Equal(-1,w.TimeMin);Assert.Equal(-1,w.TimeMax);
            Assert.StartsWith("~gravity.scav/1/1/",w.SpawnPoints);
            Assert.DoesNotContain(",",w.SpawnPoints);
        });
        Assert.Contains(ws,w=>w.SpawnPoints!.EndsWith("/S1"));
        Assert.Contains(ws,w=>w.SpawnPoints!.EndsWith("/S2"));
        Assert.Contains(ws,w=>w.SpawnPoints!.EndsWith("/S3"));
    }

    [Fact]
    public void DisabledScavZoneNeverReappearsInStartWaves()
    {
        var root=JsonNode.Parse("""{"spawn":{"scav":{"delay":0,"startMode":"count","startMin":10,"startMax":10}},"maps":{"interchange":{"scavStyle":"start","zones":{"S2":{"on":false}}}}}""");
        var cfg=ConfigModel.FromState(root,[]);var p=SpawnPlanner.Plan(cfg,Map,2400,0,7,true);
        var loc=new LocationBase{Id=Map.Id,BossLocationSpawn=[],Waves=[]};
        RaidSpawnApplier.Apply(loc,p,cfg,Map,true);
        Assert.DoesNotContain(loc.Waves,w=>w.SpawnPoints!.Contains("S2"));
    }

    [Theory]
    [InlineData("waves")]
    [InlineData("infinite")]
    public void StartBatchDoesNotReceiveAnExtraRefillAtTimeZero(string style)
    {
        var root=JsonNode.Parse("""{"spawn":{"scav":{"delay":0,"startMode":"count","startMin":10,"startMax":10,"refill":45}}}""")!;
        root["maps"]=new JsonObject {["interchange"]=new JsonObject {["scavStyle"]=style}};
        var cfg=ConfigModel.FromState(root,[]);var p=SpawnPlanner.Plan(cfg,Map,2400,0,7,true);
        var dto=ClientSettingsBuilder.Build(cfg,Map,p,true).Scav;
        var schedule=new ScavSchedule(dto,null,2000,new Random(7));
        Assert.Equal(0,schedule.Tick(0,24,10,10,0,10,1,out _));
        Assert.Equal(0,schedule.Tick(44,24,10,10,0,10,1,out _));
        Assert.Equal(1,schedule.Tick(45,24,10,10,0,10,1,out _));
    }
    [Fact]
    public void FixedStartCountsDoNotBypassLaboratoryScavOptIn()
    {
        var cfg=Config();var lab=Map with {Id="laboratory",BaseScav=0};
        Assert.Empty(SpawnPlanner.Plan(cfg,lab,2400,0,7,true).ScavWaves);
        var root=JsonNode.Parse("""{"spawn":{"allowScavLab":true,"scav":{"delay":0,"startMode":"count","startMin":10,"startMax":10}},"maps":{"laboratory":{"scavStyle":"start"}}}""");
        var allowed=ConfigModel.FromState(root,[]);
        Assert.Equal(10,SpawnPlanner.Plan(allowed,lab,2400,0,7,true).ScavWaves.Where(w=>w.IsStart).Sum(w=>w.SlotsMax));
    }}
