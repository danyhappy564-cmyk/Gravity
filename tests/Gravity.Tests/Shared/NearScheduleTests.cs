using System;
using System.Collections.Generic;
using System.Linq;
using Gravity.Shared;
using Xunit;

namespace Gravity.Tests.Shared;

// Tick(경과 초, cap, 살아 있는+로딩 봇) -> 이번 차례에 넣을 근처 자리 목록
public class NearScheduleTests
{
    private static NearDto Dto(double first = 60, double interval = 120, int waveMin = 2, int waveMax = 2,
        double slotChance = 1.0, double pmcChance = 0.0, bool stopAtCap = true, bool groupScavOff = true,
        double groupChance = 0.0, int groupMin = 2, int groupMax = 3,
        bool customOn = false, string t1 = "", int cap1 = 0, string t2 = "", int cap2 = 0, double ratio2 = 0.0) =>
        new(true, first, interval, 40, 120, 20, waveMin, waveMax, slotChance, pmcChance, 5,
            false, stopAtCap, groupScavOff, groupChance, groupMin, groupMax,
            customOn, t1, cap1, t2, cap2, ratio2);

    private static NearSchedule Make(NearDto n, int seed = 1) => new(n, new Random(seed));

    [Fact]
    public void Near_BeforeFirstDelay_Empty()
    {
        var sch = Make(Dto(first: 60));
        Assert.Empty(sch.Tick(30f, 24, 0));
        Assert.NotEmpty(sch.Tick(60f, 24, 0));
    }

    [Fact]
    public void Near_Interval()
    {
        var sch = Make(Dto(first: 10, interval: 120));
        Assert.NotEmpty(sch.Tick(10f, 24, 0));
        Assert.Empty(sch.Tick(100f, 24, 0));
        Assert.NotEmpty(sch.Tick(130f, 24, 0));
    }

    [Fact]
    public void Near_StopAtCap_ReturnsZero()
    {
        var sch = Make(Dto(first: 0, stopAtCap: true));
        Assert.Empty(sch.Tick(0f, 24, 24));
        Assert.NotEmpty(sch.Tick(1f, 24, 23));

        var open = Make(Dto(first: 0, stopAtCap: false));
        Assert.NotEmpty(open.Tick(0f, 24, 24));
    }

    [Fact]
    public void Near_SlotChanceZero_Empty()
    {
        var sch = Make(Dto(first: 0, slotChance: 0.0));
        Assert.Empty(sch.Tick(0f, 24, 0));
    }

    [Fact]
    public void Near_PmcChanceOne_BothSidesAppear()
    {
        var sch = Make(Dto(first: 0, interval: 1, waveMin: 4, waveMax: 4, pmcChance: 1.0));
        var roles = new HashSet<string>();
        for (int i = 0; i < 20; i++)
            foreach (var s in sch.Tick(i * 1f, 99, 0)) roles.Add(s.Role);
        Assert.Equal(new[] { "pmcBEAR", "pmcUSEC" }, roles.OrderBy(r => r).ToArray());
    }

    [Fact]
    public void Near_ScavGroup_SizeWithinRange()
    {
        var sch = Make(Dto(first: 0, interval: 1, waveMin: 3, waveMax: 3, groupScavOff: false, groupChance: 1.0, groupMin: 2, groupMax: 4));
        for (int i = 0; i < 20; i++)
            foreach (var s in sch.Tick(i * 1f, 99, 0))
            {
                Assert.Equal("assault", s.Role);
                Assert.InRange(s.Count, 2, 4);
            }
    }

    [Fact]
    public void Near_GroupScavOff_SingleScav()
    {
        var sch = Make(Dto(first: 0, groupScavOff: true, groupChance: 1.0, groupMin: 3, groupMax: 5));
        Assert.All(sch.Tick(0f, 24, 0), s => Assert.Equal(1, s.Count));
    }

    [Fact]
    public void Near_CustomCap_Respected()
    {
        var sch = Make(Dto(first: 0, interval: 1, waveMin: 4, waveMax: 4, customOn: true, t1: "raider", cap1: 2));
        int raiders = 0;
        for (int i = 0; i < 30; i++)
            foreach (var s in sch.Tick(i * 1f, 99, 0))
                if (s.Role == "raider") raiders += s.Count;
        Assert.Equal(2, raiders);
    }

    [Fact]
    public void Near_CustomRatio2_UsesSecondType()
    {
        var sch = Make(Dto(first: 0, interval: 1, waveMin: 4, waveMax: 4, customOn: true,
            t1: "raider", cap1: 1000, t2: "cursedAssault", cap2: 1000, ratio2: 1.0));
        var roles = new HashSet<string>();
        for (int i = 0; i < 10; i++)
            foreach (var s in sch.Tick(i * 1f, 99, 0)) roles.Add(s.Role);
        Assert.Equal(new[] { "cursedAssault" }, roles.ToArray());
    }

    [Fact]
    public void Near_BossRoleBlocked()
    {
        var sch = Make(Dto(first: 0, interval: 1, waveMin: 3, waveMax: 3, customOn: true,
            t1: "bossKilla", cap1: 100, t2: "followerBully", cap2: 100, ratio2: 0.5));
        for (int i = 0; i < 20; i++)
            foreach (var s in sch.Tick(i * 1f, 99, 0))
            {
                Assert.DoesNotContain("boss", s.Role, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("follower", s.Role, StringComparison.OrdinalIgnoreCase);
            }
    }
}

// 0.3: near.useDefault
public class NearUseDefaultTests
{
    private static NearDto Dto(bool useDefault, bool customOn, int cap1 = 2, int cap2 = 1) =>
        new(true, 0, 60, 40, 120, 20, 4, 4, 1.0, 1.0, 5, false, false, true, 0, 2, 3,
            customOn, "pmcBot", cap1, "exUsec", cap2, 0.5, useDefault);

    [Fact]
    public void UseDefaultOff_AllSlotsCustom_UntilCapsFull()
    {
        var sch = new NearSchedule(Dto(false, true), new Random(3));
        var slots = sch.Tick(0f, 99, 0);
        Assert.Equal(3, slots.Count); // 자리 4개, 한도 2+1 -> 마지막 자리는 비움
        Assert.All(slots, s => Assert.Contains(s.Role, new[] { "pmcBot", "exUsec" }));
        Assert.Equal(2, slots.Count(s => s.Role == "pmcBot"));
    }

    [Fact]
    public void UseDefaultOff_CustomOff_FallsBackToDefault()
    {
        var sch = new NearSchedule(Dto(false, false), new Random(3));
        var slots = sch.Tick(0f, 99, 0);
        Assert.Equal(4, slots.Count);
        Assert.All(slots, s => Assert.StartsWith("pmc", s.Role)); // PmcChance 1.0
    }
}
