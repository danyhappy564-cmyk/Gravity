using System.Collections.Concurrent;
using System.Reflection;
using Gravity.Core;
using HarmonyLib;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Bot;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Services.Bot;
using SPTarkov.Server.Core.Services.Profile;

namespace Gravity.Gear;

/// <summary>장비 모듈이 공유하는 상태(로더가 채움). 하모니 패치는 정적이라 여기로 접근한다.</summary>
public static class GearRuntime
{
    public static GearItems Items { get; set; } = GearItems.Empty;
    /// <summary>SPT 아이템 템플릿(충돌 확인용, 로더가 채움). 없으면 대체 부품 고르기를 건너뛴다.</summary>
    public static IReadOnlyDictionary<MongoId, TemplateItem>? Templates { get; set; }
    /// <summary>레벨별 경험치(SPT globals). PMC 레벨을 바꿀 때 경험치도 맞춘다.</summary>
    public static int[] ExpTable { get; set; } = [];
    public static ProfileActivityService? Activity { get; set; }
    /// <summary>봇 수납 칸 목록(SPT 싱글턴, 로더가 채움). 1.0.7 PackNStrap 벨트를 수납 칸으로 등록할 때 쓴다.</summary>
    public static BotInventoryContainerService? Containers { get; set; }
    public static GearRaid Raid { get; private set; } = new(null, 0, 0, 0);
    /// <summary>이번 판 묶음별 티어 개수(웹 진단).</summary>
    public static ConcurrentDictionary<string, int[]> Stats { get; } = new();
    public static int Dressed;
    public static string? LastError { get; set; }
    /// <summary>봇마다 장비 기록 한 줄(diag.botLog, 0.6). 판마다 레이드 시작 때 정한다.</summary>
    public static bool LogBots;

    /// <summary>1.1.0 티어 귀중품(귀중품·정보 아이템)·열쇠/카드 후보 id. 로더가 SPT 템플릿에서 채운다.</summary>
    public static IReadOnlyList<string> ValuableIds { get; set; } = [];
    public static IReadOnlyList<string> KeyIds { get; set; } = [];
    /// <summary>1.1.2 SPT 원본 탄약 한 칸 크기(낱개 탄약 'EFT 기본' 기준). 로더가 items.json에서 채운다.</summary>
    public static VanillaAmmo VanillaAmmo { get; set; } = VanillaAmmo.Empty;

    /// <summary>템플릿 부모 사슬로 귀중품·열쇠 후보를 만든다(퀘스트 템 제외).</summary>
    public static void BuildExtraPools(IReadOnlyDictionary<MongoId, TemplateItem> templates)
    {
        bool IsA(TemplateItem t, string baseId)
        {
            var cur = t;
            for (int d = 0; d < 20 && cur != null; d++)
            {
                if (cur.Id.ToString() == baseId) return true;
                if (cur.Parent.IsEmpty || !templates.TryGetValue(cur.Parent, out cur!)) return false;
            }
            return false;
        }
        var val = new List<string>(); var keys = new List<string>();
        foreach (var (id, t) in templates)
        {
            if (t.Type != "Item" || t.Properties?.QuestItem == true) continue;
            if (IsA(t, GearExtras.Jewelry) || IsA(t, GearExtras.InfoItem)) val.Add(id.ToString());
            else if (IsA(t, GearExtras.MechanicalKey) || IsA(t, GearExtras.Keycard)) keys.Add(id.ToString());
        }
        ValuableIds = val; KeyIds = keys;
    }

    /// <summary>아이템 자료를 다시 만드는 함수(로더가 채움, 웹 "설치된 아이템으로 다시 만들기"). 시험에서는 null.</summary>
    public static Func<GearItems>? Builder { get; set; }

