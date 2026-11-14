using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Factions;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Xunit;

namespace Gravity.Tests;

/// <summary>1.0.6 트리거 스폰 조절(레버·탈출구·게임 이벤트, RaidSpawnApplier.ApplyTriggers).</summary>
public class TriggerSpawnTests
{
    private const string Exfil632 = "autoId_00632_EXFIL";

    private static LocationBase Lab() => new()
    {
        Id = "laboratory", EscapeTimeLimit = 35, BotMax = 20,
        BossLocationSpawn =
        [
            new BossLocationSpawn { BossName = "pmcBot", BossChance = 60, BossZone = "BotZoneFloor2", BossEscortType = "pmcBot", BossEscortAmount = "1,2", Time = 300 },
            new BossLocationSpawn { BossName = "pmcBot", BossChance = 35, BossZone = "BotZoneFloor1", BossEscortType = "pmcBot", BossEscortAmount = "1,2", Time = 600, TriggerName = "interactObject", TriggerId = Exfil632 },
            new BossLocationSpawn { BossName = "pmcBot", BossChance = 45, BossZone = "BotZoneGate1", BossEscortType = "pmcBot", BossEscortAmount = "1,2", Time = -1, Delay = 8, TriggerName = "interactObject", TriggerId = Exfil632 },
            new BossLocationSpawn { BossName = "pmcBot", BossChance = 40, BossZone = "BotZoneBasement", BossEscortType = "pmcBot", BossEscortAmount = "1", Time = -1, TriggerName = "interactObject", TriggerId = "autoId_00007_EXFIL" }
        ]
    };

    private static GravityConfig Cfg(string json) => ConfigModel.FromState(JsonNode.Parse(json), []);
    private static string Rule(string body) => "{\"triggers\":{\"laboratory\":{\"" + Exfil632 + "\":{\"pmcBot\":" + body + "}}}}";

    [Fact]
    public void NoRules_Unchanged()
    {
        var l = Lab();
        var before = l.BossLocationSpawn.ToList();
        Assert.Equal(0, RaidSpawnApplier.ApplyTriggers(l, Cfg("{}"), "laboratory", false));
        Assert.Equal(before, l.BossLocationSpawn);
    }

    [Fact]
    public void ChanceAndSize_OnlyThatTrigger_KeepsTriggerZoneDelayTime()
    {
        var l = Lab();
        Assert.Equal(2, RaidSpawnApplier.ApplyTriggers(l, Cfg(Rule("""{"chance":100,"min":3,"max":4}""")), "laboratory", false));
        var t = l.BossLocationSpawn.Where(e => e.TriggerId == Exfil632).ToList();
        Assert.All(t, e => { Assert.Equal(100, e.BossChance); Assert.Equal("2,3", e.BossEscortAmount); Assert.Equal("interactObject", e.TriggerName); });
        Assert.Equal("BotZoneFloor1", t[0].BossZone); Assert.Equal(600, t[0].Time);
        Assert.Equal("BotZoneGate1", t[1].BossZone); Assert.Equal(8, t[1].Delay);
        Assert.Equal(60, l.BossLocationSpawn[0].BossChance);   // 시각 무리 그대로
        Assert.Equal(40, l.BossLocationSpawn[3].BossChance);   // 다른 트리거 그대로
    }

    [Fact]
    public void RoleSwap_ToRogues_And_BlackDivOnlyWhenInstalled()
    {
        var l = Lab();
        RaidSpawnApplier.ApplyTriggers(l, Cfg(Rule("""{"role":"exUsec"}""")), "laboratory", false);
        Assert.All(l.BossLocationSpawn.Where(e => e.TriggerId == Exfil632), e => { Assert.Equal("exUsec", e.BossName); Assert.Equal("exUsec", e.BossEscortType); });

        var l2 = Lab();
        Assert.Equal(0, RaidSpawnApplier.ApplyTriggers(l2, Cfg(Rule("""{"role":"blackDivAssault"}""")), "laboratory", false));
        Assert.All(l2.BossLocationSpawn, e => Assert.Equal("pmcBot", e.BossName));

        var l3 = Lab();
        RaidSpawnApplier.ApplyTriggers(l3, Cfg(Rule("""{"role":"blackDivAssault"}""")), "laboratory", true);
        Assert.Equal(2, l3.BossLocationSpawn.Count(e => e.BossName == "blackDivAssault"));
    }

    [Fact]
    public void AlreadyZero_StaysZero_WhenKeepOriginalOff()
    {
        var l = Lab();
        var cfg = Cfg("{\"specials\":{\"raiders\":{\"vanilla\":false}}," + Rule("""{"chance":100}""")[1..]);
        RaidSpawnApplier.ApplyBosses(l, cfg, "laboratory", 1, 2100);
        RaidSpawnApplier.ApplyTriggers(l, cfg, "laboratory", false);
        Assert.All(l.BossLocationSpawn.Where(e => e.TriggerId == Exfil632), e => Assert.Equal(0, e.BossChance));
    }

