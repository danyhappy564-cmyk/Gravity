using Gravity.Core;

namespace Gravity.Spawn;

/// <summary>
/// 순수 계획 함수: 설정 + 맵 정보 + 실제 레이드 길이 → PMC 그룹·스캐브 웨이브·상한.
/// 웨이브 시각 규칙은 웹 목업 plan()과 같다(분 단위 계산 후 초로 바꿈).
/// </summary>
public static class SpawnPlanner
{
    private static readonly string[] DiffNames = ["easy", "normal", "hard", "impossible"];
    private const int MaxWaves = 400;

    /// <param name="clientAliveCap">클라 플러그인이 "동시에 살아 있는" 그룹 제한을 직접 거는 세션이면 true:
    /// scope=alive일 때 서버는 레이드 전체 기준 제한을 대신 걸지 않는다(죽거나 탈출하면 다시 나올 수 있어야 함).</param>
    public static SpawnPlan Plan(GravityConfig cfg, MapInfo map, int raidSec, int simStartSec, int seed, bool clientAliveCap = false)
    {
        var rnd = new Random(seed);
        var s = cfg.SpawnFor(map.Id);
        var ms = cfg.MapFor(map.Id);
        var startPct = s.StartPmcPct;
        var intervalMin = s.IntervalMin;
        var waveMin = s.WaveMin;
        var waveMax = s.WaveMax;
        var inten = s.Intensity;

        var raid = Math.Max(1, raidSec) / 60.0;
        var stop = s.HardStop.Mode == "percent" ? raid * (1 - s.HardStop.PmcPct / 100) : raid - s.HardStop.BeforeEndMin;
        var end = Math.Max(1.0 / 60, stop);
        var gap = s.MinGapSec / 60;
        var first = Math.Min(end, s.FirstDelaySec / 60);
        var basePmc = (double)map.BasePmc; // 시작 인원 기준(레이드 길이와 무관 — 길이는 웨이브 수가 따라감)

        var groups = new List<PmcGroup>();
        var scav = new List<ScavWave>();
        var cap = new CapTracker(clientAliveCap && s.GroupCap.Scope == "alive" ? s.GroupCap with { On = false } : s.GroupCap);
        var dropped = 0;

        // 1) 시작 PMC
        var startCount = s.StartMode == "count" ? rnd.Next(Math.Min(s.StartPmcMin, s.StartPmcMax), Math.Max(s.StartPmcMin, s.StartPmcMax) + 1)
            : (int)R(basePmc * (ms.Style == "start" ? Math.Max(startPct, 60) : startPct) / 100);
        // 1~5초: 게임의 보스 웨이브 시계는 로딩 끝 무렵(BossSpawnScenario.Run, 시작 약 12초 전)부터 돈다. 로딩 중에 불린 분대는 장비 모델을
        // 로딩 화면에서 읽고 시작과 함께 나오지만, 시작 뒤에 불린 분대는 레이드 중 모델 읽기를 기다려 2~4분 늦게 나왔다(1.0 3차 점검 세관).
        dropped += AddPmcs(startCount, () => 1 + rnd.Next(5), true);

        // 2) 웨이브
        var pmcWaves = new List<(double t, int n)>();
        if (ms.Style == "infinite")
        {
            var step = Math.Max(ms.Inf.DelaySec / 60, (end - first) / MaxWaves);
            var j = ms.Inf.Jitter / 100;
            for (var t = first; t < end && pmcWaves.Count < MaxWaves; t += step)
            {
                var tt = Math.Min(end, Math.Max(first, t + (rnd.NextDouble() * 2 - 1) * step * j));
                var k = IntensityAt(inten, tt, raid);
                var lo = Math.Max(1, (int)R(ms.Inf.Batch * k * .5));
                var hi = Math.Max(1, (int)R(ms.Inf.Batch * k));
                var n = rnd.Next(lo, hi + 1);
                var pmcN = (int)R(n * ms.Inf.PmcShare / 100);
                pmcWaves.Add((tt, pmcN));
                if (ms.ScavStyle == "waves" && tt < raid * (1 - s.HardStop.ScavPct / 100) && n - pmcN > 0) scav.Add(Wave(tt, n - pmcN));
            }
        }
        else if (ms.Style == "waves")
        {
            var iv = Math.Max(.5, intervalMin);
            var n = Math.Max(1, (int)Math.Floor((end - first) / iv) + 1);
            var sk = -s.DistWave / 100;
            var bl = s.DistBots / 100;
            var ts = new List<(double f, double t)>();
            for (var i = 0; i < n && i < MaxWaves; i++)
            {
                var f = n == 1 ? 0 : (double)i / (n - 1);
                ts.Add((f, Math.Min(end, Math.Max(first, first + (end - first) * SkewPos(f, sk) + (rnd.NextDouble() * 2 - 1) * iv * s.RandomPct / 100))));
            }
            ts.Sort((a, b) => a.t.CompareTo(b.t));
            if (s.Smooth)
            {
                for (var i = 1; i < ts.Count; i++) if (ts[i].t - ts[i - 1].t < gap) ts[i] = (ts[i].f, ts[i - 1].t + gap);
                for (var i = ts.Count - 1; i >= 0; i--)
                {
                    var lim = i == ts.Count - 1 ? end : ts[i + 1].t - gap;
                    if (ts[i].t > lim) ts[i] = (ts[i].f, lim);
                }
                ts = ts.Where(x => x.t >= first - 0.01).ToList();
            }
            foreach (var (f, t) in ts)
            {
                var k = IntensityAt(inten, t, raid) * (1 + bl * (2 * f - 1));
                var lo = Math.Max(0, (int)R(waveMin * k));
                var hi = Math.Max(lo, (int)R(waveMax * k));
                pmcWaves.Add((t, rnd.Next(lo, hi + 1)));
            }
        }
        foreach (var (t, n) in pmcWaves)
        {
            var sec = (int)R(t * 60);
            dropped += AddPmcs(n, () => sec, false);
        }

        // 3) 스캐브(무한 보충 방식이 아닐 때 따로 계획)
        if (map.Id != "laboratory" || s.AllowScavLab) PlanScavs();
        else scav.Clear(); // 인원 방식도 연구소 스캐브 허용 스위치를 우회하지 않습니다.

        // 4) 스캐브 레이드 시작 지연: 이른 항목 빼고 시각을 당긴다
        if (simStartSec > 0)
        {
            groups = groups.Where(g => g.TimeSec >= simStartSec).Select(g => g with { TimeSec = g.TimeSec - simStartSec }).ToList();
            scav = scav.Where(w => w.TimeMinSec >= simStartSec)
                .Select(w => w with { TimeMinSec = w.TimeMinSec - simStartSec, TimeMaxSec = w.TimeMaxSec - simStartSec }).ToList();
        }
        groups.Sort((a, b) => a.TimeSec.CompareTo(b.TimeSec));
        scav.Sort((a, b) => a.TimeMinSec.CompareTo(b.TimeMinSec));
        return new SpawnPlan(map.Id, raidSec, simStartSec, seed, ms.Style, groups, scav, ms.Cap, dropped);

        int AddPmcs(int count, Func<int> time, bool isStart)
        {
            var lost = 0;
            var left = count;
            while (left > 0)
            {
                var want = Math.Min(left, PickSize(s.Squad, rnd));
                left -= want;
                var fit = cap.Fit(want, out var l);
                foreach (var size in fit)
                    groups.Add(new PmcGroup(time(), rnd.NextDouble() * 100 < s.UsecPct, size, Pick(DiffNames, s.Diff, rnd), isStart));
                lost += l;
            }
            return lost;
        }

        void PlanScavs()
        {
            var scavEnd = raid * (1 - s.HardStop.ScavPct / 100);
            var t = s.Scav.SeparateStart ? s.Scav.DelayMin : 0;
            var times = new List<double>();
            var firstWindow = -1; // 첫 활동 구간에 든 웨이브 수(= 시작 스캐브)
            while (t < scavEnd && times.Count < MaxWaves)
            {
                var activeEnd = t + Between(s.Scav.ActiveMin, s.Scav.ActiveMax);
                while (t < activeEnd && t < scavEnd && times.Count < MaxWaves)
                {
                    times.Add(t);
                    t += (60 + rnd.Next(61)) / 60.0;
                }
                if (firstWindow < 0) firstWindow = times.Count;
                t = activeEnd + Between(s.Scav.QuietMin, s.Scav.QuietMax);
            }
            if (times.Count == 0) return;
            var startMin = s.Scav.SeparateStart ? s.Scav.DelayMin : 0;
            var total = map.BaseScav * Math.Max(0, raid - startMin) / Math.Max(1.0, map.DbRaidSec / 60.0);
            var per = total / times.Count;
            // 시작 인원은 카드의 비율(또는 1.0.2 인원 범위)로 정한다. 최대 400개 구간·한정된 시작 인원만 분배한다.
            var initial = !(ms.StartScav && s.Scav.StartOn) ? 0
                : s.Scav.StartMode == "count" ? rnd.Next(Math.Min(s.Scav.StartMin, s.Scav.StartMax), Math.Max(s.Scav.StartMin, s.Scav.StartMax) + 1)
                : (int)R(map.BaseScav * s.Scav.StartPct / 100);
            // 시작 인원은 첫 활동 구간에 흩뿌리지 않고 설정한 지연 시각에 한 번 준비합니다.
            if (initial > 0)
            {
                var sec = (int)R(startMin * 60);
                scav.Add(new ScavWave(sec, sec, initial, initial, true));
            }
            // 1.0.6 웨이브 목록: 창 안 웨이브는 만들지 않는다(시작 스캐브만, 목록은 클라가 낸다).
            if (ms.ScavStyle is not ("start" or "list") && (ms.Style != "infinite" || ms.ScavStyle != "waves"))
                for (var i = firstWindow; i < times.Count; i++)
                    scav.Add(Wave(times[i], Math.Max(1, (int)R(per * ScavCurveAt(s.Scav.Curve, times[i], raid)))));
        }

        double Between(double a, double b) => a >= b ? a : a + rnd.NextDouble() * (b - a);
    }

