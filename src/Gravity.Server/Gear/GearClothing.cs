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
    public static int Apply(BotType bot, GearContext c, Random rnd) => Apply(bot, c.Settings, c.Pick.RoleId, c.Items);

    /// <summary>장비 티어를 안 고치는 봇(역할 장비 스위치 꺼짐, 기본 스캐브)도 의상만은 적용한다(Codex 1.0.5 검토 2번).</summary>
    public static int Apply(BotType bot, GearSettings settings, string roleId, GearItems items)
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
                if (m.Part == part && m.Sides.Contains(side) && allowed.Contains(m.Source)) next[new MongoId(m.Id)] = w;
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
