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
        if (info.Mod && (Settings.DisabledSources.Contains(info.Source) || !Raid.ModAllowed(id) || (Settings.ModPmcOnly && !Pick.Pmc))) return true;
        // 가져온 원작 프리셋 표(only): 모드 아이템은 어느 칸에서도 쓰지 않는다(부착물·탄약·소모품 포함, 모드 설정과 무관).
        if (info.Mod && Settings.TableFor(Pick.RoleId, Pick.Tier).Only) return true;
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
        var emptySlots = new List<string>();
        var g = c.Settings;
        var pick = c.Pick;
        var caps = c.Caps;
        var inv = bot.BotInventory;
        var addedEquipment = new HashSet<string>();

        var table = g.TableFor(pick.RoleId, pick.Tier);
        bool allowArmouredRig = rnd.NextDouble() * 100 < table.Chance.GetValueOrDefault("armouredRig", 100);

        // 1) 장비 목록
        if (inv?.Equipment != null)
        {
            foreach(var slot in Enum.GetValues<SPTarkov.Server.Core.Models.Enums.EquipmentSlots>())
                if(!inv.Equipment.ContainsKey(slot)&&GearPools.Controls(table,slot.ToString(),c.Items))inv.Equipment[slot]=new();
            foreach (var (slotEnum, pool) in inv.Equipment)
            {
                if (pool == null) continue;
                var slot = slotEnum.ToString();
                var orig = pool.ToList();
                bool userSlot = GearPools.Controls(table, slot, c.Items);
                // 실제 DB에 있고 이 장비 칸에 맞는 아이템만 추가한다. DB 원본은 SPT가 만든 봇 복제본과 분리되어 있다.
                foreach (var id in GearPools.Seeds(table,c.Items,slot))
                    if(pool.TryAdd(id,1))addedEquipment.Add(id);
                var candidates = pool.ToList();
                bool managed = Managed.Contains(slot);
                foreach (var (mid, w) in candidates)
                {
                    var id = mid.ToString();
                    var info = c.Info(id);
                    var manualWeight = info == null ? null : GearPools.Weight(table, info, id,slot);
                    if (manualWeight.HasValue) userSlot = true;
                    bool drop = c.Blocked(id);
                    if (manualWeight is <= 0) drop = true;
                    if(!managed&&!drop&&manualWeight.HasValue)pool[mid]=manualWeight.Value;
                    if (!drop && managed)
                    {
                        int t = c.TierOf(slot, id);
                        if (g.ItemTier.ContainsKey(id) ? t > pick.Tier : !manualWeight.HasValue && t > pick.Tier) drop = true;
                        else if (info != null && ClassCapped.Contains(slot) && info.ArmorClass > caps.FMax) drop = true;
                        else if (slot == "TacticalVest" && !allowArmouredRig && info != null && (info.ArmorClass > 0 || info.PlateSlots)) drop = true;
                        else pool[mid] = manualWeight ?? w * Factor(pick.Tier - t) * (info?.Mod == true ? g.ModMult / 100.0 : 1);
                    }
                    if (drop) { pool.Remove(mid); removed++; }
                }
                if (!userSlot && (pool.Count == 0 || pool.Values.All(v => v <= 0)))
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
                if (userSlot && pool.Count == 0) emptySlots.Add(slot);
            }
        }

        // 2) 탄약: 관통 상한(스캐브는 블랙리스트 스캐브 상한도), 티어 거리 무게. 칼리버마다 최소 한 개는 남긴다(총은 있는데 탄이 없으면 안 됨).
        double penCap = pick.Group == "scavs" ? Math.Min(caps.Pen, g.ScavPenCap) : caps.Pen;
        if (inv?.Ammo != null)
        {
            foreach(var key in table.Pools.Keys.Where(k=>k.StartsWith("ammo/")))inv.Ammo.TryAdd(key[5..],new());
            foreach (var (caliber, pool) in inv.Ammo)
            {
                if (pool == null) continue;
                var orig = pool.ToList();
                if(table.Pools.TryGetValue("ammo/"+caliber,out var chosenAmmo))
                    foreach (var (id,w) in chosenAmmo)
                        if(w>0&&c.Items.Info.TryGetValue(id,out var info)&&info.Section=="ammo"&&info.Slot==caliber)pool.TryAdd(id,1);
                bool explicitAmmo = chosenAmmo != null;
                foreach (var (mid, w) in pool.ToList())
                {
                    var id = mid.ToString();
                    var info = c.Info(id);
                    var weight = info == null ? null : GearPools.Weight(table, info, id);
                    explicitAmmo |= weight.HasValue;
                    var pen = info?.Pen ?? 0;
                    // 탄약 티어 = 같은 구경 안 가격 순위 7등분(직접 지정 우선). 관통 상한은 따로 막는다.
                    // 표에 직접 넣은 탄(무게 있음)은 직접 티어를 정한 경우에만 티어로 막는다(장비 칸과 같은 규칙).
                    int at = c.TierOf("ammo/" + (info?.Slot ?? caliber), id);
                    bool overTier = g.ItemTier.ContainsKey(id) ? at > pick.Tier : !weight.HasValue && at > pick.Tier;
                    if (c.Blocked(id) || weight is <= 0 || pen > penCap || overTier) { pool.Remove(mid); removed++; continue; }
                    pool[mid] = weight ?? w * Factor(Math.Max(0, pick.Tier - at)) * (info?.Mod == true ? g.ModMult / 100.0 : 1);
                }
                if (pool.Count == 0 && !explicitAmmo && orig.Count>0)
                {
                    var keep = orig.Where(kv => !c.Blocked(kv.Key.ToString())).ToList();
                    if (keep.Count == 0) keep = orig;
                    // 티어 위만 남았으면 가장 낮은 티어(같으면 관통 낮은 것) 하나를 되살린다.
                    var low = keep.OrderBy(kv => c.TierOf("ammo/" + caliber, kv.Key.ToString())).ThenBy(kv => c.Info(kv.Key.ToString())?.Pen ?? 0).First();
                    pool[low.Key] = Math.Max(1, low.Value);
                    restored++;
                }
                // 탄약을 모두 0으로 정한 구경은 그 탄을 쓰는 총도 빼서 빈 탄약 목록으로 봇 생성이 실패하지 않게 한다.
                if(pool.Count==0&&explicitAmmo&&inv.Equipment!=null)
                    foreach(var(slot,weapons) in inv.Equipment)
                    {
                        if(!GearItems.WeaponSlots.Contains(slot.ToString()))continue;
                        foreach(var mid in weapons.Keys.Where(m=>c.Info(m.ToString())?.Caliber==caliber).ToArray())weapons.Remove(mid);
                        if(weapons.Count==0)emptySlots.Add(slot.ToString());
                    }
            }
        }

        // 3) 부품 목록
        if(inv!=null && addedEquipment.Count>0)
        {
            inv.Mods??=new();
            GearCompatibility.AddMissing(inv.Mods,addedEquipment.Where(id=>!c.Blocked(id)),c.Items);
        }
        if (inv?.Mods != null)
            foreach (var (parentId, slots) in inv.Mods)
            {
                if (slots == null) continue;
                var parent = parentId.ToString();
                foreach (var (slotName, set) in slots)
                {
                    if (set == null || set.Count == 0) continue;
                    var (r, back) = FilterModSet(set, slotName, c, c.Required(parent, slotName), parent);
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
        if (bot.BotChances?.EquipmentChances != null)
            foreach(var slot in emptySlots)bot.BotChances.EquipmentChances[slot]=0;
        ApplyGeneration(bot, caps, c.Role, table, c);
        return new Result(removed, restored);
    }

    /// <summary>
    /// 부품 칸 하나 거르기(봇 목록과 SPT 동적 목록 공통). 막힌 것·방탄판 등급·열화상·배율 조준경을 뺀다.
    /// 다 빠지면: 막힌 것은 빼고 방탄판은 가장 가까운 등급, 조준경·야투경은 열화상 아닌 것만 되살린다(판정 51). 되살릴 게 없으면 비워 둔다. 반환: (뺀 수, 되살림 여부).
    /// 단 필수 칸(required)은 절대 비우지 않는다(1.0 인게임: 묶음 밖 모드 방어구 내장 부품이 다 막혀 SPT가 "Nullable object must have a value"로 봇 생성 실패).
    /// 그때는 사용자 블랙리스트에 없는 것을, 그것도 없으면 원래 목록을 되살린다.
    /// </summary>
    public static (int Removed, bool Restored) FilterModSet(HashSet<MongoId> set, string slotName, GearContext c, bool required = false, string? parentId = null)
    {
        var before = set.ToList();
        var res = FilterCore(set, slotName, c);
        if (set.Count > 0 || !required || before.Count == 0) return res;
        var soft = before.Where(m => !c.Black.Contains(m.ToString())).ToList();
        foreach (var m in soft.Count > 0 ? soft : before) set.Add(m);
        var line = $"필수 칸 복원 / Required slot restored: {c.Pick.RoleId} T{c.Pick.Tier + 1} parent={parentId ?? "?"} slot={slotName} items={string.Join(",", set)} blacklistException={soft.Count == 0}";
        DiagLog.Remember("WARN", line);
        DiagLog.GearLine(line);
        return (before.Count - set.Count, true);
    }

    /// <summary>부착물 티어(같은 종류 안 가격 순위, 직접 지정 우선). 부착물이 아니면 0.</summary>
    private static int PartTier(string id, GearContext c) => c.Info(id) is { Section: "parts" } i ? c.TierOf("parts/" + i.Slot, id) : 0;

    /// <summary>부착물 티어 규칙: 봇 티어보다 높은 부착물은 뺀다. 부착물 표에 직접 허용(무게&gt;0)한 것은 직접 티어를 정한 경우에만 티어로 막는다.</summary>
    private static bool AboveTier(ItemInfo info, string id, double? weight, GearContext c)
    {
        if (!c.Settings.PartTiers || info.Section != "parts") return false;
        if (weight is > 0 && !c.Settings.ItemTier.ContainsKey(id)) return false;
        return c.TierOf("parts/" + info.Slot, id) > c.Pick.Tier;
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
            var pw = GearPools.Weight(c.Settings.TableFor(c.Pick.RoleId,c.Pick.Tier),info,id);
            if (pw is <=0) return true;
            if (AboveTier(info, id, pw, c)) return true;
            if (front && info.Plate && (info.ArmorClass < caps.FMin || info.ArmorClass > caps.FMax)) return true;
            if (side && info.Plate && (info.ArmorClass < caps.SMin || info.ArmorClass > caps.Side)) return true;
            if (optic && info.Thermal && !c.ThermalOk) return true;
            if (optic && info.LongScope && !info.Thermal && !c.LongOk) return true;
            return false;
        });
        int removed = orig.Count - set.Count;
        if (set.Count > 0) return (removed, false);
        var ok = orig.Where(m => !c.Blocked(m.ToString()) && (c.Info(m.ToString()) is not {} i || GearPools.Weight(c.Settings.TableFor(c.Pick.RoleId,c.Pick.Tier),i,m.ToString()) is not <=0)).ToList();
        if (ok.Count == 0) return (removed, false); // 전부 막힘: 비워 둔다
        if (front || side)
        {
            int lo = front ? caps.FMin : caps.SMin, hi = front ? caps.FMax : caps.Side;
            int Dist(MongoId m) { var cl = c.Info(m.ToString())?.ArmorClass ?? 0; return cl < lo ? lo - cl : cl > hi ? cl - hi : 0; }
            int best = ok.Min(Dist);
            foreach (var m in ok.Where(m => Dist(m) == best)) set.Add(m);
        }
        else
        {
            // 조준경·야투경: 열화상은 되살리지 않는다(남는 게 열화상뿐이면 칸을 비움). 배율 조준경만 되살릴 수 있다.
            var cand = optic && !c.ThermalOk ? ok.Where(m => c.Info(m.ToString())?.Thermal != true).ToList() : ok;
            if (cand.Count == 0) return (removed, false);
            // 부착물 티어로 다 빠졌으면 맞는 것 중 가장 낮은 티어만 되살린다(부착물이 아예 안 달리는 일 방지).
            if (c.Settings.PartTiers && cand.Any(m => c.Info(m.ToString())?.Section == "parts"))
            {
                int lo = cand.Min(m => PartTier(m.ToString(), c));
                cand = cand.Where(m => PartTier(m.ToString(), c) == lo).ToList();
            }
            foreach (var m in cand) set.Add(m);
        }
        return (orig.Count - set.Count, true);
    }

    private static void ApplyChances(BotType bot, GearSettings g, TierCaps caps, RoleGear role, GearTable table, bool night)
    {
        var ch = bot.BotChances;
        if (ch == null) return;
        if(ch.EquipmentChances != null)
            foreach(var(k,v) in table.SlotChances)ch.EquipmentChances[k]=v;
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
            foreach(var(k,v) in table.PartChances)
                if(k.Contains("plate",StringComparison.OrdinalIgnoreCase) || k is "mod_nvg" or "mod_equipment" or "mod_equipment_000" or "mod_equipment_001" or "mod_equipment_002")em[k]=v;
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
                bool force=prefix=="mod_scope"&&g.ForceScope||prefix=="mod_muzzle"&&g.ForceMuzzle||prefix=="mod_stock"&&g.ForceStock;
                foreach (var key in wm.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList()) wm[key] = force ? v : table.PartChances.GetValueOrDefault(key,v);
                wm[prefix] = v;
            }
            if (g.ForceDust) wm["mod_reciever"] = 100;
            foreach(var(k,v) in table.PartChances)
                if(k.StartsWith("mod_")&&!rules.Any(r=>k.StartsWith(r.Prefix,StringComparison.OrdinalIgnoreCase))&&!(g.ForceDust&&k=="mod_reciever"))wm[k]=v;
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
            if(table.Pools.TryGetValue("meds/"+key,out var custom))
                gd.Whitelist=custom.Where(kv=>kv.Value>0&&!c.Blocked(kv.Key)&&c.Info(kv.Key)?.Section=="meds").ToDictionary(kv=>(MongoId)kv.Key,kv=>kv.Value);
            // 개수별 고정 목록(화이트리스트)에서도 막힌 것을 뺀다.
            if (gd.Whitelist != null)
                foreach (var mid in gd.Whitelist.Keys.ToList())
                    if (c.Blocked(mid.ToString())) gd.Whitelist.Remove(mid);
            // 소모품 티어(같은 종류 안 가격 순위, 직접 지정 우선): 표에 직접 넣은 목록은 직접 티어를 정한 것만, SPT 기본 목록은 티어 위 아이템을 뺀다.
            // 전부 빠지면 가장 낮은 티어만 남긴다(봇 소모품이 통째로 사라지지 않게).
            if (gd.Whitelist is { Count: > 0 } wl && key is "meds" or "stims" or "grenades" or "food" or "drink")
            {
                bool ownList = table.Pools.ContainsKey("meds/" + key);
                int TierOfMed(MongoId m) => c.TierOf("meds/" + key, m.ToString());
                var over = wl.Keys.Where(m => (!ownList || c.Settings.ItemTier.ContainsKey(m.ToString())) && TierOfMed(m) > c.Pick.Tier).ToList();
                if (over.Count < wl.Count) foreach (var m in over) wl.Remove(m);
                else if (!ownList)
                {
                    int lo = wl.Keys.Min(TierOfMed);
                    foreach (var m in wl.Keys.Where(m => TierOfMed(m) > lo).ToList()) wl.Remove(m);
                }
            }
            if (!table.Gen.TryGetValue(key, out var mm)) return;
            int mx = cap.HasValue ? Math.Min(mm.Max, cap.Value) : mm.Max;
            gd.Weights = zero ? Uniform(0, 0) : Uniform(Math.Min(mm.Min, mx), mx);
            if(!zero && table.GenWeights.TryGetValue(key,out var countWeights))
            {
                gd.Weights=new Dictionary<double,double>();
                foreach(var(count,weight) in countWeights)
                {
                    var n=Math.Clamp(count,Math.Min(mm.Min,mx),mx);gd.Weights[n]=gd.Weights.GetValueOrDefault(n)+weight;
                }
            }
            if(table.Pools.TryGetValue("meds/"+key,out _) && gd.Whitelist?.Count==0)gd.Weights=Uniform(0,0);
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
