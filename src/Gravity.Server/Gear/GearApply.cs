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
    /// <summary>1.1.0: 이번 레이드 맵 id(소문자, 모르면 null)와 밤 여부. 맵별 무기 성향·밤 손전등에 쓴다.</summary>
    public string? Map { get; init; }
    public bool Night { get; init; }
    public RoleGear Role => Settings.RoleFor(Pick.RoleId);
    /// <summary>1.1.5 이 봇의 장비 고르는 범위(봇별 설정이 global이면 전체 설정).</summary>
    public string TierRange => Role.TierRange is "exact" or "near" or "all" or "down" ? Role.TierRange : Settings.TierRange;
    /// <summary>1.0.7 PackNStrap: 벨트 아이템이 있어 완장 칸을 Gravity가 정하는지.</summary>
    public bool BeltManaged { get; set; }
    /// <summary>1.0.7 PackNStrap: 이 봇의 완장 칸 벨트 목록. null = 벨트 안 참.</summary>
    public Dictionary<MongoId, double>? Belts { get; set; }
    /// <summary>1.0.7 PackNStrap: 실제로 찬 벨트 id(수납 칸 등록됨). null = 안 참.</summary>
    public string? BeltWorn { get; set; }

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

    /// <summary>1.1.0 전리품 목록용: 막힌 것 + 열쇠 넣기를 끈 봇의 열쇠·키카드.</summary>
    public bool LootBlocked(string id) => Blocked(id) || (!Role.Keys && Items.Info.TryGetValue(id, out var x) && x.Cat == "key");

    /// <summary>블랙리스트·퀘스트 템·이번 판 묶음 밖 모드 아이템(1.1.0: 탄약은 무기를 따라가므로 묶음 검사 안 함)·PMC 전용 모드 아이템. 어떤 경우에도 되살리지 않는다.</summary>
    public bool Blocked(string id)
    {
        if (Black.Contains(id)) return true;
        if (Info(id) is not { } info) return false;
        if (info.Quest && (Settings.NoQuest || Settings.BlockQuest || !Role.Quest)) return true;
        // 1.0.9: 모드 아이템 전체 스위치가 꺼지면 모드 아이템은 막는다. PackNStrap 벨트는 따로 있는 벨트 설정(pns.on)을 따른다.
        if (info.Mod && !Settings.ModsOn && !GearBelts.IsBelt(Items, id)) return true;
        if (info.Mod && (Settings.ModDisabled(info) || (info.Section != "ammo" && !Raid.ModAllowed(id)) || (Settings.ModPmcOnly && !Pick.Pmc))) return true;
        // 가져온 원작 프리셋 표(only): 모드 아이템은 어느 칸에서도 쓰지 않는다(부착물·탄약·소모품 포함, 모드 설정과 무관).
        // 1.1.6 #106: 모드 자동 끼우기(기본 켬)가 켜져 있으면 목록 전용 표에도 모드 아이템을 끼워 넣는다(그 총의 모드 부품도 허용). 끄면 예전처럼 막는다.
        if (info.Mod && !Settings.ModAutoFill && Settings.TableFor(Pick.RoleId, Pick.Tier).Only) return true;
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

    public static GearContext Context(GearSettings g, GearPick pick, GearItems items, GearRaid raid, bool night, Random rnd, string? map = null)
    {
        var caps = g.Tiers[pick.Tier];
        return new GearContext
        {
            Pick = pick, Settings = g, Caps = caps, Items = items, Raid = raid, Black = BlackFor(g, pick),
            ThermalOk = (g.ThermalsT7 && pick.Tier == 6) || (night && rnd.NextDouble() * 100 < g.NightThermal),
            LongOk = rnd.NextDouble() * 100 < caps.LongScope,
            Map = string.IsNullOrEmpty(map) ? null : map.ToLowerInvariant(), Night = night
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
                bool controls = GearPools.Controls(table, slot, c.Items), userSlot = false;
                bool managed = Managed.Contains(slot);
                // 1.1.6: 첫 판은 봇별 표대로. 표에 무게를 준 아이템이 하나도 남지 않으면(모드 삭제·블랙리스트·위 티어 직접 지정)
                // 그 칸을 비우지 않고 표 없이(아이템 티어 규칙으로) 한 번 더 거른다. 0%만 넣은 칸(Wants 거짓)은 일부러 끈 것이라 그대로 비운다.
                for (int pass = 0; pass < 2; pass++)
                {
                bool useTable = pass == 0;
                userSlot = useTable && controls;
                if (useTable)
                {
                    // 실제 DB에 있고 이 장비 칸에 맞는 아이템만 추가한다. DB 원본은 SPT가 만든 봇 복제본과 분리되어 있다.
                    // 1.1.6 #106: 중괄호가 없어 아래 else가 안쪽 if(TryAdd)에 붙어, 원래 목록에 이미 있는 표 아이템을 만나면 목록을 원래대로 되돌려 앞서 넣은 표 아이템이 사라졌다.
                    foreach (var id in GearPools.Seeds(table,c.Items,slot))
                        if(pool.TryAdd(id,1))addedEquipment.Add(id);
                }
                else { pool.Clear(); foreach (var (k, v) in orig) pool[k] = v; }
                var candidates = pool.ToList();
                foreach (var (mid, w) in candidates)
                {
                    var id = mid.ToString();
                    var info = c.Info(id);
                    var manualWeight = info == null || !useTable ? null : GearPools.Weight(table, info, id, slot, controls);
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
                // 1.1.5 장비 후보 범위(gear.tierRange): 자동 티어 아이템 중 봇 티어(near는 한 단계 아래까지)보다 낮은 것은 뺀다.
                // 사람이 표에 무게를 직접 준 아이템은 그대로 둔다. 다 빠지면 남은 것 중 가장 가까운 아래 티어만 남긴다(스캐브처럼 원래 목록이 낮은 봇).
                // 1.1.6 #106 모드 자동 끼우기: 원래 목록·표에 없는 모드 아이템을 아이템 티어대로 더한다(이번 판 모드 묶음·블랙리스트·PMC 전용 등은 Blocked가 거름).
                // 표의 이 칸에 모드 아이템이 하나라도 있으면(0% 포함) 사람이 고른 것이라 건너뛰고, 0%만 넣어 끈 칸도 건너뛴다.
                if (managed && g.ModAutoFill && g.ModsOn && !(useTable && controls && !GearPools.Wants(table, slot, c.Items)) && !TableHasMod(table, slot, c.Items))
                {
                    var pos = pool.Where(kv => kv.Value > 0).Select(kv => kv.Value).ToList();
                    double baseW = pos.Count > 0 ? pos.Average() : 1;
                    foreach (var id in c.Items.ModFor(slot))
                    {
                        var mid = new MongoId(id);
                        if (pool.ContainsKey(mid) || c.Blocked(id)) continue;
                        int t = c.TierOf(slot, id);
                        if (t > pick.Tier) continue;
                        var inf = c.Info(id);
                        if (inf != null && ClassCapped.Contains(slot) && inf.ArmorClass > caps.FMax) continue;
                        if (slot == "TacticalVest" && !allowArmouredRig && inf != null && (inf.ArmorClass > 0 || inf.PlateSlots)) continue;
                        pool[mid] = baseW * Factor(pick.Tier - t) * g.ModMult / 100.0;
                        addedEquipment.Add(id);
                    }
                }
                if (managed && c.TierRange != "all") RangeFilter(pool, slot, table, c, useTable && controls);
                // 1.1.5(1차 하네스 F-plate): 앞·뒤판 칸에 이 티어 등급 범위([FMin,FMax]) 판이 하나도 안 들어가는 방탄복·리그는 뺀다.
                // 그대로 두면 판 거르기가 '가장 가까운 등급'으로 대체해 T5에 3급 판, T1에 5급 옆판이 들어간다. 다른 후보가 남을 때만 뺀다.
                if (slot is "ArmorVest" or "TacticalVest" && GearRuntime.Templates is { } tp)
                {
                    var bad = pool.Where(kv => kv.Value > 0 && !PlateRangeFits(kv.Key, caps, tp, c)).Select(kv => kv.Key).ToList();
                    if (bad.Count > 0 && bad.Count < pool.Count(kv => kv.Value > 0))
                        foreach (var m in bad) { pool.Remove(m); removed++; }
                }
                if (useTable && userSlot && !pool.Any(kv => kv.Value > 0) && GearPools.Wants(table, slot, c.Items))
                {
                    var line = $"봇별 표 칸 비어 아이템 티어로 / Table slot empty, item tiers used: {pick.RoleId} T{pick.Tier + 1} slot={slot}";
                    DiagLog.GearLine(line);
                    restored++;
                    continue;
                }
                break;
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

        // 1-1) 1.1.0 맵별 무기 성향: 맵 기본값보다 원거리로 옮기면 지정사수·저격총 무게를, 근거리로 옮기면 기관단총·산탄총 무게를 키운다.
        // 기본값 그대로면 바꾸지 않는다. 무게가 모두 0이 되면 그 칸은 손대지 않는다(맨손 방지).
        if (inv?.Equipment != null) ApplyRange(inv.Equipment, c);

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
        // 원래 목록에 있던 장비도 부품 표가 통째로 없으면 채운다. SPT 원본의 일반 스캐브 TT 플레이트 캐리어처럼
        // 표가 없으면 SPT가 "lacks a mod slot pool" 경고만 남기고 판·내장재를 하나도 안 단다. 표가 있는 장비는 원래 규칙 그대로 둔다.
        var noPool = inv?.Equipment == null ? [] : inv.Equipment.Values.Where(p => p != null).SelectMany(p => p.Keys)
            .Select(m => m.ToString()).Where(id => !addedEquipment.Contains(id) && c.Items.CompatibleMods.ContainsKey(id)
                && (inv.Mods == null || !inv.Mods.ContainsKey(id))).ToHashSet();
        if(inv!=null && (addedEquipment.Count>0 || noPool.Count>0))
        {
            inv.Mods??=new();
            GearCompatibility.AddMissing(inv.Mods,addedEquipment.Concat(noPool).Where(id=>!c.Blocked(id)),c.Items);
        }
        // 1.1.0: 표 안 부품 중 칸 표가 없는 것은 다른 봇 표로 메운다(TRG M10 조준경 마운트 등).
        if(inv?.Mods!=null)GearCompatibility.FillHoles(inv.Mods,c.Items,c.Blocked);
        // 1.1.5(1차 하네스 F-plate): 봇 원래 표의 판 칸에 이 티어 등급 범위 판이 하나도 없으면(SPT 레이더 Kirasa-N 앞판 = AR500 LP 3급뿐 등),
        // 그 방탄복이 실제로 받는 판 중 범위 안 것을 더한다. 안 그러면 거르기가 '가장 가까운 등급'으로 대체해 T5 레이더가 3급 판을 단다.
        if (inv?.Mods != null)
            foreach (var (parentId, slots) in inv.Mods)
            {
                if (slots == null || !c.Items.CompatibleMods.TryGetValue(parentId.ToString(), out var compat)) continue;
                foreach (var (slotName, set) in slots)
                {
                    var n = slotName.ToLowerInvariant();
                    bool front = n is "front_plate" or "back_plate", side = n is "left_side_plate" or "right_side_plate";
                    if ((!front && !side) || set == null || !compat.TryGetValue(slotName, out var all)) continue;
                    int lo = front ? caps.FMin : caps.SMin, hi = front ? caps.FMax : caps.Side;
                    bool In(string id) => c.Info(id) is { Plate: true } i && i.ArmorClass >= lo && i.ArmorClass <= hi;
                    if (set.Any(m => In(m.ToString()))) continue;
                    foreach (var id in all.Where(id => In(id) && !c.Blocked(id))) set.Add(new MongoId(id));
                }
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
                    if (c.LootBlocked(mid.ToString())) { pool.Remove(mid); removed++; }
            }

        // 5) 1.0.7 PackNStrap 벨트: 완장 칸을 만들 때(GearBeltEquipPatch) 쓸 목록을 정한다. 완장 칸이 없는 봇 종류면 만들어 둔다.
        (c.BeltManaged, c.Belts) = GearBelts.Decide(c, table, rnd);
        if (c.Belts != null && inv?.Equipment != null) inv.Equipment.TryAdd(EquipmentSlots.ArmBand, new());

        ApplyChances(bot, g, caps, c.Role, table, night);
        if (bot.BotChances?.EquipmentChances != null)
            foreach(var slot in emptySlots)bot.BotChances.EquipmentChances[slot]=0;
        ApplyGeneration(bot, caps, c.Role, table, c);
        // 1.0.5 의상 탭: 허용한 모드의 옷만 PMC·스캐브 외형 목록에(설정 전이면 그대로).
        GearClothing.Apply(bot, c, rnd);
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
        // 1.1.5(1차 하네스 A-slot): 총의 약실이 받는 탄과 하나도 겹치지 않는 탄창은 뺀다(다른 후보가 남을 때만).
        // SalcosArmory는 .300 BLK AK 탄창을 5.45 AK 탄창 칸에도 넣어, 봇이 .300 탄창에 5.45 탄을 채운 채 들고 나왔다.
        if (slotName == "mod_magazine" && parentId != null && GearRuntime.Templates is { } tp) DropWrongCaliberMags(set, parentId, tp);
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

    /// <summary>
    /// 소모품 캐시 결과를 이 봇 것으로 바꾼다(Codex 6차 2~4번). SPT 역할 캐시는 그 역할의 첫 봇 생성 목록(화이트리스트)으로 한 번만 만들어져,
    /// 첫 봇이 T1이면 뒤의 T7도 T1 목록만 받는다. 이 봇의 생성 목록(ApplyGeneration이 티어·직접 목록으로 거른 것)이 있으면 SPT처럼 그 목록을
    /// 그대로 쓰고(캐시가 비어 있어도), 없으면(SPT가 가방 목록에서 종류로 채우는 경우) 캐시를 티어로 거른다.
    /// </summary>
    public static Dictionary<MongoId, double> ConsumablesFor(Dictionary<MongoId, double> cache, GenerationData? gen, string? kind, GearContext c)
    {
        if (gen?.Whitelist is { Count: > 0 } wl)
            return wl.Where(kv => kv.Value > 0 && !c.Blocked(kv.Key.ToString())).ToDictionary(kv => kv.Key, kv => kv.Value);
        return FilterConsumables(cache.Where(kv => !c.Blocked(kv.Key.ToString())).ToDictionary(kv => kv.Key, kv => kv.Value), kind, c);
    }

    /// <summary>
    /// SPT 소모품 캐시(역할별 공용, 그 역할의 첫 봇 목록으로 한 번만 만들어짐)를 이 봇 티어에 맞춘다(1.0 점검: T1 봇에 AFAK가 쏟아짐).
    /// kind 종류에 표 직접 목록이 있으면 그 종류는 그 목록(무게&gt;0, 막힌 것·직접 티어 위 제외)으로 바꾸고,
    /// 없으면 의약품류마다 같은 종류 안 가격 티어가 봇 티어 위인 것을 뺀다(그 종류가 다 빠지면 가장 낮은 티어만 남김). 의약품류가 아닌 것은 그대로.
    /// </summary>
    public static Dictionary<MongoId, double> FilterConsumables(Dictionary<MongoId, double> pool, string? kind, GearContext c)
    {
        var table = c.Settings.TableFor(c.Pick.RoleId, c.Pick.Tier);
        string? KindOf(MongoId m) => c.Info(m.ToString()) is { Section: "meds" } i ? i.Slot : null;
        int TierOf(MongoId m) => c.TierOf("meds/" + KindOf(m), m.ToString());
        var res = new Dictionary<MongoId, double>(pool);
        if (kind != null && table.Pools.TryGetValue("meds/" + kind, out var own))
        {
            // 직접 목록이면 캐시 전체를 그 목록으로 바꾼다(SPT 음료 캐시는 음식 목록으로 만들어져 다른 종류가 섞인다, Codex 6차 3번).
            res.Clear();
            foreach (var (id, w) in own)
                if (w > 0 && !c.Blocked(id) && c.Info(id) is { Section: "meds" } i && i.Slot == kind
                    && !(c.Settings.ItemTier.ContainsKey(id) && c.TierOf("meds/" + kind, id) > c.Pick.Tier))
                    res[id] = w;
            return res;
        }
        foreach (var grp in res.Keys.Where(m => KindOf(m) != null).GroupBy(KindOf).ToList())
        {
            var list = grp.ToList();
            var over = list.Where(m => TierOf(m) > c.Pick.Tier).ToList();
            if (over.Count < list.Count) { foreach (var m in over) res.Remove(m); continue; }
            int lo = list.Min(TierOf);
            foreach (var m in list.Where(m => TierOf(m) > lo)) res.Remove(m);
        }
        return res;
    }

    /// <summary>부착물 티어(같은 종류 안 가격 순위, 직접 지정 우선). 부착물이 아니면 0.</summary>
    private static int PartTier(string id, GearContext c) => c.Info(id) is { Section: "parts" } i ? c.TierOf("parts/" + i.Slot, id) : 0;

    /// <summary>부착물 티어 규칙: 봇 티어보다 높은 부착물은 뺀다. 부착물 표에 직접 허용(무게&gt;0)한 것은 직접 티어를 정한 경우에만 티어로 막는다.</summary>
    private static bool AboveTier(ItemInfo info, string id, double? weight, GearContext c)
    {
        if (!c.Settings.PartTiers || info.Section != "parts") return false;
        // 1.1.0(#74): 탄창은 가격 티어로 빼지 않는다(대용량은 무기 화면의 대용량 탄창 제한이 맡음). 비싼 전용 탄창만 빠지면
        // 그 탄창만 맞는 개머리판(M700 Hogue → Wyatt, 모신 Archangel → Promag)이 골라졌을 때 총이 탄창 없이 나왔다.
        if (info.Slot == "magazine" && !c.Settings.ItemTier.ContainsKey(id)) return false;
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

    /// <summary>범위가 좁아 후보가 이만큼보다 적으면 바로 아래 티어를 하나씩 더한다(스캐브·레이더처럼 원래 목록이 작은 봇이 한 총만 드는 일 방지, 1차 하네스).</summary>
    internal static int MinRangeCandidates = 3;

    /// <summary>장비 고르는 범위의 가장 낮은 티어(0기반): exact = 봇 티어, near = 한 단계 아래, down = TierDown 단계 아래(1.1.6).</summary>
    public static int RangeLow(string tierRange, int tier, int down) =>
        Math.Max(0, tierRange switch { "near" => tier - 1, "down" => tier - down, _ => tier });

    /// <summary>1.1.6 #106: 표의 이 장비 칸 목록에 모드 아이템이 하나라도 있는지(무게 0 포함).</summary>
    internal static bool TableHasMod(GearTable table, string slot, GearItems items) =>
        table.Pools.Values.Any(p => p.Keys.Any(id => items.Info.TryGetValue(id, out var i) && i.Mod && i.EquipmentSlots.Contains(slot)));

    internal static void RangeFilter(Dictionary<MongoId, double> pool, string slot, GearTable table, GearContext c, bool onlyHere = true)
    {
        int lo = RangeLow(c.TierRange, c.Pick.Tier, c.Role.TierDown);
        var below = pool.Where(kv => kv.Value > 0).Select(kv => kv.Key)
            .Where(m => c.Info(m.ToString()) is { } inf && GearPools.Weight(table, inf, m.ToString(), slot, onlyHere) == null && c.TierOf(slot, m.ToString()) < lo).ToList();
        if (below.Count == 0) return;
        int kept = pool.Count(kv => kv.Value > 0) - below.Count;
        // 아래 티어를 가까운 것부터 묶음째 되살려 후보가 MinRangeCandidates 이상(또는 하나라도 있게) 되게 한다.
        var back = new HashSet<MongoId>();
        foreach (var grp in below.GroupBy(m => c.TierOf(slot, m.ToString())).OrderByDescending(gp => gp.Key))
        {
            if (kept >= MinRangeCandidates) break;
            foreach (var m in grp) back.Add(m);
            kept += grp.Count();
        }
        foreach (var m in below) if (!back.Contains(m)) pool.Remove(m);
    }

    internal static void DropWrongCaliberMags(HashSet<MongoId> set, string weaponId, IReadOnlyDictionary<MongoId, TemplateItem> tpls)
    {
        if (!tpls.TryGetValue(new MongoId(weaponId), out var w) || w.Properties?.Chambers is not { } ch) return;
        var ok = ch.SelectMany(x => x.Properties?.Filters ?? []).SelectMany(f => f.Filter ?? []).ToHashSet();
        if (ok.Count == 0) return;
        bool Fits(MongoId m) => !tpls.TryGetValue(m, out var mt) || mt.Properties?.Cartridges is not { } cs
            || cs.SelectMany(x => x.Properties?.Filters ?? []).SelectMany(f => f.Filter ?? []).Any(ok.Contains)
            || !cs.SelectMany(x => x.Properties?.Filters ?? []).SelectMany(f => f.Filter ?? []).Any();
        var bad = set.Where(m => !Fits(m)).ToList();
        if (bad.Count > 0 && bad.Count < set.Count) foreach (var m in bad) set.Remove(m);
    }

    /// <summary>판 칸이 없거나(소프트 방탄·일반 리그), 앞·뒤판 칸마다 [FMin,FMax] 안 판을 하나 이상 받고 옆판 칸마다 [SMin,Side] 안 판을 받으면 true.</summary>
    internal static bool PlateRangeFits(MongoId carrier, TierCaps caps, IReadOnlyDictionary<MongoId, TemplateItem> tpls, GearContext c)
    {
        if (!tpls.TryGetValue(carrier, out var t) || t.Properties?.Slots == null) return true;
        foreach (var slot in t.Properties.Slots)
        {
            var n = slot.Name?.ToLowerInvariant();
            bool front = n is "front_plate" or "back_plate", side = n is "left_side_plate" or "right_side_plate";
            if (!front && !side) continue;
            int lo = front ? caps.FMin : caps.SMin, hi = front ? caps.FMax : caps.Side;
            bool any = false, anyPlate = false;
            foreach (var f in slot.Properties?.Filters ?? [])
                foreach (var p in f.Filter ?? [])
                {
                    var info = c.Info(p.ToString());
                    if (info == null || !info.Plate) continue;
                    anyPlate = true;
                    if (info.ArmorClass >= lo && info.ArmorClass <= hi) { any = true; break; }
                }
            if (anyPlate && !any) return false;
        }
        return true;
    }

    /// <summary>1.1.0: 판 목록을 이 등급 이하로. 다 빠지면 가장 낮은 등급 판만 남긴다.</summary>
    internal static void CapPlateClass(HashSet<MongoId> set, double max, GearItems items)
    {
        double Cls(MongoId m) => items.Info.TryGetValue(m.ToString(), out var i) ? i.ArmorClass : 0;
        var keep = set.Where(m => Cls(m) <= max).ToList();
        if (keep.Count == 0) { var low = set.Min(Cls); keep = set.Where(m => Cls(m) == low).ToList(); }
        set.IntersectWith(keep);
    }

    internal static void ApplyRange(Dictionary<EquipmentSlots, Dictionary<MongoId, double>> equipment, GearContext c)
    {
        var (fl, fs) = c.Settings.Weapons.RangeFactors(c.Map);
        if (fl == 1 && fs == 1) return;
        foreach (var slot in new[] { EquipmentSlots.FirstPrimaryWeapon, EquipmentSlots.SecondPrimaryWeapon })
        {
            if (!equipment.TryGetValue(slot, out var pool) || pool == null || pool.Count == 0) continue;
            var next = pool.ToDictionary(kv => kv.Key, kv => kv.Value * (WeaponExtras.RangeKind(c.Info(kv.Key.ToString())?.Slot) switch
            {
                "long" => fl,
                "short" => fs,
                _ => 1.0
            }));
            if (next.Values.All(v => v <= 0)) continue;
            pool.Clear();
            foreach (var (k, v) in next) if (v > 0) pool[k] = v;
        }
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
            // 1.1.0(#64): SPT는 마운트 칸(mod_mount·mod_mount_000)에 마운트가 붙어야 그 아래 조준경 칸을 100%로 올린다.
            // 마운트 굴림이 조준경 확률보다 낮으면 AK·볼트액션처럼 마운트가 있어야 조준경을 다는 총에서 조준경이 빠지므로
            // 조준경을 받을 수 있는 두 칸(SPT _scopeIds)은 조준경 확률 이상으로 굴린다.
            double scopeChance = g.ForceScope ? 100 : Of("scope");
            foreach (var key in new[] { "mod_mount", "mod_mount_000" })
                wm[key] = Math.Max(wm.GetValueOrDefault(key, Of("mount")), scopeChance);
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
            // SPT AdjustGenerationChances는 bot.json 설정의 화이트리스트를 봇 복제본에 참조째 넣고, 전리품 캐시도 첫 봇의 목록을 참조로 보관한다.
            // 병렬 봇 생성에서 공용 사전을 고치면 사전이 깨져(Count 음수) 그 역할 봇 생성이 서버 재시작까지 모두 실패하므로 이 봇 전용 사본만 고친다.
            else if (gd.Whitelist != null)
                gd.Whitelist = new Dictionary<MongoId, double>(gd.Whitelist);
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
