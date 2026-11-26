using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using SPT.Reflection.Patching;
using UnityEngine;
using UnityEngine.AI;

namespace Gravity.Client.Patches
{
    /// <summary>
    /// 1.1.1 스폰 지점 길 검사(INBOX #85, KB projects/Gravity/spawn-audit-20261121): 레이드에서 한 번, 게임의 실제 NavMesh로 지점마다
    /// (1) 2m 안에 길이 있는지(NavMesh.SamplePosition, 게임·PmcZones와 같은 반경), (2) 그 길이 봇 순찰망과 이어지는지
    /// (15m~120m 안 가까운 AI 이동망 시작점 4개 중 하나까지 NavMesh 경로가 끝까지 나는지)를 본다. 15m 안 시작점은 건너뛴다: 공장 중앙 구덩이처럼
    /// 작은 섬 안에도 시작점이 있어 그것과 이어지면 통과해 버리기 때문(INBOX #85). 15m 밖에 없으면 가까운 것으로, 120m 안에 하나도 없으면 판정하지 않는다.
    /// 처음엔 맵 중심 쪽 기준점까지 경로를 봤지만 해안선에서 항구·밀수꾼·기상대 구역 전체가 끊김으로 나왔다(먼 경로는 끝까지 못 찾음, 1.1.1 인게임). 둘 중 하나라도 실패한 지점은 Gravity가 봇을 올리지 않는다
    /// (PMC 후보·맵 전체 배치·근처 보충·스캐브 구역 판정·바닐라 스캐브 지점 고르기). 저격 구역(IsSnipeZone) 지점은 사다리 탑·지붕이라
    /// (2)를 보지 않는다. 오프라인 조사에서 문제 대부분은 맵 가장자리 플레이어 시작점(PMC 후보)에 길이 없는 경우였다.
    /// NavMesh가 아직 없어 거의 모든 지점이 (1)에 걸리면(90% 넘게) 검사를 끄고 예전처럼 둔다.
    /// </summary>
    internal static class PointNav
    {
        private const float SampleRadius = 2f;
        private const float NearCore = 120f;
        private const float SkipCore = 15f;
        private const int CoreTries = 4;
        private static readonly NavMeshPath Path = new NavMeshPath();
        private static readonly Dictionary<ISpawnPoint, string?> Bad = new Dictionary<ISpawnPoint, string?>();
        private static readonly List<KeyValuePair<float, AICorePoint>> Near = new List<KeyValuePair<float, AICorePoint>>();
        private static List<AICorePoint>? _cores;
        private static RaidContext? _owner;
        private static bool _off;

        /// <summary>이 지점에 봇을 올리면 안 되는지(검사 전·검사 꺼짐이면 false).</summary>
        internal static bool IsBad(ISpawnPoint? p)
        {
            if (p == null) return false;
            if (_off || _owner == null || !ReferenceEquals(_owner, RaidContext.Current)) return false;
            return Bad.TryGetValue(p, out var why) && why != null;
        }

