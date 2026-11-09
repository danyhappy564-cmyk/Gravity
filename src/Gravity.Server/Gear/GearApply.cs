using Gravity.Core;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace Gravity.Gear;

/// <summary>
/// 봇 하나에 대한 장비 기준. Apply가 만들고, SPT가 나중에 새로 만드는 목록(동적 부품·방탄판·전리품 캐시)도 같은 기준으로 거른다.
/// </summary>
public sealed class GearContext
{
    public required GearPick Pick { get; init; }
    public required GearSettings Settings { get; init; }
    public required TierCaps Caps { get; init; }
    public required GearItems Items { get; init; }
    public required GearRaid Raid { get; init; }
    /// <summary>이 봇에 해당하는 블랙리스트 id.</summary>
    public required HashSet<string> Black { get; init; }
    public bool ThermalOk { get; init; }
    public bool LongOk { get; init; }
    public RoleGear Role => Settings.RoleFor(Pick.RoleId);

    public ItemInfo? Info(string id)
    {
        if (!Items.Info.TryGetValue(id, out var x)) return null;
        var thermal = (x.Thermal || Settings.ThermalAdd.Contains(id)) && !Settings.ThermalRemove.Contains(id);
        return thermal == x.Thermal ? x : x with { Thermal = thermal };
    }

    /// <summary>부모 아이템의 이 칸이 필수 칸인지(방어구 내장 부품 등). SPT는 필수 칸 후보가 비면 봇 생성 전체가 실패한다.</summary>
    public bool Required(string? parentId, string slotName) =>
        parentId != null && Items.Info.TryGetValue(parentId, out var p) && p.RequiredSlots?.Contains(slotName.ToLowerInvariant()) == true;

    public int TierOf(string slot, string id) => Settings.ItemTier.TryGetValue(id, out var tier) && tier is >= 1 and <= 7
        ? tier - 1 : Items.TierOf(slot, id);

    /// <summary>블랙리스트·퀘스트 템·이번 판 묶음 밖 모드 아이템·PMC 전용 모드 아이템. 어떤 경우에도 되살리지 않는다.</summary>
    public bool Blocked(string id)
    {
        if (Black.Contains(id)) return true;
        if (Info(id) is not { } info) return false;
        if (info.Quest && (Settings.NoQuest || Settings.BlockQuest)) return true;
        if (info.Mod && (!Raid.ModAllowed(id) || (Settings.ModPmcOnly && !Pick.Pmc))) return true;
        return false;
    }
}

/// <summary>
/// 봇 하나의 복제본(BotType)을 티어에 맞게 고친다. SPT가 봇마다 복제하므로 DB 원본은 그대로다.
/// </summary>
public static class GearApply
{
    private static readonly string[] Managed =
        ["Headwear", "Earpiece", "FaceCover", "ArmorVest", "TacticalVest", "Backpack", "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster", "Eyewear"];
    private static readonly string[] ClassCapped = ["Headwear", "ArmorVest", "TacticalVest"];

    /// <summary>티어 거리 d(봇 티어 − 아이템 티어, 0 이상)의 무게 배율. 웹 기본 표 max(2, 30 − 6d)와 같은 모양.</summary>
    public static double Factor(int d) => Math.Max(2, 30 - 6 * d) / 30.0;

    /// <summary>관통력 → 탄약 티어: 관통 상한이 그 값 이상인 첫 티어.</summary>
    public static int AmmoTier(IReadOnlyList<TierCaps> tiers, double pen)
    {
        for (int i = 0; i < tiers.Count; i++) if (pen <= tiers[i].Pen) return i;
        return tiers.Count - 1;
    }

    public static HashSet<string> BlackFor(GearSettings g, GearPick pick) =>
        g.Blacklist.Where(e => e.Bots.Any(b => b == "all" || b == pick.Group || b == pick.RoleId || (b == "scav" && pick.Group == "scavs")))
            .Select(e => e.Id).ToHashSet();

    public static GearContext Context(GearSettings g, GearPick pick, GearItems items, GearRaid raid, bool night, Random rnd)
    {
        var caps = g.Tiers[pick.Tier];
        return new GearContext
        {
            Pick = pick, Settings = g, Caps = caps, Items = items, Raid = raid, Black = BlackFor(g, pick),
            ThermalOk = (g.ThermalsT7 && pick.Tier == 6) || (night && rnd.NextDouble() * 100 < g.NightThermal),
            LongOk = rnd.NextDouble() * 100 < caps.LongScope
        };
    }

