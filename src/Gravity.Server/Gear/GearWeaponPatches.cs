using System.Reflection;
using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Services.Bot;

namespace Gravity.Gear;

/// <summary>1.1.0: 조준경·전술 장치 최대 개수. 스위치(weapons.modLimit)를 켰을 때만 SPT 역할별 값 대신 쓴다.</summary>
public class GearModLimitPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotWeaponModLimitService), nameof(BotWeaponModLimitService.GetWeaponModLimits));

    [PatchPostfix]
    public static void Postfix(ref BotModLimits __result)
    {
        var w = GearRuntime.Current?.Settings.Weapons;
        if (w is not { ModLimitOn: true } || __result == null) return;
        __result.ScopeMax = w.ScopeLimit;
        __result.FlashlightLaserMax = w.TacLimit;
    }
}

/// <summary>1.1.0: 밤 레이드 손전등·레이저 켬 확률. 스위치(weapons.night.lightOn)를 켰을 때만, 밤에만 SPT 역할별 값 대신 쓴다.</summary>
public class GearLightPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotGeneratorHelper), nameof(BotGeneratorHelper.GenerateExtraPropertiesForItem));

    [PatchPostfix]
    public static void Postfix(ref Upd? __result)
    {
        var c = GearRuntime.Current;
        if (c is not { Night: true } || __result?.Light == null) return;
        var w = c.Settings.Weapons;
        if (!w.NightLightOn) return;
        __result.Light.IsActive = Random.Shared.NextDouble() * 100 < w.NightLight;
    }
}

/// <summary>
/// 1.1.0(#66) 여분 탄창·낱개 탄약. 앞: 대용량 탄창(60발 이상)이면 여분 수를 최대치로 줄인다(스위치 켰을 때).
/// 뒤: 주무기 탄을 낱개 묶음으로 조끼·주머니·가방에 넣는다(확률·최대 묶음). 자리가 없으면 넣지 않는다.
/// </summary>
public class GearMagPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotWeaponGenerator), nameof(BotWeaponGenerator.AddExtraMagazinesToInventory));

    /// <summary>봇 아이템 넣기(로더가 채움). 없으면 낱개 탄약을 건너뛴다.</summary>
    public static BotGeneratorHelper? Helper { get; set; }
    public static int LooseAdded;

    [PatchPrefix]
    public static void Prefix(GenerateWeaponResult generatedWeaponResult, ref GenerationData magWeights)
    {
        try
        {
            var w = GearRuntime.Current?.Settings.Weapons;
            if (w is not { BigMagOn: true } || magWeights?.Weights == null || generatedWeaponResult?.Weapon == null) return;
            var root = generatedWeaponResult.Weapon.FirstOrDefault();
            var mag = generatedWeaponResult.Weapon.FirstOrDefault(i => i.SlotId == "mod_magazine" && root != null && i.ParentId == root.Id.ToString());
            if (mag == null || GearRuntime.Templates?.GetValueOrDefault(mag.Template) is not { } t) return;
            var cap = t.Properties?.Cartridges?.FirstOrDefault()?.MaxCount ?? 0;
            if (cap < WeaponExtras.BigMagRounds) return;
            magWeights = magWeights with { Weights = WeaponExtras.ClampMagWeights(magWeights.Weights, w.BigMagCount) };
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; }
    }

    [PatchPostfix]
    public static void Postfix(MongoId botId, GenerateWeaponResult generatedWeaponResult, BotBaseInventory inventory)
    {
        try
        {
            var w = GearRuntime.Current?.Settings.Weapons;
            var helper = Helper;
            if (w is not { LooseOn: true } || helper == null || inventory == null || generatedWeaponResult?.Weapon == null) return;
            if (generatedWeaponResult.Weapon.FirstOrDefault()?.SlotId != "FirstPrimaryWeapon") return;
            var ammo = generatedWeaponResult.ChosenAmmoTemplate;
            if (GearRuntime.Templates?.GetValueOrDefault(ammo) is not { } t) return;
            var slots = new HashSet<EquipmentSlots> { EquipmentSlots.TacticalVest, EquipmentSlots.Pockets, EquipmentSlots.Backpack };
            foreach (var n in w.LooseStacks(Random.Shared, t.Properties?.StackMaxSize ?? 0))
            {
                var id = new MongoId();
                var r = helper.AddItemWithChildrenToEquipmentSlot(botId, slots, id, ammo,
                    [new Item { Id = id, Template = ammo, Upd = new Upd { StackObjectsCount = n } }], inventory);
                if (r != ItemAddedResult.SUCCESS) break;
                Interlocked.Increment(ref LooseAdded);
            }
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; }
    }
}
