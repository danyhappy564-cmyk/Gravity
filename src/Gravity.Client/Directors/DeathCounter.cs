using System;
using EFT;

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
                var settings = bot?.Profile?.Info?.Settings;
                if (settings == null) return;
                c.Killed[Key(settings.Role, settings.IsBoss() || settings.IsFollower())]++;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("DeathCounter.Count", ex);
            }
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