    /// <summary>아이템 가격 티어를 다시 계산한다(LiveFleaPrices처럼 가격이 바뀌는 모드용). 요약 한 줄, 못 하면 null.</summary>
    public static string? RebuildItems()
    {
        if (Builder == null) return null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var items = Builder();
        Items = items;
        _catalog = null;
        return L.T($"아이템 {items.Info.Count}개, 장비 칸 {items.SlotTier.Count}개, 모드 무기 {items.ModWeapons.Count}·장비 {items.ModEquipment.Count}·부품 {items.ModAttachments.Count}",
            $"{items.Info.Count} items, {items.SlotTier.Count} gear slots, mod {items.ModWeapons.Count} weapons · {items.ModEquipment.Count} gear · {items.ModAttachments.Count} attachments") + $", {sw.ElapsedMilliseconds}ms";
    }

    [ThreadStatic] internal static GearContext? Current;
    [ThreadStatic] internal static int? PmcLevel;

    /// <summary>지금 장비 모듈을 쓸 수 있으면 설정, 아니면 null(꺼짐·APBS 잠금·서버 준비 전).</summary>
    public static GearSettings? Active()
    {
        var rt = GravityRuntime.Instance;
        var g = rt?.Store.Current.Gear;
        if (rt == null || g == null || !g.Module || rt.Detect.GearLocked) return null;
        return g;
    }

    public static void NewRaid(GearSettings g, int seed)
    {
        Raid = GearRaid.Pick(Items, g.ModSubset, g.ModWeapons, g.ModEquipment, g.ModAttachments, g.ModOther, g.ModClothing, seed);
        Stats.Clear();
        Dressed = 0;
    }

    public static bool Night(MongoId session)
    {
        try { return Activity?.GetProfileActivityRaidData(session)?.RaidConfiguration?.IsNightRaid ?? false; }
        catch { return false; }
    }

    /// <summary>로케일(언어 → id·이름 표). 로더가 채운다. 시험에서는 null.</summary>
    public static Func<string, Dictionary<string, string>>? Locale { get; set; }
    private static IReadOnlyList<GearCatalogEntry>? _catalog;
    private static GearItems? _catalogItems;
    private static readonly object CatalogLock = new();
    public static IReadOnlyList<GearCatalogEntry> Catalog()
    {
        lock (CatalogLock)
        {
            if (_catalog == null || !ReferenceEquals(_catalogItems, Items))
            {
                _catalogItems = Items;
                _catalog = GearCatalog.Build(Items, Locale?.Invoke("kr") ?? [], Locale?.Invoke("en") ?? []);
            }
            return _catalog;
        }
    }

    /// <summary>웹 블랙리스트 검색: 이름(한/영)·id 일부. 봇 목록에 나오는 아이템을 앞에 둔다.</summary>
    public static IEnumerable<object> Search(string q, string cat, int max = 60)
    {
        q=(q??"").Trim();
        return Catalog().Where(x=>(cat=="all"||string.IsNullOrEmpty(cat)||x.Cat==cat)
            && (q.Length==0||x.Id.Contains(q,StringComparison.OrdinalIgnoreCase)||x.Ko.Contains(q,StringComparison.OrdinalIgnoreCase)||x.En.Contains(q,StringComparison.OrdinalIgnoreCase)))
            .Take(Math.Clamp(max,0,1000)).Select(x=>new {id=x.Id,ko=x.Ko,en=x.En,cat=x.Cat,mod=x.Mod,source=x.Source});
    }

    public static string? NameOf(string id)
    {
        var ko = Locale?.Invoke("kr");
        return ko?.GetValueOrDefault(id + " Name") ?? Locale?.Invoke("en")?.GetValueOrDefault(id + " Name");
    }

    public static int ExpFor(int level, Random rnd)
    {
        var t = ExpTable;
        if (t.Length == 0) return 0;
        level = Math.Clamp(level, 1, t.Length);
        int sum = t.Take(level).Sum();
        return sum + (level < t.Length - 1 && t[level] > 1 ? rnd.Next(0, t[level] - 1) : 0);
    }
}

