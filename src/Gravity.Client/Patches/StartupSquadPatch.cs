using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using EFT.Game.Spawning;
using SPT.Reflection.Patching;
using Gravity.Shared;

namespace Gravity.Client.Patches
{
    /// <summary>시작 분대에 한해 분대장의 활성화 전에도 대원 모델을 준비합니다. 기존 지점·분대·상한 검사를 통과한 요청만 사용합니다.</summary>
    internal sealed class StartupSquadPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(BotBossSpawn).GetMethod(nameof(BotBossSpawn.SpawnBossAndFollowers))!;

        [PatchPrefix]
        private static void Prefix(BotBossSpawn __instance, BotCreationData creationData, BossLocationSpawn wave,
            BotSpawnParams spawnParams, int followersCount, BotZone botZone, List<ISpawnPoint> openedPositions)
        {
            var c = RaidContext.Current;
            if (c == null || c.Started || followersCount <= 0 || !c.IsStartupWave(wave)
                || !c.WatchOf.TryGetValue(wave, out var w) || w.Life.Terminal) return;
            // 원래 함수가 분대장 지점을 뺀 뒤 콜백이 불리므로, 나머지 네이티브 지점 목록을 그대로 사용합니다.
            w.PrepareStarts = () =>
            {
                if (!ReferenceEquals(RaidContext.Current, c) || !StartupPreparation.CanPrepareSupports(c.Started,
                    c.IsStartupWave(wave), w.Life.Terminal, followersCount, openedPositions?.Count ?? 0)) return;
                // 네이티브 초기 웨이브와 같은 경로입니다. 분대장 지점의 재사용 대기가 대원을 레이드 시작 뒤로 미루는 것을 피합니다.
                // 맵·그룹 상한과 지점 선택은 앞선 TrySpawn에서 통과했습니다. 임의 지점을 만들거나 추가 인원을 요청하지 않습니다.
                Log.Verbose($"{c.S.MapId} 시작 PMC 대원 {followersCount}명: 선택된 {openedPositions!.Count}개 지점으로 로딩 중 준비");
                Observe(__instance.SpawnBossSupports(creationData, botZone, followersCount,
                    spawnParams, wave, EPlayerSide.Savage, openedPositions, true));
            };
        }

        private static async void Observe(Task task)
        {
            try { await task; }
            catch (Exception ex) { Log.ErrorOnce("StartupSquad", ex); }
        }
    }
}
