using System.Globalization;
using Gravity.Core;

namespace Gravity.Gear;

/// <summary>1.1.6 판정 한 줄의 제안: 아이템 id를 From 티어에서 To 티어로(둘 다 화면 티어 1~7).</summary>
public sealed record NeedFix(string Id, int From, int To);

/// <summary>
/// 1.1.6 판정 한 줄: 아이템(ItemId)이 그 티어(Tier, 1~7)에서 필요한 것을 못 쓴다.
/// Kind: scope(조준경 경로) · ammo(맞는 탄약) · required(필수 칸). Manual: 관련 아이템 중 직접 티어를 정한 것이 있음(화면 정렬용).
/// </summary>
public sealed record Need(string Kind, string ItemId, int Tier, string? Slot, IReadOnlyList<NeedFix> Suggest, string? Note, bool Manual)
{
    public string Key => Kind + "|" + ItemId + "|" + Slot;
}

/// <summary>
/// 1.1.6 아이템 티어 점검. 봇 생성(GearApply)의 거르기 규칙 중 봇·판과 무관한 부분을 그대로 따라,
/// 총이 자기 티어에서 조준경·탄약·필수 칸 부품을 쓸 수 있는지 본다. 읽기만 한다.
/// </summary>
public static class GearNeeds
{
    private static readonly HashSet<string> PlateSlots = ["front_plate", "back_plate", "left_side_plate", "right_side_plate"];

    /// <summary>아이템의 티어(0~6): 직접 티어(1~7)가 있으면 그것, 없으면 자동 티어. 칸 키는 GearCatalog.Build와 같다.</summary>
    public static int Tier0(GearItems items, IReadOnlyDictionary<string, int> itemTier, string id)
    {
        if (itemTier.TryGetValue(id, out var t) && t is >= 1 and <= 7) return t - 1;
        if (!items.Info.TryGetValue(id, out var i)) return 0;
        return items.TierOf(i.EquipmentSlots.FirstOrDefault() ?? i.Section + "/" + i.Slot, id);
    }

    /// <summary>봇·판과 무관하게 막힌 아이템이 아닌지(전체 대상 블랙리스트·없는 아이템·퀘스트 템 막기·모드 꺼짐).</summary>
    public static bool Usable(GearSettings g, GearItems items, string id) => Usable(g, items, BlackAll(g), id);

    private static HashSet<string> BlackAll(GearSettings g) =>
        g.Blacklist.Where(e => e.Bots.Contains("all")).Select(e => e.Id).ToHashSet();

    private static bool Usable(GearSettings g, GearItems items, HashSet<string> black, string id)
    {
        if (black.Contains(id)) return false;
        if (!items.Info.TryGetValue(id, out var info)) return true; // 자료 없는 아이템은 서버도 거르지 않는다
        if (!info.Available) return false;
        if (info.Quest && (g.NoQuest || g.BlockQuest)) return false;
        if (info.Mod && !g.ModsOn && !GearBelts.IsBelt(items, id)) return false;
        return !g.ModDisabled(info);
    }

    /// <summary>프리셋 전체 판정. 정렬: 직접 티어 관련 먼저 → 티어 → 종류 → id.</summary>
    public static IReadOnlyList<Need> Check(GearSettings g, GearItems items, IReadOnlyDictionary<string, int> itemTier)
    {
        var ctx = new Ctx(g, items, itemTier, BlackAll(g));
        var res = new List<Need>();
        foreach (var (id, info) in items.Info)
        {
            if (!ctx.Ok(id)) continue;
            bool gun = info.Section == "weapons" && info.Slot != "scabbard";
            bool req = info.RequiredSlots is { Count: > 0 };
            if (!gun && !req) continue;
            int t = ctx.Tier(id);
            if (gun && ctx.AmmoNeed(id, info, t) is { } a) res.Add(a);
            if (req) res.AddRange(ctx.RequiredNeeds(id, info, t));
        }
        return res.OrderByDescending(n => n.Manual).ThenBy(n => n.Tier).ThenBy(n => n.Kind, StringComparer.Ordinal)
            .ThenBy(n => n.ItemId, StringComparer.Ordinal).ThenBy(n => n.Slot, StringComparer.Ordinal).ToList();
    }

    private sealed class Ctx(GearSettings g, GearItems items, IReadOnlyDictionary<string, int> itemTier, HashSet<string> black)
    {
        private Dictionary<string, List<string>>? _ammo;
        public bool Ok(string id) => Usable(g, items, black, id);
        public int Tier(string id) => Tier0(items, itemTier, id);
        private bool Manual(string id) => itemTier.TryGetValue(id, out var t) && t is >= 1 and <= 7;
        private double Price(string id) => items.Info.TryGetValue(id, out var i) ? i.Price : 0;
        private TierCaps Caps(int t) => g.Tiers[Math.Clamp(t, 0, g.Tiers.Count - 1)];

        /// <summary>부품 티어 거르기(GearApply.AboveTier와 같은 범위): 부품만, 부착물 티어 켜짐일 때만, 탄창은 직접 티어가 있을 때만.</summary>
        public bool TierPass(string id, int t)
        {
            if (!items.Info.TryGetValue(id, out var i) || i.Section != "parts" || !g.PartTiers) return true;
            if (i.Slot == "magazine" && !Manual(id)) return true;
            return Tier(id) <= t;
        }

        private string Lowest(IEnumerable<string> ids) =>
            ids.OrderBy(Tier).ThenBy(Price).ThenBy(x => x, StringComparer.Ordinal).First();

        private Need Make(string kind, string id, int t, string? slot, IReadOnlyList<string> lift, string? note) =>
            new(kind, id, t + 1, slot, lift.Select(x => new NeedFix(x, Tier(x) + 1, t + 1)).ToList(), note,
                Manual(id) || lift.Any(Manual));

        public Need? AmmoNeed(string id, ItemInfo gun, int t)
        {
            if (gun.Caliber.Length == 0) return null;
            _ammo ??= items.Info.Where(kv => kv.Value.Section == "ammo" && Ok(kv.Key))
                .GroupBy(kv => kv.Value.Slot).ToDictionary(gr => gr.Key, gr => gr.Select(kv => kv.Key).ToList());
            if (!_ammo.TryGetValue(gun.Caliber, out var cand) || cand.Count == 0) return null;
            double cap = Caps(t).Pen;
            var underCap = cand.Where(a => items.Info[a].Pen <= cap).ToList();
            if (underCap.Any(a => Tier(a) <= t)) return null;
            return underCap.Count > 0
                ? Make("ammo", id, t, null, [Lowest(underCap)], null)
                : Make("ammo", id, t, null, [], "pen:" + cap.ToString(CultureInfo.InvariantCulture));
        }

        public IEnumerable<Need> RequiredNeeds(string id, ItemInfo info, int t)
        {
            if (!items.CompatibleMods.TryGetValue(id, out var slots)) yield break;
            foreach (var (slot, raw) in slots.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var s = slot.ToLowerInvariant();
                if (PlateSlots.Contains(s) || info.RequiredSlots?.Contains(s) != true) continue;
                var cand = raw.Where(Ok).ToList();
                if (cand.Count == 0 || cand.Any(c => TierPass(c, t))) continue;
                yield return Make("required", id, t, slot, [Lowest(cand)], null);
            }
        }
    }
}
