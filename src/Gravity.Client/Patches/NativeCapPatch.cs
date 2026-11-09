using System;
using System.Reflection;
using EFT;
using SPT.Reflection.Patching;

namespace Gravity.Client.Patches
{
    /// <summary>
    /// BotBossSpawn.Spawn(BotBossSpawn.cs:250) 앞뒤: Gravity 분대(PMC 역할 또는 근처 보충 웨이브)가 게임 자체 상한 검사를 받는 동안만 표시를 둔다.
    /// Spawn은 async지만 상한 검사(_spawner.CheckOnMax)는 첫 await(프로필 받기) 전에 같은 호출 안에서 돈다. 그래서 Prefix에서 켜고
    /// Postfix(첫 await에서 돌아온 뒤)에서 끄면 그 검사만 고칠 수 있다. 대기열 재시도(CheckSpawnAfterDelay → Spawn)도 같은 길이다.
    /// 1.0, Codex 검토 1번: 바닐라 검사는 상한 제외 팩션(서버 CapExclude)도 세므로 Gravity 기준으로 자리가 있어도 분대가 막혔다.
    /// </summary>
    internal sealed class BossSpawnScopePatch : ModulePatch
    {
        /// <summary>지금 Spawn 안에서 뺄 팩션 봇 수. -1이면 Gravity 분대가 아니다(바닐라 검사 그대로). 게임 스레드에서만 쓴다.</summary>
        internal static int Excluded = -1;

        protected override MethodBase GetTargetMethod() =>
            typeof(BotBossSpawn).GetMethod(nameof(BotBossSpawn.Spawn), BindingFlags.Instance | BindingFlags.Public)!;

        [PatchPrefix]
        private static void Prefix(BossLocationSpawn wave)
        {
            Excluded = -1;
            try
            {
                var c = RaidContext.Current;
                if (c == null || wave == null) return;
                if (!wave.BossType.IsPmcBot() && !c.NearWaves.TryGetValue(wave, out _)) return;
                Excluded = c.CountCapExcluded();
            }
            catch (Exception ex)
            {
                Excluded = -1;
                Log.ErrorOnce("BossSpawnScope", ex);
            }
        }

        [PatchPostfix]
        private static void Postfix() => Excluded = -1;

        [PatchFinalizer]
        private static void Finalizer() => Excluded = -1;
    }

    /// <summary>
    /// BotSpawner.CheckOnMax(BotSpawner.cs:424) 앞: BossSpawnScopePatch가 표시한 Gravity 분대 검사일 때만
    /// 점유 = max(생존+생성 중, 생존+로딩) − 상한 제외 팩션으로 다시 계산한다. 그 밖(스캐브 웨이브·보스·다른 모드)은 바닐라 그대로.
    /// 대기열·Gravity 예약은 여기서 세지 않는다: 이 검사는 대기열에서 다시 나온 분대 자신에게도 불리므로, 대기열을 세면 대기 분대끼리
    /// 서로를 막아 영영 못 나올 수 있다. 대기열·예약까지 센 앞단 검사는 ScavDirector·NearDirector가 요청 전에 한다(RaidContext.TryReserve).
    /// </summary>
    internal sealed class CheckOnMaxPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(BotSpawner).GetMethod(nameof(BotSpawner.CheckOnMax), BindingFlags.Instance | BindingFlags.Public)!;

        [PatchPrefix]
        private static bool Prefix(BotSpawner __instance, int wantSpawn, ref int toDelay, ref int toSpawn, bool calcOnlySimpleBots)
        {
            int excluded = BossSpawnScopePatch.Excluded;
            if (excluded < 0 || calcOnlySimpleBots || __instance == null || __instance.MaxBots == 0) return true;
            try
            {
                int occupied = Math.Max(0, Math.Max(__instance.AllBotsWithLoaded, __instance.AliveAndLoadingBotsCount) - excluded);
                int room = __instance.MaxBots - occupied;
                if (room <= 0) { toDelay = wantSpawn; toSpawn = 0; }
                else if (room < wantSpawn) { toDelay = wantSpawn - room; toSpawn = room; }
                else { toDelay = 0; toSpawn = wantSpawn; }
                return false;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("CheckOnMax", ex);
                return true;
            }
        }
    }
}
