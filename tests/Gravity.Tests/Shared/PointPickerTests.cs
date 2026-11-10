using Gravity.Shared;
using Xunit;

namespace Gravity.Tests.Shared;

public class PointPickerTests
{
    private static PickRules Rules(double pmc = 120, bool protect = false, double protectR = 200) =>
        new(pmc, 60, 30, protect, protectR);

    private static readonly Actor Human0 = new(new Vec3(0, 0, 0), ActorKind.Human);
    private static readonly Vec3[] NoRes = new Vec3[0];

    private static Candidate C(int id, float x, bool player = false) => new(id, new Vec3(x, 0, 0), player);

    [Fact]
    public void Pick_PrefersPlayerPoints()
    {
        var cands = new[] { C(1, 300), C(2, 400, true), C(3, 500) };
        for (int seed = 0; seed < 20; seed++)
        {
            var r = PointPicker.PickSquad(cands, new[] { Human0 }, NoRes, 1, Rules(), new Random(seed));
            Assert.Equal(2, r.Ids[0]);
            Assert.Equal("ok", r.Reason);
        }
    }

    [Fact]
    public void Pick_PmcDistance_Respected()
    {
        var cands = new[] { C(1, 100), C(2, 130) };
        for (int seed = 0; seed < 20; seed++)
        {
            var r = PointPicker.PickSquad(cands, new[] { Human0 }, NoRes, 1, Rules(), new Random(seed));
            Assert.Equal(2, r.Ids[0]);
            Assert.Equal("ok", r.Reason);
        }
    }

    [Fact]
    public void Pick_StartProtection_ExcludesRadius()
    {
        var cands = new[] { C(1, 150), C(2, 250) };
        for (int seed = 0; seed < 20; seed++)
        {
            var r = PointPicker.PickSquad(cands, new[] { Human0 }, NoRes, 1, Rules(50, true, 200), new Random(seed));
            Assert.Equal(2, r.Ids[0]);
        }
    }

    [Fact]
    public void Pick_StartProtection_NotRelaxed()
    {
        var r = PointPicker.PickSquad(new[] { C(1, 150) }, new[] { Human0 }, NoRes, 1, Rules(50, true, 200), new Random(1));
        Assert.Empty(r.Ids);
        Assert.Equal("noPoint", r.Reason);
    }

    [Fact]
    public void Pick_StartProtection_Fallback_UsesFarthestPoint()
    {
        // 공장처럼 맵 전체가 보호 반경 안이면(호출하는 쪽이 ProtectFallback=true) 가장 먼 지점을 쓴다.
        var cands = new[] { C(1, 60), C(2, 110, true), C(3, 90) };
        var rules = new PickRules(40, 30, 40, true, 120, ProtectFallback: true);
        for (int seed = 0; seed < 20; seed++)
        {
            var r = PointPicker.PickSquad(cands, new[] { Human0 }, NoRes, 2, rules, new Random(seed));
            Assert.Equal("farthest", r.Reason);
            Assert.Equal(2, r.Ids[0]);
        }
    }

    [Fact]
    public void Pick_StartProtection_Fallback_KeepsOtherRules()
    {
        // 가장 먼 지점이라도 사람·PMC 거리(완화 75%)와 예약은 지킨다.
        var rules = new PickRules(40, 30, 40, true, 120, ProtectFallback: true);
        var near = PointPicker.PickSquad(new[] { C(1, 20), C(2, 25) }, new[] { Human0 }, NoRes, 1, rules, new Random(1));
        Assert.Empty(near.Ids);
        Assert.Equal("noPoint", near.Reason);
        var reserved = new[] { new Vec3(100, 0, 0) };
        var r = PointPicker.PickSquad(new[] { C(1, 100), C(2, 50) }, new[] { Human0 }, reserved, 1, rules, new Random(1));
        Assert.Equal(new[] { 2 }, r.Ids);
    }

    [Fact]
    public void Pick_Relaxed_WhenStrictFails()
    {
        var r = PointPicker.PickSquad(new[] { C(1, 100) }, new[] { Human0 }, NoRes, 1, Rules(), new Random(1));
        Assert.Equal("relaxed", r.Reason);
        Assert.Equal(1, r.Ids[0]);
    }

