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
            if (cands.Count == 0) return false;

            c.Reserved.RemoveAll(r => r.until <= now);
            var reserved = new List<Vec3>(c.Reserved.Count);
            for (int i = 0; i < c.Reserved.Count; i++) reserved.Add(c.Reserved[i].pos);

            var s = c.S;
            var rules = new PickRules(
                PmcDist: s.PmcDist, BotDist: s.SpacingBots, ReserveDist: s.PmcDist,
                ProtectActive: s.ProtectOn && c.Elapsed < s.ProtectSec, ProtectRadius: s.ProtectRadius);
            var r = PointPicker.PickSquad(cands, PmcZones.Actors(), reserved, size, rules, c.Rng);
            if (r.Ids.Count == 0) return false;

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
}
