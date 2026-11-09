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
    /// <summary>레벨별 경험치(SPT globals). PMC 레벨을 바꿀 때 경험치도 맞춘다.</summary>
    public static int[] ExpTable { get; set; } = [];
    public static ProfileActivityService? Activity { get; set; }
    public static GearRaid Raid { get; private set; } = new(null, 0, 0, 0);
    /// <summary>이번 판 묶음별 티어 개수(웹 진단).</summary>
    public static ConcurrentDictionary<string, int[]> Stats { get; } = new();
    public static int Dressed;
    public static string? LastError { get; set; }
    /// <summary>봇마다 장비 기록 한 줄(diag.botLog, 0.6). 판마다 레이드 시작 때 정한다.</summary>
    public static bool LogBots;

    /// <summary>아이템 자료를 다시 만드는 함수(로더가 채움, 웹 "설치된 아이템으로 다시 만들기"). 시험에서는 null.</summary>
    public static Func<GearItems>? Builder { get; set; }

    /// <summary>아이템 가격 티어를 다시 계산한다(LiveFleaPrices처럼 가격이 바뀌는 모드용). 요약 한 줄, 못 하면 null.</summary>
    public static string? RebuildItems()
    {
        if (Builder == null) return null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var items = Builder();
        Items = items;
        _names = null;
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
        Raid = GearRaid.Pick(Items, g.ModSubset, g.ModWeapons, g.ModEquipment, g.ModAttachments, seed);
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
    private static List<(string Id, string Ko, string En, string Cat, bool Mod)>? _names;

    /// <summary>웹 블랙리스트 검색: 이름(한/영)·id 일부. 봇 목록에 나오는 아이템을 앞에 둔다.</summary>
    public static IEnumerable<object> Search(string q, string cat, int max = 60)
    {
        if (_names == null)
        {
            var ko = Locale?.Invoke("kr") ?? new Dictionary<string, string>();
            var en = Locale?.Invoke("en") ?? new Dictionary<string, string>();
            _names = Items.Info.Where(kv => kv.Value.Cat != "loot" || kv.Value.Price > 0)
                .Select(kv => (kv.Key, ko.GetValueOrDefault(kv.Key + " Name") ?? en.GetValueOrDefault(kv.Key + " Name") ?? "",
                    en.GetValueOrDefault(kv.Key + " Name") ?? "", kv.Value.Cat, kv.Value.Mod))
                .Where(x => x.Item2.Length > 0 || x.Item3.Length > 0).ToList();
        }
        q = (q ?? "").Trim().ToLowerInvariant();
        var inPools = Items.SlotTier.Values.SelectMany(d => d.Keys).ToHashSet();
        return _names.Where(x => (cat == "all" || string.IsNullOrEmpty(cat) || x.Cat == cat)
                && (q.Length == 0 || x.Id.Contains(q) || x.Ko.ToLowerInvariant().Contains(q) || x.En.ToLowerInvariant().Contains(q)))
            .OrderByDescending(x => inPools.Contains(x.Id)).ThenBy(x => x.En, StringComparer.OrdinalIgnoreCase)
            .Take(max).Select(x => new { id = x.Id, ko = x.Ko, en = x.En, cat = x.Cat, mod = x.Mod });
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
            if (!role.On) return;
            var group = GearDefaults.GroupOf(id);
            var rnd = Random.Shared;
            int? lvl = botGenerationDetails.IsPmc ? GearRuntime.PmcLevel ?? botGenerationDetails.BotLevel : null;
            var pick = new GearPick(id, group, GearTier.Pick(g, role, group, botGenerationDetails.PlayerLevel ?? 1, lvl, rnd), botGenerationDetails.IsPmc);
            var night = GearRuntime.Night(sessionId);
            var ctx = GearApply.Context(g, pick, GearRuntime.Items, GearRuntime.Raid, night, rnd);
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
    public static bool Prefix(string modSlot, HashSet<MongoId> existingPlateTplPool, ref FilterPlateModsForSlotByLevelResult __result)
    {
        var c = GearRuntime.Current;
        if (c == null || existingPlateTplPool == null || existingPlateTplPool.Count == 0) return true;
        var set = new HashSet<MongoId>(existingPlateTplPool);
        GearApply.FilterModSet(set, modSlot ?? "", c);
        if (set.Count == 0) return true;
        __result = new FilterPlateModsForSlotByLevelResult { Result = Result.SUCCESS, PlateModTemplates = set };
        return false;
    }
}

/// <summary>무기 부품을 SPT가 DB 호환 목록에서 뽑을 때(무작위 부품 칸)도 같은 기준으로 거른다.</summary>
public class GearDynamicModPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotEquipmentModGenerator), nameof(BotEquipmentModGenerator.GetDynamicModPool));

    [PatchPostfix]
    public static void Postfix(string modSlot, ref HashSet<MongoId> __result)
    {
        var c = GearRuntime.Current;
        if (c == null || __result == null || __result.Count == 0) return;
        try
        {
            var set = new HashSet<MongoId>(__result);
            GearApply.FilterModSet(set, modSlot ?? "", c);
            __result = set;
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; }
    }
}

