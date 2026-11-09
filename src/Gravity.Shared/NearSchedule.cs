using System;
using System.Collections.Generic;

namespace Gravity.Shared
{
    /// <summary>근처 보충으로 넣을 한 자리. Role = pmcUSEC, pmcBEAR, assault 또는 사용자 지정 역할. Count = 무리 크기.</summary>
    public sealed record NearSlot(string Role, int Count);

    /// <summary>
    /// 근처 보충의 시각과 자리 구성을 정하는 순수 일정. 지점 계산과 실제 생성은 호출 쪽(NearDirector) 몫이다.
    /// 첫 차례는 FirstDelay, 이후는 Interval마다. 퍼센트 값은 0~1 비율로 받는다.
    /// </summary>
    public sealed class NearSchedule
    {
        private readonly NearDto _n;
        private readonly Random _rng;
        private double _nextAt;
        private int _used1;
        private int _used2;

        public NearSchedule(NearDto n, Random rng)
        {
            _n = n;
            _rng = rng;
            _nextAt = n.FirstDelay;
        }

        public IReadOnlyList<NearSlot> Tick(float raidElapsed, int cap, int aliveLoading)
        {
            var result = new List<NearSlot>();
            if (!_n.On || raidElapsed < _nextAt) return result;
            // 맵 상한이면 차례를 쓰지 않고 다음 틱에 다시 본다.
            if (_n.StopAtCap && aliveLoading >= cap) return result;

            _nextAt = raidElapsed + _n.Interval;

            int slots = _n.WaveMax <= _n.WaveMin ? _n.WaveMin : _rng.Next(_n.WaveMin, _n.WaveMax + 1);
            for (int i = 0; i < slots; i++)
            {
                if (_rng.NextDouble() >= _n.SlotChance) continue;
                if (OnlyCustom)
                {
                    var cs = CustomSlot(1, onlyCustom: true);
                    if (cs != null) result.Add(cs);
                    continue;
                }
                if (_rng.NextDouble() < _n.PmcChance)
                {
                    result.Add(new NearSlot(_rng.Next(2) == 0 ? "pmcUSEC" : "pmcBEAR", 1));
                    continue;
                }
                result.Add(ScavSlot());
            }
            return result;
        }

        private NearSlot ScavSlot()
        {
            int count = 1;
            if (!_n.GroupScavOff && _rng.NextDouble() < _n.GroupChance)
                count = _n.GroupMax <= _n.GroupMin ? _n.GroupMin : _rng.Next(_n.GroupMin, _n.GroupMax + 1);
            if (count < 1) count = 1;

            if (!_n.CustomOn) return new NearSlot("assault", count);
            return CustomSlot(count, onlyCustom: false) ?? new NearSlot("assault", count);
        }

        /// <summary>기본 PMC/스캐브를 끄고(UseDefault=false) 직접 고르기만 쓰는가. 직접 고르기가 꺼져 있으면 기본을 쓴다.</summary>
        private bool OnlyCustom => !_n.UseDefault && _n.CustomOn;

        /// <summary>직접 고른 종류로 한 자리. 고른 쪽 한도가 차면 onlyCustom일 때 다른 쪽을 보고, 둘 다 차면 null.</summary>
        private NearSlot? CustomSlot(int count, bool onlyCustom)
        {
            bool second = _rng.NextDouble() < _n.CustomRatio2;
            for (int tries = 0; tries < (onlyCustom ? 2 : 1); tries++, second = !second)
            {
                string role = second ? _n.CustomT2 : _n.CustomT1;
                int room = second ? _n.CustomCap2 - _used2 : _n.CustomCap1 - _used1;
                if (string.IsNullOrWhiteSpace(role) || room <= 0) continue;
                if (count > room) count = room;
                if (second) _used2 += count; else _used1 += count;
                if (IsBossRole(role)) role = "assault";
                return new NearSlot(role, count);
            }
            return null;
        }

        private static bool IsBossRole(string role) =>
            role.StartsWith("boss", StringComparison.OrdinalIgnoreCase) ||
            role.StartsWith("follower", StringComparison.OrdinalIgnoreCase);
    }
}
