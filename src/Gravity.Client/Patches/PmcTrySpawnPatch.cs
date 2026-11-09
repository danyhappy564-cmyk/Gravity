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
    /// - 근처 보충(F8) PMC 웨이브(RaidContext.NearWaves)는 Active만 두고(PmcPointsPatch가 고른 지점을 넣게) 그룹 제한·대기열 만료·
    ///   Spawned[역할] 기록은 하지 않는다. 인원은 NearDirector가 Spawned["near"]로 세고, 실패하면 Spawn이 DelayBossSpawn을 불러
    ///   PmcCapDelayPatch가 버린다.
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

            /// <summary>근처 보충 웨이브면 그 표시(아니면 null).</summary>
            public Directors.NearDirector.Tag? Near;
        }

        private static Ctx? _active;

        /// <summary>지금 도는 Gravity 분대 TrySpawn의 맥락(그 creationData일 때만).</summary>
        internal static Ctx? ActiveFor(BotCreationData? data)
        {
            var a = _active;
            return a != null && data != null && ReferenceEquals(a.Data, data) ? a : null;
        }

        /// <summary>GetPossibleZones 뒤에서, 끈 구역을 지우고 다시 섞은 "최종" 목록으로 부른다(Codex 검토 6번):
        /// 지금 TrySpawn이 받은 구역 목록이 쓸 만한지 남긴다.</summary>
        internal static void SawZones(BossLocationSpawn wave, List<BotZone> zones)
        {
            var a = _active;
            if (a == null || !ReferenceEquals(a.Wave, wave)) return;
            a.ZonesSeen = true;
            // TrySpawn의 "can't find zones" 두 조건(BotBossSpawn.cs:312·317)의 반대
            a.ZonesOk = PmcTally.ZonesUsable(zones.Count, zones.Count > 0 && zones[0] == null);
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
                // PMC 분대와 근처 보충 웨이브(역할 무관)만: 근처 스캐브 무리도 구역 판정·상한 예약 정리를 같이 받는다(재검토 1번).
                if (c == null || wave == null || creationData == null) return true;
                if (!wave.BossType.IsPmcBot() && !c.NearWaves.TryGetValue(wave, out _)) return true;
                PmcZones.EnsureZones(c, __instance);

                var ctx = new Ctx { Raid = c, Wave = wave, Data = creationData, Size = 1 + Math.Max(0, wave.EscortCount) };
                _active = ctx;

                if (c.NearWaves.TryGetValue(wave, out var near))
                {
                    ctx.Near = near;
                    return true;
                }

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
            var ctx = ActiveFor(creationData);
            bool original = __result;
            try
            {
                Tally(ctx, wave, ref __result);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("PmcTrySpawn.Postfix", ex);
            }
            finally
            {
                Settle(ctx, wave, original, __result);
            }
        }

        /// <summary>
        /// 상한 예약 정리(재검토 1번). false면 Spawn이 DelayBossSpawn을 부르므로 PmcCapDelayPatch가 대기/버림을 정한다.
        /// 실제로 분대장이 생성 처리에 들어갔으면(생성 중 집계에 바로 잡힘, BotSpawner.method_7) 대원 몫만 남기고 BossSupportsPatch가 마저 푼다.
        /// 구역이 없어 버려졌거나 만료로 버렸으면 다 푼다.
        /// </summary>
        private static void Settle(Ctx? ctx, BossLocationSpawn? wave, bool original, bool final)
        {
            try
            {
                if (wave == null || !final) return;
                var c = RaidContext.Current;
                if (c == null) return;
                bool spawned = original && ctx != null && ReferenceEquals(ctx.Wave, wave)
                    && PmcTally.Classify(true, ctx.ZonesSeen, ctx.ZonesOk) == PmcOutcome.Spawned;
                if (spawned) c.Ledger.Shrink(wave, Math.Max(0, wave.EscortCount), false, Time.time);
                else c.Ledger.Release(wave);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("PmcTrySpawn.Settle", ex);
            }
        }

        private static void Tally(Ctx? ctx, BossLocationSpawn wave, ref bool __result)
        {
            {
                if (ctx == null || !ReferenceEquals(ctx.Wave, wave)) return;
                var c = ctx.Raid;
                if (!ReferenceEquals(c, RaidContext.Current)) return;
                if (ctx.Near != null)
                {
                    // 바닐라가 구역을 못 찾아 true로 버린 경우만 여기서 센다. false는 DelayBossSpawn → PmcCapDelayPatch가 버린다.
                    if (PmcTally.Classify(__result, ctx.ZonesSeen, ctx.ZonesOk) == PmcOutcome.NoPoint && c.NearWaves.TryGetValue(wave, out _))
                    {
                        c.NearWaves.Remove(wave);
                        c.Spawned["near"] -= ctx.Near.Size;
                        c.Dropped["noPoint"]++;
                    }
                    return;
                }
                var now = Time.time;

                if (__result)
                {
                    c.FirstFail.Remove(wave);
                    if (PmcTally.Classify(__result, ctx.ZonesSeen, ctx.ZonesOk) == PmcOutcome.Spawned)
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

                // 시작 전(로딩 중) 실패는 만료 시계에 넣지 않는다: 시계가 OnGameStarted 때 0으로 돌아가므로 시작 뒤부터 센다.
                if (!c.Started) return;
                var first = c.FirstFail.GetValue(wave, _ => new StrongBox<float>(now));
                if (!SpawnExpiry.Expired(first.Value, now, c.Elapsed, c.S.HardStopPmcSec)) return;

                c.FirstFail.Remove(wave);
                c.Dropped[ctx.Waited ? "groupCap" : "expired"]++;
                __result = true; // 처리 끝으로 돌려 Spawn이 DelayBossSpawn을 부르지 않게 한다(대기열에서 빠짐)
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

    /// <summary>
    /// BotBossSpawn.DelayBossSpawn(BotBossSpawn.cs:292) 앞: 하드 스톱이 지난 Gravity 분대는 대기열에 다시 넣지 않고 버린다(Dropped["cap"]).
    /// 맵 상한이 차면 Spawn이 TrySpawn 없이 바로 DelayBossSpawn을 불러(BotBossSpawn.cs:259-262·279·286-288) PmcTrySpawnPatch의 만료가 돌지 않는다.
    /// 하드 스톱 전에는 그대로 자리 날 때까지 기다린다. 대기 항목은 3초마다 CheckSpawnAfterDelay → Spawn(:297-300)으로 다시 오므로,
    /// 하드 스톱 뒤 첫 재시도에서 여기로 와서 빠진다. DelayBossSpawn을 부르는 곳은 Spawn의 두 곳(:283·:288)뿐이고,
    /// TrySpawn 실패 경로(:283)는 하드 스톱이면 PmcTrySpawnPatch가 이미 true로 바꿔 여기까지 오지 않으므로 여기서 버리는 것은 상한 대기 몫이다.
    /// 근처 보충(F8) 웨이브(RaidContext.NearWaves, 역할 무관)는 대기열에 넣지 않고 바로 버린다: 나중에 내면 그때 플레이어 위치와 맞지 않는
    /// 낡은 지점에 나기 때문이다. 상한 때문이면 Dropped["nearCap"], 아니면(지점·구역 실패) Dropped["noPoint"], Spawned["near"]에서 뺀다.
    /// 단 봇 프로필을 아직 다 못 읽었으면(BotSpawner.IsProfilesLoaded=false, TrySpawn이 바로 false, BotBossSpawn.cs:305) 바닐라처럼 대기열에 둔다.
    /// </summary>
    internal sealed class PmcCapDelayPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(BotBossSpawn).GetMethod(nameof(BotBossSpawn.DelayBossSpawn), BindingFlags.Instance | BindingFlags.Public)!;

        [PatchPrefix]
        private static bool Prefix(BotBossSpawn __instance, BossLocationSpawn wave)
        {
            try
            {
                var c = RaidContext.Current;
                if (c == null || wave == null) return true;
                // 대기열에 들어가면 게임은 분대를 1로 센다(WaitCount): 대원 몫은 "대기 중" 예약으로 계속 잡는다. 버리면 다 푼다(재검토 1번).
                if (c.NearWaves.TryGetValue(wave, out var near))
                {
                    bool keep = DropNear(c, __instance, wave, near);
                    if (keep) Wait(c, wave); else c.Ledger.Release(wave);
                    return keep;
                }
                if (!wave.BossType.IsPmcBot()) return true;
                if (!SpawnExpiry.PastHardStop(c.Elapsed, c.S.HardStopPmcSec)) { Wait(c, wave); return true; }
                c.Ledger.Release(wave);
                c.FirstFail.Remove(wave);
                c.Dropped["cap"]++;
                return false; // 대기열에 넣지 않음 = 버림
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("PmcCapDelay", ex);
                return true;
            }
        }

        private static void Wait(RaidContext c, BossLocationSpawn wave) =>
            c.Ledger.Shrink(wave, Math.Max(0, wave.EscortCount), true, Time.time);

        private static bool DropNear(RaidContext c, BotBossSpawn? boss, BossLocationSpawn wave, Directors.NearDirector.Tag near)
        {
            var spawner = boss?._spawner;
            if (spawner != null && !spawner.IsProfilesLoaded) return true;
            int toDelay = 0;
            spawner?.CheckOnMax(near.Size, out toDelay, out _);
            c.NearWaves.Remove(wave);
            c.Spawned["near"] -= near.Size;
            c.Dropped[toDelay > 0 ? "nearCap" : "noPoint"]++;
            return false;
        }
    }
}