        /// <summary>레이드에서 한 번: 봇·PMC·플레이어 시작 지점을 모두 검사한다(PmcZones.EnsurePoints가 부른다).</summary>
        internal static void Build(RaidContext c, IEnumerable<ISpawnPoint> all, List<AICorePoint>? cores)
        {
            if (ReferenceEquals(_owner, c)) return;
            _owner = c;
            _off = false;
            Bad.Clear();
            var sw = Stopwatch.StartNew();
            _cores = cores;
            int n = 0, noNav = 0, island = 0, noNavPlayer = 0, islandPlayer = 0, low = 0, lowPlayer = 0, blocked = 0, paths = 0;
            var lines = new List<string>();
            foreach (var p in all)
            {
                if (p == null || Bad.ContainsKey(p)) continue;
                var cat = p.Categories;
                bool player = cat.ContainPlayerCategory() && p.Infiltration != null;
                if (!player && !cat.ContainBotPmcCategory() && !cat.ContainBotCategory()) continue;
                n++;
                string? why = null;
                try
                {
                    if (BlockedSpot(c.S.MapId, p.Position)) why = "막은 자리";
                    else if (!NavMesh.SamplePosition(p.Position, out var hit, SampleRadius, NavMesh.AllAreas)) why = "길 없음";
                    else if (!p.IsSnipeZone) why = Connected(p.Position, hit.position, ref paths);
                }
                catch (Exception ex) { Log.ErrorOnce("PointNav", ex); }
                Bad[p] = why;
                if (why == null) continue;
                if (why == "길 없음") { noNav++; if (player) noNavPlayer++; }
                else if (why == "낮은 통로") { low++; if (player) lowPlayer++; }
                else if (why == "막은 자리") blocked++;
                else { island++; if (player) islandPlayer++; }
                if (lines.Count < 400)
                    lines.Add($"{Short(p.Id)} {(player ? "플레이어" : "봇")} {p.BotZoneName} ({p.Position.x:0.0},{p.Position.y:0.0},{p.Position.z:0.0}) {why}");
            }
            if (n >= 10 && noNav > n * 0.9)
            {
                _off = true;
                Log.Warn($"{c.S.MapId} 스폰 지점 길 검사: {n}개 중 {noNav}개가 길 없음 — NavMesh가 아직 없는 것으로 보고 검사를 끔");
                return;
            }
            Log.Info($"{c.S.MapId} 스폰 지점 길 검사: {n}개 중 길 없음 {noNav}개(플레이어 시작 {noNavPlayer}), 순찰망과 끊김 {island}개(플레이어 시작 {islandPlayer}), 낮은 통로로만 나감 {low}개(플레이어 시작 {lowPlayer})" + (blocked > 0 ? $", 막은 자리 {blocked}개" : "") + " → 봇을 올리지 않음"
                + $" (경로 {paths}번, {sw.ElapsedMilliseconds}ms)");
            foreach (var l in lines) Log.Verbose($"{c.S.MapId} 뺀 지점: {l}");
        }

        /// <summary>
        /// 손으로 막은 자리(맵 id, 중심, 반지름 m, 이 높이보다 낮을 때만). 자동 검사로 못 잡는 곳.
        /// 공장 중앙 3통 아래 탱크 옆 벽 구덩이: 바닥에 NavMesh가 없고 웅크려야 나가는 틈뿐이라 봇이 못 나옴(INBOX #85, 사용자 확인 11-21).
        /// </summary>
        private static readonly (string map, Vector3 center, float radius, float belowY)[] Blocked =
        {
            ("factory4_day", new Vector3(-5.5f, -2.6f, 16f), 5f, -1f),
            ("factory4_night", new Vector3(-5.5f, -2.6f, 16f), 5f, -1f),
        };

        internal static bool BlockedSpot(string? map, Vector3 pos)
        {
            if (string.IsNullOrEmpty(map)) return false;
            foreach (var b in Blocked)
            {
                if (!string.Equals(b.map, map, StringComparison.OrdinalIgnoreCase) || pos.y > b.belowY) continue;
                float dx = pos.x - b.center.x, dz = pos.z - b.center.z;
                if (dx * dx + dz * dz <= b.radius * b.radius) return true;
            }
            return false;
        }

