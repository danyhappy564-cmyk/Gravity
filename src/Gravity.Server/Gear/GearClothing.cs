using Gravity.Core;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Gravity.Gear;

/// <summary>모드가 추가한 옷 한 벌. Part = "Body"|"Feet", Sides = "usec"|"bear"|"savage", Source = 모드 폴더 이름.</summary>
public sealed record ClothingItem(string Id, string Part, IReadOnlyList<string> Sides, string Source);

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

    private static string? SideOf(string roleId) => roleId switch { "usec" => "usec", "bear" => "bear", "scav" => "savage", _ => null };

    /// <returns>바꾼 항목 수(더함 + 뺌 + 무게 고침).</returns>
    public static int Apply(BotType bot, GearContext c, Random rnd) => LevelFilter(bot, c) + Apply(bot, c.Settings, c.Pick.RoleId, c.Items, c.Raid);

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
        if (!g.ClothingLevel || !g.Module || bot.BotAppearance == null || c.Items.ClothingLevel.Count == 0 || g.Tiers.Count == 0) return 0;
        if (c.Pick.RoleId is not ("usec" or "bear")) return 0;
        int bt = c.Pick.Tier, lo = g.ClothingRange switch { "all" => int.MinValue, "exact" => bt, _ => bt - 1 };
        int removed = 0;
        foreach (var pool in new[] { bot.BotAppearance.Body, bot.BotAppearance.Feet })
        {
            if (pool == null) continue;
            var tiered = pool.Keys.Select(k => (Key: k, Lv: c.Items.ClothingLevel.TryGetValue(k.ToString(), out var lv) ? lv : -1))
                .Where(x => x.Lv >= 0).Select(x => (x.Key, Tier: GearTier.TierOfLevel(g.Tiers, Math.Max(1, x.Lv)))).ToList();
            if (tiered.Count == 0) continue;
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
            var w = Math.Max(1, Math.Round(baseW * settings.ModMult / 100));
            var next = vanilla.ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var m in items.Clothing)
                if (m.Part == part && m.Sides.Contains(side) && allowed.Contains(m.Source) && (raid?.Clothing == null || raid.Clothing.Contains(m.Id))) next[new MongoId(m.Id)] = w;
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
