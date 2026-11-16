using System;

namespace Gravity.Shared
{
    /// <summary>
    /// 웨이브 몰림·인원 몰림(-100 앞 ~ 0 ~ 100 뒤). PMC 계획(SpawnPlanner)과 1.0.8 스캐브 몰림이 같은 식을 쓴다.
    /// </summary>
    public static class Lean
    {
        /// <summary>레이드 진행 f(0~1)에서의 배수 1 + lean·(2f−1). 음수면 앞이 크고 뒤가 작다. 0이면 늘 1.</summary>
        public static double Linear(double lean, double f)
        {
            if (lean == 0 || double.IsNaN(lean)) return 1;
            var l = Math.Max(-100, Math.Min(100, lean)) / 100;
            var x = Math.Max(0, Math.Min(1, f));
            return 1 + l * (2 * x - 1);
        }

        /// <summary>시각 위치 f(0~1)를 몰림만큼 앞(음수)·뒤(양수)로 옮긴다. 0이면 그대로.</summary>
        public static double Skew(double f, double lean)
        {
            if (lean == 0 || double.IsNaN(lean)) return f;
            var s = -Math.Max(-100, Math.Min(100, lean)) / 100;
            var x = Math.Max(0, Math.Min(1, f));
            return s >= 0 ? Math.Pow(x, 1 + 2 * s) : 1 - Math.Pow(1 - x, 1 - 2 * s);
        }
    }
}