    [Fact]
    public void Pick_NoValidPoint_ReturnsEmptyWithReason()
    {
        var r = PointPicker.PickSquad(new[] { C(1, 50) }, new[] { Human0 }, NoRes, 1, Rules(), new Random(1));
        Assert.Empty(r.Ids);
        Assert.Equal("noPoint", r.Reason);
    }

    [Fact]
    public void Pick_SquadWithinRadius_AndDuplicatesFirst()
    {
        var cands = new[] { C(3, 300), C(4, 310), C(5, 600) };
        for (int seed = 0; seed < 20; seed++)
        {
            var r = PointPicker.PickSquad(cands, new[] { Human0 }, NoRes, 4, Rules(), new Random(seed));
            Assert.Equal(4, r.Ids.Count);
            if (r.Ids[0] == 5) Assert.All(r.Ids, id => Assert.Equal(5, id));
            else Assert.DoesNotContain(5, r.Ids);
        }
        // 첫 지점이 3번이고 20m 안에 후보가 없으면 3번이 반복된다.
        var one = PointPicker.PickSquad(new[] { C(3, 300) }, new[] { Human0 }, NoRes, 4, Rules(), new Random(1));
        Assert.Equal(new[] { 3, 3, 3, 3 }, one.Ids);
    }

    [Fact]
    public void Pick_Reserved_Excluded()
    {
        var cands = new[] { C(1, 50), C(2, 200) };
        var reserved = new[] { new Vec3(0, 0, 0) };
        for (int seed = 0; seed < 20; seed++)
        {
            var r = PointPicker.PickSquad(cands, new Actor[0], reserved, 1,
                new PickRules(0, 0, 120, false, 0), new Random(seed));
            Assert.Equal(2, r.Ids[0]);
        }
    }

    [Fact]
    public void Ring_WithinBand_AndNotVisible()
    {
        var cands = new[] { C(1, 100), C(2, 250), C(3, 260), C(4, 400) };
        for (int seed = 0; seed < 20; seed++)
        {
            var r = PointPicker.PickRing(cands, new Vec3(0, 0, 0), 200, 300, new Vec3[0], 10,
                p => p.X == 250, new Random(seed));
            Assert.Equal("ok", r.Reason);
            Assert.Equal(new[] { 3 }, r.Ids);
        }
    }
    [Fact]
    public void HasClearPoint_DistanceFromHumans()
    {
        var pts = new[] { new Vec3(100, 0, 0), new Vec3(250, 0, 0) };
        var humans = new[] { new Vec3(0, 0, 0) };
        Assert.True(PointPicker.HasClearPoint(pts, humans, 200, false, 0));
        Assert.False(PointPicker.HasClearPoint(pts, humans, 300, false, 0));
    }

    [Fact]
    public void HasClearPoint_EveryHumanCounts()
    {
        var pts = new[] { new Vec3(250, 0, 0) };
        var humans = new[] { new Vec3(0, 0, 0), new Vec3(300, 0, 0) };
        Assert.False(PointPicker.HasClearPoint(pts, humans, 100, false, 0));
    }

    [Fact]
    public void HasClearPoint_ProtectRadiusOnlyWhileActive()
    {
        var pts = new[] { new Vec3(250, 0, 0) };
        var humans = new[] { new Vec3(0, 0, 0) };
        Assert.True(PointPicker.HasClearPoint(pts, humans, 200, false, 400));
        Assert.False(PointPicker.HasClearPoint(pts, humans, 200, true, 400));
        Assert.True(PointPicker.HasClearPoint(pts, humans, 200, true, 100)); // 보호 반경이 더 작으면 scavDist가 기준
    }

    [Fact]
    public void HasClearPoint_NoHumansOrNoPoints()
    {
        Assert.True(PointPicker.HasClearPoint(new[] { new Vec3(1, 0, 0) }, new Vec3[0], 500, true, 500));
        Assert.False(PointPicker.HasClearPoint(new Vec3[0], new Vec3[0], 0, false, 0));
    }
}
