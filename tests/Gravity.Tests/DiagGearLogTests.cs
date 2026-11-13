using Gravity.Core;
using Xunit;

namespace Gravity.Tests;

/// <summary>1.0.5: 봇 장비 기록이 같은 "필수 칸 복원" 줄 반복으로 2000줄 한도를 채우지 않게 — 같은 줄은 한 판에 한 번.</summary>
[Collection("DiagLog")]
public class DiagGearLogTests
{
    [Fact]
    public void 같은_줄은_한_판에_한_번만_남는다()
    {
        var old = DiagLog.ReportsDir;
        var dir = Path.Combine(Path.GetTempPath(), "gravity-diag-" + Guid.NewGuid().ToString("N"));
        try
        {
            DiagLog.ReportsDir = dir;
            DiagLog.NewGearRaid("h");
            for (int i = 0; i < 3; i++) DiagLog.GearLine("restore x");
            DiagLog.GearLine("bot y");
            DiagLog.FlushGear();
            Assert.Equal(["h", "restore x", "bot y"], File.ReadAllLines(Path.Combine(dir, "gear-last.log")));
            DiagLog.NewGearRaid("h2");
            DiagLog.GearLine("restore x");
            DiagLog.FlushGear();
            Assert.Equal(["h2", "restore x"], File.ReadAllLines(Path.Combine(dir, "gear-last.log")));
        }
        finally { DiagLog.ReportsDir = old; if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
