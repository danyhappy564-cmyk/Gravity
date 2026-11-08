using System;
using Gravity.Shared;
using Xunit;

namespace Gravity.Tests.Shared;

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
        Assert.Equal(1, sch.Tick(0f, 24, 0, 0, 0, 1, out _));
        Assert.Equal(0, sch.Tick(90f, 24, 0, 0, 0, 1, out _));
    }

    [Fact]
    public void Scav_ActiveAgainAfterQuiet()
    {
        var sch = Make(Dto(refill: 10));
        sch.Tick(0f, 24, 0, 0, 0, 1, out _);
        Assert.Equal(0, sch.Tick(90f, 24, 0, 0, 0, 1, out _));
        Assert.Equal(1, sch.Tick(130f, 24, 0, 0, 0, 1, out _));
    }

    [Fact]
    public void Scav_Interval_Respected()
    {
        var sch = Make(Dto(refill: 120, aMin: 10, aMax: 10));
        Assert.Equal(1, sch.Tick(0f, 24, 0, 0, 0, 1, out _));
        Assert.Equal(0, sch.Tick(60f, 24, 0, 0, 0, 1, out _));
        Assert.Equal(1, sch.Tick(120f, 24, 0, 0, 0, 1, out _));
    }

    [Fact]
    public void Scav_AtCap_ReturnsZero()
    {
        var sch = Make(Dto(softCap: 18));
        Assert.Equal(0, sch.Tick(0f, 24, 6, 0, 0, 1, out _));
        Assert.Equal(1, sch.Tick(1f, 24, 5, 0, 0, 1, out _));
    }

    [Fact]
    public void Scav_PendingLimit()
    {
        var sch = Make(Dto(pending: 3));
        Assert.Equal(0, sch.Tick(0f, 24, 0, 4, 0, 1, out _));
        Assert.Equal(1, sch.Tick(1f, 24, 0, 3, 0, 1, out _));
    }

    [Fact]
    public void Scav_PerPlayerLimit()
    {
        var sch = Make(Dto(perPlayer: 10));
        Assert.Equal(0, sch.Tick(0f, 24, 0, 0, 10, 1, out _));
        Assert.Equal(1, sch.Tick(1f, 24, 0, 0, 9, 1, out _));
    }

    [Fact]
    public void Scav_AfterHardStop_Zero()
    {
        var sch = Make(Dto(), hardStop: 30);
        Assert.Equal(0, sch.Tick(30f, 24, 0, 0, 0, 1, out _));
    }

    [Fact]
    public void Scav_NoPmcTurn()
    {
        var sch = Make(Dto());
        sch.Tick(0f, 24, 0, 0, 0, 1, out var pmc);
        Assert.False(pmc);
    }

    [Fact]
    public void Inf_BatchAndShare()
    {
        var sch = Make(Dto(), new InfDto(30, 2, 1.0, 0.0));
        Assert.Equal(2, sch.Tick(0f, 24, 0, 0, 0, 1, out var pmc));
        Assert.True(pmc);
    }

    [Fact]
    public void Inf_BatchCappedByFreeSlots()
    {
        var sch = Make(Dto(softCap: 18), new InfDto(30, 5, 0.0, 0.0));
        Assert.Equal(2, sch.Tick(0f, 24, 4, 0, 0, 1, out var pmc));
        Assert.False(pmc);
    }

    [Fact]
    public void Inf_UsesDelayNotRefill()
    {
        var sch = Make(Dto(refill: 120, aMin: 10, aMax: 10), new InfDto(30, 1, 0.0, 0.0));
        Assert.Equal(1, sch.Tick(0f, 24, 0, 0, 0, 1, out _));
        Assert.Equal(0, sch.Tick(20f, 24, 0, 0, 0, 1, out _));
        Assert.Equal(1, sch.Tick(30f, 24, 0, 0, 0, 1, out _));
    }

    [Fact]
    public void Inf_JitterStaysInRange()
    {
        // delay 100, jitter 0.5 -> next attempt between 50 and 150 s after the last
        for (int seed = 0; seed < 20; seed++)
        {
            var sch = new ScavSchedule(Dto(aMin: 60, aMax: 60), new InfDto(100, 1, 0.0, 0.5), 100000, new Random(seed));
            sch.Tick(0f, 24, 0, 0, 0, 1, out _);
            Assert.Equal(0, sch.Tick(49f, 24, 0, 0, 0, 1, out _));
            Assert.Equal(1, sch.Tick(151f, 24, 0, 0, 0, 1, out _));
        }
    }
    [Fact]
    public void LastBlock_ReportsReason()
    {
        var sch = Make(Dto(refill: 120, softCap: 18, aMin: 10, aMax: 10), hardStop: 1000);
        Assert.Equal(0, sch.Tick(0f, 24, 6, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.Cap, sch.LastBlock);
        Assert.Equal(1, sch.Tick(2f, 24, 0, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.None, sch.LastBlock);
        Assert.Equal(0, sch.Tick(4f, 24, 0, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.Interval, sch.LastBlock);
        Assert.Equal(0, sch.Tick(200f, 24, 0, 4, 0, 1, out _));
        Assert.Equal(ScavBlock.Pending, sch.LastBlock);
        Assert.Equal(0, sch.Tick(200f, 24, 0, 0, 10, 1, out _));
        Assert.Equal(ScavBlock.PerPlayer, sch.LastBlock);
        Assert.Equal(0, sch.Tick(1000f, 24, 0, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.HardStop, sch.LastBlock);
    }

    [Fact]
    public void LastBlock_Quiet()
    {
        var sch = Make(Dto(refill: 10));
        sch.Tick(0f, 24, 0, 0, 0, 1, out _);
        Assert.Equal(0, sch.Tick(90f, 24, 0, 0, 0, 1, out _));
        Assert.Equal(ScavBlock.Quiet, sch.LastBlock);
    }

    [Fact]
    public void Inf_AtCap_ZeroNoPmcTurn()
    {
        var sch = Make(Dto(softCap: 18), new InfDto(30, 2, 1.0, 0.0));
        Assert.Equal(0, sch.Tick(0f, 24, 6, 0, 0, 1, out var pmc));
        Assert.False(pmc);
        Assert.Equal(ScavBlock.Cap, sch.LastBlock);
    }
}
