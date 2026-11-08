using System;
using Gravity.Shared;
using Xunit;

namespace Gravity.Tests.Shared;

// Tick(경과 초, cap, 살아 있는+로딩+대기 봇, 살아 있는 스캐브, 대기 수, 플레이어당 넣은 스캐브, 사람 수, out pmcTurn)
public class ScavScheduleTests
{
    private static ScavDto Dto(double refill = 120, int pending = 3, int softCap = 18, int perPlayer = 10,
        double aMin = 1, double aMax = 1, double qMin = 1, double qMax = 1) =>
        new(true, aMin, aMax, qMin, qMax, refill, pending, softCap, perPlayer, 0.1);

    private static ScavSchedule Make(ScavDto s, InfDto? inf = null, int hardStop = 100000) =>
        new(s, inf, hardStop, new Random(1));

    [Fact]
    public void Scav_QuietWindow_ReturnsZero()
    {
        var sch = Make(Dto(refill: 10));
        Assert.Equal(1, sch.Tick(0f, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(0, sch.Tick(90f, 24, 0, 0, 0, 0, 1, out _));
    }

    [Fact]
    public void Scav_ActiveAgainAfterQuiet()
    {
        var sch = Make(Dto(refill: 10));
        sch.Tick(0f, 24, 0, 0, 0, 0, 1, out _);
        Assert.Equal(0, sch.Tick(90f, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(1, sch.Tick(130f, 24, 0, 0, 0, 0, 1, out _));
    }

    [Fact]
    public void Scav_Interval_Respected()
    {
        var sch = Make(Dto(refill: 120, aMin: 10, aMax: 10));
        Assert.Equal(1, sch.Tick(0f, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(0, sch.Tick(60f, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(1, sch.Tick(120f, 24, 0, 0, 0, 0, 1, out _));
    }

    [Fact]
    public void Scav_AtMapCap_ReturnsZero()
    {
        var sch = Make(Dto(softCap: 18));
        Assert.Equal(0, sch.Tick(0f, 24, 24, 0, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.Cap, sch.LastBlock);
        Assert.Equal(1, sch.Tick(1f, 24, 23, 0, 0, 0, 1, out _));
    }

    [Fact]
    public void Scav_SoftCap_IsAbsoluteAliveScavLimit()
    {
        // softCap = 살아 있는 스캐브 절대 한도(cap에서 빼는 여유가 아님)
        var sch = Make(Dto(softCap: 6));
        Assert.Equal(0, sch.Tick(0f, 24, 10, 6, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.SoftCap, sch.LastBlock);
        Assert.Equal(1, sch.Tick(1f, 24, 10, 5, 0, 0, 1, out _));
    }

    [Theory]
    [InlineData(10)] // 미궁 기본 상한
    [InlineData(14)] // 공장 기본 상한
    [InlineData(24)] // 그 밖 맵 기본 상한
    public void Scav_DefaultLikeValues_ActuallySpawn(int cap)
    {
        // 기본값 softCap 18: 예전 산식(cap - softCap - alive)은 늘 0 이하였다
        var sch = Make(Dto(softCap: 18));
        Assert.Equal(1, sch.Tick(0f, cap, cap - 2, 3, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.None, sch.LastBlock);
    }

    [Fact]
    public void Scav_PendingLimit()
    {
        var sch = Make(Dto(pending: 3));
        Assert.Equal(0, sch.Tick(0f, 24, 0, 0, 4, 0, 1, out _));
        Assert.Equal(1, sch.Tick(1f, 24, 0, 0, 3, 0, 1, out _));
    }

    [Fact]
    public void Scav_PerPlayerLimit()
    {
        var sch = Make(Dto(perPlayer: 10));
        Assert.Equal(0, sch.Tick(0f, 24, 0, 0, 0, 10, 1, out _));
        Assert.Equal(1, sch.Tick(1f, 24, 0, 0, 0, 9, 1, out _));
    }

    [Fact]
    public void Scav_AfterHardStop_Zero()
    {
        var sch = Make(Dto(), hardStop: 30);
        Assert.Equal(0, sch.Tick(30f, 24, 0, 0, 0, 0, 1, out _));
    }

    [Fact]
    public void Scav_NoPmcTurn()
    {
        var sch = Make(Dto());
        sch.Tick(0f, 24, 0, 0, 0, 0, 1, out var pmc);
        Assert.False(pmc);
    }

    [Fact]
    public void Inf_BatchAndShare()
    {
        var sch = Make(Dto(), new InfDto(30, 2, 1.0, 0.0));
        Assert.Equal(2, sch.Tick(0f, 24, 0, 0, 0, 0, 1, out var pmc));
        Assert.True(pmc);
    }

    [Fact]
    public void Inf_BatchCappedByMapRoom()
    {
        var sch = Make(Dto(softCap: 18), new InfDto(30, 5, 0.0, 0.0));
        Assert.Equal(2, sch.Tick(0f, 24, 22, 0, 0, 0, 1, out var pmc));
        Assert.False(pmc);
    }

    [Fact]
    public void Inf_BatchCappedBySoftCap()
    {
        var sch = Make(Dto(softCap: 18), new InfDto(30, 5, 0.0, 0.0));
        Assert.Equal(2, sch.Tick(0f, 30, 16, 16, 0, 0, 1, out _));
    }

    [Fact]
    public void Inf_UsesDelayNotRefill()
    {
        var sch = Make(Dto(refill: 120, aMin: 10, aMax: 10), new InfDto(30, 1, 0.0, 0.0));
        Assert.Equal(1, sch.Tick(0f, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(0, sch.Tick(20f, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(1, sch.Tick(30f, 24, 0, 0, 0, 0, 1, out _));
    }

    [Fact]
    public void Inf_JitterStaysInRange()
    {
        // delay 100, jitter 0.5 -> next attempt between 50 and 150 s after the last
        for (int seed = 0; seed < 20; seed++)
        {
            var sch = new ScavSchedule(Dto(aMin: 60, aMax: 60), new InfDto(100, 1, 0.0, 0.5), 100000, new Random(seed));
            sch.Tick(0f, 24, 0, 0, 0, 0, 1, out _);
            Assert.Equal(0, sch.Tick(49f, 24, 0, 0, 0, 0, 1, out _));
            Assert.Equal(1, sch.Tick(151f, 24, 0, 0, 0, 0, 1, out _));
        }
    }

    [Fact]
    public void LastBlock_ReportsReason()
    {
        var sch = Make(Dto(refill: 120, softCap: 18, aMin: 10, aMax: 10), hardStop: 1000);
        Assert.Equal(0, sch.Tick(0f, 24, 24, 0, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.Cap, sch.LastBlock);
        Assert.Equal(1, sch.Tick(2f, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.None, sch.LastBlock);
        Assert.Equal(0, sch.Tick(4f, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.Interval, sch.LastBlock);
        Assert.Equal(0, sch.Tick(200f, 24, 0, 0, 4, 0, 1, out _));
        Assert.Equal(ScavBlock.Pending, sch.LastBlock);
        Assert.Equal(0, sch.Tick(200f, 24, 0, 0, 0, 10, 1, out _));
        Assert.Equal(ScavBlock.PerPlayer, sch.LastBlock);
        Assert.Equal(0, sch.Tick(200f, 24, 18, 18, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.SoftCap, sch.LastBlock);
        Assert.Equal(0, sch.Tick(1000f, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.HardStop, sch.LastBlock);
    }

    [Fact]
    public void LastBlock_Quiet()
    {
        var sch = Make(Dto(refill: 10));
        sch.Tick(0f, 24, 0, 0, 0, 0, 1, out _);
        Assert.Equal(0, sch.Tick(90f, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.Quiet, sch.LastBlock);
    }

    [Fact]
    public void Inf_AtMapCap_ZeroNoPmcTurn()
    {
        var sch = Make(Dto(softCap: 18), new InfDto(30, 2, 1.0, 0.0));
        Assert.Equal(0, sch.Tick(0f, 24, 24, 0, 0, 0, 1, out var pmc));
        Assert.False(pmc);
        Assert.Equal(ScavBlock.Cap, sch.LastBlock);
    }

    [Fact]
    public void Inf_SoftCapReached_PmcTurnStillAllowedUnderCap()
    {
        // 살아 있는 스캐브가 softCap에 닿아도 PMC 차례는 맵 상한만 본다
        var sch = Make(Dto(softCap: 6), new InfDto(30, 2, 1.0, 0.0));
        Assert.Equal(0, sch.Tick(0f, 10, 8, 6, 0, 0, 1, out var pmc));
        Assert.True(pmc);
        Assert.Equal(ScavBlock.SoftCap, sch.LastBlock);
    }

    [Fact]
    public void Inf_PerPlayer_BlocksScavOnly_NotPmcTurn()
    {
        var pmcSch = Make(Dto(perPlayer: 10), new InfDto(30, 2, 1.0, 0.0));
        Assert.Equal(0, pmcSch.Tick(0f, 10, 2, 2, 0, 50, 1, out var pmc));
        Assert.True(pmc);
        Assert.Equal(ScavBlock.PerPlayer, pmcSch.LastBlock);

        var scavSch = Make(Dto(perPlayer: 10), new InfDto(30, 2, 0.0, 0.0));
        Assert.Equal(0, scavSch.Tick(0f, 10, 2, 2, 0, 50, 1, out var pmc2));
        Assert.False(pmc2);
        Assert.Equal(ScavBlock.PerPlayer, scavSch.LastBlock);
    }

    [Fact]
    public void Inf_PmcTurn_IgnoresQuietWindowAndScavHardStop()
    {
        var sch = Make(Dto(refill: 10), new InfDto(30, 2, 1.0, 0.0), hardStop: 0);
        Assert.Equal(0, sch.Tick(90f, 10, 0, 0, 0, 0, 1, out var pmc)); // 휴식 창 + 스캐브 하드 스톱
        Assert.True(pmc);
    }

    [Fact]
    public void Inf_PmcTurn_GivesScavFallbackCount()
    {
        // PMC 차례에도 반환값 = PMC를 못 넣을 때 대신 넣을 스캐브 수(스캐브 판정을 거친 값)
        var sch = Make(Dto(softCap: 18), new InfDto(30, 2, 1.0, 0.0));
        Assert.Equal(2, sch.Tick(0f, 10, 3, 1, 0, 0, 1, out var pmc));
        Assert.True(pmc);
        Assert.Equal(ScavBlock.None, sch.LastBlock);
        Assert.Equal(0, sch.Tick(1f, 10, 3, 1, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.Interval, sch.LastBlock); // 차례는 썼다
    }

    [Fact]
    public void Inf_BlockedScavTurn_IsConsumed_WavesTurnIsNot()
    {
        var inf = Make(Dto(pending: 3), new InfDto(30, 2, 0.0, 0.0));
        Assert.Equal(0, inf.Tick(0f, 10, 0, 0, 4, 0, 1, out _));
        Assert.Equal(ScavBlock.Pending, inf.LastBlock);
        Assert.Equal(0, inf.Tick(2f, 10, 0, 0, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.Interval, inf.LastBlock);
        Assert.Equal(2, inf.Tick(30f, 10, 0, 0, 0, 0, 1, out _));

        var waves = Make(Dto(pending: 3));
        Assert.Equal(0, waves.Tick(0f, 10, 0, 0, 4, 0, 1, out _));
        Assert.Equal(1, waves.Tick(2f, 10, 0, 0, 0, 0, 1, out _));
    }

    [Fact]
    public void Inf_DefaultLabyrinthValues_KeepRefillingBothSides()
    {
        // 미궁 기본: cap 10, softCap 18, delay 45 ± 30 %, batch 2, pmcShare 50 %, perPlayer 20
        var sch = new ScavSchedule(Dto(refill: 120, softCap: 18, perPlayer: 20, aMin: 3, aMax: 7, qMin: 1, qMax: 3),
            new InfDto(45, 2, 0.5, 0.3), 100000, new Random(7));
        int pmcTurns = 0, scavs = 0, scavSpawned = 0;
        for (float t = 0; t < 2400; t += 2)
        {
            int n = sch.Tick(t, 10, 4, 2, 0, scavSpawned, 1, out var pmc);
            if (pmc) pmcTurns++;
            else { scavs += n; scavSpawned += n; }
        }
        Assert.True(pmcTurns >= 10, $"PMC 차례 {pmcTurns}");
        Assert.True(scavs >= 10, $"스캐브 {scavs}");
        // perPlayer(20)를 넘은 뒤에도 PMC 차례는 계속 온다
        int late = 0;
        for (float t = 2400; t < 3600; t += 2)
            if (sch.Tick(t, 10, 4, 2, 0, 100, 1, out var pmc) >= 0 && pmc) late++;
        Assert.True(late >= 5, $"늦은 PMC 차례 {late}");
    }
}
