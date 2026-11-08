using System;

namespace Gravity.Shared
{
    /// <summary>ScavSchedule.Tick이 0을 낸 이유(마지막 틱). None = 막힌 이유 없음(넣을 수를 냈거나 Batch 0 설정).</summary>
    public enum ScavBlock { None, Quiet, HardStop, Interval, Pending, PerPlayer, Cap }

    /// <summary>스캐브 활동/휴식 창과 시도 간격을 관리하는 순수 일정. 무한 보충이면 Inf 값을 쓴다.</summary>
    public sealed class ScavSchedule
    {
        private readonly ScavDto _s;
        private readonly InfDto? _inf;
        private readonly int _hardStop;
        private readonly Random _rng;

        private bool _active = true;
        private double _windowEnd;      // 현재 창이 끝나는 경과 초
        private double _nextAttempt;    // 다음 시도 가능 경과 초

        /// <summary>마지막 Tick이 0을 낸 이유. 상한(Cap) 때문에 막힌 차례를 셀 때 쓴다.</summary>
        public ScavBlock LastBlock { get; private set; }

        public ScavSchedule(ScavDto s, InfDto? inf, int hardStopScavSec, Random rng)
        {
            _s = s;
            _inf = inf;
            _hardStop = hardStopScavSec;
            _rng = rng;
            _windowEnd = Draw(s.ActiveMin, s.ActiveMax) * 60.0;
            _nextAttempt = 0;
        }

        private double Draw(double min, double max)
        {
            if (max <= min) return min;
            return min + _rng.NextDouble() * (max - min);
        }

        public int Tick(float raidElapsed, int cap, int aliveLoadingDelayed, int pending,
            int spawnedPerPlayer, int players, out bool pmcTurn)
        {
            pmcTurn = false;

            while (raidElapsed >= _windowEnd)
            {
                _active = !_active;
                double len = _active ? Draw(_s.ActiveMin, _s.ActiveMax) : Draw(_s.QuietMin, _s.QuietMax);
                // 길이 0 창이 무한 반복하지 않도록 최소 1초
                _windowEnd += Math.Max(1.0, len * 60.0);
            }

            if (!_active) return Block(ScavBlock.Quiet);
            if (raidElapsed >= _hardStop) return Block(ScavBlock.HardStop);
            if (raidElapsed < _nextAttempt) return Block(ScavBlock.Interval);
            if (pending > _s.Pending) return Block(ScavBlock.Pending);
            if (spawnedPerPlayer >= _s.PerPlayer) return Block(ScavBlock.PerPlayer);

            int free = cap - _s.SoftCap - aliveLoadingDelayed;
            if (free <= 0) return Block(ScavBlock.Cap);

            int count = 1;
            double interval = _s.RefillSec;
            if (_inf != null)
            {
                count = Math.Min(_inf.Batch, free);
                pmcTurn = _rng.NextDouble() < _inf.PmcShare;
                double j = _inf.Jitter;
                interval = _inf.DelaySec * (1.0 + (j > 0 ? (_rng.NextDouble() * 2.0 - 1.0) * j : 0.0));
            }
            if (count <= 0) { pmcTurn = false; return Block(ScavBlock.None); } // Batch 0 설정(상한 탓 아님)

            _nextAttempt = raidElapsed + interval;
            LastBlock = ScavBlock.None;
            return count;
        }

        private int Block(ScavBlock why)
        {
            LastBlock = why;
            return 0;
        }
    }
}
