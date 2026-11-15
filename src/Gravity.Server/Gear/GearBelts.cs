using System.Reflection;
using Gravity.Core;
using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace Gravity.Gear;

/// <summary>
/// 1.0.7 WTT PackNStrap 벨트. PackNStrap 벨트 아이템은 전부 addtoBots=false라 봇에게 저절로 가지 않는다.
/// Gravity가 봇 완장(ArmBand) 칸을 만들 때 티어에 맞는 벨트 목록으로 바꾸고, 찬 벨트를 수납 칸으로 등록해 탄창을 먼저 넣는다.
/// PackNStrap이 없으면(벨트 아이템 0개) 아무것도 하지 않는다. APBS가 있으면 장비 모듈 전체가 잠겨 여기도 돌지 않는다.
/// </summary>
public static class GearBelts
{
    /// <summary>PackNStrap 벨트 부모 클래스(CustomBeltItem).</summary>
    public const string BeltBase = "6815465859b8c6ff13f94026";
    /// <summary>웹 티어 표의 벨트 확률 키(chance.belt)와 장비 목록 칸 키(armor/belt).</summary>
    public const string ChanceKey = "belt", PoolKey = "armor/belt";

    public static bool IsBelt(GearItems items, string id) =>
        items.Info.TryGetValue(id, out var i) && i.Section == "armor" && i.Slot == "belt";

    public static IEnumerable<string> All(GearItems items) =>
        items.Info.Where(kv => kv.Value.Available && kv.Value.Section == "armor" && kv.Value.Slot == "belt").Select(kv => kv.Key).Order(StringComparer.Ordinal);

    /// <summary>
    /// 이 봇의 벨트 결정. Managed=false: 벨트 아이템이 없음(PackNStrap 없음) → SPT 그대로.
    /// Managed=true·Pool=null: 이번 봇은 벨트를 차지 않음(완장 칸에서 벨트를 뺀다). Pool: 완장 칸을 이 목록으로 바꾼다.
    /// </summary>
    public static (bool Managed, Dictionary<MongoId, double>? Pool) Decide(GearContext c, GearTable table, Random rnd)
    {
        var all = All(c.Items).ToList();
        if (all.Count == 0) return (false, null);
        var s = c.Settings.Belts;
        if (!s.On || (s.PmcOnly && !c.Pick.Pmc)) return (true, null);
        if (!(rnd.NextDouble() * 100 < table.Chance.GetValueOrDefault(ChanceKey, 0))) return (true, null);
        var ok = all.Where(id => Allowed(c, id)).ToList();
        var pool = new Dictionary<MongoId, double>();
        if (table.Pools.TryGetValue(PoolKey, out var own))
        {
            // 직접 정한 벨트 목록: 무게 0은 뺀다. 직접 티어를 정한 벨트만 티어로 막는다(다른 장비 칸과 같은 규칙).
            foreach (var (id, w) in own)
                if (w > 0 && ok.Contains(id) && !(c.Settings.ItemTier.ContainsKey(id) && c.TierOf(PoolKey, id) > c.Pick.Tier))
                    pool[id] = w;
            return (true, pool.Count > 0 ? pool : null);
        }
        // 자동: 이 봇 티어 이하 벨트, 티어가 멀수록 적게(웹 자동 목록 max(2, 30 − 6d)와 같은 모양).
        foreach (var id in ok)
        {
            int t = c.TierOf(PoolKey, id);
            if (t <= c.Pick.Tier) pool[id] = 30 * GearApply.Factor(c.Pick.Tier - t);
        }
        if (pool.Count == 0 && ok.Count > 0)
        {
            // 티어 위 벨트만 있으면 가장 낮은 티어만(다른 장비 칸이 다 빠졌을 때와 같다).
            int lo = ok.Min(id => c.TierOf(PoolKey, id));
            foreach (var id in ok.Where(id => c.TierOf(PoolKey, id) == lo)) pool[id] = 1;
        }
        return (true, pool.Count > 0 ? pool : null);
    }

    /// <summary>블랙리스트·끈 모드 출처만 막는다. 판마다 고르는 모드 묶음·원작 프리셋 표(only)는 벨트 설정이 따로 정하므로 보지 않는다.</summary>
    private static bool Allowed(GearContext c, string id)
    {
        if (c.Black.Contains(id) || c.Info(id) is not { } info) return false;
        return !(info.Mod && c.Settings.DisabledSources.Contains(info.Source));
    }

