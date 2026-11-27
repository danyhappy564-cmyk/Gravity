using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

    /// <summary>총 gunId가 티어 t(0~6)에서 조준경을 달 수 있는지. HasAny = 티어를 무시하면 경로가 있음, Ok = 티어 안 경로가 있음,
    /// Lift = 티어 위 고리 수가 가장 적은 경로에서 옮겨야 할 고리(같으면 티어 합 → 가격 합이 낮은 경로).</summary>
    internal static (bool HasAny, bool Ok, IReadOnlyList<string> Lift) ScopePath(GearSettings g, GearItems items,
        IReadOnlyDictionary<string, int> itemTier, string gunId, int t) => new Ctx(g, items, itemTier, BlackAll(g)).ScopePath(gunId, t);

    /// <summary>탐색 상한(GearCompatibility와 같은 값).</summary>
    private const int MaxDepth = 12, MaxVisit = 512;

    /// <summary>티어 변경(change, 0 = 자동으로) 전후 판정 비교. Added = 바꾼 뒤에만 있는 문제, Fixed = 바꾸기 전에만 있던 문제.</summary>
    public static (IReadOnlyList<Need> Added, IReadOnlyList<Need> Fixed) Diff(GearSettings g, GearItems items,
        IReadOnlyDictionary<string, int> itemTier, IReadOnlyDictionary<string, int> change)
    {
        var after = new Dictionary<string, int>(itemTier);
        foreach (var (id, n) in change) { if (n is >= 1 and <= 7) after[id] = n; else after.Remove(id); }
        var before = Check(g, items, itemTier);
        var now = Check(g, items, after);
        var bk = before.Select(n => n.Key).ToHashSet();
        var ak = now.Select(n => n.Key).ToHashSet();
        return (now.Where(n => !bk.Contains(n.Key)).ToList(), before.Where(n => !ak.Contains(n.Key)).ToList());
    }

    private static readonly Regex IdRx = new("^[0-9a-f]{24}$", RegexOptions.Compiled);

    /// <summary>웹이 보낸 {id: 티어} 맵. 객체가 아니거나 max개를 넘으면 null. 형식이 틀린 항목(id·0~7 정수 아님)은 뺀다.</summary>
    public static Dictionary<string, int>? ParseTierMap(JsonNode? node, int max = 20000)
    {
        if (node is not JsonObject o || o.Count > max) return null;
        var res = new Dictionary<string, int>();
        foreach (var (k, v) in o)
        {
            var id = k.ToLowerInvariant();
            if (!IdRx.IsMatch(id) || v is not JsonValue jv || !jv.TryGetValue<int>(out var n) || n is < 0 or > 7) continue;
            res[id] = n;
        }
        return res;
    }

    /// <summary>1.1.6 묶음으로 옮기기: 총에서 호환 부품 표를 따라 닿는 부품(Section parts) 전부. 막힌 것은 지나가지도 않는다. 깊이 12, max개까지.</summary>
    public static IReadOnlyList<string> Parts(GearSettings g, GearItems items, string gunId, int max = 2000)
    {
        var black = BlackAll(g);
        var seen = new HashSet<string> { gunId };
        var res = new List<string>();
        var q = new Queue<(string Id, int Depth)>();
        q.Enqueue((gunId, 0));
        while (q.Count > 0 && res.Count < max)
        {
            var (cur, depth) = q.Dequeue();
            if (depth >= MaxDepth || !items.CompatibleMods.TryGetValue(cur, out var slots)) continue;
            foreach (var list in slots.Values)
                foreach (var m in list)
                {
                    if (!seen.Add(m) || !Usable(g, items, black, m)) continue;
                    if (items.Info.TryGetValue(m, out var i) && i.Section == "parts") { res.Add(m); if (res.Count >= max) return res; }
                    q.Enqueue((m, depth + 1));
                }
        }
        return res;
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
            if (gun && ctx.ScopeNeed(id, t) is { } sc) res.Add(sc);
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

        private bool ScopeEnd(string id, int t)
        {
            if (!items.Info.TryGetValue(id, out var x) || x.Section != "parts" || x.Slot != "scope") return false;
            bool thermal = (x.Thermal || g.ThermalAdd.Contains(id)) && !g.ThermalRemove.Contains(id);
            if (thermal) return g.ThermalsT7 && t == 6; // 서버 ThermalOk의 판과 무관한 부분(밤 확률은 보지 않음)
            return !x.LongScope || Caps(t).LongScope > 0;
        }

        /// <summary>
        /// 총에서 호환 부품 표를 따라 조준경까지. 비용 = (티어 위 고리 수, 그 고리 티어 합, 가격 합)을 사전순으로 비교하는 최단 경로.
        /// 막힌 부품은 지나가지 않는다. 깊이 12·방문 512에서 멈춘다.
        /// </summary>
        public (bool HasAny, bool Ok, IReadOnlyList<string> Lift) ScopePath(string gunId, int t)
        {
            var best = new Dictionary<string, (int C, int T, double P)>();
            var prev = new Dictionary<string, string>();
            var pq = new PriorityQueue<(string Id, int Depth), (int C, int T, double P)>(
                Comparer<(int C, int T, double P)>.Create((a, b) => a.C != b.C ? a.C.CompareTo(b.C) : a.T != b.T ? a.T.CompareTo(b.T) : a.P.CompareTo(b.P)));
            best[gunId] = (0, 0, 0);
            pq.Enqueue((gunId, 0), (0, 0, 0));
            int visits = 0;
            string? end = null;
            while (pq.TryDequeue(out var cur, out var cost))
            {
                if (best[cur.Id] != cost) continue; // 더 좋은 값으로 이미 처리함
                if (cur.Id != gunId && ScopeEnd(cur.Id, t)) { end = cur.Id; break; } // 첫 조준경 = 가장 싼 경로
                if (++visits > MaxVisit || cur.Depth >= MaxDepth) continue;
                if (!items.CompatibleMods.TryGetValue(cur.Id, out var slots)) continue;
                foreach (var list in slots.Values)
                    foreach (var m in list)
                    {
                        if (m == gunId || !Ok(m)) continue;
                        bool up = !TierPass(m, t);
                        var nc = up ? (cost.C + 1, cost.T + Tier(m), cost.P + Price(m)) : cost;
                        if (best.TryGetValue(m, out var old) && pq.Comparer.Compare(old, nc) <= 0) continue;
                        best[m] = nc; prev[m] = cur.Id;
                        pq.Enqueue((m, cur.Depth + 1), nc);
                    }
            }
            if (end == null) return (false, false, []);
            var lift = new List<string>();
            for (var x = end; x != gunId; x = prev[x]) if (!TierPass(x, t)) lift.Add(x);
            lift.Reverse();
            return (true, lift.Count == 0, lift);
        }

        public Need? ScopeNeed(string id, int t)
        {
            if (!g.PartTiers) return null;
            var (any, ok, lift) = ScopePath(id, t);
            return any && !ok ? Make("scope", id, t, null, lift, null) : null;
        }

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
