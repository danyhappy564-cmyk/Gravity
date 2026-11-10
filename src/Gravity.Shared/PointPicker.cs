using System;
using System.Collections.Generic;

namespace Gravity.Shared
{
    public sealed record Candidate(int Id, Vec3 Pos, bool PlayerPoint);

    public sealed record PickRules(
        double PmcDist, double BotDist, double ReserveDist, bool ProtectActive, double ProtectRadius,
        double SquadRadius = 20, double OccupiedRadius = 2, double RelaxFactor = 0.75, bool ProtectFallback = false);

    /// <summary>성공이면 Reason "ok"·"relaxed"·"farthest", 실패면 "noPoint".</summary>
    public sealed record PickResult(IReadOnlyList<int> Ids, string Reason);

    public static class PointPicker
    {
        private static readonly int[] None = new int[0];

        public static PickResult PickSquad(IReadOnlyList<Candidate> cands, IReadOnlyList<Actor> actors,
            IReadOnlyList<Vec3> reserved, int size, PickRules rules, Random rng)
        {
            var list = new List<Candidate>(cands); // 복사 1번
            Shuffle(list, rng);

            var ids = TryPick(list, actors, reserved, size, rules, rules.PmcDist, rules.BotDist, rules.ReserveDist, rules.ProtectRadius);
            if (ids != null) return new PickResult(ids, "ok");

            double f = rules.RelaxFactor;
            ids = TryPick(list, actors, reserved, size, rules, rules.PmcDist * f, rules.BotDist * f, rules.ReserveDist * f, rules.ProtectRadius);
            if (ids != null) return new PickResult(ids, "relaxed");

            // 맵 전체에 시작 보호 반경 밖 지점이 하나도 없을 때만(공장처럼 작은 맵, ProtectFallback은 호출하는 쪽이 맵 전체로 판정):
            // 보호 반경 대신 사람에게서 가장 먼 지점부터 본다. 다른 규칙(완화 거리·예약·자리 겹침)은 그대로 지킨다.
            if (rules.ProtectActive && rules.ProtectFallback)
            {
                var far = new List<Candidate>(list);
                far.Sort((a, b) => HumanDist(b.Pos, actors).CompareTo(HumanDist(a.Pos, actors)));
                for (int i = 0; i < far.Count; i++)
                    if (FirstOk(far[i].Pos, actors, reserved, rules, rules.PmcDist * f, rules.BotDist * f, rules.ReserveDist * f, 0))
                        return new PickResult(Squad(far, far[i], actors, size, rules, false), "farthest");
            }

            return new PickResult(None, "noPoint");
        }

        private static int[]? TryPick(List<Candidate> list, IReadOnlyList<Actor> actors, IReadOnlyList<Vec3> reserved,
            int size, PickRules r, double pmcDist, double botDist, double reserveDist, double protectRadius)
        {
            // 1단계: 플레이어 지점만, 2단계: 전체
            for (int pass = 0; pass < 2; pass++)
            {
                bool playerOnly = pass == 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var c = list[i];
                    if (playerOnly && !c.PlayerPoint) continue;
                    if (!FirstOk(c.Pos, actors, reserved, r, pmcDist, botDist, reserveDist, protectRadius)) continue;

                    return Squad(list, c, actors, size, r, playerOnly);
                }
            }
            return null;
        }

        /// <summary>첫 지점 c와 그 둘레(SquadRadius) 빈 지점으로 분대 자리를 채운다. 모자라면 첫 지점 반복.</summary>
        private static int[] Squad(List<Candidate> list, Candidate c, IReadOnlyList<Actor> actors, int size, PickRules r, bool playerOnly)
        {
            var res = new int[size];
            res[0] = c.Id;
            int n = 1;
            for (int j = 0; j < list.Count && n < size; j++)
            {
                var o = list[j];
                if (o.Id == c.Id) continue;
                if (playerOnly && !o.PlayerPoint) continue;
                if (Vec3.Dist(c.Pos, o.Pos) > r.SquadRadius) continue;
                if (Occupied(o.Pos, actors, r.OccupiedRadius)) continue;
                res[n++] = o.Id;
            }
            for (; n < size; n++) res[n] = c.Id;
            return res;
        }

