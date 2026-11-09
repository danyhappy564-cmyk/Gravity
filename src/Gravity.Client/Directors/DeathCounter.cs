using System;
using EFT;
using Gravity.Shared;

namespace Gravity.Client.Directors
{
    /// <summary>
    /// 0.3 보고용 사망 수. 레이드마다 BotSpawner.OnBotRemoved(BotSpawner.BotDied에서 한 번만 부름)에 한 번 붙어 역할별로 센다.
    /// 봇 행동·생성에는 손대지 않는다(세기만). 예외는 한 번만 기록하고 그 뒤로는 세지 않는다.
    /// </summary>
    internal static class DeathCounter
    {
        internal static void Hook(RaidContext c, BotsController? bc)
        {
            var spawner = bc?.BotSpawner;
            if (spawner == null || ReferenceEquals(c.DeathHookedTo, spawner)) return;
            try
            {
                spawner.OnBotRemoved += bot => Count(c, bot);
                c.DeathHookedTo = spawner;
            }
            catch (Exception ex)
            {
                c.DeathHookedTo = spawner; // 다시 시도하지 않는다
                Log.ErrorOnce("DeathCounter.Hook", ex);
            }
        }

        private static void Count(RaidContext c, BotOwner? bot)
        {
            try
            {
                // 분대장이 대원 생성 전에 맵에서 빠지면 대원은 나오지 않는다: 남은 대원 예약을 푼다(4차 재검토 1번).
                if (bot != null) c.Requests.Retire(bot.Profile.Id);
                if (bot != null && c.LeaderOf.TryGetValue(bot, out var squad)) c.SquadFailed(squad, "이 대원 생성 전에 빠짐");
                var settings = bot?.Profile?.Info?.Settings;
                if (settings == null) return;
                var key = Key(settings.Role, settings.IsBoss() || settings.IsFollower());
                // BotDespawn(탈출·다른 모드의 정리)도 BotDied를 거친다: 그때 플레이어가 아직 살아 있으면 탈출(판정 56).
                var hc = bot!.GetPlayer?.HealthController;
                if (hc != null && hc.IsAlive)
                {
                    c.Extracted[key]++;
                    OnExtracted(c, settings.Role);
                }
                else c.Killed[key]++;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("DeathCounter.Count", ex);
            }
        }

        /// <summary>설정한 방식·상한 안에서 탈출 PMC와 같은 진영 1명을 보충한다.</summary>
        private static void OnExtracted(RaidContext c, WildSpawnType role)
        {
            if (!ExtractRefillRule.CanQueue(c.S, role == WildSpawnType.pmcUSEC || role == WildSpawnType.pmcBEAR, c.ExtractRefill, c.Elapsed)) return;
            c.ExtractRefill++;
            c.ExtractQueue.Enqueue(new RefillGroup(role == WildSpawnType.pmcUSEC, 1, "normal"));
            Log.Verbose($"{c.S.MapId} PMC 탈출 → 보충 분대 1명 추가({c.ExtractRefill}/{ExtractRefillRule.Cap(c.S)})");
        }

        internal static string Key(WildSpawnType role, bool bossOrFollower)
        {
            if (role == WildSpawnType.pmcUSEC) return "pmcUSEC";
            if (role == WildSpawnType.pmcBEAR) return "pmcBEAR";
            if (role == WildSpawnType.assault || role == WildSpawnType.assaultGroup) return "assault";
            if (role == WildSpawnType.marksman) return "marksman";
            return bossOrFollower ? "boss" : "other";
        }
    }
}