    /// <summary>무한 보충용 PMC 분대 목록(크기·진영·난이도를 계획기와 같은 비율로 미리 뽑는다). 시드가 같으면 같은 목록.</summary>
    public static List<Gravity.Shared.RefillGroup> PlanRefill(GravityConfig cfg, int seed, int count)
    {
        var rnd = new Random(unchecked(seed * 31 + 7));
        var s = cfg.Spawn;
        var list = new List<Gravity.Shared.RefillGroup>();
        for (var i = 0; i < count; i++)
            list.Add(new Gravity.Shared.RefillGroup(rnd.NextDouble() * 100 < s.UsecPct, PickSize(s.Squad, rnd), Pick(DiffNames, s.Diff, rnd)));
        return list;
    }

    private static ScavWave Wave(double tMin, int n)
    {
        var sec = (int)R(tMin * 60);
        return new ScavWave(sec, sec + 30, Math.Max(1, (int)Math.Floor(n * .7)), Math.Max(1, n));
    }

    /// <summary>1.0.6 스캐브 강도(분 단위 t, 레이드 3등분). 없으면 1.</summary>
    internal static double ScavCurveAt(double[]? c, double t, double raid)
    {
        if (c is not { Length: >= 3 } || raid <= 0) return 1;
        var f = t / raid;
        return Math.Clamp(f < 1.0 / 3 ? c[0] : f < 2.0 / 3 ? c[1] : c[2], 0.2, 3);
    }