    [Fact]
    public void GameEvent_ChanceOnly_NoRoleSwap_PartisanLeftToBossSettings()
    {
        var l = new LocationBase
        {
            Id = "labyrinth", EscapeTimeLimit = 30, BotMax = 10,
            BossLocationSpawn =
            [
                new BossLocationSpawn { BossName = "tagillaHelperAgro", BossChance = 50, BossEscortAmount = "0", Time = 450, TriggerName = "botEvent", TriggerId = "AGRO_HELPERS_EVENT" },
                new BossLocationSpawn { BossName = "bossPartisan", BossChance = 15, BossEscortAmount = "0", Time = -1, TriggerName = "botEvent", TriggerId = "PARTISAN_TRIGGER" }
            ]
        };
        var cfg = Cfg("""{"triggers":{"labyrinth":{"AGRO_HELPERS_EVENT":{"tagillaHelperAgro":{"chance":90,"role":"exUsec"}},"PARTISAN_TRIGGER":{"bossPartisan":{"chance":100}}}}}""");
        Assert.Equal(1, RaidSpawnApplier.ApplyTriggers(l, cfg, "labyrinth", true));
        Assert.Equal(90, l.BossLocationSpawn[0].BossChance);
        Assert.Equal("tagillaHelperAgro", l.BossLocationSpawn[0].BossName);
        Assert.Equal(15, l.BossLocationSpawn[1].BossChance);
    }

    [Fact]
    public void BlackDivGate_TakenByGravity_NotTouchedByRaiderRule()
    {
        // BlackDiv 관리 켬 + 게이트 100%: Gate1 항목이 BlackDiv로 바뀐 뒤 레이더 규칙은 남은 pmcBot에만 걸린다.
        var l = Lab();
        var data = new FactionData
        {
            LabGateVanilla = new Dictionary<string, List<BossLocationSpawn>>
            {
                [Exfil632] = l.BossLocationSpawn.Where(e => e.TriggerId == Exfil632).ToList(),
                ["autoId_00014_EXFIL"] = []
            }
        };
        var cfg = Cfg("{\"factions\":{\"blackdiv\":{\"managed\":true,\"bd\":{\"labsGate\":100,\"wedge\":false}}},\"lab\":{\"blackoutLink\":\"always\"}," + Rule("""{"chance":100,"role":"exUsec"}""")[1..]);
        FactionRaid.Apply(l, cfg, "laboratory", 1, 2100, data, false, true, []);
        RaidSpawnApplier.ApplyTriggers(l, cfg, "laboratory", true);
        var gate = Assert.Single(l.BossLocationSpawn, e => e.BossZone == "BotZoneGate1");
        Assert.Equal("blackDivAssault", gate.BossName);
        var floor = Assert.Single(l.BossLocationSpawn, e => e.TriggerId == Exfil632 && e.BossZone == "BotZoneFloor1");
        Assert.Equal("exUsec", floor.BossName);
        Assert.Equal(100, floor.BossChance);
    }

    [Fact]
    public void Config_BadKeysAndValues_Ignored()
    {
        var cfg = Cfg("""{"triggers":{"laboratory":{"bad key!":{"pmcBot":{"chance":50}},"autoId_1":{"pmcBot":{"role":"bossKilla"}},"autoId_2":{"pmcBot":{"chance":150}}}}}""");
        var rules = cfg.Triggers!["laboratory"];
        Assert.False(rules.ContainsKey("bad key!|pmcBot"));
        Assert.False(rules.ContainsKey("autoId_1|pmcBot"));   // 허용 밖 역할 → 규칙 없음
        Assert.Equal(100, rules["autoId_2|pmcBot"].Chance);   // 0~100으로 자름
    }
}

/// <summary>1.0.6 RUAF 순찰 시각 곡선.</summary>
public class PatrolCurveTests
{
    [Fact]
    public void CurveSec_LateWeighted_StaysInWindow()
    {
        int late = 0, early = 0;
        for (var seed = 1; seed <= 200; seed++)
        {
            var t = FactionRaid.CurveSec(new Random(seed), 60, 1800, [0.2, 1, 3]);
            Assert.InRange(t, 60, 1800);
            if (t >= 1220) late++; else if (t < 640) early++;
        }
        Assert.True(late > early * 5, $"late {late} early {early}");
    }

    [Fact]
    public void Config_NoCurve_IsNull_SoOldRandomOrderKept()
    {
        var cfg = ConfigModel.FromState(JsonNode.Parse("""{"factions":{"ruaf":{"patrol":{"curve":{"early":1,"mid":1,"late":1}}}}}"""), []);
        Assert.Null(cfg.Ruaf!.Patrol.Curve);
        var cfg2 = ConfigModel.FromState(JsonNode.Parse("""{"factions":{"ruaf":{"patrol":{"curve":{"late":2}}}}}"""), []);
        Assert.Equal([1, 1, 2], cfg2.Ruaf!.Patrol.Curve);
    }
}
