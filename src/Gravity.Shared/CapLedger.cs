using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Gravity.Shared
{
    /// <summary>
    /// 맵 동시 상한의 공용 자리 계산과 "요청했지만 아직 게임 집계에 안 잡힌" 생성 예약(1.0, Codex 검토 1번).
    /// - 점유 = 게임이 세는 봇(생존·로딩·대기·프로필 받는 중) − 상한에서 빼는 팩션 봇 + 예약.
    /// - 게임은 프로필을 한 명씩 받으며 그동안 1명만 세므로(BotCreationData.ProfilesLoadingProcess), 비동기 요청이 겹치면
    ///   상한을 넘을 수 있다. 그래서 Gravity가 요청한 인원을 요청 순간 예약하고, 게임 집계에 잡히는 순간(분대 TrySpawn 끝·
    ///   대기열 들어감·스캐브 생성 Task 끝) 푼다. 풀지 못한 예약은 Ttl초 뒤 저절로 사라진다(생성 실패로 영영 남지 않게).
    /// - 게임 집계와 예약이 잠깐 겹쳐 두 번 셀 수 있다: 상한을 넘기는 쪽이 아니라 몇 초 덜 채우는 쪽이라 그대로 둔다.
    /// 스캐브 생성 Task 끝은 다른 스레드일 수 있어 잠근다.
    /// </summary>
    public sealed class CapLedger
    {
        public const float DefaultTtl = 60f;

        private readonly object _gate = new object();
        private readonly Dictionary<object, (int Size, float Until)> _held = new Dictionary<object, (int, float)>(RefEq.Instance);
        private readonly List<object> _expired = new List<object>();

        /// <summary>cap이 0 이하면 상한 없음(바닐라 BotSpawner.MaxBots 0과 같다).</summary>
        public static int Room(int cap, int occupied, int excluded, int held) =>
            cap <= 0 ? int.MaxValue : cap - Math.Max(0, occupied - Math.Max(0, excluded)) - Math.Max(0, held);

        /// <summary>지금 잡혀 있는 예약 인원(지난 것은 지운다).</summary>
        public int Held(float now)
        {
            lock (_gate)
            {
                _expired.Clear();
                int sum = 0;
                foreach (var kv in _held)
                {
                    if (kv.Value.Until <= now) _expired.Add(kv.Key);
                    else sum += kv.Value.Size;
                }
                for (int i = 0; i < _expired.Count; i++) _held.Remove(_expired[i]);
                return sum;
            }
        }

        public int Count { get { lock (_gate) return _held.Count; } }

        /// <summary>자리가 size만큼 있으면 예약하고 true. 없으면 아무것도 안 하고 false.</summary>
        public bool TryReserve(object key, int size, int cap, int occupied, int excluded, float now, float ttl = DefaultTtl)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            size = Math.Max(1, size);
            lock (_gate)
            {
                if (Room(cap, occupied, excluded, Held(now)) < size) return false;
                _held[key] = (size, now + Math.Max(1f, ttl));
                return true;
            }
        }

        /// <summary>예약을 푼다(없으면 false). 같은 요청을 두 번 풀어도 된다.</summary>
        public bool Release(object key)
        {
            if (key == null) return false;
            lock (_gate) return _held.Remove(key);
        }

        private sealed class RefEq : IEqualityComparer<object>
        {
            public static readonly RefEq Instance = new RefEq();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
        }
    }

    /// <summary>
    /// "레이드 전체" 그룹 개수 제한의 클라 장부(1.0, Codex 검토 2번). 서버 계획(CapTracker)이 이미 쓴 분대 크기로 시작하고,
    /// 클라가 계획 밖에서 만드는 분대(무한 보충·탈출 보충)도 같은 규칙으로 센다. 죽거나 탈출해도 돌려주지 않는다.
    /// scope=alive(동시 생존)는 PmcTrySpawnPatch가 살아 있는 분대로 따로 보므로 이 장부는 항상 허용한다.
    /// </summary>
    public sealed class RaidGroupLedger
    {
        private readonly GroupCapDto? _cap;
        private readonly Dictionary<int, int> _used = new Dictionary<int, int>();

        public RaidGroupLedger(GroupCapDto? cap, IEnumerable<int>? planned)
        {
            _cap = cap;
            if (planned == null) return;
            foreach (var k in planned) Add(k);
        }

        public bool Active => _cap != null && _cap.On && _cap.Scope == "raid" && _cap.Rules != null && _cap.Rules.Count > 0;

        public int Used(int size) => _used.TryGetValue(size, out var n) ? n : 0;

        public bool CanAdd(int size)
        {
            if (!Active) return true;
            var rules = _cap!.Rules;
            for (int r = 0; r < rules.Count; r++)
            {
                var rule = rules[r];
                if (!Matches(rule, size)) continue;
                int used = 0;
                foreach (var kv in _used)
                    if (Matches(rule, kv.Key)) used += kv.Value;
                if (used >= rule.Count) return false;
            }
            return true;
        }

        /// <summary>허용되면 쓴 것으로 세고 true. 막히면 false(세지 않음). 장부가 꺼져 있으면 늘 true.</summary>
        public bool TryUse(int size)
        {
            size = Math.Max(1, size);
            if (!CanAdd(size)) return false;
            if (Active) Add(size);
            return true;
        }

        private void Add(int size)
        {
            size = Math.Max(1, size);
            _used[size] = Used(size) + 1;
        }

        private static bool Matches(GroupCapRuleDto rule, int size) =>
            rule.Cmp == "gte" ? size >= rule.Size : size == rule.Size;
    }

    /// <summary>구역 비중 읽기(1.0, Codex 검토 4번): 설정에 없는 구역은 웹 기본값 100(보통)이다. 명시한 0은 그대로 0.</summary>
    public static class ZoneWeight
    {
        public const double Default = 100;

        public static double Of(IReadOnlyDictionary<string, double>? weights, string? zone) =>
            weights != null && zone != null && weights.TryGetValue(zone, out var w) ? w : Default;
    }

    /// <summary>PMC 분대 TrySpawn 결과 분류(1.0, Codex 검토 6번). 구역 판정은 끈 구역을 지운 "최종" 목록으로 한다.</summary>
    public enum PmcOutcome { Spawned, NoPoint, Failed }

    public static class PmcTally
    {
        /// <summary>바닐라 TrySpawn의 "can't find zones" 두 조건(목록 비었음·첫 칸 null)의 반대.</summary>
        public static bool ZonesUsable(int finalCount, bool firstIsNull) => finalCount > 0 && !firstIsNull;

        public static PmcOutcome Classify(bool result, bool zonesSeen, bool zonesOk) =>
            !result ? PmcOutcome.Failed : zonesSeen && zonesOk ? PmcOutcome.Spawned : PmcOutcome.NoPoint;
    }
}