    /// <summary>결과 요약(시험·로그용).</summary>
    public sealed record Result(int Removed, int Restored);

    public static Result Apply(BotType bot, GearSettings g, GearPick pick, GearItems items, GearRaid raid, bool night, Random rnd) =>
        Apply(bot, Context(g, pick, items, raid, night, rnd), night, rnd);

    public static Result Apply(BotType bot, GearContext c, bool night, Random rnd)
    {
        int removed = 0, restored = 0;
        var g = c.Settings;
        var pick = c.Pick;
        var caps = c.Caps;
        var inv = bot.BotInventory;

        var table = g.TableFor(pick.RoleId, pick.Tier);
        bool allowArmouredRig = rnd.NextDouble() * 100 < table.Chance.GetValueOrDefault("armouredRig", 100);

        // 1) 장비 목록
        if (inv?.Equipment != null)
            foreach (var (slotEnum, pool) in inv.Equipment)
            {
                if (pool == null || pool.Count == 0) continue;
                var slot = slotEnum.ToString();
                var orig = pool.ToList();
                bool managed = Managed.Contains(slot);
                foreach (var (mid, w) in orig)
                {
                    var id = mid.ToString();
                    var info = c.Info(id);
                    bool drop = c.Blocked(id);
                    if (!drop && managed)
                    {
                        int t = c.TierOf(slot, id);
                        if (t > pick.Tier) drop = true;
                        else if (info != null && ClassCapped.Contains(slot) && info.ArmorClass > caps.FMax) drop = true;
                        else if (slot == "TacticalVest" && !allowArmouredRig && info != null && (info.ArmorClass > 0 || info.PlateSlots)) drop = true;
                        else pool[mid] = w * Factor(pick.Tier - t) * (info?.Mod == true ? g.ModMult / 100.0 : 1);
                    }
                    if (drop) { pool.Remove(mid); removed++; }
                }
                if (pool.Count == 0 || pool.Values.All(v => v <= 0))
                {
                    // 다 빠졌으면: 막히지 않은 것 중 가장 낮은 티어만 원래 무게로 되살린다.
                    // 막힌 것은 되살리지 않는다. 주무기만은 봇이 맨손이 되지 않게 원래 목록으로 둔다(판정 50).
                    var keep = orig.Where(kv => !c.Blocked(kv.Key.ToString())).ToList();
                    if (keep.Count == 0 && slot == "FirstPrimaryWeapon") keep = orig;
                    pool.Clear();
                    if (keep.Count > 0)
                    {
                        int minT = keep.Min(kv => c.TierOf(slot, kv.Key.ToString()));
                        foreach (var (k, v) in keep.Where(kv => c.TierOf(slot, kv.Key.ToString()) == minT)) pool[k] = Math.Max(1, v);
                    }
                    restored++;
                }
            }

        // 2) 탄약: 관통 상한(스캐브는 블랙리스트 스캐브 상한도), 티어 거리 무게. 칼리버마다 최소 한 개는 남긴다(총은 있는데 탄이 없으면 안 됨).
        double penCap = pick.Group == "scavs" ? Math.Min(caps.Pen, g.ScavPenCap) : caps.Pen;
        if (inv?.Ammo != null)
            foreach (var (_, pool) in inv.Ammo)
            {
                if (pool == null || pool.Count == 0) continue;
                var orig = pool.ToList();
                foreach (var (mid, w) in orig)
                {
                    var id = mid.ToString();
                    var info = c.Info(id);
                    var pen = info?.Pen ?? 0;
                    if (c.Blocked(id) || pen > penCap) { pool.Remove(mid); removed++; continue; }
                    pool[mid] = w * Factor(Math.Max(0, pick.Tier - AmmoTier(g.Tiers, pen))) * (info?.Mod == true ? g.ModMult / 100.0 : 1);
                }
                if (pool.Count == 0)
                {
                    var keep = orig.Where(kv => !c.Blocked(kv.Key.ToString())).ToList();
                    if (keep.Count == 0) keep = orig;
                    var low = keep.OrderBy(kv => c.Info(kv.Key.ToString())?.Pen ?? 0).First();
                    pool[low.Key] = Math.Max(1, low.Value);
                    restored++;
                }
            }

        // 3) 부품 목록
        if (inv?.Mods != null)
            foreach (var (parentId, slots) in inv.Mods)
            {
                if (slots == null) continue;
                var parent = parentId.ToString();
                foreach (var (slotName, set) in slots)
                {
                    if (set == null || set.Count == 0) continue;
                    var (r, back) = FilterModSet(set, slotName, c, c.Required(parent, slotName));
                    removed += r;
                    if (back) restored++;
                }
            }

        // 4) 전리품 목록: 막힌 것만 뺀다(보안 컨테이너는 그대로). PMC 가방·주머니·리그는 SPT가 캐시에서 새로 만들므로 GearLootPatch가 한 번 더 거른다.
        var ip = inv?.Items;
        if (ip != null)
            foreach (var pool in new[] { ip.Backpack, ip.Pockets, ip.TacticalVest, ip.SpecialLoot })
            {
                if (pool == null) continue;
                foreach (var mid in pool.Keys.ToList())
                    if (c.Blocked(mid.ToString())) { pool.Remove(mid); removed++; }
            }

        ApplyChances(bot, g, caps, c.Role, table, night);
        ApplyGeneration(bot, caps, c.Role, table, c);
        return new Result(removed, restored);
    }