/// <summary>PMC 레벨을 웹 "봇 레벨 기준"으로 다시 뽑는다(장비 모듈 켜짐·PMC 봇 적용일 때).</summary>
public class GearLevelPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotLevelGenerator), nameof(BotLevelGenerator.GenerateBotLevel));

    [PatchPostfix]
    public static void Postfix(BotGenerationDetails botGenerationDetails, ref RandomisedBotLevelResult __result)
    {
        GearRuntime.PmcLevel = null;
        try
        {
            var g = GearRuntime.Active();
            if (g == null || !botGenerationDetails.IsPmc || botGenerationDetails.IsPlayerScav) return;
            var id = GearTier.RoleId(botGenerationDetails.Role, true, botGenerationDetails.Side);
            if (id == null || !g.RoleFor(id).On) return;
            var rnd = Random.Shared;
            int L = GearTier.RandomLevel(g, botGenerationDetails.PlayerLevel ?? 1, false, rnd);
            if (GearRuntime.ExpTable.Length > 0) L = Math.Min(L, GearRuntime.ExpTable.Length);
            __result = new RandomisedBotLevelResult { Level = L, Exp = GearRuntime.ExpFor(L, rnd) };
            GearRuntime.PmcLevel = L;
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; }
    }
}

/// <summary>SPT 레벨 필터 바로 뒤: 티어를 정하고 이 봇의 복제본 목록·확률·개수를 고친다.</summary>
public class GearFilterPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotEquipmentFilterService), nameof(BotEquipmentFilterService.FilterBotEquipment));

    [PatchPostfix]
    public static void Postfix(MongoId sessionId, BotType baseBotNode, BotGenerationDetails botGenerationDetails)
    {
        GearRuntime.Current = null;
        try
        {
            var g = GearRuntime.Active();
            if (g == null || botGenerationDetails.IsPlayerScav) return;
            var id = GearTier.RoleId(botGenerationDetails.Role, botGenerationDetails.IsPmc, botGenerationDetails.Side);
            if (id == null) return;
            var role = g.RoleFor(id);
            // 1.0.5: 장비 스위치가 꺼진 봇(기본 스캐브)도 의상 탭 설정은 따른다.
            if (!role.On) { GearClothing.Apply(baseBotNode, g, id, GearRuntime.Items, GearRuntime.Raid); return; }
            var group = GearDefaults.GroupOf(id);
            var rnd = Random.Shared;
            int? lvl = botGenerationDetails.IsPmc ? GearRuntime.PmcLevel ?? botGenerationDetails.BotLevel : null;
            int tier = GearTier.Pick(g, role, group, botGenerationDetails.PlayerLevel ?? 1, lvl, rnd);
            // 1.1.0 장비 다시 굴리기: 이 확률로 한 번 더 굴려 높은 쪽을 쓴다.
            if (role.Reroll > 0 && rnd.NextDouble() * 100 < role.Reroll) tier = Math.Max(tier, GearTier.Pick(g, role, group, botGenerationDetails.PlayerLevel ?? 1, lvl, rnd));
            var pick = new GearPick(id, group, tier, botGenerationDetails.IsPmc);
            var night = GearRuntime.Night(sessionId);
            var ctx = GearApply.Context(g, pick, GearRuntime.Items, GearRuntime.Raid, night, rnd, botGenerationDetails.Location);
            GearApply.Apply(baseBotNode, ctx, night, rnd);
            GearRuntime.Current = ctx;
            GearRuntime.Stats.AddOrUpdate(group, _ => { var a = new int[7]; a[pick.Tier] = 1; return a; }, (_, a) => { Interlocked.Increment(ref a[pick.Tier]); return a; });
            Interlocked.Increment(ref GearRuntime.Dressed);
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; GearRuntime.Current = null; }
    }
}

