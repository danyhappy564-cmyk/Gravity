using System;
using System.Reflection;
using EFT;
using SPT.Reflection.Patching;

namespace Gravity.Client.Patches
{
    /// <summary>
    /// BotsController.SetSettings 뒤: 서버가 정한 맵 동시 상한(S.Cap)으로 _maxCount·BotSpawner·ZonesLeaveController를 맞춘다.
    /// LocalGame.vmethod_1에서 SetSettings가 Init보다 먼저 돌므로, 그때는 _maxCount만 바꾸면 Init이 그 값으로 BotSpawner·ZonesLeaveController를 만든다.
    /// </summary>
    internal sealed class CapPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(BotsController).GetMethod(nameof(BotsController.SetSettings), BindingFlags.Instance | BindingFlags.Public)!;

        [PatchPostfix]
        private static void Postfix(BotsController __instance)
        {
            try
            {
                var c = RaidContext.Current;
                if (c != null) Apply(__instance, c);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("CapPatch", ex);
            }
        }

        /// <summary>상한을 건다. SetSettings 뒤 또는 OnGameStarted 뒤(설정이 늦게 왔을 때) 부른다.</summary>
        internal static void Apply(BotsController? bc, RaidContext c)
        {
            if (bc == null) return;
            var cap = c.S.Cap;
            if (cap <= 0) return;
            bc._maxCount = cap;
            if (bc.BotSpawner != null)
            {
                bc.BotSpawner.SetMaxBots(cap);
                bc.ZonesLeaveController?.SetMaxBots(cap);
            }
            c.CapApplied = true;
            Log.Info($"{c.S.MapId} 동시 상한 {cap}");
        }
    }
}