    /// <summary>
    /// 부품 칸 하나 거르기(봇 목록과 SPT 동적 목록 공통). 막힌 것·방탄판 등급·열화상·배율 조준경을 뺀다.
    /// 다 빠지면: 막힌 것은 빼고 방탄판은 가장 가까운 등급, 조준경·야투경은 열화상 아닌 것만 되살린다(판정 51). 되살릴 게 없으면 비워 둔다. 반환: (뺀 수, 되살림 여부).
    /// 단 필수 칸(required)은 절대 비우지 않는다(1.0 인게임: 묶음 밖 모드 방어구 내장 부품이 다 막혀 SPT가 "Nullable object must have a value"로 봇 생성 실패).
    /// 그때는 사용자 블랙리스트에 없는 것을, 그것도 없으면 원래 목록을 되살린다.
    /// </summary>
    public static (int Removed, bool Restored) FilterModSet(HashSet<MongoId> set, string slotName, GearContext c, bool required = false)
    {
        var before = set.ToList();
        var res = FilterCore(set, slotName, c);
        if (set.Count > 0 || !required || before.Count == 0) return res;
        var soft = before.Where(m => !c.Black.Contains(m.ToString())).ToList();
        foreach (var m in soft.Count > 0 ? soft : before) set.Add(m);
        return (before.Count - set.Count, true);
    }

    private static (int Removed, bool Restored) FilterCore(HashSet<MongoId> set, string slotName, GearContext c)
    {
        var s = slotName.ToLowerInvariant();
        bool front = s is "front_plate" or "back_plate", side = s is "left_side_plate" or "right_side_plate";
        bool optic = s.StartsWith("mod_scope") || s.StartsWith("mod_nvg");
        var caps = c.Caps;
        var orig = set.ToList();
        set.RemoveWhere(mid =>
        {
            var id = mid.ToString();
            if (c.Blocked(id)) return true;
            var info = c.Info(id);
            if (info == null) return false;
            if (front && info.Plate && (info.ArmorClass < caps.FMin || info.ArmorClass > caps.FMax)) return true;
            if (side && info.Plate && info.ArmorClass > caps.Side) return true;
            if (optic && info.Thermal && !c.ThermalOk) return true;
            if (optic && info.LongScope && !info.Thermal && !c.LongOk) return true;
            return false;
        });
        int removed = orig.Count - set.Count;
        if (set.Count > 0) return (removed, false);
        var ok = orig.Where(m => !c.Blocked(m.ToString())).ToList();
        if (ok.Count == 0) return (removed, false); // 전부 막힘: 비워 둔다
        if (front || side)
        {
            int lo = front ? caps.FMin : 0, hi = front ? caps.FMax : caps.Side;
            int Dist(MongoId m) { var cl = c.Info(m.ToString())?.ArmorClass ?? 0; return cl < lo ? lo - cl : cl > hi ? cl - hi : 0; }
            int best = ok.Min(Dist);
            foreach (var m in ok.Where(m => Dist(m) == best)) set.Add(m);
        }
        else
        {
            // 조준경·야투경: 열화상은 되살리지 않는다(남는 게 열화상뿐이면 칸을 비움). 배율 조준경만 되살릴 수 있다.
            var cand = optic && !c.ThermalOk ? ok.Where(m => c.Info(m.ToString())?.Thermal != true).ToList() : ok;
            if (cand.Count == 0) return (removed, false);
            foreach (var m in cand) set.Add(m);
        }
        return (orig.Count - set.Count, true);
    }