/// <summary>
/// Gravity가 입히는 봇이면 SPT의 레벨별 방탄판 무게 대신 티어 등급으로 거른 판 목록을 쓴다.
/// SPT가 방어구 부품을 DB에서 새로 뽑는 경우(무작위 방어구 칸)도 여기서 걸러진다.
/// </summary>
public class GearPlatePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotEquipmentModGenerator), nameof(BotEquipmentModGenerator.FilterPlateModsForSlotByLevel));

    [PatchPrefix]
    public static bool Prefix(string modSlot, HashSet<MongoId> existingPlateTplPool, int? maxArmorLevel, ref FilterPlateModsForSlotByLevelResult __result)
    {
        var c = GearRuntime.Current;
        if (c == null || existingPlateTplPool == null || existingPlateTplPool.Count == 0) return true;
        var set = new HashSet<MongoId>(existingPlateTplPool);
        GearApply.FilterModSet(set, modSlot ?? "", c);
        if (set.Count == 0) return true;
        // 1.1.0: 뒤판 등급을 앞판 이하로(SPT가 앞판 등급을 maxArmorLevel로 넘김). 다 빠지면 가장 낮은 등급 하나를 남긴다.
        if (maxArmorLevel is { } max) GearApply.CapPlateClass(set, max, c.Items);
        __result = new FilterPlateModsForSlotByLevelResult { Result = Result.SUCCESS, PlateModTemplates = set };
        return false;
    }
}

/// <summary>무기 부품을 SPT가 DB 호환 목록에서 뽑을 때(무작위 부품 칸)도 같은 기준으로 거른다.</summary>
public class GearDynamicModPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotEquipmentModGenerator), nameof(BotEquipmentModGenerator.GetDynamicModPool));

    [PatchPostfix]
    public static void Postfix(string parentItemId, string modSlot, ref HashSet<MongoId> __result)
    {
        var c = GearRuntime.Current;
        if (c == null || __result == null || __result.Count == 0) return;
        try
        {
            var set = new HashSet<MongoId>(__result);
            GearApply.FilterModSet(set, modSlot ?? "", c, c.Required(parentItemId, modSlot ?? ""), parentItemId);
            __result = set;
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; }
    }
}

/// <summary>
/// 무기 부품 칸 하나를 고를 때 SPT가 쓰는 최종 목록. 봇 목록 밖에서 새로 만든 목록(새로 고른 부품의 하위 칸:
/// GetRequiredModsForWeaponSlot·GetModsForWeaponSlot, 조준경 마운트의 DB 호환 목록, 기본 프리셋 부품)도 여기를 지나므로
/// 같은 기준으로 거른다(1.0 점검: 원작 프리셋 판에 모드 가늠자·총열이 섞임). SPT 캐시 집합을 건드리지 않게 복사본을 돌려준다.
/// </summary>
public class GearModPoolPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotEquipmentModGenerator), nameof(BotEquipmentModGenerator.GetModPoolForSlot));

    [PatchPostfix]
    public static void Postfix(ModToSpawnRequest request, ref HashSet<MongoId>? __result)
    {
        var c = GearRuntime.Current;
        if (c == null || __result == null || __result.Count == 0 || request?.ParentTemplate == null) return;
        try
        {
            var slot = request.ModSlot ?? "";
            var parent = request.ParentTemplate.Id.ToString();
            var set = new HashSet<MongoId>(__result);
            GearApply.FilterModSet(set, slot, c, c.Required(parent, slot), parent);
            __result = set;
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; }
    }
}

