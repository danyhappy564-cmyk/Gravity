using System.Text.Json.Nodes;
using Gravity.Client;
using Gravity.Core;
using Gravity.Shared;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Xunit;

namespace Gravity.Tests;

/// <summary>1.0 종류별 상한 표(사용자 11-10)와 맵별 등장 목록(레이더·로그 원래 스폰 유지).</summary>
public class CapTableTests
{
    private static GravityConfig Cfg(string json) => ConfigModel.FromState(JsonNode.Parse(json), []);

    [Theory]
    [InlineData("pmcUSEC", CapKinds.Pmc)]
    [InlineData("pmcBEAR", CapKinds.Pmc)]
    [InlineData("assault", CapKinds.Scav)]
    [InlineData("marksman", CapKinds.Scav)]
    [InlineData("bossBully", CapKinds.Boss)]
    [InlineData("followerBigPipe", CapKinds.Boss)]
    [InlineData("tagillaHelperAgro", CapKinds.Boss)]
    [InlineData("pmcBot", CapKinds.Special)]
    [InlineData("exUsec", CapKinds.Special)]
    [InlineData("sectantWarrior", CapKinds.Special)]
    [InlineData("arenaFighterEvent", CapKinds.Special)]
    [InlineData("infectedPmc", CapKinds.Event)]
    [InlineData("gifter", CapKinds.Event)]
    [InlineData("sectantOni", CapKinds.Event)]
    [InlineData("shooterBTR", CapKinds.Other)]
    [InlineData("848400", CapKinds.Other)]
    public void Kinds_ByRole(string role, string kind) => Assert.Equal(kind, CapKinds.Of(role));