    private static void ApplyChances(BotType bot, GearSettings g, TierCaps caps, RoleGear role, GearTable table, bool night)
    {
        var ch = bot.BotChances;
        if (ch == null) return;
        if (ch.EquipmentChances != null)
            foreach (var (k, key) in new[] { ("headwear", "Headwear"), ("earpiece", "Earpiece"), ("faceCover", "FaceCover"), ("armorVest", "ArmorVest"),
                         ("backpack", "Backpack"), ("holster", "Holster"), ("eyewear", "Eyewear") })
                if (table.Chance.TryGetValue(k, out var v)) ch.EquipmentChances[key] = v;
        if (ch.EquipmentModsChances != null)
        {
            var em = ch.EquipmentModsChances;
            em["front_plate"] = em["back_plate"] = role.FrontPlate;
            em["left_side_plate"] = em["right_side_plate"] = role.SidePlate;
            foreach (var k in new[] { "mod_equipment_000", "mod_equipment_001", "mod_equipment_002" }) em[k] = caps.HelmetAcc;
            if (night) em["mod_nvg"] = g.NightNvg;
        }
        var wm = ch.WeaponModsChances;
        if (wm != null)
        {
            double Of(string k) => table.Mods.GetValueOrDefault(k, 50);
            var rules = new (string Prefix, double Value)[]
            {
                ("mod_scope", g.ForceScope ? 100 : Of("scope")), ("mod_muzzle", g.ForceMuzzle ? 100 : Of("muzzle")),
                ("mod_tactical", Of("tactical")), ("mod_foregrip", Of("foregrip")),
                ("mod_stock", g.ForceStock ? 100 : Of("stock")), ("mod_mount", Of("mount"))
            };
            foreach (var (prefix, v) in rules)
            {
                foreach (var key in wm.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList()) wm[key] = v;
                wm[prefix] = v;
            }
            if (g.ForceDust) wm["mod_reciever"] = 100;
        }
    }

    /// <summary>개수 표 → SPT 개수 무게(최소~최대 고르게). 티어 상한이 최대를 자른다(판정 44).</summary>
    public static Dictionary<double, double> Uniform(int min, int max)
    {
        if (max < min) min = max;
        var d = new Dictionary<double, double>();
        for (int n = Math.Max(0, min); n <= Math.Max(0, max); n++) d[n] = 1;
        return d;
    }

    private static void ApplyGeneration(BotType bot, TierCaps caps, RoleGear role, GearTable table, GearContext c)
    {
        var items = bot.BotGeneration?.Items;
        if (items == null) return;
        void Set(GenerationData? gd, string key, int? cap = null, bool zero = false)
        {
            if (gd == null) return;
            // 개수별 고정 목록(화이트리스트)에서도 막힌 것을 뺀다.
            if (gd.Whitelist != null)
                foreach (var mid in gd.Whitelist.Keys.ToList())
                    if (c.Blocked(mid.ToString())) gd.Whitelist.Remove(mid);
            if (!table.Gen.TryGetValue(key, out var mm)) return;
            int mx = cap.HasValue ? Math.Min(mm.Max, cap.Value) : mm.Max;
            gd.Weights = zero ? Uniform(0, 0) : Uniform(Math.Min(mm.Min, mx), mx);
        }
        Set(items.Magazines, "magazines", caps.Mags);
        Set(items.Healing, "meds", caps.Meds);
        Set(items.Stims, "stims");
        Set(items.Grenades, "grenades", caps.Nades);
        Set(items.Food, "food");
        Set(items.Drink, "drink");
        Set(items.BackpackLoot, "backpackLoot", zero: !role.Loot);
        Set(items.PocketLoot, "pocketLoot", zero: !role.Loot);
        Set(items.VestLoot, "vestLoot", zero: !role.Loot);
        Set(items.Drugs, "-");
        Set(items.SpecialItems, "-");
    }

    /// <summary>내구도 범위(%): 티어 범위와 봇 범위가 겹치는 구간, 안 겹치면 티어 범위(판정 45).</summary>
    public static (double Lo, double Hi) Durability(double tLo, double tHi, double rLo, double rHi)
    {
        double lo = Math.Max(tLo, rLo), hi = Math.Min(tHi, rHi);
        return lo <= hi ? (lo, hi) : (tLo, tHi);
    }
}
