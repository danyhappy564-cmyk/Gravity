using System;
using System.Collections.Generic;

namespace Gravity.Shared
{
    /// <summary>
    /// 종류별 인원·PMC 무리의 요청 장부입니다. 시간으로 만료하지 않습니다.
    /// 실제 생존 목록에서 확인한 인원만 예약에서 빼므로 빠른 생성은 중복 집계하지 않고 지연된 생성은 계속 보호합니다.
    /// 레이드가 끝나면 장부 전체가 버려집니다. 게임 스냅샷은 재사용 버퍼만 갱신합니다.
    /// </summary>
    public sealed class SpawnRequestLedger
    {
        private sealed class Request
        {
            public string Kind = "";
            public int Pending, Live;
            public bool PmcGroup;
        }

        private sealed class Member
        {
            public Request Request = null!;
            public bool Observed;
            public int Stamp;
        }

        private readonly object _gate = new object();
        private readonly Dictionary<object, Request> _requests = new Dictionary<object, Request>();
        private readonly Dictionary<string, Member> _members = new Dictionary<string, Member>(StringComparer.Ordinal);
        private int _stamp;

        public bool Contains(object key) { lock (_gate) return _requests.ContainsKey(key); }

        /// <summary>승인된 요청만 기록합니다. 재시도는 같은 키의 기존 몫을 유지합니다.</summary>
        public void Claim(object key, string kind, int size, bool pmcGroup)
        {
            lock (_gate)
            {
                if (!_requests.ContainsKey(key))
                    _requests.Add(key, new Request { Kind = kind, Pending = Math.Max(0, size), PmcGroup = pmcGroup });
            }
        }

        public int Pending(string kind, object? except = null)
        {
            lock (_gate)
            {
                int n = 0;
                foreach (var kv in _requests)
                    if (!ReferenceEquals(kv.Key, except) && kv.Value.Kind == kind) n += kv.Value.Pending;
                return n;
            }
        }

        public int PendingPmcGroups(object? except = null)
        {
            lock (_gate)
            {
                int n = 0;
                foreach (var kv in _requests)
                    if (!ReferenceEquals(kv.Key, except) && kv.Value.PmcGroup && kv.Value.Pending > 0 && kv.Value.Live == 0) n++;
                return n;
            }
        }

        public void Register(object key, string profileId)
        {
            lock (_gate)
                if (_requests.TryGetValue(key, out var r) && !_members.ContainsKey(profileId))
                    _members.Add(profileId, new Member { Request = r });
        }

        public void BeginSnapshot()
        {
            lock (_gate)
            {
                _stamp++;
                foreach (var r in _requests.Values) r.Live = 0;
            }
        }

        public void Observe(string profileId)
        {
            lock (_gate)
            {
                if (!_members.TryGetValue(profileId, out var m) || m.Stamp == _stamp) return;
                m.Stamp = _stamp;
                m.Request.Live++;
                if (m.Observed) return;
                m.Observed = true;
                m.Request.Pending = Math.Max(0, m.Request.Pending - 1);
            }
        }

        /// <summary>프로필 누락·활성화 실패 등 실제로 나오지 않을 인원을 정리합니다.</summary>
        public void Fail(object key, int count)
        {
            lock (_gate)
                if (_requests.TryGetValue(key, out var r)) r.Pending = Math.Max(0, r.Pending - Math.Max(0, count));
        }

        public void Abort(object key)
        {
            lock (_gate)
                if (_requests.TryGetValue(key, out var r)) { r.Pending = 0; r.PmcGroup = false; }
        }

        public void Retire(string profileId)
        {
            lock (_gate)
            {
                if (!_members.TryGetValue(profileId, out var m)) return;
                if (!m.Observed) m.Request.Pending = Math.Max(0, m.Request.Pending - 1);
                _members.Remove(profileId);
            }
        }
    }

    /// <summary>분대장 폐기 뒤 늦은 활성화와 대원 생성 진입을 막는 종료 상태입니다.</summary>
    public sealed class SquadLifecycle
    {
        private readonly object _gate = new object();
        public bool LeaderSeen { get; private set; }
        public bool SupportsStarted { get; private set; }
        public bool Terminal { get; private set; }

        public bool SeeLeader() { lock (_gate) { if (Terminal) return false; LeaderSeen = true; return true; } }
        public bool StartSupports() { lock (_gate) { if (Terminal || SupportsStarted) return false; SupportsStarted = true; return true; } }
        public bool Terminate() { lock (_gate) { Terminal = true; return !SupportsStarted; } }
    }

    public static class EscortBudget
    {
        /// <summary>실제 생성 목록을 직접 자릅니다. 0명 역할은 빼서 빈 프로필 요청도 만들지 않습니다.</summary>
        public static int Apply<T>(IList<T> supports, int budget, Func<T, int> count, Action<T, int> setCount)
        {
            int left = Math.Max(0, budget), total = 0;
            for (int i = 0; i < supports.Count; i++)
            {
                int take = Math.Min(left, Math.Max(0, count(supports[i])));
                setCount(supports[i], take);
                left -= take; total += take;
            }
            for (int i = supports.Count - 1; i >= 0; i--)
                if (count(supports[i]) == 0) supports.RemoveAt(i);
            return total;
        }

    }

    /// <summary>대기 중에도 스캐브는 계속 처리하되 한 틱의 PMC 시도는 하나로 제한합니다.</summary>
    public sealed class RefillPriority
    {
        public int Size { get; private set; }
        public bool Extract { get; private set; }
        public bool Attempted { get; private set; }
        public void BeginTick() => Attempted = false;
        public bool TryAttempt(bool extract)
        {
            if (Attempted || (Size > 0 && Extract != extract)) return false;
            Attempted = true;
            return true;
        }
        public void Wait(int size, bool extract) { Size = Math.Max(0, size); Extract = extract; }
        public void Clear() { Size = 0; Extract = false; }
    }

    public static class SpawnCapacity
    {
        public static int FitPmc(int size, int cap) => Math.Max(1, cap > 0 ? Math.Min(size, cap) : size);
        public static bool SplitPmc(IList<RefillGroup> groups, int index, int cap)
        {
            var g = groups[index];
            int fit = FitPmc(g.Size, cap);
            if (fit >= g.Size) return false;
            groups[index] = g with { Size = fit };
            groups.Insert(index + 1, g with { Size = g.Size - fit });
            return true;
        }
        public static int RoomForScavs(int cap, int occupied, int excluded, int held, int pmcReserve) =>
            cap <= 0 ? int.MaxValue : CapLedger.Room(cap, occupied, excluded, held) - Math.Max(0, pmcReserve);
    }
}
