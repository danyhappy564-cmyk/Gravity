using Gravity.Shared;
using Xunit;

namespace Gravity.Tests.Shared;

public class WeightedOrderTests
{
    private static double W(string s) => s == "A" ? 100 : 1;

    private static double FirstIsA(double share)
    {
        int a = 0;
        for (int seed = 1; seed <= 200; seed++)
            if (WeightedOrder.Shuffle(new[] { "A", "B" }, W, share, new Random(seed))[0] == "A") a++;
        return a / 200.0;
    }

    // share는 0~1 비율(서버가 보내는 모든 퍼센트 값과 같은 규칙). 0이면 균등 섞기만.
    [Fact]
    public void Weighted_ShareZero_IsUniformOnly()
    {
        Assert.InRange(FirstIsA(0.0), 0.35, 0.65);
    }

    // share 1.0(=100%)이면 가중 추첨 순서만.
    [Fact]
    public void Weighted_Share100_HeavyFirst()
    {
        Assert.True(FirstIsA(1.0) > 0.9);
    }

    [Fact]
    public void Weighted_KeepsAllItems_AndLeavesSourceUntouched()
    {
        var src = new[] { "A", "B", "C" };
        var r = WeightedOrder.Shuffle(src, W, 0.5, new Random(3));
        Assert.Equal(new[] { "A", "B", "C" }, src);
        Assert.Equal(3, r.Count);
        Assert.Equal(new[] { "A", "B", "C" }, r.OrderBy(x => x).ToArray());
    }
}
