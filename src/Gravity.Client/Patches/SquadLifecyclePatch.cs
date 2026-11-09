using System;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using Gravity.Shared;
using SPT.Reflection.Patching;

namespace Gravity.Client.Patches
{
    /// <summary>프로필 누락·취소로 실제 생성 요청에 도달하지 못한 인원만 장부에서 정리합니다.</summary>
    internal sealed class CreateProfilesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(BotCreationData).GetMethod(nameof(BotCreationData.Create))!;

        [PatchPrefix]
        private static void Prefix(IGetProfileData profileData, int count, ref RaidContext.SquadWatch? __state)
        {
            var c = RaidContext.Current;
            if (c == null || profileData?.SpawnParams == null || !c.ParamsOf.TryGetValue(profileData.SpawnParams, out var w)) return;
            __state = w;
            System.Threading.Interlocked.Add(ref w.Requested, count);
        }

        [PatchPostfix]
        private static void Postfix(int count, Task<BotCreationData> __result, RaidContext.SquadWatch? __state)
        {
            var c = RaidContext.Current;
            var w = __state;
            if (c == null || w == null) return;
            void Done(Task<BotCreationData> t)
            {
                int made = t != null && t.Status == TaskStatus.RanToCompletion && t.Result != null ? t.Result.Count : 0;
                c.Requests.Fail(w.Wave, Math.Max(0, count - made));
                if (!w.LeaderSeen && made == 0 && !w.SupportsStarted)
                    c.SquadFailed(w, "프로필 생성 실패");
            }
            if (__result == null || __result.IsCompleted) Done(__result!);
            else __result.ContinueWith(Done, TaskContinuationOptions.ExecuteSynchronously);
        }
    }

    /// <summary>BotOwner가 상태 구독을 지우기 전에 직접 폐기 진입을 감지합니다.</summary>
    internal sealed class BotOwnerDisposePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(BotOwner).GetMethod(nameof(BotOwner.Dispose), Type.EmptyTypes)!;

        [PatchPrefix]
        private static void Prefix(BotOwner __instance)
        {
            var c = RaidContext.Current;
            if (c == null || __instance == null) return;
            c.Requests.Retire(__instance.Profile.Id);
            if (c.LeaderOf.TryGetValue(__instance, out var w)) c.SquadFailed(w, "직접 폐기");
        }
    }

    internal sealed class StopCreationPatch : ModulePatch
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BotCreationData, object> Stopped =
            new System.Runtime.CompilerServices.ConditionalWeakTable<BotCreationData, object>();
        protected override MethodBase GetTargetMethod() => typeof(BotCreationData).GetMethod(nameof(BotCreationData.StopSpawn))!;
        [PatchPrefix]
        private static void Prefix(BotCreationData __instance)
        {
            var c = RaidContext.Current;
            if (c == null || !c.DataOf.TryGetValue(__instance, out var w)) return;
            if (!Stopped.TryGetValue(__instance, out _))
            {
                Stopped.Add(__instance, __instance);
                c.Requests.Fail(w.Wave, __instance.Count);
            }
            if (ReferenceEquals(__instance, w.LeaderData)) c.SquadFailed(w, "생성 취소");
        }
    }
}