    internal static double IntensityAt(Intensity c, double t, double raid)
    {
        var f = t / raid;
        return f < 1.0 / 3 ? c.Early : f < 2.0 / 3 ? c.Mid : c.Late;
    }

    private static double R(double x) => Math.Round(x, MidpointRounding.AwayFromZero);

    internal static double SkewPos(double f, double s) => s >= 0 ? Math.Pow(f, 1 + 2 * s) : 1 - Math.Pow(1 - f, 1 - 2 * s);

    private static int PickSize(double[] squad, Random rnd) => PickIndex(squad, rnd) + 1;

    private static int PickIndex(double[] w, Random rnd)
    {
        var total = w.Sum();
        if (total <= 0) return 0;
        var x = rnd.NextDouble() * total;
        for (var i = 0; i < w.Length; i++) { x -= w[i]; if (x < 0) return i; }
        return w.Length - 1;
    }

    private static string Pick(string[] names, double[] w, Random rnd) => names[Math.Min(names.Length - 1, PickIndex(w, rnd))];

    /// <summary>그룹 개수 제한(레이드 전체 기준). 넘치면 더 작은 허용 크기로 나누거나(split) 뺀다(drop).</summary>
    internal sealed class CapTracker(GroupCap gc)
    {
        private readonly int[] _count = new int[6];
        private readonly List<GroupCapRule> _rules = gc.On ? gc.Rules.ToList() : [];

        private static bool Applies(GroupCapRule r, int k) => r.Cmp == "ge" ? k >= r.Size : k == r.Size;

        private int Used(GroupCapRule r)
        {
            var n = 0;
            for (var k = 1; k <= 5; k++) if (Applies(r, k)) n += _count[k];
            return n;
        }

        public bool CanAdd(int k) => _rules.All(r => !Applies(r, k) || Used(r) < r.Count);

        public List<int> Fit(int want, out int lost)
        {
            var result = new List<int>();
            lost = 0;
            if (CanAdd(want)) { _count[want]++; result.Add(want); return result; }
            if (gc.Over != "split") { lost = want; return result; }
            var left = want;
            while (left > 0)
            {
                var k = Math.Min(left, 5);
                while (k >= 1 && !CanAdd(k)) k--;
                if (k == 0) { lost = left; break; }
                _count[k]++;
                result.Add(k);
                left -= k;
            }
            return result;
        }
    }
}