/// <summary>
/// 필수 칸에 맞는 부품을 못 찾으면 SPT는 DB 호환 목록 전체에서 아무거나 고른다. 고른 것이 이 봇에 막힌 아이템(모드 묶음 밖·원작 표 등)이면
/// 같은 칸의 허용 부품 중 지금 무기와 충돌하지 않는 것으로 바꾼다. 그런 게 없으면 SPT 선택을 그대로 둔다(총이 깨지는 것보다 낫다).
/// </summary>
public class GearItemDbModPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotEquipmentModGenerator), nameof(BotEquipmentModGenerator.GetRandomModTplFromItemDb));

    [PatchPostfix]
    public static void Postfix(Slot parentSlot, string modSlot, IEnumerable<Item> items, ref MongoId? __result)
    {
        var c = GearRuntime.Current;
        var tpls = GearRuntime.Templates;
        if (c == null || tpls == null || __result == null || !c.Blocked(__result.Value.ToString())) return;
        try
        {
            var filter = parentSlot?.Properties?.Filters?.FirstOrDefault()?.Filter;
            if (filter == null || filter.Count == 0) return;
            var set = new HashSet<MongoId>(filter.Where(m => !c.Blocked(m.ToString())));
            if (set.Count == 0) return;
            GearApply.FilterModSet(set, modSlot ?? "", c, true);
            var have = items?.Select(i => i.Template).ToList() ?? [];
            var ok = set.Where(m => !c.Blocked(m.ToString()) && tpls.TryGetValue(m, out var t) && !Conflicts(t, have, tpls)).ToList();
            if (ok.Count > 0) __result = ok[Random.Shared.Next(ok.Count)];
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; }
    }

    private static bool Conflicts(TemplateItem t, List<MongoId> have, IReadOnlyDictionary<MongoId, TemplateItem> tpls)
    {
        var mine = t.Properties?.ConflictingItems;
        foreach (var h in have)
        {
            if (mine != null && mine.Contains(h)) return true;
            if (tpls.TryGetValue(h, out var ht) && ht.Properties?.ConflictingItems?.Contains(t.Id) == true) return true;
        }
        return false;
    }
}

/// <summary>무작위 방어구 칸의 부품 목록(SPT가 DB에서 새로 만듦)을 복제해 거른다.</summary>
public class GearDynamicGearPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotInventoryGenerator), nameof(BotInventoryGenerator.GetFilteredDynamicModsForItem));

    [PatchPostfix]
    public static void Postfix(MongoId itemTpl, ref Dictionary<string, HashSet<MongoId>> __result)
    {
        var c = GearRuntime.Current;
        if (c == null || __result == null) return;
        try
        {
            var parent = itemTpl.ToString();
            var copy = new Dictionary<string, HashSet<MongoId>>();
            foreach (var (slot, set) in __result)
            {
                var s = set == null ? new HashSet<MongoId>() : new HashSet<MongoId>(set);
                if (s.Count > 0) GearApply.FilterModSet(s, slot, c, c.Required(parent, slot), parent);
                copy[slot] = s;
            }
            __result = copy;
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; }
    }
}

/// <summary>SPT 전리품 캐시(역할별)가 돌려준 목록에서 이 봇에 막힌 아이템을 뺀다. 보안 컨테이너는 그대로.</summary>
public class GearLootPatch : AbstractPatch
{
    /// <summary>소모품 캐시 종류 → 웹 소모품 종류(진통제 DrugItems는 표 목록 없이 티어만).</summary>
    private static readonly Dictionary<string, string?> MedKinds = new()
    {
        ["HealingItems"] = "meds", ["StimItems"] = "stims", ["GrenadeItems"] = "grenades", ["FoodItems"] = "food", ["DrinkItems"] = "drink", ["DrugItems"] = null,
    };

    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotLootCacheService), nameof(BotLootCacheService.GetLootFromCache));

    [PatchPostfix]
    public static void Postfix(string lootType, BotType botJsonTemplate, ref Dictionary<MongoId, double> __result)
    {
        var c = GearRuntime.Current;
        if (c == null || __result == null || lootType == "SecuredContainer") return;
        try
        {
            if (MedKinds.TryGetValue(lootType, out var kind))
            {
                var it = botJsonTemplate?.BotGeneration?.Items;
                var gen = lootType switch
                {
                    "HealingItems" => it?.Healing, "StimItems" => it?.Stims, "GrenadeItems" => it?.Grenades,
                    "FoodItems" => it?.Food, "DrinkItems" => it?.Drink, _ => it?.Drugs
                };
                __result = GearApply.ConsumablesFor(__result, gen, kind, c);
                return;
            }
            if (__result.Count > 0) __result = __result.Where(kv => !c.LootBlocked(kv.Key.ToString())).ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; }
    }
}

