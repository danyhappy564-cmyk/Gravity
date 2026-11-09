using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using EFT.Game.Spawning;
using Gravity.Shared;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Gravity.Client.Patches
{
    /// <summary>
    /// 게임 자체 상한 검사(BotSpawner.CheckOnMax)를 Gravity 요청에 한해 고치는 범위 표시(1.0, Codex 검토 1번·재검토 1·3번).
    /// Excluded = 뺄 팩션 봇 수(-1이면 Gravity 요청 아님 → 바닐라 검사 그대로), Own = 지금 검사받는 요청의 예약 키(자기 예약은 더하지 않는다).
    /// 게임 스레드에서만 쓴다. 표시를 켜는 패치는 모두 같은 호출 안(동기 구간)에서 켜고 끈다.
    /// </summary>
    internal static class NativeScope
    {
        internal static int Excluded = -1;
        internal static object? Own;

        /// <summary>앞단 예약 없이 들어온 Gravity 분대(서버 계획 분대)면 true: 검사를 통과하는 순간 분대 전체를 예약한다(3차 재검토 1번).</summary>
        internal static bool ReserveOnPass;

        /// <summary>Gravity PMC 분대(근처 보충 제외) 검사면 true: 상한 표 "PMC 동시 무리 최대"에 닿았으면 대기열로 보낸다(1.0).</summary>
        internal static bool PmcWave;

        internal static void Set(RaidContext c, object own, bool reserveOnPass = false, bool pmcWave = false)
        {
            Excluded = c.CountCapExcluded();
            Own = own;
            ReserveOnPass = reserveOnPass;
            PmcWave = pmcWave;
        }

        internal static void Clear()
        {
            Excluded = -1;
            Own = null;
            ReserveOnPass = false;
            PmcWave = false;
        }
    }

    /// <summary>
    /// BotBossSpawn.Spawn(BotBossSpawn.cs:250) 앞뒤: Gravity 분대(PMC 역할 또는 근처 보충 웨이브)의 상한 검사 동안만 범위를 표시한다.
    /// Spawn은 async지만 상한 검사(_spawner.CheckOnMax)는 첫 await(프로필 받기) 전에 같은 호출 안에서 돈다. 그래서 Prefix에서 켜고
    /// Postfix(첫 await에서 돌아온 뒤)에서 끄면 그 검사만 고칠 수 있다. 대기열 재시도(CheckSpawnAfterDelay → Spawn)도 같은 길이다.
    /// </summary>
    internal sealed class BossSpawnScopePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(BotBossSpawn).GetMethod(nameof(BotBossSpawn.Spawn), BindingFlags.Instance | BindingFlags.Public)!;

        /// <summary>종류별 최대 인원 판정을 이미 한 웨이브(대기열 재시도는 다시 세지 않는다).</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BossLocationSpawn, object> Checked =
            new System.Runtime.CompilerServices.ConditionalWeakTable<BossLocationSpawn, object>();

        [PatchPrefix]
        private static bool Prefix(BossLocationSpawn wave, ref Task __result)
        {
            NativeScope.Clear();
            try
            {
                var c = RaidContext.Current;
                if (c == null || wave == null) return true;
                if (c.Owns(wave))
                {
                    NativeScope.Set(c, wave, reserveOnPass: !wave.IgnoreMaxBots && !c.Ledger.IsHeld(wave),
                        pmcWave: wave.BossType.IsPmcBot() && !c.NearWaves.TryGetValue(wave, out _));
                    return true;
                }
                // 옛 서버(상한 표 없음): 다른 모드 PMC·보스는 바닐라 검사(3차 재검토 3번)
                if (c.S.Caps == null) return true;

                // 상한 표(1.0): 보스·특수·팩션·이벤트 무리는 종류별 최대 인원 안으로 호위 수를 줄이고, 이미 꽉 찼으면 이 무리는 건너뛴다.
                var kind = c.KindOf(wave.BossType);
                int max = CapKinds.MaxOf(c.S.Caps, kind);
                if (max > 0 && Array.IndexOf(CapKinds.Table, kind) >= 0 && !Checked.TryGetValue(wave, out _))
                {
                    Checked.Add(wave, wave);
                    int alive = c.CountKind(kind) + c.AdmittedRecently(kind, Time.time);
                    int esc = CapKinds.FitEscorts(max, alive, wave.EscortCount);
                    if (esc < 0)
                    {
                        Log.Verbose($"{c.S.MapId} {wave.BossType} 무리 건너뜀: {kind} 최대 {max}명(지금 {alive}명)");
                        __result = Task.CompletedTask;
                        return false;
                    }
                    if (esc < wave.EscortCount)
                    {
                        Log.Verbose($"{c.S.MapId} {wave.BossType} 호위 {wave.EscortCount}→{esc}명: {kind} 최대 {max}명(지금 {alive}명)");
                        wave.EscortCount = esc;
                    }
                    c.Admit(kind, esc + 1, Time.time);
                }
                // 맵 상한 검사(상한을 지키는 항목만)도 상한 표대로 센다: "따로 셈" 종류를 빼고 Gravity 진행 중 예약을 더한다. 예약은 하지 않는다.
                NativeScope.Set(c, wave);
            }
            catch (Exception ex)
            {
                NativeScope.Clear();
                Log.ErrorOnce("BossSpawnScope", ex);
            }
            return true;
        }

        /// <summary>표시를 끄고, 생성 Task가 실패·취소로 끝나면 그 분대 예약을 푼다(시간만으로는 풀지 않는다, 3차 재검토 2번).</summary>
        [PatchPostfix]
        private static void Postfix(BossLocationSpawn wave, Task __result)
        {
            NativeScope.Clear();
            try
            {
                var c = RaidContext.Current;
                if (c == null || wave == null || __result == null || !c.Ledger.IsHeld(wave)) return;
                if (__result.IsCompleted) { if (__result.IsFaulted || __result.IsCanceled) c.Ledger.Release(wave); return; }
                __result.ContinueWith(t => { if (t.IsFaulted || t.IsCanceled) c.Ledger.Release(wave); }, TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("BossSpawnScope.Post", ex);
            }
        }

        [PatchFinalizer]
        private static void Finalizer() => NativeScope.Clear();
    }

    /// <summary>
    /// BotSpawner.CheckOnMax(BotSpawner.cs:424) 앞: NativeScope가 켜진 Gravity 요청 검사일 때만
    /// 점유 = max(생존+생성 중, 생존+로딩) − 상한 제외 팩션 + 다른 Gravity 요청의 "진행 중" 예약(분대 대원 생성 대기 등)으로 다시 계산한다.
    /// 그 밖(서버 계획 웨이브·보스·다른 모드)은 바닐라 그대로. 대기열과 "대기 중" 예약은 세지 않는다: 이 검사는 대기열에서 다시 나온
    /// 요청 자신에게도 불리므로, 대기 몫을 세면 대기 요청끼리 서로를 막아 영영 못 나올 수 있다(앞단 검사 RaidContext.TryReserve는 센다).
    /// </summary>
    internal sealed class CheckOnMaxPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(BotSpawner).GetMethod(nameof(BotSpawner.CheckOnMax), BindingFlags.Instance | BindingFlags.Public)!;

        [PatchPrefix]
        private static bool Prefix(BotSpawner __instance, int wantSpawn, ref int toDelay, ref int toSpawn, bool calcOnlySimpleBots)
        {
            int excluded = NativeScope.Excluded;
            if (excluded < 0 || calcOnlySimpleBots || __instance == null || __instance.MaxBots == 0) return true;
            try
            {
                var c = RaidContext.Current;
                int others = c?.Ledger.HeldInFlight(Time.time, NativeScope.Own) ?? 0;
                int occupied = Math.Max(0, Math.Max(__instance.AllBotsWithLoaded, __instance.AliveAndLoadingBotsCount) - excluded) + others;
                int room = __instance.MaxBots - occupied;
                // 상한 표 "PMC 동시 무리 최대"(1.0): 서버 계획 분대도 꽉 찼으면 게임 대기열에서 기다린다
                if (NativeScope.PmcWave && c != null && c.PmcGroupsFull()) room = 0;
                if (room <= 0) { toDelay = wantSpawn; toSpawn = 0; }
                else if (room < wantSpawn) { toDelay = wantSpawn - room; toSpawn = room; }
                else { toDelay = 0; toSpawn = wantSpawn; }
                // 서버 계획 분대: 통과하면 분대 전체를 지금 예약(프로필 받는 동안·대원 생성 전까지 다른 요청이 이 자리를 못 쓰게)
                if (toDelay == 0 && NativeScope.ReserveOnPass && c != null && NativeScope.Own != null)
                {
                    c.Ledger.Hold(NativeScope.Own, wantSpawn, false, Time.time);
                    NativeScope.ReserveOnPass = false;
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("CheckOnMax", ex);
                return true;
            }
        }
    }

    /// <summary>
    /// BotBossSpawn.SpawnBossSupports(BotBossSpawn.cs:509) 뒤: 분대장이 활성화된 뒤 대원 생성을 시작하는 함수다. 이 Task가 끝나면
    /// 대원도 게임 집계(생성 중 또는 대기열)에 잡혔으므로 그 분대의 남은 예약을 푼다(재검토 1번). 대원 생성은 상한 검사를 하지 않으므로
    /// (withCheckMinMax:false, BotSpawner.cs:506) 그 전까지 예약으로 자리를 잡아 둬야 다른 요청이 그 자리를 쓰지 않는다.
    /// </summary>
    internal sealed class BossSupportsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(BotBossSpawn).GetMethod(nameof(BotBossSpawn.SpawnBossSupports), BindingFlags.Instance | BindingFlags.Public)!;

        /// <summary>대원 생성이 시작됐다: 이제 분대장이 죽어도 대원 예약은 이 작업이 끝날 때 푼다.</summary>
        [PatchPrefix]
        private static void Prefix(BossLocationSpawn wave)
        {
            var c = RaidContext.Current;
            if (c != null && wave != null && c.WatchOf.TryGetValue(wave, out var w)) w.SupportsStarted = true;
        }

        [PatchPostfix]
        private static void Postfix(BossLocationSpawn wave, Task __result)
        {
            try
            {
                var c = RaidContext.Current;
                if (c == null || wave == null || !c.Ledger.IsHeld(wave)) return;
                if (__result == null || __result.IsCompleted) { c.Ledger.Release(wave); return; }
                __result.ContinueWith(_ => c.Ledger.Release(wave), TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("BossSupports", ex);
            }
        }
    }

    /// <summary>
    /// BotBossSpawn.SpawnBossMain(BotBossSpawn.cs:555) 앞: 추적 중인 분대(RaidContext.SquadOf)의 분대장 생성 콜백을 감싸,
    /// 분대장이 실제로 만들어졌는지 표시하고 활성화 실패(ActiveFail)를 지켜본다(4차 재검토 1번). 게임 콜백은 그대로 부른다.
    /// </summary>
    internal sealed class SpawnBossMainPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(BotBossSpawn).GetMethod(nameof(BotBossSpawn.SpawnBossMain), BindingFlags.Instance | BindingFlags.Public)!;

        [PatchPrefix]
        private static void Prefix(BotCreationData creationData, ref Action<BotOwner> callback)
        {
            try
            {
                var c = RaidContext.Current;
                if (c == null || creationData == null || !c.SquadOf.TryGetValue(creationData, out var w)) return;
                var inner = callback;
                callback = owner =>
                {
                    try
                    {
                        w.LeaderSeen = true;
                        if (owner != null && !c.LeaderOf.TryGetValue(owner, out _))
                        {
                            c.LeaderOf.Add(owner, w);
                            Action<EBotState>? onState = null;
                            onState = state =>
                            {
                                if (state == EBotState.Active) owner.OnBotStateChange -= onState;
                                else if (state == EBotState.ActiveFail) { owner.OnBotStateChange -= onState; c.SquadFailed(w, "활성화 실패"); }
                            };
                            if (owner.BotState == EBotState.ActiveFail) c.SquadFailed(w, "활성화 실패");
                            else if (owner.BotState != EBotState.Active) owner.OnBotStateChange += onState;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.ErrorOnce("SpawnBossMain.Watch", ex);
                    }
                    inner?.Invoke(owner!);
                };
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("SpawnBossMain", ex);
            }
        }
    }

    /// <summary>
    /// BotCreatorClient.ActivateBot(BotCreationData, …)(BotCreatorClient.cs:165) 뒤: 봇 하나를 만드는 Task. 분대장 생성 자료의 Task가 끝났는데
    /// 분대장 콜백이 없었으면(프로필·플레이어 생성 null·취소) 분대장은 만들어지지 않았다 → 남은 대원 예약을 푼다(4차 재검토 1번).
    /// 성공이면 콜백이 이 Task 안에서 먼저 불린다(method_0 → method_3 → callback).
    /// </summary>
    internal sealed class ActivateBotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(BotCreatorClient).GetMethod(nameof(BotCreatorClient.ActivateBot), BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(BotCreationData), typeof(BotZone), typeof(bool), typeof(Func<BotOwner, BotZone, BotsGroup>), typeof(Action<BotOwner>), typeof(System.Threading.CancellationToken) }, null)!;

        [PatchPostfix]
        private static void Postfix(BotCreationData data, Task __result)
        {
            try
            {
                var c = RaidContext.Current;
                if (c == null || data == null || __result == null || !c.SquadOf.TryGetValue(data, out var w)) return;
                if (__result.IsCompleted) { if (!w.LeaderSeen) c.SquadFailed(w, "생성 실패"); return; }
                __result.ContinueWith(_ => { if (!w.LeaderSeen) c.SquadFailed(w, "생성 실패·취소"); }, TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("ActivateBot", ex);
            }
        }
    }

    /// <summary>
    /// BotSpawner.ActivateBotsByWave(SpawnWave)(BotSpawner.cs:383) 앞뒤: ScavDirector가 넣은 스캐브 웨이브면, 그 동기 구간에서
    /// 만들어지는 BotCreationData(아래 CreationDataMarkPatch)에 그 웨이브를 붙인다(재검토 3번). 첫 await 전에 BotCreationData.Create가
    /// 불리고, Create는 맨 앞에서 생성자를 부르므로 이 범위 안에서 잡힌다.
    /// </summary>
    internal sealed class ScavWaveScopePatch : ModulePatch
    {
        internal static SpawnWave? Current;

        protected override MethodBase GetTargetMethod() =>
            typeof(BotSpawner).GetMethod(nameof(BotSpawner.ActivateBotsByWave), BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(SpawnWave) }, null)!;

        [PatchPrefix]
        private static void Prefix(SpawnWave wave)
        {
            Current = null;
            var c = RaidContext.Current;
            if (c != null && wave != null && c.ScavWaves.TryGetValue(wave, out _)) Current = wave;
        }

        [PatchPostfix]
        private static void Postfix() => Current = null;

        [PatchFinalizer]
        private static void Finalizer() => Current = null;
    }

    /// <summary>BotCreationData(IGetProfileData) 생성자 뒤: ScavWaveScopePatch 범위 안이면 그 생성 자료를 Gravity 스캐브 요청으로 기억한다.</summary>
    internal sealed class CreationDataMarkPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(BotCreationData).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(IGetProfileData) }, null)!;

        [PatchPostfix]
        private static void Postfix(BotCreationData __instance)
        {
            var wave = ScavWaveScopePatch.Current;
            if (wave == null || __instance == null) return;
            try
            {
                var c = RaidContext.Current;
                if (c != null && !c.ScavData.TryGetValue(__instance, out _)) c.ScavData.Add(__instance, wave);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("CreationDataMark", ex);
            }
        }
    }

    /// <summary>
    /// BotSpawner.TryToSpawnInZoneInner(BotSpawner.cs:476) 앞뒤: Gravity 스캐브 요청의 생성 자료면 그 안의 상한 검사(CheckOnMax)에
    /// 팩션 제외를 적용한다(재검토 3번). 대기열 재시도(CheckSpawnOnFreeAfterDelay)도 같은 생성 자료로 이 함수를 다시 부른다. 동기 함수다.
    /// </summary>
    internal sealed class SpawnInZoneScopePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(BotSpawner).GetMethod(nameof(BotSpawner.TryToSpawnInZoneInner), BindingFlags.Instance | BindingFlags.Public)!;

        [PatchPrefix]
        private static void Prefix(BotCreationData data, ref bool __state)
        {
            __state = false;
            try
            {
                var c = RaidContext.Current;
                if (c == null || data == null || !c.ScavData.TryGetValue(data, out var wave)) return;
                NativeScope.Set(c, wave);
                __state = true;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("SpawnInZoneScope", ex);
            }
        }

        [PatchPostfix]
        private static void Postfix(bool __state)
        {
            if (__state) NativeScope.Clear();
        }

        [PatchFinalizer]
        private static void Finalizer(bool __state)
        {
            if (__state) NativeScope.Clear();
        }
    }
}
