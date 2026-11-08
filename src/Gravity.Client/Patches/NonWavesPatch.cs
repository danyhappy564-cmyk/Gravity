using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using Gravity.Client.Directors;
using SPT.Reflection.Patching;

namespace Gravity.Client.Patches
{
    /// <summary>
    /// NonWavesSpawnScenario.Update(NonWavesSpawnScenario.cs:116) 앞. 세 가지를 한다.
    /// 1) 레이드 시간 60초마다 살아 있는 봇 수 [분, PMC, 스캐브, 전체]를 RaidContext에 적는다(보고용, 아래와 무관하게 늘 한다).
    /// 2) 근처 보충(F8): NearDirector.Tick. 스캐브 관리 여부와 상관없이 부른다(연구소처럼 스캐브가 바닐라인 맵에서도 근처 보충은 돈다).
    ///    반환값에는 영향이 없다. NearDirector에서 예외가 나면 그 레이드는 NearFailed로 표시하고 남은 시간은 근처 보충을 하지 않는다.
    /// 3) 서버가 스캐브를 관리하는 레이드(S.Scav.Managed)면 ScavDirector.Tick을 부르고 false(바닐라 자동 스폰을 건너뜀).
    ///    이 컴포넌트는 LocalGame.Create가 늘 붙이므로(LocalGame.cs:144) 바닐라가 돌지 않는 맵(OfflineNewSpawn=false: 공장·연구소·미궁·리저브,
    ///    LocalGame.cs:195·BaseLocalGame.cs:519)에서도 Update는 매 프레임 불린다. 그래서 바닐라의 _started 대신 RaidContext.Started로 시작을 본다.
    ///    ScavDirector에서 예외가 나면 그 레이드는 ScavFailed로 표시하고 남은 시간은 바닐라로 돌린다(예외 시 바닐라 경로).
    /// 그 밖(설정 없음·관리 안 함)은 true(바닐라 그대로).
    /// </summary>
    internal sealed class NonWavesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(NonWavesSpawnScenario).GetMethod(nameof(NonWavesSpawnScenario.Update), BindingFlags.Instance | BindingFlags.Public)!;

        /// <summary>____botsController·____botDifficultiesWights: Harmony가 넣어 주는 비공개 필드
        /// _botsController·_botDifficultiesWights(NonWavesSpawnScenario.cs:15·17, 레이드 난이도 설정이 반영된 가중치 ImplementWaveSettings :208).</summary>
        [PatchPrefix]
        private static bool Prefix(BotsController ____botsController, WDictionary<BotDifficulty> ____botDifficultiesWights)
        {
            RaidContext? c = null;
            try
            {
                c = RaidContext.Current;
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

            if (c != null && !c.NearFailed)
            {
                try
                {
                    NearDirector.Tick(c, ____botsController);
                }
                catch (Exception ex)
                {
                    c.NearFailed = true;
                    Log.ErrorOnce("NearDirector", ex);
                    Log.Warn($"{c.S.MapId} 근처 보충 중 예외: 이 레이드의 남은 시간은 근처 보충 없음");
                }
            }

            if (c == null || c.ScavFailed || c.S.Scav == null || !c.S.Scav.Managed) return true;
            try
            {
                ScavDirector.Tick(c, ____botsController, ____botDifficultiesWights);
                return false;
            }
            catch (Exception ex)
            {
                c.ScavFailed = true;
                Log.ErrorOnce("ScavDirector", ex);
                Log.Warn($"{c.S.MapId} 스캐브 관리 중 예외: 이 레이드의 남은 시간은 바닐라 자동 스폰");
                return true;
            }
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
