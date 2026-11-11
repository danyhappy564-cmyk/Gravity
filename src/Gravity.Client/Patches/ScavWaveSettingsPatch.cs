using System;
using System.Reflection;
using EFT;
using Gravity.Shared;
using SPT.Reflection.Patching;

namespace Gravity.Client.Patches
{
    /// <summary>게임의 봇 양 변환 뒤 Gravity 표식이 있는 시작 스캐브만 원래 인원과 단일 구역으로 돌립니다.</summary>
    internal sealed class ScavWaveSettingsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(LocalGame).GetMethod(nameof(LocalGame.ModifySettings), BindingFlags.Static | BindingFlags.Public)!;

        [PatchPostfix]
        private static void Postfix(WildSpawnWave[] __result)
        {
            if (__result == null) return;
            int count = 0, waves = 0;
            foreach (var wave in __result)
            {
                if (wave == null || !ScavWaveMark.TryDecode(wave.SpawnPoints, out var min, out var max, out var zone)) continue;
                wave.slots_min = min;
                wave.slots_max = max;
                wave.SpawnPoints = zone;
                count += max;
                waves++;
            }
            if (waves > 0) Log.Info($"시작 스캐브 {count}명({waves}개 웨이브): 설정 인원·단일 구역 유지");
        }
    }
}