        /// <summary>가장 가까운 사람까지 거리(사람이 없으면 무한대).</summary>
        public static double HumanDist(Vec3 p, IReadOnlyList<Actor> actors)
        {
            double m = double.MaxValue;
            for (int i = 0; i < actors.Count; i++)
                if (actors[i].Kind == ActorKind.Human) m = Math.Min(m, Vec3.Dist(p, actors[i].Pos));
            return m;
        }

        private static bool FirstOk(Vec3 p, IReadOnlyList<Actor> actors, IReadOnlyList<Vec3> reserved, PickRules r,
            double pmcDist, double botDist, double reserveDist, double protectRadius)
        {
            for (int i = 0; i < actors.Count; i++)
            {
                var a = actors[i];
                double d = Vec3.Dist(p, a.Pos);
                if (d < r.OccupiedRadius) return false;
                bool humanOrPmc = a.Kind == ActorKind.Human || a.Kind == ActorKind.Pmc;
                if (d < (humanOrPmc ? pmcDist : botDist)) return false;
                if (r.ProtectActive && a.Kind == ActorKind.Human && d < protectRadius) return false;
            }
            for (int i = 0; i < reserved.Count; i++)
                if (Vec3.Dist(p, reserved[i]) < reserveDist) return false;
            return true;
        }

        private static bool Occupied(Vec3 p, IReadOnlyList<Actor> actors, double radius)
        {
            for (int i = 0; i < actors.Count; i++)
                if (Vec3.Dist(p, actors[i].Pos) < radius) return true;
            return false;
        }

        /// <summary>
        /// F6 스캐브 구역 판정: points 중 하나라도 모든 사람(humans)과 minDist 이상이고,
        /// 시작 보호 중(protectActive)이면 protectRadius 이상인 지점이 있으면 true. 사람이 없으면 지점이 하나라도 있으면 true. 할당 없음.
        /// </summary>
        public static bool HasClearPoint(IReadOnlyList<Vec3> points, IReadOnlyList<Vec3> humans, double minDist,
            bool protectActive, double protectRadius)
        {
            double need = protectActive ? Math.Max(minDist, protectRadius) : minDist;
            for (int i = 0; i < points.Count; i++)
            {
                bool ok = true;
                for (int h = 0; h < humans.Count; h++)
                    if (Vec3.Dist(points[i], humans[h]) < need) { ok = false; break; }
                if (ok) return true;
            }
            return false;
        }

        /// <summary>F8용: 플레이어에서 [minDist,maxDist] 띠 안, 안 보이고, 이미 고른 지점과 spacing 이상 떨어진 1개.</summary>
        public static PickResult PickRing(IReadOnlyList<Candidate> cands, Vec3 player, double minDist, double maxDist,
            IReadOnlyList<Vec3> taken, double spacing, Func<Vec3, bool>? visible, Random rng)
        {
            var list = new List<Candidate>(cands);
            Shuffle(list, rng);
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                double d = Vec3.Dist(player, c.Pos);
                if (d < minDist || d > maxDist) continue;
                bool near = false;
                for (int t = 0; t < taken.Count; t++)
                    if (Vec3.Dist(c.Pos, taken[t]) < spacing) { near = true; break; }
                if (near) continue;
                if (visible != null && visible(c.Pos)) continue;
                return new PickResult(new[] { c.Id }, "ok");
            }
            return new PickResult(None, "noPoint");
        }

        private static void Shuffle<T>(List<T> l, Random rng)
        {
            for (int i = l.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (l[i], l[j]) = (l[j], l[i]);
            }
        }
    }
}