    /// <summary>
    /// 아이템을 넣을 칸 목록에 벨트(ArmBand)를 끼운다. 탄창은 벨트 먼저(APBS와 같음), 의약품·주사기·투척물은 다른 칸이 다 찼을 때만.
    /// 바꿀 필요가 없으면 null. 벨트 칸 필터가 받지 않는 아이템은 SPT가 알아서 다음 칸으로 넘긴다.
    /// </summary>
    public static HashSet<EquipmentSlots>? SlotsFor(HashSet<EquipmentSlots> slots, ItemInfo? item)
    {
        if (item == null || slots.Contains(EquipmentSlots.ArmBand)
            || !(slots.Contains(EquipmentSlots.TacticalVest) || slots.Contains(EquipmentSlots.Pockets))) return null;
        if (item.Section == "parts" && item.Slot == "magazine") return [EquipmentSlots.ArmBand, .. slots];
        if (item.Section == "meds" && item.Slot is "meds" or "stims" or "grenades") return [.. slots, EquipmentSlots.ArmBand];
        return null;
    }

    /// <summary>
    /// 봇이 찬 벨트를 못 줍게(서버 시작 때 한 번, APBS PackNStrap_UnlootablePMCArmbandBelts와 같은 값).
    /// 템플릿 값이라 서버를 다시 켜야 바뀐다. 바꾼 개수를 돌려준다.
    /// </summary>
    public static int MarkUnlootable(IReadOnlyDictionary<MongoId, TemplateItem> templates, GearItems items)
    {
        int n = 0;
        foreach (var id in All(items))
        {
            if (!templates.TryGetValue(id, out var t) || t.Properties == null) continue;
            t.Properties.Unlootable = true;
            t.Properties.UnlootableFromSide = [PlayerSideMask.Bear, PlayerSideMask.Usec, PlayerSideMask.Savage];
            t.Properties.UnlootableFromSlot = "ArmBand";
            n++;
        }
        return n;
    }
}

/// <summary>봇 완장 칸을 만들 때: 벨트를 차는 봇이면 벨트 목록으로 바꾸고, 다 만든 뒤 벨트를 수납 칸으로 등록한다.</summary>
public class GearBeltEquipPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotInventoryGenerator), nameof(BotInventoryGenerator.GenerateEquipment));

    [PatchPrefix]
    public static void Prefix(GenerateEquipmentProperties settings)
    {
        var c = GearRuntime.Current;
        if (c == null || !c.BeltManaged || settings == null || settings.RootEquipmentSlot != EquipmentSlots.ArmBand) return;
        try
        {
            if (c.Belts is { Count: > 0 } belts)
            {
                // SPT는 PMC 완장을 진영 완장 하나로 바꿔 둔다(pmc.json forceArmband). 벨트를 차는 봇은 그 자리를 벨트로 바꾼다.
                settings.RootEquipmentPool = new Dictionary<MongoId, double>(belts);
                if (settings.SpawnChances?.EquipmentChances != null) settings.SpawnChances.EquipmentChances["ArmBand"] = 100;
            }
            else if (settings.RootEquipmentPool is { Count: > 0 } pool && pool.Keys.Any(k => GearBelts.IsBelt(c.Items, k.ToString())))
                settings.RootEquipmentPool = pool.Where(kv => !GearBelts.IsBelt(c.Items, kv.Key.ToString())).ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; }
    }

    [PatchPostfix]
    public static void Postfix(GenerateEquipmentProperties settings, bool __result)
    {
        var c = GearRuntime.Current;
        if (!__result || c?.Belts == null || settings?.RootEquipmentSlot != EquipmentSlots.ArmBand) return;
        try
        {
            var item = settings.Inventory?.Items?.LastOrDefault(i => i.SlotId == "ArmBand");
            if (item == null || !GearBelts.IsBelt(c.Items, item.Template.ToString())) return;
            GearRuntime.Containers?.AddEmptyContainerToBot(settings.BotId, EquipmentSlots.ArmBand, item);
            c.BeltWorn = item.Template.ToString();
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; }
    }
}

/// <summary>탄창·의약품을 봇 칸에 넣을 때 찬 벨트도 후보에 넣는다(SPT 기본은 리그·주머니만).</summary>
public class GearBeltSlotsPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(SPTarkov.Server.Core.Helpers.Bot.BotGeneratorHelper),
        nameof(SPTarkov.Server.Core.Helpers.Bot.BotGeneratorHelper.AddItemWithChildrenToEquipmentSlot));

    [PatchPrefix]
    public static void Prefix(ref HashSet<EquipmentSlots> equipmentSlots, MongoId rootItemTplId)
    {
        var c = GearRuntime.Current;
        if (c?.BeltWorn == null || equipmentSlots == null) return;
        try
        {
            var next = GearBelts.SlotsFor(equipmentSlots, c.Items.Info.GetValueOrDefault(rootItemTplId.ToString()));
            if (next != null) equipmentSlots = next;
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; }
    }
}
