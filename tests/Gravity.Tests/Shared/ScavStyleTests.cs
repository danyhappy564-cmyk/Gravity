using Gravity.Shared;
using Xunit;

namespace Gravity.Tests.Shared;

public class ScavStyleTests
{
    private static ScavSchedule Make(string style, InfDto? inf = null, double delay = 0) =>
        new(new ScavDto(true, 1, 1, 10, 10, 30, 3, 18, 20, 0, delay, style), inf, 600, new Random(1));

    [Fact]
    public void Infinite_IgnoresQuietWindow_UsesScavRefillInterval()
    {
        var s = Make("infinite");
        Assert.Equal(1, s.Tick(0, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(0, s.Tick(29, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(1, s.Tick(30, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(1, s.Tick(90, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(0, s.Tick(600, 24, 0, 0, 0, 0, 1, out _));
    }

    [Fact]
    public void Infinite_RespectsDelayAndCaps()
    {
        var s = Make("infinite", delay: 60);
        Assert.Equal(0, s.Tick(0, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(0, s.Tick(60, 24, 24, 0, 0, 0, 1, out _));
        Assert.Equal(0, s.Tick(62, 24, 18, 18, 0, 18, 1, out _));
        Assert.Equal(1, s.Tick(64, 24, 17, 17, 0, 17, 1, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartOnly_NeverRefillsScavs_ButKeepsPmcTurns(bool infinitePmc)
    {
        var s = Make("start", infinitePmc ? new InfDto(10, 2, 1, 0) : null);
        for (var t = 0; t < 1200; t += 2)
        {
            Assert.Equal(0, s.Tick(t, 24, 0, 0, 0, 0, 1, out var pmc));
            if (t == 0) Assert.Equal(infinitePmc, pmc);
        }
    }

    [Fact]
    public void InfiniteScavs_AreIndependentOfPmcCadence()
    {
        var s = Make("infinite", new InfDto(120, 2, 1, 0));
        s.Tick(0, 24, 0, 0, 0, 0, 1, out var firstPmc);
        Assert.True(firstPmc);
        Assert.Equal(1, s.Tick(2, 24, 0, 0, 0, 0, 1, out _));
        Assert.Equal(1, s.Tick(32, 24, 0, 0, 0, 0, 1, out var pmc));
        Assert.False(pmc);
        s.Tick(120, 24, 0, 0, 0, 0, 1, out pmc);
        Assert.True(pmc);
    }
}
