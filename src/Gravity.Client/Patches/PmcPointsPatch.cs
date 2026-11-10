using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.Game.Spawning;
using Gravity.Shared;
using SPT.Reflection.Patching;

namespace Gravity.Client.Patches
{
    /// <summary>
    /// SpawnSystem.GetPmcSpawnPoints 앞(정적 메서드, SpawnSystem.cs:1179): Gravity 분대의 TrySpawn 안에서 불렸으면
    /// 바닐라("사람 PMC에게서 먼 1등 지점 복제", kb 2.33) 대신 PointPicker.PickSquad로 고른 지점을 filteredPoints에 넣고 원래 함수는 건너뛴다.
    /// 후보는 RaidContext.ZonePoints[zone] 중 재사용 대기(IsValid(time))와 진영(IsValid(side))이 맞는 것(바닐라와 같은 두 필터).
    /// 고른 지점은 10초 예약. 고르지 못하면 아무것도 넣지 않는다 → SelectAISpawnPoints가 빈 목록 → TrySpawn이 다음 구역, 끝나면 대기열.
    /// 근처 보충(F8) PMC 웨이브면 NearDirector가 고른 지점(Tag.PointId) 하나만 같은 두 필터를 거쳐 넣는다(맞지 않으면 아무것도 넣지 않음).
    /// TrySpawn 밖에서 불린 경우(ValidateSpawnPosition 등)와 예외는 바닐라.
    /// 바닐라 TrySpawn은 maxCount=1로 지점 하나만 받고(BotBossSpawn.cs:363·366) SelectAISpawnPoints가 maxCount개만 꺼내므로(SpawnSystem.cs:1009-1013)
    /// 분대 전체 인원만큼 넣어도 실제로는 첫 지점에 리더가 나고 호위는 그 지점 복제(BotBossSpawn.cs:374-381)에 난다(바닐라와 같은 결과).
    /// </summary>
    internal sealed class PmcPointsPatch : ModulePatch
    {
        private const float ReserveSec = 10f;

        protected override MethodBase GetTargetMethod() =>
            typeof(SpawnSystem).GetMethod(nameof(SpawnSystem.GetPmcSpawnPoints), BindingFlags.Static | BindingFlags.Public)!;

        [PatchPrefix]
        private static bool Prefix(SpawnSystem.SpawnPointsFilteredCollection filteredPoints, int maxCount, BotZone zone,
            BotCreationData creationData, float time)
        {
            try
            {
                var ctx = PmcTrySpawnPatch.ActiveFor(creationData);
                if (ctx == null || filteredPoints == null || zone == null) return true;
                var c = RaidContext.Current;
                if (c == null || !ReferenceEquals(c, ctx.Raid)) return true;
                var profiles = creationData.Profiles;
                if (profiles == null || profiles.Count == 0) return true;
                var role = profiles[0]?.Info?.Settings?.Role;
                if (role == null || !role.Value.IsPmcBot()) return true;
                if (!PmcZones.EnsurePoints(c)) return true;

                if (ctx.Near != null)
                {
                    ctx.PickedAny |= PickNear(c, filteredPoints, ctx.Near.PointId, creationData, time);
                    return false;
                }

                var picked = Pick(c, filteredPoints, zone, creationData, Math.Max(ctx.Size, maxCount), time);
                ctx.PickedAny |= picked;
                return false;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("PmcPoints", ex);
                return true;
            }
        }

        /// <summary>맵 전체 후보 중 사람에게서 시작 보호 반경 이상 떨어진 지점이 하나라도 있는지. 없으면(공장처럼 작은 맵) 가장 먼 지점을 허용한다.
        /// 구역 하나가 아니라 맵 전체로 본다: 큰 맵에서 플레이어 가까운 구역이 보호에 다 걸려도 다른 구역으로 넘어가야 하므로.</summary>
        private static bool AnyOutsideProtect(RaidContext c, List<Actor> actors, double radius)
        {
            foreach (var list in c.ZonePoints.Values)
                foreach (var cd in list)
                    if (PointPicker.HumanDist(cd.Pos, actors) >= radius) return true;
            return false;
        }

        private static bool PickNear(RaidContext c, SpawnSystem.SpawnPointsFilteredCollection filteredPoints, int id,
            BotCreationData data, float now)
        {
            if (!c.PointById.TryGetValue(id, out var sp) || sp == null) return false;
            if (!sp.IsValid(now) || !sp.IsValid(data.Side)) return false;
            filteredPoints.InsertRange(new List<ISpawnPoint> { sp });
            c.Reserved.Add((PmcZones.V(sp.Position), now + ReserveSec));
            return true;
        }

        private static bool Pick(RaidContext c, SpawnSystem.SpawnPointsFilteredCollection filteredPoints, BotZone z,
            BotCreationData data, int size, float now)
        {
            if (!c.ZonePoints.TryGetValue(z, out var all) || all.Count == 0) return false;

            var side = data.Side;
            var cands = new List<Candidate>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                var sp = c.PointById[all[i].Id];
                if (sp.IsValid(now) && sp.IsValid(side)) cands.Add(all[i]);
            }
            if (cands.Count == 0) { PickDiag.Fail(c, z, all.Count, cands, null, now); return false; }

