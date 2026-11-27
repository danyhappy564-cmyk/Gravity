using Gravity.Core;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Gravity.Gear;

/// <summary>모드가 추가한 옷 한 벌. Part = "Body"|"Feet", Sides = "usec"|"bear"|"savage", Source = 모드 폴더 이름.</summary>
public sealed record ClothingItem(string Id, string Part, IReadOnlyList<string> Sides, string Source);

/// <summary>
/// 1.1.5 모드 옷 티어 규칙. Kind: level = 상인 요구 레벨(2 이상) 그대로, price = 레벨 1인데 값이 있어 바닐라 래그맨 가격으로 레벨 추정,
/// unlock = 업적·퀘스트로 푸는 옷(레벨 1, 설정한 티어 gear.modClothUnlockTier), cheap = 레벨 1·값 거의 없음, none = 상인 판매 자료 없음.
/// cheap·none은 기본으로 봇이 입지 않는다(gear.modClothCheap 켜면 티어 없이 입음).
/// </summary>
public sealed record ModClothRule(string Kind, int Level);

/// <summary>상인 의상 판매 한 줄을 판정용으로 줄인 것: 그 의상이 쓰는 Body·Feet id, 요구 레벨, 루블 환산 값, 업적·퀘스트 조건 여부.</summary>
public sealed record SuitOffer(string PartId, int Level, double Rub, bool Unlock);

