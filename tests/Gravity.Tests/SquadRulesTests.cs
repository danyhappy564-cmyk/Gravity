using System.Collections.Generic;
using Gravity.Shared;
using Xunit;

namespace Gravity.Tests;

/// <summary>1.0.8 INBOX #59: 분대 겹침(②③)·늦은 대원(④) 규칙.</summary>
public class SquadRulesTests
{
    [Fact]
    public void Escort_on_time_is_kept_even_if_leader_died_or_human_near()
    {
        Assert.Equal(EscortVerdict.Keep, SquadRules.JudgeEscort(lateSec: 2, leaderAlive: false, humanDist: 5, minHumanDist: 60));
    }

    [Fact]
    public void Late_escort_after_leader_death_is_dropped()
        => Assert.Equal(EscortVerdict.DropLeaderDead, SquadRules.JudgeEscort(150, false, 500, 60));

    [Fact]
    public void Late_escort_next_to_a_human_is_dropped()
        => Assert.Equal(EscortVerdict.DropNearHuman, SquadRules.JudgeEscort(150, true, 20, 60));

    [Fact]
    public void Late_escort_far_from_humans_with_live_leader_is_kept()
        => Assert.Equal(EscortVerdict.Keep, SquadRules.JudgeEscort(150, true, 200, 60));

    [Fact]
    public void Report_counters_use_fixed_thresholds()
    {
        Assert.True(SquadRules.Overlaps(29.9));
        Assert.False(SquadRules.Overlaps(30));
        Assert.True(SquadRules.IsLate(60));
        Assert.False(SquadRules.IsLate(59));
    }

    [Fact]
    public void In_flight_point_blocks_until_cap_from_first_seen()
    {
        var f = new InFlightPoints();
        var entry = new object();
        var into = new List<Vec3>();
        Assert.True(f.Add(entry, new Vec3(1, 0, 1), now: 100, maxSec: 240, into));
        Assert.True(f.Add(entry, new Vec3(1, 0, 1), now: 339, maxSec: 240, into));
        Assert.False(f.Add(entry, new Vec3(1, 0, 1), now: 340, maxSec: 240, into));
        Assert.Equal(2, into.Count);
    }

    [Fact]
    public void Squad_picker_skips_points_near_a_squad_still_loading()
    {
        // 분대 A가 (0,0,0)을 골라 몸을 읽는 중(살아 있는 목록에는 아직 없음). 분대 B 후보 중 A 옆 지점은 빠져야 한다.
        var f = new InFlightPoints();
        var reserved = new List<Vec3>();
        f.Add(new object(), new Vec3(0, 0, 0), 10, SquadRules.InFlightMaxSec, reserved);
        var cands = new List<Candidate> { new(1, new Vec3(10, 0, 0), false), new(2, new Vec3(400, 0, 0), false) };
        var rules = new PickRules(PmcDist: 80, BotDist: 20, ReserveDist: 80, ProtectActive: false, ProtectRadius: 0);
        for (int seed = 0; seed < 20; seed++)
        {
            var r = PointPicker.PickSquad(cands, new List<Actor>(), reserved, 1, rules, new System.Random(seed));
            Assert.Equal(2, r.Ids[0]);
        }
    }
}