    [Fact]
    public void Kinds_FactionRolesFirst()
    {
        var f = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "848400", "ruafRifleman", "bossWedge" };
        Assert.Equal(CapKinds.Faction, CapKinds.Of("848400", f));
        Assert.Equal(CapKinds.Faction, CapKinds.Of("RuafRifleman", f));
        Assert.Equal(CapKinds.Faction, CapKinds.Of("bossWedge", f)); // 블랙 디비전 웨지는 보스 이름이지만 팩션
    }

    [Theory]
    [InlineData(0, 10, 3, 3)]   // 최대 없음
    [InlineData(6, 2, 3, 3)]    // 4명 들어감
    [InlineData(6, 4, 3, 1)]    // 2자리: 분대장 + 호위 1
    [InlineData(6, 5, 3, 0)]    // 1자리: 분대장만
    [InlineData(6, 6, 3, -1)]   // 꽉 참: 건너뜀
    public void FitEscorts_TrimsOrSkips(int max, int alive, int escorts, int expect) =>
        Assert.Equal(expect, CapKinds.FitEscorts(max, alive, escorts));

    [Fact]
    public void ReserveLeft_OnlyUnfilledPart()
    {
        Assert.Equal(6, CapKinds.ReserveLeft(6, 0));
        Assert.Equal(2, CapKinds.ReserveLeft(6, 4));
        Assert.Equal(0, CapKinds.ReserveLeft(6, 9));
    }

    [Fact]
    public void Config_DefaultsSeparate_AndOldShareCapBecomesRespect()
    {
        var d = Cfg("{}").Spawn.CapsOrDefault;
        Assert.Equal(0, d.PmcReserve);
        foreach (var k in CapKinds.Table) Assert.Equal(CapKinds.Separate, d.For(k).Mode);
        Assert.Equal(CapKinds.Respect, Cfg("""{"factions":{"ruaf":{"shareCap":true}}}""").Spawn.CapsOrDefault.For(CapKinds.Faction).Mode);
        var c = Cfg("""{"spawn":{"caps":{"pmcReserve":6,"pmcGroups":3,"boss":{"mode":"respect","max":4},"event":{"mode":"bogus"}}}}""").Spawn.CapsOrDefault;
        Assert.Equal(6, c.PmcReserve);
        Assert.Equal(3, c.PmcGroups);
        Assert.Equal(new CapKindSettings(CapKinds.Respect, 4), c.For(CapKinds.Boss));
        Assert.Equal(CapKinds.Separate, c.For(CapKinds.Event).Mode); // 모르는 값은 기본
    }

    [Fact]
    public void Config_MapLimitsCardOverridesCaps()
    {
        var cfg = Cfg("""
        {"spawn":{"caps":{"pmcReserve":2}},
         "maps":{"bigmap":{"ovr":{"limits":true},"caps":{"pmcReserve":8,"special":{"mode":"vanilla","max":5}}},
                 "woods":{"caps":{"pmcReserve":9}}}}
        """);
        Assert.Equal(8, cfg.SpawnFor("bigmap").CapsOrDefault.PmcReserve);
        Assert.Equal(new CapKindSettings(CapKinds.Vanilla, 5), cfg.SpawnFor("bigmap").CapsOrDefault.For(CapKinds.Special));
        Assert.Equal(2, cfg.SpawnFor("woods").CapsOrDefault.PmcReserve); // 카드 꺼짐: 공통 값
    }

    private static LocationBase Loc() => new()
    {
        Id = "bigmap",
        BossLocationSpawn =
        [
            new BossLocationSpawn { BossName = "pmcUSEC", IgnoreMaxBots = true },
            new BossLocationSpawn { BossName = "bossBully", IgnoreMaxBots = true },
            new BossLocationSpawn { BossName = "pmcBot", IgnoreMaxBots = false },
            new BossLocationSpawn { BossName = "sectantPriest", IgnoreMaxBots = true },
            new BossLocationSpawn { BossName = "ruafRifleman", IgnoreMaxBots = false },
            new BossLocationSpawn { BossName = "gifter", IgnoreMaxBots = true },
            new BossLocationSpawn { BossName = "shooterBTR", IgnoreMaxBots = true }
        ]
    };

    private static bool Ign(LocationBase l, string n) => l.BossLocationSpawn.First(e => e.BossName == n).IgnoreMaxBots == true;

    [Fact]
    public void Applier_SetsIgnoreMaxBotsByMode()
    {
        var f = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ruafRifleman" };
        var l = Loc();
        CapApplier.Apply(l, CapTable.Default, f);
        Assert.True(Ign(l, "pmcBot"));        // 특수 봇 따로 셈 → 상한 무시
        Assert.True(Ign(l, "ruafRifleman"));  // 팩션 따로 셈
        Assert.True(Ign(l, "pmcUSEC"));       // PMC는 그대로
        Assert.True(Ign(l, "shooterBTR"));    // 모르는 역할 그대로

        var l2 = Loc();
        var t = Cfg("""{"spawn":{"caps":{"boss":{"mode":"respect"},"special":{"mode":"vanilla"},"faction":{"mode":"respect"},"event":{"mode":"respect"}}}}""").Spawn.CapsOrDefault;
        CapApplier.Apply(l2, t, f);
        Assert.False(Ign(l2, "bossBully"));
        Assert.False(Ign(l2, "pmcBot"));      // 원본대로: 원래 false
        Assert.True(Ign(l2, "sectantPriest"));// 원본대로: 원래 true
        Assert.False(Ign(l2, "ruafRifleman"));
        Assert.False(Ign(l2, "gifter"));
        Assert.True(Ign(l2, "pmcUSEC"));
    }

    [Fact]
    public void ClientDto_CarriesCapsAndFactionRoles()
    {
        var dto = ClientSettingsBuilder.CapsDto(Cfg("""{"spawn":{"caps":{"pmcReserve":4,"faction":{"mode":"respect","max":6}}}}""").Spawn.CapsOrDefault, true, false);
        Assert.Equal(4, dto.PmcReserve);
        Assert.Equal(new CapKindDto(CapKinds.Respect, 6), dto.Kinds[CapKinds.Faction]);
        Assert.Contains("ruafRifleman", dto.FactionRoles!);
        Assert.DoesNotContain("blackDivLead", dto.FactionRoles!);
        Assert.False(CapKinds.Excluded(dto, CapKinds.Faction));
        Assert.True(CapKinds.Excluded(dto, CapKinds.Boss));
        Assert.Equal(6, CapKinds.MaxOf(dto, CapKinds.Faction));
        Assert.Equal(0, CapKinds.MaxOf(null, CapKinds.Faction));
    }

    [Fact]
    public void Schedule_ScavReserveLeavesRoomForPmc()
    {
        var sc = new ScavDto(true, 30, 30, 1, 1, 2, 3, 18, 20, 0, 0, "waves");
        var s = new ScavSchedule(sc, null, 100000, new Random(1));
        // 상한 24, 점유 20: 예약 없으면 스캐브 1명, PMC 예약 4자리가 남아 있으면 0명(상한 막힘)
        Assert.Equal(1, s.Tick(10, 24, 20, 0, 0, 0, 1, out _));
        var s2 = new ScavSchedule(sc, null, 100000, new Random(1));
        Assert.Equal(0, s2.Tick(10, 24, 20, 0, 0, 0, 1, out _, 4));
        Assert.Equal(ScavBlock.Cap, s2.LastBlock);
        Assert.Equal(1, s2.Tick(12, 24, 20, 0, 0, 0, 1, out _, 3));
    }

    [Fact]
    public void RaidersKeep_PerMap()
    {
        var l = new LocationBase
        {
            Id = "rezervbase",
            BossLocationSpawn = [new BossLocationSpawn { BossName = "pmcBot", BossChance = 40, BossZone = "ZoneRaiders", BossEscortAmount = "1,2" }]
        };
        RaidSpawnApplier.ApplyBosses(l, Cfg("""{"specials":{"raiders":{"keep":{"rezervbase":false}}}}"""), "rezervbase", 1, 2400);
        Assert.Equal(0, l.BossLocationSpawn[0].BossChance);
        var l2 = new LocationBase
        {
            Id = "laboratory",
            BossLocationSpawn = [new BossLocationSpawn { BossName = "pmcBot", BossChance = 40, BossZone = "ZoneRaiders", BossEscortAmount = "1,2" }]
        };
        RaidSpawnApplier.ApplyBosses(l2, Cfg("""{"specials":{"raiders":{"keep":{"rezervbase":false}}}}"""), "laboratory", 1, 2400);
        Assert.Equal(40, l2.BossLocationSpawn[0].BossChance);
    }
}
