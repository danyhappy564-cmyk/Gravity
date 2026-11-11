using Gravity.Shared;
using Xunit;

namespace Gravity.Tests.Shared;

public class StartupPreparationTests
{
    [Fact]
    public void CountsOnlyInitialBodiesOnceAndRequiresFollowers()
    {
        var p=new StartupPreparation(new[]{4,2},new[]{0});
        p.Mark(0,"leader");p.Mark(0,"leader");p.Mark(1,"later");
        Assert.Equal(4,p.Expected);Assert.Equal(1,p.Prepared);
        Assert.True(p.ShouldWait(1,2,true,false,false));
        foreach(var id in new[]{"a","b","c"})p.Mark(0,id);
        Assert.Equal(4,p.Prepared);Assert.False(p.ShouldWait(2,4,true,false,false));
    }

    [Theory]
    [InlineData(60,1,true,false,false)]
    [InlineData(1,120,true,false,false)]
    [InlineData(1,1,false,false,false)]
    [InlineData(1,1,true,true,false)]
    [InlineData(1,1,true,false,true)]
    public void TimeoutCancellationAndCapacityAlwaysStop(double seconds,int polls,bool same,bool started,bool full)
    {
        Assert.False(new StartupPreparation(new[]{4},new[]{0}).ShouldWait(seconds,polls,same,started,full));
    }

    [Fact]
    public void StalledCreationCannotKeepLoadingLoopAlive()
    {
        var p=new StartupPreparation(new[]{4},new[]{0});
        int polls=0;while(p.ShouldWait(polls*.5,polls,true,false,false))polls++;
        Assert.Equal(120,polls);
    }

    [Fact]
    public void InvalidAndDuplicateWaveIndicesDoNotInflateTarget()
    {
        var p=new StartupPreparation(new[]{4},new[]{-1,0,0,2});
        Assert.Equal(4,p.Expected);
        Assert.False(new StartupPreparation(null,null).ShouldWait(0,0,true,false,false));
    }
    [Theory]
    [InlineData(false,true,false,3,3,true)]
    [InlineData(false,true,false,3,4,true)]
    [InlineData(true,true,false,3,3,false)]
    [InlineData(false,false,false,3,3,false)]
    [InlineData(false,true,true,3,3,false)]
    [InlineData(false,true,false,0,3,false)]
    [InlineData(false,true,false,3,0,false)]
    [InlineData(false,true,false,3,2,false)]
    public void OnlyInitialFollowersWithAlreadySelectedPositionsUseStartupPath(bool started,bool initial,bool terminated,int followers,int positions,bool allowed)
    {
        Assert.Equal(allowed,StartupPreparation.CanPrepareSupports(started,initial,terminated,followers,positions));
    }
}
