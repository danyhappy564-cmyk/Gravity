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

/// <summary>
/// 1.1.0 봇별 장비 '앞판이 없으면 뒤판도 빼기'·'뒤판 등급을 앞판 이하로': SPT 역할 설정(EquipmentFilters)을 이 봇 장비 생성에만 바꾼 복사본으로.
/// 등급 제한은 SPT가 판 등급 거르기(FilterPlatesByLevel)를 켠 경우에만 앞판 등급을 넘기므로 함께 켠다(판 목록은 GearPlatePatch가 티어로 거름).
/// </summary>
public class GearEquipModPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotEquipmentModGenerator), nameof(BotEquipmentModGenerator.GenerateModsForEquipment));

    [PatchPrefix]
    public static void Prefix(ref GenerateEquipmentProperties settings)
    {
        var c = GearRuntime.Current;
        if (c == null || settings?.BotEquipmentConfig is not { } cfg) return;
        var r = c.Role;
        if ((cfg.SkipBackPlateIfFrontPlateMissing ?? false) == r.SkipBack && (cfg.LimitPlateClassToFrontPlateClass ?? false) == r.LimitClass
            && (!r.LimitClass || (cfg.FilterPlatesByLevel ?? false))) return;
        settings = settings with
        {
            BotEquipmentConfig = cfg with
            {
                SkipBackPlateIfFrontPlateMissing = r.SkipBack, LimitPlateClassToFrontPlateClass = r.LimitClass,
                FilterPlatesByLevel = (cfg.FilterPlatesByLevel ?? false) || r.LimitClass
            }
        };
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
    public static void Prefix(ref GenerateWeaponResult generatedWeaponResult, ref GenerationData magWeights)
    {
        try
        {
            // 1.1.0 상탄/하탄 분리: 여분 탄창·보안 컨테이너·낱개 탄은 티어 예비 탄 관통 상한 안에서(총에 꽂힌 탄창은 그대로).
            var ctx = GearRuntime.Current;
            if (ctx != null && generatedWeaponResult != null && GearExtras.SpareAmmo(ctx, generatedWeaponResult.ChosenAmmoTemplate.ToString()) is { } spare)
                generatedWeaponResult = generatedWeaponResult with { ChosenAmmoTemplate = spare };
            var w = ctx?.Settings.Weapons;
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
            var ctx = GearRuntime.Current;
            var w = ctx?.Settings.Weapons;
            var helper = Helper;
            if (ctx == null || w == null || helper == null || inventory?.Items == null || generatedWeaponResult?.Weapon == null) return;
            if (generatedWeaponResult.Weapon.FirstOrDefault()?.SlotId != "FirstPrimaryWeapon") return;
            // 1.1.0 상탄 따로 넣기: 총·여분 탄창 위쪽 일부를 더 좋은 탄으로(주무기 차례에 한 번).
            GearExtras.TopLoad(inventory.Items, ctx, Random.Shared);
            // 1.1.0 티어 귀중품·열쇠/카드: 확률에 걸리면 하나를 가방·주머니·조끼 빈자리에.
            AddOne(helper, botId, inventory, ctx, ctx.Caps.Valuables, GearRuntime.ValuableIds);
            if (ctx.Role.Keys) AddOne(helper, botId, inventory, ctx, ctx.Caps.Keycard, GearRuntime.KeyIds);
            if (!w.LooseOn) return;
            var ammo = generatedWeaponResult.ChosenAmmoTemplate;
            if (GearRuntime.Templates?.GetValueOrDefault(ammo) is not { } t) return;
            var slots = new HashSet<EquipmentSlots> { EquipmentSlots.TacticalVest, EquipmentSlots.Pockets, EquipmentSlots.Backpack };
            // 1.1.2: 한 칸 크기 기준 = EFT 기본(원본 items.json, 기본값) 또는 게임 값(SVM 등이 바꾼 값 그대로).
            var (vs, vc) = GearRuntime.VanillaAmmo.Lookup(ammo.ToString(), t.Properties?.Caliber);
            var stack = w.LooseStackBase(t.Properties?.StackMaxSize ?? 0, vs, vc);
            foreach (var n in w.LooseStacks(Random.Shared, stack))
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

    public static int ExtrasAdded;

    private static void AddOne(BotGeneratorHelper helper, MongoId botId, BotBaseInventory inventory, GearContext c, double chance, IReadOnlyList<string> ids)
    {
        if (chance <= 0 || ids.Count == 0 || Random.Shared.NextDouble() * 100 >= chance) return;
        if (GearExtras.PickByBand(GearExtras.Pool(c, ids), c.Pick.Tier, Random.Shared) is not { } tpl) return;
        var id = new MongoId();
        var r = helper.AddItemWithChildrenToEquipmentSlot(botId, [EquipmentSlots.Backpack, EquipmentSlots.Pockets, EquipmentSlots.TacticalVest], id, tpl,
            [new Item { Id = id, Template = tpl, Upd = new Upd { StackObjectsCount = 1 } }], inventory);
        if (r == ItemAddedResult.SUCCESS) Interlocked.Increment(ref ExtrasAdded);
    }
}