/// <summary>무작위 방어구 칸의 부품 목록(SPT가 DB에서 새로 만듦)을 복제해 거른다.</summary>
public class GearDynamicGearPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotInventoryGenerator), nameof(BotInventoryGenerator.GetFilteredDynamicModsForItem));

    [PatchPostfix]
    public static void Postfix(ref Dictionary<string, HashSet<MongoId>> __result)
    {
        var c = GearRuntime.Current;
        if (c == null || __result == null) return;
        try
        {
            var copy = new Dictionary<string, HashSet<MongoId>>();
            foreach (var (slot, set) in __result)
            {
                var s = set == null ? new HashSet<MongoId>() : new HashSet<MongoId>(set);
                if (s.Count > 0) GearApply.FilterModSet(s, slot, c);
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
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotLootCacheService), nameof(BotLootCacheService.GetLootFromCache));

    [PatchPostfix]
    public static void Postfix(string lootType, ref Dictionary<MongoId, double> __result)
    {
        var c = GearRuntime.Current;
        if (c == null || __result == null || __result.Count == 0 || lootType == "SecuredContainer") return;
        try { __result = __result.Where(kv => !c.Blocked(kv.Key.ToString())).ToDictionary(kv => kv.Key, kv => kv.Value); }
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
    public static void Postfix(BotBase __result)
    {
        var ctx = GearRuntime.Current;
        GearRuntime.Current = null;
        GearRuntime.PmcLevel = null;
        var pick = ctx?.Pick;
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
            if (GearRuntime.LogBots) Gravity.Core.DiagLog.GearLine(BotLine(pick, __result));
        }
        catch (Exception ex) { GearRuntime.LastError = ex.Message; }
    }

    /// <summary>장비 기록 한 줄: 역할 티어 레벨 주무기 방탄 최고 등급(조끼·리그·헬멧·판).</summary>
    internal static string BotLine(GearPick pick, BotBase bot)
    {
        string? weapon = null;
        double armor = 0;
        foreach (var it in bot.Inventory!.Items!)
        {
            var tpl = it.Template.ToString();
            if (it.SlotId == "FirstPrimaryWeapon") weapon = tpl;
            if (GearRuntime.Items.Info.TryGetValue(tpl, out var info) && (info.ArmorClass > 0)) armor = Math.Max(armor, info.ArmorClass);
        }
        var wname = weapon == null ? "-" : (GearRuntime.NameOf(weapon) ?? weapon);
        return $"{pick.RoleId}\tT{pick.Tier + 1}\tLv{bot.Info?.Level ?? 0}\t{wname}\t{L.T("방탄", "armor")} {armor:0}";
    }
}