        /// <summary>
        /// 가까운 AI 이동망 시작점(15m~120m, 위아래 차이는 3배로 셈) 4개까지 경로를 본다. 하나라도 경로가 끝까지 나고 앞부분 25m에 낮은 천장이 없으면 null(통과).
        /// 경로는 나지만 모두 낮은 천장(1.45m 미만)을 지나야 하면 "낮은 통로"(공장 중앙 구덩이: NavMesh는 웅크려 나가는 틈을 길로 잡지만 봇은 못 나감, INBOX #85),
        /// 경로가 하나도 안 나면 "끊김". 120m 안에 시작점이 없으면 판정하지 않는다(null).
        /// </summary>
        private static string? Connected(Vector3 pos, Vector3 from, ref int paths)
        {
            var cores = _cores;
            if (cores == null || cores.Count == 0) return null;
            Near.Clear();
            float max = NearCore * NearCore;
            foreach (var cp in cores)
            {
                if (cp == null) continue;
                var d = cp.Position - pos;
                float dist = d.x * d.x + d.z * d.z + 9f * d.y * d.y;
                if (dist < max) Near.Add(new KeyValuePair<float, AICorePoint>(dist, cp));
            }
            if (Near.Count == 0) return null;
            Near.Sort((x, y) => x.Key.CompareTo(y.Key));
            int start = 0;
            while (start < Near.Count && Near[start].Key < SkipCore * SkipCore) start++;
            if (start >= Near.Count) start = 0;
            bool anyPath = false;
            for (int i = start; i < Near.Count && i < start + CoreTries; i++)
            {
                var cp = Near[i].Value;
                var to = NavMesh.SamplePosition(cp.Position, out var hit, SampleRadius, NavMesh.AllAreas) ? hit.position : cp.Position;
                paths++;
                if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, Path) || Path.status != NavMeshPathStatus.PathComplete) continue;
                anyPath = true;
                if (!LowCeiling(Path)) return null;
            }
            return anyPath ? "낮은 통로" : "끊김";
        }

        private const float CheckLen = 25f;
        private const float Step = 0.5f;
        private const float Headroom = 1.45f;

        /// <summary>경로 앞부분 25m를 0.5m마다 바닥 0.2m 위에서 위로 쏴 1.45m 안에 막히면 true(사람 몸이 부딪히는 정적 충돌체 레이어).</summary>
        private static bool LowCeiling(NavMeshPath path)
        {
            var c = path.corners;
            if (c == null || c.Length < 2) return false;
            float walked = 0f;
            for (int i = 0; i + 1 < c.Length && walked < CheckLen; i++)
            {
                var a = c[i];
                var seg = c[i + 1] - a;
                float len = seg.magnitude;
                if (len < 0.01f) continue;
                for (float t = 0f; t < len && walked + t < CheckLen; t += Step)
                {
                    var o = a + seg * (t / len) + Vector3.up * 0.2f;
                    if (Physics.Raycast(o, Vector3.up, Headroom - 0.2f, LayersMaskController.PlayerStaticCollisionsMask)) return true;
                }
                walked += len;
            }
            return false;
        }

        /// <summary>진단(브릿지에서 부름): 한 위치의 판정과 가까운 시작점별 경로 상태·길이·꺾임점·가장 낮은 천장.</summary>
        internal static string Probe(Vector3 pos)
        {
            var sb = new System.Text.StringBuilder();
            if (!NavMesh.SamplePosition(pos, out var hit, SampleRadius, NavMesh.AllAreas)) return "길 없음(2m)";
            sb.Append($"길 {Vector3.Distance(pos, hit.position):0.00}m 위 ({hit.position.x:0.0},{hit.position.y:0.0},{hit.position.z:0.0}); ");
            int paths = 0;
            sb.Append("판정 ").Append(Connected(pos, hit.position, ref paths) ?? "통과").Append("; ");
            for (int i = 0; i < Near.Count && i < 8; i++)
            {
                var cp = Near[i].Value;
                var to = NavMesh.SamplePosition(cp.Position, out var h2, SampleRadius, NavMesh.AllAreas) ? h2.position : cp.Position;
                bool ok = NavMesh.CalculatePath(hit.position, to, NavMesh.AllAreas, Path);
                var c = Path.corners;
                float len = 0f, minHead = 99f; Vector3 at = Vector3.zero;
                for (int k = 0; k + 1 < c.Length; k++)
                {
                    var seg = c[k + 1] - c[k];
                    float l = seg.magnitude;
                    for (float t = 0f; t < l && len + t < CheckLen; t += Step)
                    {
                        var o = c[k] + seg * (t / Mathf.Max(l, 0.01f)) + Vector3.up * 0.2f;
                        float hd = Physics.Raycast(o, Vector3.up, out var rh, 3f, LayersMaskController.PlayerStaticCollisionsMask) ? rh.distance + 0.2f : 99f;
                        if (hd < minHead) { minHead = hd; at = o; }
                    }
                    len += l;
                }
                sb.Append($"[시작점 {Mathf.Sqrt(Near[i].Key):0}m {(ok ? Path.status.ToString() : "실패")} 길이 {len:0} 꺾임 {c.Length} 최저천장 {(minHead > 90 ? "-" : minHead.ToString("0.00"))}@({at.x:0.0},{at.y:0.0},{at.z:0.0}) 꺾임:");
                for (int k = 0; k < c.Length && k < 6; k++) sb.Append($" ({c[k].x:0.0},{c[k].y:0.0},{c[k].z:0.0})");
                sb.Append("] ");
            }
            return sb.ToString();
        }

        /// <summary>진단(브릿지): 게임 지점 모음에서 pos 둘레 r m 안 지점 — Id·분류·Infiltration·위치·검사 결과(검사 안 함이면 "-").</summary>
        internal static string Around(Vector3 pos, float r)
        {
            var sb = new System.Text.StringBuilder();
            IEnumerable<ISpawnPoint>? all = null;
            try
            {
                var spawner = Singleton<IBotGame>.Instance?.BotsController?.BotSpawner;
                all = (spawner?.BossSpawner?._spawnSystem as SpawnSystem)?._spawnPoints;
            }
            catch { }
            if (all == null) return "지점 모음 없음";
            foreach (var p in all)
            {
                if (p == null || (p.Position - pos).magnitude > r) continue;
                string res = Bad.TryGetValue(p, out var why) ? (why ?? "통과") : "-";
                sb.Append($"{Short(p.Id)} {p.Categories} infil={(p.Infiltration == null ? "null" : "'" + p.Infiltration + "'")} ({p.Position.x:0.00},{p.Position.y:0.00},{p.Position.z:0.00}) core={p.CorePointId} {res}; ");
            }
            return sb.ToString();
        }

        internal static string Short(string? id) => string.IsNullOrEmpty(id) ? "-" : id!.Length > 8 ? id.Substring(0, 8) : id;
    }

    /// <summary>
    /// 1.1.1 SpawnSystem.SelectAISpawnPoints(시간 인자 판) 뒤: 스캐브류(assault·assaultGroup·cursedAssault·marksman) 지점을 다시 거른다(INBOX #84).
    /// 바닐라는 사람에게서 1.7m(제곱 3) 안만 빼고 시야는 안 봐서, 서버 시간 웨이브·Gravity 스캐브 웨이브 모두 바로 앞·보이는 곳에 날 수 있었다.
    /// 바닐라가 거른 후보(_filteredPointsCache의 유효 지점, 이미 무작위·PerfectPos 순)에서 순서대로
    ///   1단계: 길 검사 통과 + 모든 사람에게서 스캐브 거리(설정 scavDist) 이상 + 사람 눈에서 안 보임(Linecast, 지형·하이폴리 벽, 200m 안 사람만)
    ///   2단계: 거리 절반 + 안 보임, 3단계: 거리 이상(보여도 멂)
    /// 처음 1개 이상 나오는 단계를 쓴다. 하나도 없으면 빈 목록 → BotSpawner가 그 인원을 대기열(SpawnDelaysService)로 미뤘다가 다시 고른다.
    /// PerfectPos가 있는 호출(근처 보충·지정 위치 분대)은 NearDirector가 이미 거리·시야를 봤으므로 길 검사만 한다.
    /// 바닐라가 고른 지점이 1단계를 통과하면 아무것도 바꾸지 않는다. PMC·보스·호위는 손대지 않는다.
    /// </summary>
    internal sealed class ScavPointFilterPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(SpawnSystem).GetMethod(nameof(SpawnSystem.SelectAISpawnPoints), BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(BotCreationData), typeof(BotZone), typeof(int), typeof(float), typeof(Vector3?), typeof(ActionIfNotEnoughPoints) }, null)!;

        [PatchPostfix]
        private static void Postfix(SpawnSystem __instance, BotCreationData profileData, BotZone zone, int maxCount, Vector3? closestBossPos,
            ActionIfNotEnoughPoints mode, ref List<ISpawnPoint> __result)
        {
            try
            {
                ScavPointFilter.After(__instance, profileData, zone, maxCount, closestBossPos, mode, ref __result);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("ScavPointFilter", ex);
            }
        }
    }

    internal static class ScavPointFilter
    {
        private const float TargetLift = 1.5f;
        private const float SightRange = 200f;
        private static readonly List<(Vector3 pos, Vector3 eye)> Humans = new List<(Vector3, Vector3)>();
        private static readonly List<ISpawnPoint> Pool = new List<ISpawnPoint>();
        private static readonly HashSet<ISpawnPoint> Seen = new HashSet<ISpawnPoint>();
        private static readonly Dictionary<string, float> LastDelayLog = new Dictionary<string, float>();

        private static bool ScavRole(WildSpawnType r) =>
            r == WildSpawnType.assault || r == WildSpawnType.assaultGroup || r == WildSpawnType.cursedAssault || r == WildSpawnType.marksman;

        internal static void After(SpawnSystem sys, BotCreationData data, BotZone zone, int maxCount, Vector3? perfectPos,
            ActionIfNotEnoughPoints mode, ref List<ISpawnPoint> result)
        {
            if (result == null || result.Count == 0 || maxCount <= 0 || data == null || zone == null) return; // 바닐라가 못 고른 호출은 그대로
            var c = RaidContext.Current;
            if (c == null) return;
            if (c.S.Scav == null || !c.S.Scav.Managed) return; // 1.1.4: 스캐브를 바닐라에 맡긴 맵(scavVanilla·연구소 기본)은 지점도 바닐라 그대로
            if (data._profileData == null || !data._profileData.TryGetRole(out var role, out _) || !ScavRole(role)) return;
            PmcZones.EnsurePoints(c);

            bool near = perfectPos.HasValue;
            double dist = c.S.ScavDist;
            if (!near) CollectHumans();
            if (Log.Level == "verbose")
                Log.Verbose($"{c.S.MapId} 스캐브 지점 검사: {role} {maxCount}명, 구역 {zone.NameZone}, 방식 {mode}{(near ? ", 지정 위치" : "")}, 바닐라 {result.Count}개"
                    + (result.Count > 0 ? $" 첫 지점 {Describe(result[0], near)}" : ""));

            // 바닐라가 고른 지점이 1단계를 통과하면 그대로.
            bool allOk = true;
            for (int i = 0; i < result.Count && allOk; i++) allOk = Pass(result[i], near, dist, true, out _);
            if (allOk && result.Count > 0) return;

            var cache = sys._filteredPointsCache;
            Pool.Clear();
            Seen.Clear();
            if (cache != null)
                for (int i = 0; i < cache.ValidPointsCount && i < cache.AllPoints.Count; i++)
                {
                    var p = cache.AllPoints[i];
                    if (p != null && Seen.Add(p)) Pool.Add(p);
                }
            foreach (var p in result) if (p != null && Seen.Add(p)) Pool.Add(p);

            var good = new List<ISpawnPoint>();
            int stage = 0;
            for (int s = 1; s <= (near ? 1 : 3) && good.Count == 0; s++)
            {
                stage = s;
                double d = s == 2 ? dist * 0.5 : dist;
                bool hidden = s != 3;
                foreach (var p in Pool) if (Pass(p, near, d, hidden, out _)) good.Add(p);
            }

            var old = result.Count > 0 ? result[0] : null;
            if (good.Count == 0)
            {
                if (mode == ActionIfNotEnoughPoints.FillWithDiscardedPointsAndDuplicates) return; // 강제로 채우는 호출은 그대로
                result = mode == ActionIfNotEnoughPoints.ReturnNothing ? null! : new List<ISpawnPoint>();
                string key = zone.NameZone ?? "";
                float now = Time.time;
                if (!LastDelayLog.TryGetValue(key, out var t) || now - t > 30f || now < t)
                {
                    LastDelayLog[key] = now;
                    Log.Info($"{c.S.MapId} 스캐브 지점 없음 → 미룸: {role} {maxCount}명, 구역 {zone.NameZone} 후보 {Pool.Count}개"
                        + (old != null ? $", 바닐라 지점 {Describe(old, near)}" : ""));
                }
                return;
            }

            var picked = new List<ISpawnPoint>(maxCount);
            for (int i = 0; i < good.Count && picked.Count < maxCount; i++) picked.Add(good[i]);
            if (picked.Count < maxCount)
            {
                switch (mode)
                {
                    case ActionIfNotEnoughPoints.ReturnNothing:
                        result = null!;
                        return;
                    case ActionIfNotEnoughPoints.DuplicateIfAtLeastOne:
                        while (picked.Count < maxCount) picked.Add(picked[0]);
                        break;
                    case ActionIfNotEnoughPoints.FillWithDiscardedPointsAndDuplicates:
                        while (picked.Count < maxCount) picked.Add(picked[picked.Count - 1]);
                        break;
                }
            }
            result = picked;
            bool same = old != null && ReferenceEquals(old, picked[0]);
            Log.Info($"{c.S.MapId} 스캐브 지점 {(same ? "그대로" : "바꿈")}({stage}단계{(stage == 2 ? ", 거리 절반" : stage == 3 ? ", 보여도 멂" : "")}): {role} {maxCount}명, 구역 {zone.NameZone}, "
                + (old != null ? $"바닐라 {Describe(old, near)} → " : "")
                + $"{Describe(picked[0], near)}");
        }

        /// <summary>길 검사 + (근처 보충이 아니면) 모든 사람과 거리 d 이상 + hidden이면 안 보임.</summary>
        private static bool Pass(ISpawnPoint p, bool near, double d, bool hidden, out string why)
        {
            why = "";
            if (p == null) { why = "없음"; return false; }
            if (PointNav.IsBad(p)) { why = "길"; return false; }
            if (near) return true;
            var pos = p.Position;
            float d2 = (float)(d * d);
            var target = pos + Vector3.up * TargetLift;
            for (int i = 0; i < Humans.Count; i++)
            {
                var h = Humans[i];
                float sq = (h.pos - pos).sqrMagnitude;
                if (sq < d2) { why = "거리"; return false; }
                if (hidden && sq < SightRange * SightRange && !Physics.Linecast(h.eye, target, LayersMaskController.HighPolyWithTerrainMask))
                {
                    why = "보임";
                    return false;
                }
            }
            return true;
        }

        private static string Describe(ISpawnPoint p, bool near)
        {
            double human = double.MaxValue;
            foreach (var h in Humans) human = Math.Min(human, Vector3.Distance(h.pos, p.Position));
            Pass(p, near, 0, true, out var why);
            return $"{PointNav.Short(p.Id)}({p.Position.x:0},{p.Position.z:0}) 사람 {(human == double.MaxValue ? "-" : human.ToString("0"))}m"
                + (why == "보임" ? "·보임" : why == "길" ? "·길 검사 걸림" : "");
        }

        private static void CollectHumans()
        {
            Humans.Clear();
            var gw = Singleton<GameWorld>.Instance;
            var alive = gw?.AllAlivePlayersList;
            if (alive == null) return;
            for (int i = 0; i < alive.Count; i++)
            {
                var pl = alive[i];
                if (pl == null || pl.IsAI) continue;
                Humans.Add((pl.Position, Eye(pl)));
            }
        }

        /// <summary>눈 위치: 카메라 뼈 → 머리 뼈 → 발 + 1.6m (NearDirector와 같음).</summary>
        private static Vector3 Eye(Player player)
        {
            var cam = player.CameraPosition;
            if (cam != null) return cam.position;
            var head = player.PlayerBones?.Head;
            if (head != null) return head.position;
            return player.Position + Vector3.up * 1.6f;
        }
    }
}
