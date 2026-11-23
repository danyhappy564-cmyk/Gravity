using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using SPT.Reflection.Patching;

namespace Gravity.Client.Patches
{
    /// <summary>
    /// BotSpawner.GetGroupAndSetEnemies 앞: PMC 분대 웨이브 하나 = BotsGroup 하나.
    /// 게임은 PMC 역할(pmcUSEC·pmcBEAR, 보스·호위 둘 다로 표시)을 같은 구역·같은 진영의 첫 보스 무리에 넣는다.
    /// 그 무리의 IsFull은 대원이 Active가 될 때(BotsGroup.AddMember)만 갱신되고, 분대장의 IamBoss도 무리 배정 뒤
    /// (BotSpawner.ActivateBotCallback의 SetBoss)에 켜진다. 그래서 몸을 읽는 중인 분대가 있는 구역에 같은 진영 분대가 나오면
    /// 두 분대가 한 무리로 합쳐졌다(5명 이상 무리, 동시 그룹 제한은 웨이브 크기만 봐서 막지 못함).
    /// SpawnBossAndFollowers가 웨이브마다 새로 만드는 ShallBeGroupParams를 열쇠로, 처음 온 봇이 새 무리를 만들고
    /// 같은 웨이브의 나머지 봇만 그 무리에 넣는다. 무리 생성은 원래 함수의 보스 경로와 같다(EFT 0.16.9.40743).
    /// </summary>
    internal sealed class PmcSquadGroupPatch : ModulePatch
    {
        /// <summary>웨이브(ShallBeGroupParams) → 그 웨이브의 무리. 웨이브 자료가 사라지면 항목도 사라진다.</summary>
        private static readonly ConditionalWeakTable<ShallBeGroupParams, BotsGroup> Squads = new ConditionalWeakTable<ShallBeGroupParams, BotsGroup>();

        protected override MethodBase GetTargetMethod() =>
            typeof(BotSpawner).GetMethod(nameof(BotSpawner.GetGroupAndSetEnemies), BindingFlags.Instance | BindingFlags.Public)!;

        [PatchPrefix]
        private static bool Prefix(BotSpawner __instance, BotOwner bot, BotZone zone, ref BotsGroup __result)
        {
            try
            {
                if (RaidContext.Current == null || bot == null || zone == null) return true;
                var role = bot.Profile?.Info?.Settings?.Role;
                if (role == null || !role.Value.IsPmcBot()) return true;
                // 보스 웨이브(SpawnBossAndFollowers)로 나온 분대만. 그 밖의 PMC 생성 경로는 원래 규칙 그대로.
                var squad = bot.SpawnProfileData?.SpawnParams?.ShallBeGroup;
                if (squad == null || !squad.Group) return true;

                if (Squads.TryGetValue(squad, out var group) && ReferenceEquals(group.BotZone, zone))
                {
                    __instance.SetBotAsEnemy(bot);
                    __result = group;
                    return false;
                }

                // 원래 함수와 같은 순서: 적 목록을 먼저 모으고 자신을 적으로 등록한 뒤 무리를 만든다.
                var enemies = new List<BotOwner>(__instance.GetBotEnemiesList(bot));
                __instance.SetBotAsEnemy(bot);
                group = new BotsGroup(zone, __instance.BotGame, bot, enemies, __instance._deadBodiesController, __instance._allPlayers, forBoss: true);
                group.TargetMembersCount = squad.StartCount;
                __instance.Groups.Add(zone, bot.Profile!.Info.Side, group, isBossOrFollower: true);
                Squads.Remove(squad);
                Squads.Add(squad, group);
                __result = group;
                return false;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("PmcSquadGroup", ex);
                return true;
            }
        }
    }
}