/// <summary>Unheard 판 PMC에 SPT가 주머니 전리품 개수를 더하는데, 전리품을 끈 봇이면 다시 0으로.</summary>
public class GearUnheardPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotGenerator), "AddAdditionalPocketLootWeightsForUnheardBot");

    [PatchPostfix]
    public static void Postfix(BotType botJsonTemplate)
    {
        var c = GearRuntime.Current;
        var pl = botJsonTemplate?.BotGeneration?.Items?.PocketLoot;
        if (c == null || c.Role.Loot || pl == null) return;
        pl.Weights = GearApply.Uniform(0, 0);
    }
}

/// <summary>봇 하나 생성 앞뒤: 앞에서 지난 봇 상태를 지우고, 뒤에서 내구도를 티어 범위로 맞춘다.</summary>
public class GearBotPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotGenerator), "GenerateBot");

    [PatchPrefix]
    public static void Prefix()
    {
        GearRuntime.Current = null;
        GearRuntime.PmcLevel = null;
    }

    [PatchPostfix]
    public static void Postfix(BotGenerationDetails botGenerationDetails, BotBase __result)
    {
        var ctx = GearRuntime.Current;
        GearRuntime.Current = null;
        GearRuntime.PmcLevel = null;
        var pick = ctx?.Pick;
        ApplyStats(botGenerationDetails, __result, pick?.Tier);
        if (pick == null || __result?.Inventory?.Items == null) return;
        try
        {
            var g = GearRuntime.Active();
            if (g == null) return;
            var caps = g.Tiers[pick.Tier];
            var role = g.RoleFor(pick.RoleId);
            var w = GearApply.Durability(caps.WMin, caps.WMax, role.WDurMin, role.WDurMax);
            var a = GearApply.Durability(caps.AMin, caps.AMax, role.ADurMin, role.ADurMax);
            var rnd = Random.Shared;
            foreach (var it in __result.Inventory.Items)
            {
                var rep = it.Upd?.Repairable;
                if (rep == null || !GearRuntime.Items.Info.TryGetValue(it.Template.ToString(), out var info) || info.MaxDurability <= 0) continue;
                (double Lo, double Hi)? range = info.Weapon ? w : info.ArmorClass > 0 || info.Plate ? a : null;
                if (range is not { } r) continue;
                double pct = r.Lo + rnd.NextDouble() * (r.Hi - r.Lo);
                rep.MaxDurability = info.MaxDurability;
                rep.Durability = Math.Round(info.MaxDurability * pct / 100.0, 1);
            }
            // 1.1.0: 의료품·음식 남은 양 무작위를 끈 봇은 가득 채운다.
            if (!role.Meds && GearRuntime.Templates is { } tpls) GearExtras.FillConsumables(__result.Inventory.Items, t => tpls.GetValueOrDefault(t));
            // 1.1.5: 헬멧 부품이 막거나 충돌하는 헤드셋·안경·얼굴 가리개, 리그가 막는 방탄복 빼기 — 모드가 지운 원래 제한도 봇에는 적용(WearRules, #93)
            if (GearRuntime.Templates is { } ht)
                GearExtras.DropHeadClashes(__result.Inventory.Items, __result.Inventory.Equipment?.ToString(), t => WearRules.For(t, x => ht.GetValueOrDefault(x)));
            // 1.1.5: 예비 탄창은 봇 전체 기준 티어 상한(SPT는 총마다 적용)
            if (GearRuntime.Templates is { } mt)
                GearExtras.TrimSpareMags(__result.Inventory.Items, __result.Inventory.Equipment?.ToString(), caps.Mags,
                    id => GearRuntime.Items.Info.GetValueOrDefault(id) is { Section: "parts", Slot: "magazine" }, t => mt.GetValueOrDefault(t));
            if (GearRuntime.LogBots) Gravity.Core.DiagLog.GearLine(BotLine(pick, __result));
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; }
    }

    /// <summary>
    /// 1.0.6 봇 체력·스킬(Forge #2): 그 봇 티어(입힌 장비 티어, 없으면 봇별 장비 티어 방식)의 규칙대로 고친다.
    /// 장비 모듈이 꺼져 있어도 동작한다. 플레이어 자신의 스캐브는 건드리지 않는다.
    /// </summary>
    internal static void ApplyStats(BotGenerationDetails? d, BotBase? bot, int? dressedTier)
    {
        try
        {
            var cfg = Gravity.Core.GravityRuntime.Instance?.Store.Current;
            var st = cfg?.Stats;
            if (st == null || !st.Any || bot == null || d == null || d.IsPlayerScav || cfg!.Gear == null) return;
            var id = GearTier.RoleId(d.Role, d.IsPmc, d.Side);
            if (id == null) return;
            var rule = st.RuleFor(BotStats.GroupOfId(id), BotStats.TierOf(cfg.Gear, id, dressedTier, bot.Info?.Level ?? 1));
            if (rule != null) BotStats.Apply(bot, rule);
            BotStats.ApplyMult(bot, st.MultFor(id)); // 1.1.4 봇 종류별 체력 배율: 티어 체력 뒤에 곱함(1이면 그대로)
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; }
    }

    /// <summary>장비 기록 한 줄: 역할 티어 레벨 주무기 방탄 최고 등급(조끼·리그·헬멧·판), 방탄복(조끼 또는 방탄 리그)·헤드셋·앞뒤판 있음(O/-), 옷 출처(상의/하의, 1.0.5), 조준경·부품 수(1.0.9), 머리 칸 장비 있음(1.1.0 #68).</summary>
    internal static string BotLine(GearPick pick, BotBase bot, GearItems? items = null)
    {
        items ??= GearRuntime.Items;
        string? weapon = null, weaponItem = null;
        double armor = 0;
        bool vest = false, ear = false, plate = false;
        string head = "-"; string? headId = null;
        foreach (var it in bot.Inventory?.Items ?? [])
        {
            var tpl = it.Template.ToString();
            if (it.SlotId == "FirstPrimaryWeapon") { weapon = tpl; weaponItem = it.Id.ToString(); }
            var info = items.Info.GetValueOrDefault(tpl);
            if (info is { ArmorClass: > 0 }) armor = Math.Max(armor, info.ArmorClass);
            if (it.SlotId == "ArmorVest" || (it.SlotId == "TacticalVest" && info is { ArmorClass: > 0 })) vest = true;
            if (it.SlotId == "Earpiece") ear = true;
            if (it.SlotId == "Headwear") { headId = it.Id.ToString(); head = info is { ArmorClass: > 0 } ? "O" : "c"; } // O=방탄 헬멧, c=모자 등
            if (string.Equals(it.SlotId, "front_plate", StringComparison.OrdinalIgnoreCase) || string.Equals(it.SlotId, "back_plate", StringComparison.OrdinalIgnoreCase)) plate = true;
        }
        // 4.x 헬멧은 방탄 등급이 자식 삽입물(Helmet_top 등)에 있다
        if (head == "c" && headId != null && (bot.Inventory?.Items ?? []).Any(i => i.ParentId == headId && items.Info.GetValueOrDefault(i.Template.ToString()) is { ArmorClass: > 0 })) head = "O";
        var wname = weapon == null ? "-" : (GearRuntime.NameOf(weapon) ?? weapon) + (items.Info.GetValueOrDefault(weapon)?.Mod == true ? "*" : "");
        // 1.0.9 측정: 주무기 아래(부모 사슬) 부품 수와 조준경 여부(마운트 위 조준경 포함)
        var (parts, scope) = WeaponParts(bot.Inventory?.Items, weaponItem, items);
        // 1.1.5 래그맨 원본 옷이면 요구 레벨(Lv18)을 함께 남겨 의상 레벨 티어를 레이드 기록으로 확인할 수 있게 한다.
        string Src(MongoId? id) => id is { } m ? items.Clothing.FirstOrDefault(c => c.Id == m.ToString())?.Source
            ?? (items.ClothingLevel.TryGetValue(m.ToString(), out var lv) ? "vanilla Lv" + lv : "vanilla") : "-";
        string On(bool b) => b ? "O" : "-";
        return $"{pick.RoleId}\tT{pick.Tier + 1}\tLv{bot.Info?.Level ?? 0}\t{wname}\t{L.T("방탄", "armor")} {armor:0}"
            + $"\t{L.T("방탄복", "vest")} {On(vest)}\t{L.T("헤드셋", "ear")} {On(ear)}\t{L.T("앞뒤판", "plate")} {On(plate)}"
            + $"\t{L.T("옷", "outfit")} {Src(bot.Customization?.Body)}/{Src(bot.Customization?.Feet)}"
            + $"\t{L.T("조준경", "scope")} {On(scope)}\t{L.T("부품", "parts")} {parts}"
            + $"\t{L.T("머리", "head")} {head}"
            + Belt(bot, items);
    }

    private static (int Parts, bool Scope) WeaponParts(IEnumerable<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item>? all, string? weaponItem, GearItems items)
    {
        if (all == null || weaponItem == null) return (0, false);
        var list = all.ToList();
        var parent = list.Where(i => i.ParentId != null).GroupBy(i => i.Id.ToString()).ToDictionary(g => g.Key, g => g.First().ParentId!);
        bool Under(string id)
        {
            for (int d = 0; d < 20 && parent.TryGetValue(id, out var p); d++) { if (p == weaponItem) return true; id = p; }
            return false;
        }
        int n = 0; bool scope = false;
        foreach (var it in list)
        {
            if (!Under(it.Id.ToString())) continue;
            n++;
            // 1.1.0: 마운트 칸에 바로 달린 조준기(도브테일 EKP-8-02, P90 링 사이트 등)도 조준경. 가늠자·가늠쇠 칸(mod_sight_*)은 뺀다.
            var slot = it.SlotId ?? "";
            if (slot.StartsWith("mod_scope", StringComparison.OrdinalIgnoreCase)
                || (!slot.StartsWith("mod_sight", StringComparison.OrdinalIgnoreCase) && items.Info.GetValueOrDefault(it.Template.ToString()) is { Section: "parts", Slot: "scope" })) scope = true;
        }
        return (n, scope);
    }

    /// <summary>1.0.7 PackNStrap: 벨트를 찬 봇이면 벨트 이름과 벨트 안 탄창 수(안 찼으면 빈 글자라 기록 모양 그대로).</summary>
    private static string Belt(BotBase bot, GearItems items)
    {
        var inv = bot.Inventory?.Items;
        var belt = inv?.FirstOrDefault(i => i.SlotId == "ArmBand" && GearBelts.IsBelt(items, i.Template.ToString()));
        if (belt == null) return "";
        int mags = inv!.Count(i => i.ParentId == belt.Id.ToString() && items.Info.GetValueOrDefault(i.Template.ToString()) is { Section: "parts", Slot: "magazine" });
        var name = GearRuntime.NameOf(belt.Template.ToString()) ?? belt.Template.ToString();
        return $"\t{L.T("벨트", "belt")} {name} ({L.T("탄창", "mags")} {mags})";
    }
}
