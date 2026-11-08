using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using SPT.Reflection.Patching;

namespace Gravity.Client.Patches
{
    /// <summary>
    /// NonWavesSpawnScenario.Update 앞. 지금은 항상 true(바닐라가 그대로 돈다): 레이드 시간 60초마다
    /// 살아 있는 봇 수 [분, PMC, 스캐브, 전체]를 RaidContext에 적어 둘 뿐이다(보고용).
    /// 이 패치는 Task 8에서 스캐브 교체로 이어서 쓴다.
    /// </summary>
    internal sealed class NonWavesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(NonWavesSpawnScenario).GetMethod(nameof(NonWavesSpawnScenario.Update), BindingFlags.Instance | BindingFlags.Public)!;

        [PatchPrefix]
        private static bool Prefix()
        {
            try
            {
                var c = RaidContext.Current;
                if (c != null && c.DueForSample(out var minute))
                {
                    var (pmc, scav, total) = CountAlive();
                    c.AlivePerMinute.Add(new[] { minute, pmc, scav, total });
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("NonWaves", ex);
            }
            return true;
        }

        /// <summary>살아 있는 봇을 역할로 센다: PMC(IsPmcBot), 스캐브(assault), 전체(사람 제외 모든 봇).</summary>
        private static (int pmc, int scav, int total) CountAlive()
        {
            int pmc = 0, scav = 0, total = 0;
            var gw = Singleton<GameWorld>.Instance;
            if (gw == null) return (0, 0, 0);
            var alive = gw.AllAlivePlayersList;
            for (int i = 0; i < alive.Count; i++)
            {
                var p = alive[i];
                if (p == null || !p.IsAI) continue;
                total++;
                var role = p.Profile?.Info?.Settings?.Role;
                if (role == null) continue;
                if (role.Value.IsPmcBot()) pmc++;
                else if (role.Value == WildSpawnType.assault) scav++;
            }
            return (pmc, scav, total);
        }
    }
}