/// <summary>
/// 1.0.5 의상 탭: 웹에서 허용한 모드(mods.clothingOn)의 옷만 PMC·스캐브 외형 목록(Body·Feet)에 남기거나 더한다.
/// 설정이 없으면(한 번도 저장 안 함) 아무것도 바꾸지 않는다. 보스·호위·특수·팩션 봇은 건드리지 않는다.
/// </summary>
public static class GearClothing
{
    /// <summary>모드 옷 목록(SPT 원본 customization.json에 없는 Body·Feet 중 출처를 찾은 것).</summary>
    public static IReadOnlyList<ClothingItem> Build(IEnumerable<(string Id, string? Part, IEnumerable<string>? Sides)> custom,
        IReadOnlySet<string>? vanilla, IReadOnlyDictionary<string, string> sources)
    {
        if (vanilla == null) return [];
        var list = new List<ClothingItem>();
        foreach (var (id, part, sides) in custom)
        {
            var key = id.ToLowerInvariant();
            if (part is not ("Body" or "Feet") || vanilla.Contains(key) || !sources.TryGetValue(key, out var src)) continue;
            var s = (sides ?? []).Select(x => x.ToLowerInvariant()).Where(x => x is "usec" or "bear" or "savage").Distinct().ToArray();
            if (s.Length > 0) list.Add(new ClothingItem(key, part, s, src));
        }
        return list.OrderBy(x => x.Source, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>값이 이보다 작고 레벨 1 이하이면(업적·퀘스트 조건도 없으면) '거의 공짜' 옷으로 본다. 바닐라 레벨 2~10 래그맨 옷 최저가 2.5만₽.</summary>
    public const double CheapRub = 25000;

    /// <summary>1.1.5: 모드 옷마다 티어 규칙을 정한다. 한 옷에 판매가 여럿이면 level → price → unlock → cheap 순으로 앞선 것(같으면 낮은 레벨).</summary>
    public static Dictionary<string, ModClothRule> ModRules(IEnumerable<ClothingItem> clothing, IEnumerable<SuitOffer> offers, IReadOnlyList<(double Rub, int Level)> vanillaPrices)
    {
        var byPart = offers.GroupBy(o => o.PartId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var res = new Dictionary<string, ModClothRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in clothing)
        {
            if (!byPart.TryGetValue(c.Id, out var list) || list.Count == 0) { res[c.Id] = new ModClothRule("none", 0); continue; }
            res[c.Id] = list.Select(o => Judge(o, vanillaPrices)).OrderBy(r => Rank(r.Kind)).ThenBy(r => r.Level).First();
        }
        return res;
    }

    private static int Rank(string kind) => kind switch { "level" => 0, "price" => 1, "unlock" => 2, "cheap" => 3, _ => 4 };

    public static ModClothRule Judge(SuitOffer o, IReadOnlyList<(double Rub, int Level)> vanillaPrices)
    {
        if (o.Level >= 2) return new ModClothRule("level", o.Level);
        if (o.Unlock) return new ModClothRule("unlock", 0);
        if (o.Rub >= CheapRub) return new ModClothRule("price", EstimateLevel(o.Rub, vanillaPrices));
        return new ModClothRule("cheap", Math.Max(0, o.Level));
    }

    /// <summary>루블 값 → 레벨: 값이 ±50% 안인 바닐라 래그맨 옷(레벨 2 이상)들의 레벨 중간값, 없으면 값이 가장 가까운(로그 거리) 옷의 레벨.</summary>
    public static int EstimateLevel(double rub, IReadOnlyList<(double Rub, int Level)> vanilla)
    {
        if (vanilla.Count == 0 || rub <= 0) return 1;
        var near = vanilla.Where(v => v.Rub >= rub / 1.5 && v.Rub <= rub * 1.5).Select(v => v.Level).Order().ToList();
        if (near.Count > 0) return near[near.Count / 2];
        return vanilla.MinBy(v => Math.Abs(Math.Log(v.Rub / rub))).Level;
    }

    private static string? SideOf(string roleId) => roleId switch { "usec" => "usec", "bear" => "bear", "scav" => "savage", _ => null };

    /// <returns>바꾼 항목 수(더함 + 뺌 + 무게 고침).</returns>
    /// <summary>장비를 고치는 봇: 의상 탭대로 모드 옷을 넣은 뒤, 바닐라·모드 옷을 함께 레벨 티어로 거른다(1.1.5).</summary>
    public static int Apply(BotType bot, GearContext c, Random rnd) => Apply(bot, c.Settings, c.Pick.RoleId, c.Items, c.Raid) + LevelFilter(bot, c);

    /// <summary>옷 하나의 티어(0부터). null = 티어 없음(그대로 둠), -1 = 봇에게 입히지 않음.</summary>
    internal static int? ClothTier(string id, GearContext c)
    {
        var g = c.Settings;
        if (c.Items.ClothingLevel.TryGetValue(id, out var lv)) return GearTier.TierOfLevel(g.Tiers, Math.Max(1, lv));
        if (!c.Items.ModClothRules.TryGetValue(id, out var r)) return null;
        return r.Kind switch
        {
            "level" or "price" => GearTier.TierOfLevel(g.Tiers, Math.Max(1, r.Level)),
            "unlock" => Math.Clamp(g.ModClothUnlockTier - 1, 0, Math.Max(0, g.Tiers.Count - 1)),
            _ => g.ModClothCheap ? null : -1
        };
    }

    /// <summary>
    /// 1.1.5 의상 레벨 티어(gear.clothingLevel): PMC(usec·bear) 상의·하의 중 래그맨 원본 옷은 요구 레벨이 속한 티어(장비 티어 레벨 구간)로 나눠
    /// 봇 티어보다 높은 옷은 빼고, 고르는 범위(gear.clothingRange: exact 봇 티어만·near 한 티어 아래까지·all 아래 전부) 밖 낮은 옷도 뺀다.
    /// 남은 옷은 아래 티어일수록 무게를 낮춘다(장비와 같은 GearApply.Factor). 래그맨 옷이 MinRangeCandidates(3)벌도 안 되면
    /// 바로 아래 티어부터 묶음째 보탠다(티어마다 옷이 1~5벌뿐이고 T7 구간에는 없다). 그래도 없으면 가장 낮은 티어 옷을 남긴다.
    /// 요구 레벨 표에 없는 옷(모드 옷·이벤트 옷)은 그대로 둔다(모드 옷은 의상 탭이 정함).
    /// </summary>
    /// <returns>뺀 옷 수.</returns>
    public static int LevelFilter(BotType bot, GearContext c)
    {
        var g = c.Settings;
        if (!g.ClothingLevel || !g.Module || bot.BotAppearance == null || (c.Items.ClothingLevel.Count == 0 && c.Items.ModClothRules.Count == 0) || g.Tiers.Count == 0) return 0;
        if (c.Pick.RoleId is not ("usec" or "bear")) return 0;
        int bt = c.Pick.Tier, lo = g.ClothingRange switch { "all" => int.MinValue, "exact" => bt, _ => bt - 1 };
        int removed = 0;
        foreach (var pool in new[] { bot.BotAppearance.Body, bot.BotAppearance.Feet })
        {
            if (pool == null) continue;
            var all = pool.Keys.Select(k => (Key: k, Tier: ClothTier(k.ToString(), c))).Where(x => x.Tier != null).ToList();
            // 막힌 모드 옷(거의 공짜·판매 자료 없음): 다른 옷이 남을 때만 뺀다.
            var blocked = all.Where(x => x.Tier < 0).ToList();
            if (blocked.Count > 0 && blocked.Count < pool.Count) { foreach (var x in blocked) pool.Remove(x.Key); removed += blocked.Count; }
            var tiered = all.Where(x => x.Tier >= 0).Select(x => (x.Key, Tier: x.Tier!.Value)).ToList();
            if (tiered.Count == 0) continue;
            // 티어 없는 옷(래그맨 표 밖 이벤트 옷 — 컬티스트 상의 등, 공짜 옷 허용 때의 모드 옷)은 거르기 전 비중을 그대로 둔다.
            // 그대로 두면 다른 옷이 걸러지는 높은 티어에서 비중이 커진다(11-28 인게임: T6·T7 PMC 8명 중 4명이 컬티스트 상의).
            var tieredKeys = tiered.Select(x => x.Key).ToHashSet();
            var untiered = pool.Where(kv => !tieredKeys.Contains(kv.Key)).ToList();
            double sumAll = pool.Values.Sum(), share = sumAll > 0 ? untiered.Sum(kv => kv.Value) / sumAll : 0;
            var keep = tiered.Where(x => x.Tier <= bt && x.Tier >= lo).ToList();
            foreach (var grp in tiered.Where(x => x.Tier < lo).GroupBy(x => x.Tier).OrderByDescending(gp => gp.Key))
            {
                if (keep.Count >= GearApply.MinRangeCandidates) break;
                keep.AddRange(grp);
            }
            if (keep.Count == 0) { int min = tiered.Min(x => x.Tier); keep = tiered.Where(x => x.Tier == min).ToList(); }
            var kept = keep.ToDictionary(x => x.Key, x => x.Tier);
            foreach (var x in tiered)
            {
                if (!kept.TryGetValue(x.Key, out var t)) { pool.Remove(x.Key); removed++; }
                else pool[x.Key] = pool[x.Key] * GearApply.Factor(Math.Max(0, bt - t));
            }
            if (untiered.Count > 0 && share > 0 && share < 1)
            {
                double sumT = kept.Keys.Sum(k => pool[k]);
                foreach (var (k, w) in untiered) pool[k] = w / sumAll * sumT / (1 - share);
            }
        }
        return removed;
    }

    /// <summary>장비 티어를 안 고치는 봇(역할 장비 스위치 꺼짐, 기본 스캐브)도 의상만은 적용한다(Codex 1.0.5 검토 2번).</summary>
    public static int Apply(BotType bot, GearSettings settings, string roleId, GearItems items, GearRaid? raid = null)
    {
        var allowed = settings.ClothingOn;
        if (allowed == null || !settings.Module || bot.BotAppearance == null) return 0;
        var side = SideOf(roleId);
        if (side == null) return 0;
        var mods = items.Clothing.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        int changed = 0;
        foreach (var part in new[] { "Body", "Feet" })
        {
            var pool = part == "Body" ? bot.BotAppearance.Body : bot.BotAppearance.Feet;
            if (pool == null) continue;
            var vanilla = pool.Where(kv => !mods.ContainsKey(kv.Key.ToString())).ToList();
            var baseW = (vanilla.Count > 0 ? vanilla : pool.ToList()).Select(kv => kv.Value).DefaultIfEmpty(1).Average();
            // 1.1.5: 반올림·최소 1을 없앤다. SPT 바닐라 PMC 옷 무게가 모두 1이라 반올림하면 등장 배율 150% 미만이 전부 1(=100%)이 됐다.
            // SPT WeightedRandom은 소수 무게를 그대로 쓴다. 배율 0이면 모드 옷을 넣지 않는다.
            var w = baseW * settings.ModMult / 100;
            var next = vanilla.ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var m in items.Clothing)
                if (w > 0 && m.Part == part && m.Sides.Contains(side) && allowed.Contains(m.Source) && (raid?.Clothing == null || raid.Clothing.Contains(m.Id))) next[new MongoId(m.Id)] = w;
            if (next.Count == 0) continue;
            var before = pool.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
            changed += next.Count(kv => !before.TryGetValue(kv.Key.ToString(), out var old) || old != kv.Value)
                + before.Keys.Count(k => !next.ContainsKey(new MongoId(k)));
            pool.Clear();
            foreach (var (k, v) in next) pool[k] = v;
        }
        return changed;
    }
}
