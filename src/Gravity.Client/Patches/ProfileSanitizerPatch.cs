using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Customization;
using EFT.InventoryLogic;
using SPT.Reflection.Patching;

namespace Gravity.Client.Patches
{
    /// <summary>
    /// Profile.GetAllPrefabPaths 앞: 봇 프로필의 외형 ID를 클라가 모르면(CustomizationSolver.GetItem == null 또는 Prefab 없음)
    /// 같은 진영·같은 부위의 클라 외형으로 바꾸고, Prefab 없는 장비는 뺀다.
    /// 그대로 두면 GetBundle이 null을 내고 ObjectsFactory.Pools.ConvertResourceInfo에서 NRE가 나 봇 생성이 통째로 실패한다(kb 2.17).
    /// GetAllPrefabPaths는 반복기라도 Prefix는 부르는 순간 돈다.
    /// </summary>
    internal sealed class ProfileSanitizerPatch : ModulePatch
    {
        private static readonly EBodyModelPart[] VisualParts =
            { EBodyModelPart.Body, EBodyModelPart.Feet, EBodyModelPart.Head, EBodyModelPart.Hands };

        private static readonly object Gate = new object();
        private static CustomizationSolver? _cachedFor;
        private static readonly Dictionary<(EPlayerSide, EBodyModelPart), MongoID?> Fallbacks =
            new Dictionary<(EPlayerSide, EBodyModelPart), MongoID?>();

        protected override MethodBase GetTargetMethod() =>
            typeof(Profile).GetMethod(nameof(Profile.GetAllPrefabPaths), BindingFlags.Instance | BindingFlags.Public)!;

        [PatchPrefix]
        private static void Prefix(Profile __instance, bool allCustomization)
        {
            try
            {
                // 봇만: 봇 생성 경로(BotProfileClient·BotSpawner)는 allCustomization=false로 부르고 Info.Settings(역할)가 있다.
                // 플레이어·은신처는 기본값 true로 부른다.
                if (allCustomization || __instance?.Info?.Settings == null) return;
                var solver = Singleton<CustomizationSolver>.Instance;
                if (solver == null) return;
                var role = __instance.Info.Settings.Role.ToString();
                var n = FixCustomization(__instance, solver, role) + FixEquipment(__instance, role);
                if (n > 0)
                {
                    var c = RaidContext.Current;
                    if (c != null) c.Sanitized += n;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("ProfileSanitizer", ex);
            }
        }

        private static int FixCustomization(Profile p, CustomizationSolver solver, string role)
        {
            var custom = p.Customization;
            if (custom == null) return 0;
            var n = 0;
            foreach (var part in VisualParts)
            {
                if (!custom.TryGetValue(part, out var id) || Known(solver, id)) continue;
                var fb = Fallback(solver, p.Info.Side, part);
                if (fb == null) continue; // 바꿀 외형이 없으면 그대로(바닐라와 같은 결과)
                custom[part] = fb.Value;
                n++;
                Log.InfoOnce("cust:" + id, $"프로필 정리: {role} {part} {id} → 기본");
            }
            return n;
        }

        private static int FixEquipment(Profile p, string role)
        {
            var eq = p.Inventory?.Equipment;
            if (eq == null) return 0;
            List<Item>? bad = null;
            foreach (var item in eq.GetAllItemsFromCollection())
            {
                if (item == null || ReferenceEquals(item, eq)) continue;
                if (item.Template?.Prefab != null) continue;
                (bad ??= new List<Item>()).Add(item);
            }
            if (bad == null) return 0;
            var n = 0;
            foreach (var item in bad)
            {
                var addr = item.CurrentAddress;
                if (addr == null) continue;
                var r = addr.RemoveWithoutRestrictions(item);
                if (r.Failed) continue;
                n++;
                Log.InfoOnce("item:" + item.TemplateId, $"프로필 정리: {role} 장비 {item.TemplateId} → 제거");
            }
            return n;
        }

        /// <summary>클라 외형 목록에 있고 Prefab이 있는 ID인지(GetBundle이 null이 아닌지).</summary>
        private static bool Known(CustomizationSolver solver, MongoID id) => solver.GetItem(id)?.Prefab != null;

        /// <summary>
        /// 진영·부위별 대체 외형: 클라 외형 목록(_customizationClothingTemplates·_headTemplates)에서 그 부위이고 Prefab 경로가 있는 것 중
        /// 그 진영(Side)에 들어가는 첫 항목, 없으면 진영 무관 첫 항목. 목록 순서는 서버가 준 customization 순서라 매번 같다.
        /// 클라에는 진영 기본 외형 표가 따로 없어서 이렇게 고른다.
        /// </summary>
        private static MongoID? Fallback(CustomizationSolver solver, EPlayerSide side, EBodyModelPart part)
        {
            lock (Gate)
            {
                if (!ReferenceEquals(_cachedFor, solver))
                {
                    Fallbacks.Clear();
                    _cachedFor = solver;
                }
                if (Fallbacks.TryGetValue((side, part), out var hit)) return hit;

                IEnumerable<CustomizationClothing> pool = part == EBodyModelPart.Head
                    ? solver._headTemplates.Values.Cast<CustomizationClothing>()
                    : solver._customizationClothingTemplates.Values;
                var usable = pool.Where(x => x != null && (part == EBodyModelPart.Head || x.BodyPart == part) && x.Prefab != null && !string.IsNullOrEmpty(x.Prefab.path)).ToList();
                var pick = usable.FirstOrDefault(x => x.Side != null && x.Side.Contains(side)) ?? usable.FirstOrDefault();
                MongoID? id = pick == null ? (MongoID?)null : pick.Id;
                Fallbacks[(side, part)] = id;
                if (id == null) Log.WarnOnce($"fb:{side}:{part}", $"프로필 정리: {side} {part} 대체 외형이 클라에 없음");
                return id;
            }
        }
    }
}
