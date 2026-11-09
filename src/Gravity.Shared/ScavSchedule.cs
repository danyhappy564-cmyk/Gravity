using System;

namespace Gravity.Shared
{
    /// <summary>
    /// ScavSchedule.Tick이 스캐브를 0마리 낸 이유(마지막 틱). None = 막힌 이유 없음(넣을 수를 냈거나 Batch 0 설정).
    /// Cap = 맵 상한(살아 있는+로딩+대기 봇이 cap 이상), SoftCap = 살아 있는 스캐브가 softCap 이상(스캐브만 쉼).
    /// </summary>
    public enum ScavBlock { None, Quiet, HardStop, Interval, Pending, PerPlayer, SoftCap, Cap }

    /// <summary>
    /// 스캐브 활동/휴식 창과 시도 간격을 관리하는 순수 일정. 무한 보충이면 Inf 값을 쓴다.
    /// 스캐브 빈자리 = min(cap - 살아 있는+로딩+대기 봇, softCap - 살아 있는 스캐브). softCap은 웹 뜻 그대로
    /// "살아 있는 스캐브가 이 수를 넘으면 보충 멈춤"인 절대 한도다(상한에서 빼는 여유가 아님).
    /// 무한 방식의 PMC 차례는 공통 판정(시도 간격·맵 상한) 뒤, 스캐브 전용 판정(휴식 창·스캐브 하드 스톱·대기 수·
    /// 플레이어당 한도·softCap) 앞에서 정한다. PMC 차례는 맵 상한만 본다.
    /// </summary>
    public sealed class ScavSchedule
    {
        private readonly ScavDto _s;
        private readonly InfDto? _inf;
        private readonly int _hardStop;
        private readonly Random _rng;

        private bool _active = true;
        private double _windowEnd;      // 현재 창이 끝나는 경과 초
        private double _nextAttempt;    // 다음 시도 가능 경과 초

        /// <summary>마지막 Tick에서 스캐브가 0마리였던 이유. 맵 상한(Cap) 때문에 막힌 차례를 셀 때 쓴다.</summary>
        public ScavBlock LastBlock { get; private set; }

        public ScavSchedule(ScavDto s, InfDto? inf, int hardStopScavSec, Random rng)
        {
            _s = s;
            _inf = inf;
            _hardStop = hardStopScavSec;
            _rng = rng;
            // 첫 시도는 StartDelaySec(서버 시작 스캐브와 같은 지연) 뒤. 첫 활동 창도 그때부터 센다.
            _nextAttempt = Math.Max(0, s.StartDelaySec);
            _windowEnd = _nextAttempt + Draw(s.ActiveMin, s.ActiveMax) * 60.0;
        }

        private double Draw(double min, double max)
        {
            if (max <= min) return min;
            return min + _rng.NextDouble() * (max - min);
        }

        /// <summary>
        /// 이번 차례에 넣을 스캐브 수를 낸다.
        /// pmcTurn이 true면 이 차례는 PMC 분대 차례다(무한 방식만). 이때 반환값은 PMC를 못 넣을 때(목록 다 씀 등) 대신 넣을
        /// 스캐브 수이고(스캐브 전용 판정을 거친 값, 0일 수 있음), 차례는 이미 쓴 것으로 친다.
        /// </summary>
        /// <param name="aliveLoadingDelayed">살아 있는+로딩 중+대기 중 봇 전체(맵 상한 판정).</param>
        /// <param name="aliveScavs">살아 있는 스캐브(assault) 수(softCap 판정).</param>
        /// <param name="spawnedPerPlayer">살아 있는 스캐브 ÷ 사람 수(웹 뜻 "한 명당 살아 있을 수 있는 수", 스캐브만 막는다).</param>
        public int Tick(float raidElapsed, int cap, int aliveLoadingDelayed, int aliveScavs, int pending,
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

            // 공통 판정: 시도 간격, 맵 상한. 막히면 차례를 쓰지 않고 다음 틱에 다시 본다.
            if (raidElapsed < _nextAttempt) return Block(ScavBlock.Interval);
            int room = cap - aliveLoadingDelayed;
            if (room <= 0) return Block(ScavBlock.Cap);

            // PMC 차례는 스캐브 전용 판정보다 먼저 정한다(무한 방식만).
            if (_inf != null) pmcTurn = _rng.NextDouble() < _inf.PmcShare;

            int want = _inf != null ? _inf.Batch : 1;
            ScavBlock why = ScavGate(raidElapsed, pending, spawnedPerPlayer, aliveScavs);
            int count = why == ScavBlock.None ? Math.Min(want, Math.Min(room, _s.SoftCap - aliveScavs)) : 0;

            if (pmcTurn)
            {
                Consume(raidElapsed);
                LastBlock = why;
                return count;
            }
            if (why != ScavBlock.None)
            {
                // 무한 방식: 스캐브 차례가 스캐브 전용 판정에 막히면 그 차례는 넘긴다(PMC 비율이 웹 설정대로 유지되게).
                // 웨이브 방식: 차례를 쓰지 않고 다음 틱에 다시 본다(Task 7 동작 그대로).
                if (_inf != null) Consume(raidElapsed);
                return Block(why);
            }
            if (count <= 0) return Block(ScavBlock.None); // Batch 0 설정(상한 탓 아님)

            Consume(raidElapsed);
            LastBlock = ScavBlock.None;
            return count;
        }

        /// <summary>스캐브에만 걸리는 판정. 순서: 휴식 창, 스캐브 하드 스톱, 대기 수, 플레이어당 한도, softCap.</summary>
        private ScavBlock ScavGate(float raidElapsed, int pending, int spawnedPerPlayer, int aliveScavs)
        {
            if (!_active) return ScavBlock.Quiet;
            if (raidElapsed >= _hardStop) return ScavBlock.HardStop;
            if (pending > _s.Pending) return ScavBlock.Pending;
            if (spawnedPerPlayer >= _s.PerPlayer) return ScavBlock.PerPlayer;
            if (aliveScavs >= _s.SoftCap) return ScavBlock.SoftCap;
            return ScavBlock.None;
        }

        /// <summary>차례를 쓴다: 다음 시도 시각을 정한다(무한 방식은 delay ± jitter).</summary>
        private void Consume(float raidElapsed)
        {
            double interval = _s.RefillSec;
            if (_inf != null)
            {
                double j = _inf.Jitter;
                interval = _inf.DelaySec * (1.0 + (j > 0 ? (_rng.NextDouble() * 2.0 - 1.0) * j : 0.0));
            }
            _nextAttempt = raidElapsed + interval;
        }

        private int Block(ScavBlock why)
        {
            LastBlock = why;
            return 0;
        }
    }
}
