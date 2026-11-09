using Gravity.Shared;
using Xunit;

namespace Gravity.Tests;

public class SpawnRequestTests
{
    [Fact]
    public void 지연된_두_보스와_PMC는_전체_호위를_예약합니다()
    {
        var map = new CapLedger(); var a = new object(); var b = new object(); var pmc = new object();
        Assert.True(map.TryReserve(a, 5, 24, 17, 0, 0));
        Assert.False(map.TryReserve(b, 5, 24, 17, 0, 100));
        Assert.False(map.TryReserve(pmc, 3, 24, 17, 0, 100));
        Assert.Equal(5, map.Held(100));
    }

    [Fact]
    public void 생존으로_넘긴_인원은_중복하지_않고_오래_대기한_몫은_유지합니다()
    {
        var l = new SpawnRequestLedger(); var a = new object(); var b = new object();
        l.Claim(a, CapKinds.Boss, 3, false);
        l.Register(a, "a"); l.Register(a, "b"); l.Register(a, "c");
        l.BeginSnapshot(); l.Observe("a"); l.Observe("b"); l.Observe("c");
        Assert.Equal(0, l.Pending(CapKinds.Boss));
        Assert.Equal(3, 3 + l.Pending(CapKinds.Boss));
        l.Claim(b, CapKinds.Boss, 3, false);
        for (int i = 0; i < 20; i++) { l.BeginSnapshot(); l.Observe("a"); l.Observe("b"); l.Observe("c"); }
        Assert.Equal(3, l.Pending(CapKinds.Boss));
        Assert.Equal(0, l.Pending(CapKinds.Boss, b));
        l.Abort(b);
        Assert.Equal(0, l.Pending(CapKinds.Boss));
        l.Claim(new object(), CapKinds.Boss, 3, false);
        Assert.Equal(3, l.Pending(CapKinds.Boss));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 인원상한_무시와_무관하게_생성중_무리도_한개입니다(bool ignore)
    {
        var l = new SpawnRequestLedger(); var a = new object();
        l.Claim(a, CapKinds.Pmc, 5, true);
        Assert.Equal(1, l.PendingPmcGroups());
        Assert.Equal(0, l.PendingPmcGroups(a));
        l.Register(a, "leader"); l.BeginSnapshot(); l.Observe("leader");
        Assert.Equal(0, l.PendingPmcGroups()); // 살아 있는 BotsGroup 한 개로 넘어갑니다.
        Assert.Equal(4, l.Pending(CapKinds.Pmc));
        l.Fail(a, 4);
        l.BeginSnapshot(); // 분대도 소멸했습니다.
        Assert.Equal(0, l.PendingPmcGroups());
        _ = ignore;
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(5, 2, 3)]
    public void 호위_목록을_앞역할부터_잘라_실제_요청을_맞춥니다(int budget, int first, int second)
    {
        var list = new List<int[]> { new[] { 2 }, new[] { 3 } };
        Assert.Equal(budget, EscortBudget.Apply(list, budget, x => x[0], (x, n) => x[0] = n));
        // 0명이 된 역할은 목록에서 빠져 빈 프로필 요청을 만들지 않는다.
        Assert.Equal(new[] { first, second }.Where(n => n > 0).ToArray(), list.Select(x => x[0]).ToArray());
    }

    [Fact]
    public void 직접폐기_뒤_활성화와_대원시작을_차단합니다()
    {
        var s = new SquadLifecycle();
        Assert.True(s.SeeLeader());
        Assert.True(s.Terminate());
        Assert.False(s.SeeLeader());
        Assert.False(s.StartSupports());
    }

    [Fact]
    public void 대원시작후_분대장제거는_작업끝까지_예약합니다()
    {
        var s = new SquadLifecycle();
        Assert.True(s.SeeLeader()); Assert.True(s.StartSupports());
        Assert.False(s.Terminate());
        Assert.True(s.SupportsStarted);
        Assert.False(s.SeeLeader());
    }

    [Theory]
    [InlineData(24, 19, 4, 1)]
    [InlineData(24, 20, 4, 0)]
    [InlineData(4, 0, 0, 4)]
    public void 실제예약마다_PMC자리_바깥여유를_씁니다(int cap, int occupied, int reserve, int expected) =>
        Assert.Equal(expected, SpawnCapacity.RoomForScavs(cap, occupied, 0, 0, reserve));

    [Theory]
    [InlineData(5, 4, 4)]
    [InlineData(5, 0, 5)]
    [InlineData(3, 24, 3)]
    public void 영구불가_크기는_맵에_들어가는_첫조각으로_나눕니다(int size, int cap, int first) =>
        Assert.Equal(first, SpawnCapacity.FitPmc(size, cap));

    [Fact]
    public void ExtraSpecialMark_IsNotPlannedPmcMark()
    {
        Assert.True(WaveMark.Is(WaveMark.For("bigmap", 1, 2), ""));
        Assert.False(WaveMark.Is(WaveMark.ExtraSpecial("bigmap", "pmcBot"), ""));
        Assert.True(WaveMark.IsExtraSpecial(WaveMark.ExtraSpecial("bigmap", "pmcBot"), ""));
    }
}
