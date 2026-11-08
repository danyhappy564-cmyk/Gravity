using Gravity.Shared;
using Xunit;

namespace Gravity.Tests.Shared;

public class SpawnExpiryTests
{
    // 인자 순서: (첫 실패 시각, 지금, 레이드 경과, 하드 스톱 초). 대기 상한 기본 180초.
    [Fact]
    public void Expiry_Boundaries()
    {
        Assert.False(SpawnExpiry.Expired(0, 179, 100, 1000));
        Assert.True(SpawnExpiry.Expired(0, 180, 100, 1000));
        Assert.True(SpawnExpiry.Expired(0, 1, 1000, 1000));
    }

    // 맵 상한 대기: 하드 스톱 전에는 계속 기다리고, 닿은 순간부터 버린다.
    [Fact]
    public void PastHardStop_Boundaries()
    {
        Assert.False(SpawnExpiry.PastHardStop(999.9f, 1000));
        Assert.True(SpawnExpiry.PastHardStop(1000, 1000));
        Assert.True(SpawnExpiry.PastHardStop(1500, 1000));
    }

    [Fact]
    public void Expiry_CustomMaxWait()
    {
        Assert.False(SpawnExpiry.Expired(10, 69, 100, 1000, maxWaitSec: 60));
        Assert.True(SpawnExpiry.Expired(10, 70, 100, 1000, maxWaitSec: 60));
    }
}
