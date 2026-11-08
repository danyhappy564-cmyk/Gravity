using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Comfort.Common;
using EFT;
using Gravity.Shared;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Gravity.Client.Patches
{
    /// <summary>
    /// BotBossSpawn.TrySpawn(BotBossSpawn.cs:303) 앞뒤: Gravity 분대(RaidContext 있음 + wave.BossType이 pmcUSEC·pmcBEAR)의
    /// "동시에 살아 있는 그룹" 제한과 대기열 만료.
    /// 바닐라 계약: true면 처리 끝(생성했거나 "can't find zones"로 버림), false면 Spawn이 DelayBossSpawn → SpawnDelaysService가 3초마다 다시 부른다(시간 초과 없음).
    /// - Prefix: GroupCap.Scope=="alive"이고 규칙에 걸리면 __result=false로 원래 함수를 건너뛴다(대기열로).
    /// - Postfix: 실패면 첫 실패 시각을 FirstFail에 남기고 SpawnExpiry.Expired면 __result=true로 바꿔 버린다.
    ///   이유: 마지막 실패가 그룹 제한이면 groupCap, 지점·구역 실패면 expired. 바닐라가 구역 없이 true를 낸 경우는 noPoint.
    ///   실제 생성이면 Spawned[역할] += 1 + EscortCount.
    /// - 같은 호출 안에서 GetPossibleZones·GetPmcSpawnPoints 패치가 이 호출을 알아보도록 Active를 둔다(게임 스레드, 다시 들어오지 않음).
    /// </summary>
    internal sealed class PmcTrySpawnPatch : ModulePatch
    {
        internal sealed class Ctx
        {
            public RaidContext Raid = null!;
            public BossLocationSpawn Wave = null!;
            public BotCreationData Data = null!;
            public int Size;
            public bool Waited;
            public bool ZonesSeen;
            public bool ZonesOk;
            public bool PickedAny;
        }

        private static Ctx? _active;

        /// <summary>지금 도는 Gravity 분대 TrySpawn의 맥락(그 creationData일 때만).</summary>
        internal static Ctx? ActiveFor(BotCreationData? data)
        {
            var a = _active;
            return a != null && data != null && ReferenceEquals(a.Data, data) ? a : null;
        }

        /// <summary>GetPossibleZones 뒤에서 부른다: 지금 TrySpawn이 받은 구역 목록이 쓸 만한지 남긴다.</summary>
        internal static void SawZones(BossLocationSpawn wave, List<BotZone> zones)
        {
            var a = _active;
            if (a == null || !ReferenceEquals(a.Wave, wave)) return;
            a.ZonesSeen = true;
            a.ZonesOk = zones.Count > 0 && zones[0] != null; // TrySpawn의 "can't find zones" 두 조건(BotBossSpawn.cs:312·317)의 반대
        }

        protected override MethodBase GetTargetMethod() =>
            typeof(BotBossSpawn).GetMethod(nameof(BotBossSpawn.TrySpawn), BindingFlags.Instance | BindingFlags.Public)!;

        [PatchPrefix]
        private static bool Prefix(BotBossSpawn __instance, BossLocationSpawn wave, BotCreationData creationData, ref bool __result)
        {
            _active = null;
            try
            {
                var c = RaidContext.Current;
                if (c == null || wave == null || creationData == null || !wave.BossType.IsPmcBot()) return true;
                PmcZones.EnsureZones(c, __instance);

                var ctx = new Ctx { Raid = c, Wave = wave, Data = creationData, Size = 1 + Math.Max(0, wave.EscortCount) };
                _active = ctx;

                var cap = c.S.GroupCap;
                if (cap != null && cap.Scope == "alive"
                    && AliveGroupRule.Check(AliveGravitySquads(), ctx.Size, cap) == CapVerdict.Wait)
                {
                    ctx.Waited = true;
                    __result = false;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _active = null;
                Log.ErrorOnce("PmcTrySpawn.Prefix", ex);
                return true;
            }
        }

        [PatchPostfix]
        private static void Postfix(BossLocationSpawn wave, BotCreationData creationData, ref bool __result)
        {
            try
            {
                var ctx = ActiveFor(creationData);
                if (ctx == null || !ReferenceEquals(ctx.Wave, wave)) return;
                var c = ctx.Raid;
                if (!ReferenceEquals(c, RaidContext.Current)) return;
                var now = Time.time;

                if (__result)
                {
                    c.FirstFail.Remove(wave);
                    if (ctx.ZonesSeen && ctx.ZonesOk)
                    {
                        var key = wave.BossType == WildSpawnType.pmcBEAR ? "pmcBEAR" : "pmcUSEC";
                        c.Spawned[key] += ctx.Size;
                    }
                    else
                    {
                        c.Dropped["noPoint"]++; // 바닐라가 구역을 못 찾아 버린 경우
                    }
                    return;
                }

                var first = c.FirstFail.GetValue(wave, _ => new StrongBox<float>(now));
                if (!SpawnExpiry.Expired(first.Value, now, c.Elapsed, c.S.HardStopPmcSec)) return;

                c.FirstFail.Remove(wave);
                c.Dropped[ctx.Waited ? "groupCap" : "expired"]++;
                __result = true; // 처리 끝으로 돌려 Spawn이 DelayBossSpawn을 부르지 않게 한다(대기열에서 빠짐)
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("PmcTrySpawn.Postfix", ex);
            }
        }

        [PatchFinalizer]
        private static void Finalizer()
        {
            _active = null;
        }

        /// <summary>살아 있는 PMC 봇을 BotsGroup별로 센 인원 목록(분대 일부가 죽었으면 줄어든 크기).</summary>
        private static List<int> AliveGravitySquads()
        {
            var res = new List<int>();
            var gw = Singleton<GameWorld>.Instance;
            if (gw == null) return res;
            var counts = new Dictionary<BotsGroup, int>();
            var alive = gw.AllAlivePlayersList;
            for (int i = 0; i < alive.Count; i++)
            {
                var p = alive[i];
                if (p == null || !p.IsAI) continue;
                var role = p.Profile?.Info?.Settings?.Role;
                if (role == null || !role.Value.IsPmcBot()) continue;
                var g = p.AIData?.BotOwner?.BotsGroup;
                if (g == null) continue;
                counts.TryGetValue(g, out var n);
                counts[g] = n + 1;
            }
            foreach (var n in counts.Values) res.Add(n);
            return res;
        }
    }
}
