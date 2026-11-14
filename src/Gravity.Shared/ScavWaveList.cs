using System;
using System.Collections.Generic;
using System.Linq;

namespace Gravity.Shared
{
    /// <summary>
    /// 1.0.6 스캐브 웨이브 목록의 판 일정(순수 로직). 판 시작 때 시드로 각 웨이브의 실제 시각(± 흔들림)과 인원(최소~최대)을 정하고,
    /// Due가 틱마다 "지금 낼 인원"을 돌려준다. 상한에 막힌 인원은 그 웨이브에 남아 시각 뒤 60초까지 재시도하고, 넘으면 버린다
    /// (다음 웨이브와 합쳐 한꺼번에 몰리지 않게). 반복은 웨이브 수(서버가 60개로 자름)로 끝난다.
    /// </summary>
    public sealed class ScavWaveList
    {
        public const double RetrySec = 60;

        private readonly double[] _at;
        private readonly int[] _left;
        private readonly int[] _taken; // 마지막 Due에서 웨이브별로 낸 수(GiveBack용)
        private int _first;

        /// <param name="list">원래 레이드 기준 초·인원.</param>
        /// <param name="jitterSec">판마다 각 웨이브 시각 흔들림(±초).</param>
        /// <param name="offsetSec">스캐브 레이드 지연(원래 레이드 초 − 클라 경과 초). 이미 지난 웨이브는 뺀다.</param>
        /// <param name="hardStopSec">클라 경과 기준 스캐브 하드 스톱. 이 뒤 웨이브는 뺀다.</param>
        public ScavWaveList(ScavWaveDto[]? list, double jitterSec, double offsetSec, double hardStopSec, Random rng)
        {
            var at = new List<double>();
            var n = new List<int>();
            var src = list ?? Array.Empty<ScavWaveDto>();
            var jit = Math.Max(0, jitterSec);
            for (var i = 0; i < src.Length && i < 60; i++)
            {
                var w = src[i];
                // 난수는 웨이브마다 늘 두 번 뽑는다(빠지는 웨이브가 있어도 다음 웨이브 결과가 같게).
                var t = Math.Max(0, w.TimeSec + (rng.NextDouble() * 2 - 1) * jit) - offsetSec;
                var lo = Math.Max(0, Math.Min(w.Min, w.Max));
                var hi = Math.Max(lo, w.Max);
                var k = rng.Next(lo, hi + 1);
                if (t < 0 || t >= hardStopSec || k <= 0) continue;
                at.Add(t);
                n.Add(k);
            }
            var order = Enumerable.Range(0, at.Count).OrderBy(i => at[i]).ToArray();
            _at = order.Select(i => at[i]).ToArray();
            _left = order.Select(i => n[i]).ToArray();
            Counts = (int[])_left.Clone();
            _taken = new int[_at.Length];
        }

        /// <summary>이 판에 낼 웨이브 수(지난 것·하드 스톱 뒤·0명 제외).</summary>
        public int Count => _at.Length;
        /// <summary>웨이브별 실제 시각(클라 경과 초, 시각순).</summary>
        public IReadOnlyList<double> Times => _at;
        /// <summary>웨이브별 굴린 인원.</summary>
        public IReadOnlyList<int> Counts { get; }
        /// <summary>상한에 막혀 60초 안에 못 내고 버린 인원.</summary>
        public int Dropped { get; private set; }
        /// <summary>마지막 Due 뒤, 시각이 됐는데 아직 못 낸 인원(상한 대기).</summary>
        public int Waiting { get; private set; }
        /// <summary>끝난 웨이브 수(다 냈거나 버림).</summary>
        public int Done => _first;
        public int Spawned { get; private set; }

        /// <summary>경과 초 elapsed에 빈자리 room만큼 낼 인원. 남은 인원은 웨이브에 남기고, 시각+60초가 지난 웨이브의 남은 인원은 버린다.</summary>
        public int Due(double elapsed, int room)
        {
            var give = 0;
            room = Math.Max(0, room);
            Waiting = 0;
            Array.Clear(_taken, 0, _taken.Length);
            for (var i = _first; i < _at.Length; i++)
            {
                if (_at[i] > elapsed) break;
                if (_left[i] > 0 && elapsed > _at[i] + RetrySec) { Dropped += _left[i]; _left[i] = 0; }
                var take = Math.Min(_left[i], room);
                _left[i] -= take;
                room -= take;
                give += take;
                _taken[i] = take;
                Waiting += _left[i];
                if (_left[i] == 0 && i == _first) _first++;
            }
            Spawned += give;
            return give;
        }

        /// <summary>마지막 Due로 받은 인원 중 실제로 못 넣은 n마리(지점 없음 등)를 그 웨이브에 돌려준다. 그 웨이브의 60초 재시도 창 안에서 다시 나온다.</summary>
        public void GiveBack(int n)
        {
            for (var i = _taken.Length - 1; i >= 0 && n > 0; i--)
            {
                var back = Math.Min(n, _taken[i]);
                if (back <= 0) continue;
                _taken[i] -= back;
                _left[i] += back;
                n -= back;
                Spawned -= back;
                Waiting += back;
                if (i < _first) _first = i;
            }
        }

        public string Describe() => $"{Count} waves ({Counts.Sum()} scavs)";
    }
}