            c.Reserved.RemoveAll(r => r.until <= now);
            var reserved = new List<Vec3>(c.Reserved.Count);
            for (int i = 0; i < c.Reserved.Count; i++) reserved.Add(c.Reserved[i].pos);

            var s = c.S;
            var actors = PmcZones.Actors();
            bool protect = s.ProtectOn && (!c.Started || c.Elapsed < s.ProtectSec);
            var rules = new PickRules(
                PmcDist: s.PmcDist, BotDist: s.SpacingBots, ReserveDist: s.PmcDist,
                ProtectActive: protect, ProtectRadius: s.ProtectRadius,
                ProtectFallback: protect && !AnyOutsideProtect(c, actors, s.ProtectRadius));
            var r = PointPicker.PickSquad(cands, actors, reserved, size, rules, c.Rng);
            if (r.Ids.Count == 0) { PickDiag.Fail(c, z, all.Count, cands, (actors, reserved, rules), now); return false; }
            if (r.Reason == "farthest")
                Log.InfoOnce("PmcPoints.Farthest:" + s.Seed, $"{s.MapId} 맵 전체가 시작 보호 반경({s.ProtectRadius:0}m) 안: 시작 PMC는 플레이어에게서 가장 먼 지점에 냄");

            var pts = new List<ISpawnPoint>(r.Ids.Count);
            var seen = new HashSet<int>();
            for (int i = 0; i < r.Ids.Count; i++)
            {
                var sp = c.PointById[r.Ids[i]];
                pts.Add(sp);
                if (seen.Add(r.Ids[i])) c.Reserved.Add((PmcZones.V(sp.Position), now + ReserveSec));
            }
            filteredPoints.InsertRange(pts);
            return true;
        }
    }

    /// <summary>
    /// 진단(diag.log = verbose일 때만): PMC 분대가 지점을 못 고른 이유를 구역별로 20초에 한 줄. 후보마다 처음 걸린 규칙을 센다
    /// (PointPicker.FirstOk와 같은 순서·같은 엄격한 거리). 1.0 점검: 감염 판 세관에서 시작 PMC가 시작 보호 90초 뒤에야 나온 원인 확인용.
    /// </summary>
    internal static class PickDiag
    {
        private static readonly Dictionary<string, float> Last = new Dictionary<string, float>();

        internal static void Fail(RaidContext c, BotZone z, int total, List<Candidate> cands,
            (List<Actor> actors, List<Vec3> reserved, PickRules r)? why, float now)
        {
            if (Log.Level != "verbose") return;
            string key = c.S.Seed + ":" + z.NameZone;
            if (Last.TryGetValue(key, out var t) && now - t < 20f) return;
            Last[key] = now;
            int occ = 0, near = 0, bot = 0, prot = 0, res = 0, ok = 0;
            double humanMin = double.MaxValue;
            if (why is { } w)
            {
                foreach (var cd in cands)
                {
                    string? hit = null;
                    foreach (var a in w.actors)
                    {
                        double d = Vec3.Dist(cd.Pos, a.Pos);
                        bool hp = a.Kind == ActorKind.Human || a.Kind == ActorKind.Pmc;
                        if (a.Kind == ActorKind.Human && d < humanMin) humanMin = d;
                        if (hit != null) continue;
                        if (d < w.r.OccupiedRadius) hit = "occ";
                        else if (d < (hp ? w.r.PmcDist : w.r.BotDist)) hit = hp ? "near" : "bot";
                        else if (w.r.ProtectActive && a.Kind == ActorKind.Human && d < w.r.ProtectRadius) hit = "prot";
                    }
                    if (hit == null)
                        foreach (var rv in w.reserved)
                            if (Vec3.Dist(cd.Pos, rv) < w.r.ReserveDist) { hit = "res"; break; }
                    switch (hit) { case "occ": occ++; break; case "near": near++; break; case "bot": bot++; break; case "prot": prot++; break; case "res": res++; break; default: ok++; break; }
                }
            }
            Log.Verbose($"{c.S.MapId} PMC 지점 못 고름: 구역 {z.NameZone} 지점 {total}개 중 사용 가능 {cands.Count}개"
                + (why is { } w2 ? $" — 자리 겹침 {occ}·사람/PMC 거리 {near}·봇 거리 {bot}·시작 보호 {prot}·예약 {res}·통과 {ok}(완화 전), 가장 가까운 사람 {(humanMin == double.MaxValue ? "-" : humanMin.ToString("0"))}m, 보호 {(w2.r.ProtectActive ? "켬" : "끔")}" : " — 재사용 대기·진영 필터에 다 걸림")
                + $", 경과 {(c.Started ? c.Elapsed.ToString("0") + "초" : "시작 전")}");
        }
    }
}
