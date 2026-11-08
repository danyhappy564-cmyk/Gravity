using System;
using System.Collections.Generic;

namespace Gravity.Shared
{
    public static class WeightedOrder
    {
        /// <summary>share(0~1 비율) 확률로 가중 추첨 순서, 아니면 균등 섞기. 원본은 건드리지 않는다.</summary>
        public static List<T> Shuffle<T>(IReadOnlyList<T> items, Func<T, double> weight, double share, Random rng)
        {
            int n = items.Count;
            var arr = new T[n];
            for (int i = 0; i < n; i++) arr[i] = items[i];

            if (n > 1 && rng.NextDouble() < share)
            {
                // 가중 추첨(Efraimidis-Spirakis): 키 = ln(u)/w, 큰 순서. 가중치 0 이하는 맨 뒤.
                var keys = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double w = weight(arr[i]);
                    double u = 1.0 - rng.NextDouble();
                    keys[i] = w > 0 ? Math.Log(u) / w : double.NegativeInfinity;
                }
                Array.Sort(keys, arr);
                Array.Reverse(arr);
            }
            else
            {
                for (int i = n - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (arr[i], arr[j]) = (arr[j], arr[i]);
                }
            }
            return new List<T>(arr);
        }
    }
}
