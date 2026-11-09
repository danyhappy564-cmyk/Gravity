using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using EFT.Game.Spawning;
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

        internal static void Set(RaidContext c, object own, bool reserveOnPass = false)
        {
            Excluded = c.CountCapExcluded();
            Own = own;
            ReserveOnPass = reserveOnPass;
        }

        internal static void Clear()
        {
            Excluded = -1;
            Own = null;
            ReserveOnPass = false;
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

        [PatchPrefix]
        private static void Prefix(BossLocationSpawn wave)
        {
            NativeScope.Clear();
            try
            {
                var c = RaidContext.Current;
                if (c == null || wave == null || !c.Owns(wave)) return; // 다른 모드 PMC·보스는 바닐라 검사(3차 재검토 3번)
                NativeScope.Set(c, wave, reserveOnPass: !wave.IgnoreMaxBots && !c.Ledger.IsHeld(wave));
            }
            catch (Exception ex)
            {
                NativeScope.Clear();
                Log.ErrorOnce("BossSpawnScope", ex);
            }
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
