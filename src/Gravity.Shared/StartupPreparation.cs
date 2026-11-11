using System;
using System.Collections.Generic;

namespace Gravity.Shared
{
    /// <summary>시작 PMC의 몸체 준비와 로딩 대기 예산입니다. 같은 프로필과 후속 웨이브는 중복 집계하지 않습니다.</summary>
    public sealed class StartupPreparation
    {
        public const int MaxPolls = 120;
        public const int PollMilliseconds = 500;
        public const double MaxSeconds = 60;
        private readonly HashSet<int> _starts = new HashSet<int>();
        private readonly HashSet<string> _profiles = new HashSet<string>(StringComparer.Ordinal);
        public int Expected { get; }
        public int Prepared => _profiles.Count;

        public StartupPreparation(IReadOnlyList<int>? sizes, IReadOnlyList<int>? starts)
        {
            if (sizes == null || starts == null) return;
            int expected = 0;
            for (int n = 0; n < starts.Count && n < 400; n++)
            {
                int i = starts[n];
                if (i >= 0 && i < sizes.Count && _starts.Add(i)) expected += Math.Max(0, sizes[i]);
            }
            Expected = expected;
        }

        /// <summary>초기 분대가 이미 선택한 충분한 지점만 로딩 경로에서 재사용합니다.</summary>
        public static bool CanPrepareSupports(bool started, bool initial, bool terminated, int followers, int positions) =>
            !started && initial && !terminated && followers > 0 && positions >= followers;

        public bool ContainsWave(int waveIndex) => _starts.Contains(waveIndex);

        public void Mark(int waveIndex, string profileId)
        {
            if (_starts.Contains(waveIndex) && !string.IsNullOrEmpty(profileId)) _profiles.Add(profileId);
        }

        public bool ShouldWait(double seconds, int polls, bool sameRaid, bool started, bool capFull) =>
            sameRaid && !started && !capFull && Expected > Prepared && seconds < MaxSeconds && polls < MaxPolls;
    }
}
