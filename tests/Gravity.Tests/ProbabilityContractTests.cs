using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Factions;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Eft.Common;
using Xunit;

namespace Gravity.Tests;

// 서버가 최종 레이드 복제본에 넣는 확률을 검사합니다. 게임의 실제 난수·발동을 대신하는 시험은 아닙니다.
public class ProbabilityContractTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(100)]
    public void FinalRaidPayloadPreservesBloodhoundAndManagedFactionChances(int chance)
    {
        var raw=JsonNode.Parse("""{"spawn":{"startMode":"count","startPmcMin":0,"startPmcMax":0},"maps":{"bigmap":{"style":"start"}},"specials":{"bloodhounds":{"chance":{"bigmap":5}}},"factions":{"blackdiv":{"managed":true,"maps":{"bigmap":true},"bd":{"chance":5}},"ruaf":{"managed":true,"maps":{"bigmap":true},"patrol":{"chance":5,"count":1},"checkpoint":{"on":true,"chance":5,"count":1},"hunt":{"on":true,"chance":5}}}}""")!;
        raw["specials"]!["bloodhounds"]!["chance"]!["bigmap"]=(double)chance;
        raw["factions"]!["blackdiv"]!["bd"]!["chance"]=(double)chance;
        raw["factions"]!["ruaf"]!["patrol"]!["chance"]=(double)chance;
        raw["factions"]!["ruaf"]!["checkpoint"]!["chance"]=(double)chance;
        raw["factions"]!["ruaf"]!["hunt"]!["chance"]=(double)chance;
        var cfg=ConfigModel.FromState(raw,[]);
        var info=new MapInfo("bigmap",12,20,2400,["ZoneA"],["ZoneS"]);
        var factions=new FactionData {BlackDivMaps=["bigmap"],Ruaf=new Dictionary<string,RuafMap>{["bigmap"]=new(["ZoneA"],["ZoneB"],"ZoneC")}};
        // 팩션·보스·이벤트·상한의 최종 처리 순서에서 확률을 검사합니다.
        for(int seed=1;seed<=100;seed++)
        {
            var loc=new LocationBase{Id="bigmap",EscapeTimeLimit=40,Waves=[],BossLocationSpawn=[new BossLocationSpawn{BossName="arenaFighterEvent",BossChance=80,BossEscortAmount="3,4",BossZone="ZoneA",Time=-1}]};
            FactionRaid.Apply(loc,cfg,"bigmap",seed,2400,factions,true,true,["ZoneA"]);
            RaidSpawnApplier.ApplyBosses(loc,cfg,"bigmap",seed,2400);
            Gravity.Events.EventRaid.Apply(loc,cfg,"bigmap",seed,true,true);
            CapApplier.Apply(loc,cfg.SpawnFor("bigmap").CapsOrDefault,FactionRaid.FactionRoleSet(true,true));
            var blood=Assert.Single(loc.BossLocationSpawn,x=>x.BossName=="arenaFighterEvent");
            Assert.Equal(chance,blood.BossChance);
            foreach(var role in new[]{"blackDivAssault","ruafRiflemanSenior"})
            {
                var groups=loc.BossLocationSpawn.Where(x=>x.BossName==role).ToArray();
                if(chance==0)Assert.Empty(groups);
                else
                {
                    Assert.Equal(role=="ruafRiflemanSenior"?3:1,groups.Length);
                    Assert.All(groups,g=>Assert.Equal(chance,g.BossChance));
                }
            }
            var build=new GravityRuntime.RaidBuild(new SpawnPlan("bigmap",2400,0,seed,"start",[],[],24,0),info,null,null,0,null,null);
            var row=GravityRuntime.Roll(loc,build,cfg,"bigmap",true,"original",new Random(seed));
            if(chance==0)Assert.DoesNotContain("bloodhounds",row.Specials);
            if(chance==100)Assert.Contains("bloodhounds",row.Specials);
        }
    }
}
