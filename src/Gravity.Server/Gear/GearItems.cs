namespace Gravity.Gear;

/// <summary>장비 결정에 쓰는 아이템 한 개의 정보(서버 시작 때 DB에서 한 번 뽑음).</summary>
public sealed record ItemInfo(double Price, int ArmorClass, double Pen, bool Quest, bool Mod,
    bool Thermal = false, bool LongScope = false, bool Weapon = false, bool Plate = false, bool PlateSlots = false, double MaxDurability = 0,
    string Cat = "loot");

/// <summary>
/// 아이템 정보와 자동 티어. 장비 칸 티어 = 모든 봇 목록에 그 칸으로 나오는 아이템을 가격순으로 7등분(판정 43).
/// 모드 아이템 = SPT 원본 items.json에 없는 아이템.
/// </summary>
public sealed class GearItems
{
    public static readonly GearItems Empty = new(new Dictionary<string, ItemInfo>(), []);

    public IReadOnlyDictionary<string, ItemInfo> Info { get; }
    /// <summary>장비 칸 이름 → 아이템 id → 티어(0~6).</summary>
    public IReadOnlyDictionary<string, Dictionary<string, int>> SlotTier { get; }
    /// <summary>모드 아이템 묶음(판마다 일부만 고르는 대상).</summary>
    public IReadOnlyList<string> ModWeapons { get; }
    public IReadOnlyList<string> ModEquipment { get; }
    public IReadOnlyList<string> ModAttachments { get; }

    public static readonly string[] WeaponSlots = ["FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster"];

    /// <param name="slotPools">(칸 이름, 아이템 id): 모든 봇 종류의 장비 목록. 부품은 칸 이름 "mods".</param>
    public GearItems(IReadOnlyDictionary<string, ItemInfo> info, IEnumerable<(string Slot, string Id)> slotPools)
    {
        Info = info;
        var bySlot = slotPools.Where(p => info.ContainsKey(p.Id)).GroupBy(p => p.Slot)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Id).Distinct().ToList());
        var tiers = new Dictionary<string, Dictionary<string, int>>();
        foreach (var (slot, ids) in bySlot)
        {
            if (slot == "mods") continue;
            var sorted = ids.OrderBy(id => info[id].Price).ThenBy(id => id, StringComparer.Ordinal).ToList();
            var d = new Dictionary<string, int>();
            for (int i = 0; i < sorted.Count; i++) d[sorted[i]] = Math.Min(6, i * 7 / Math.Max(1, sorted.Count));
            tiers[slot] = d;
        }
        SlotTier = tiers;
        List<string> Mods(Func<string, bool> slotPick) => bySlot.Where(kv => slotPick(kv.Key)).SelectMany(kv => kv.Value)
            .Where(id => info[id].Mod).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
        ModWeapons = Mods(s => WeaponSlots.Contains(s));
        ModEquipment = Mods(s => s != "mods" && !WeaponSlots.Contains(s));
        ModAttachments = Mods(s => s == "mods");
    }

    public int TierOf(string slot, string id) =>
        SlotTier.TryGetValue(slot, out var d) && d.TryGetValue(id, out var t) ? t : 0;
}

/// <summary>판마다 정하는 장비 상태: 이번 판 모드 아이템 묶음. 같은 판 봇은 같은 묶음을 쓴다.</summary>
public sealed class GearRaid
{
    /// <summary>null = 묶음 제한 없음(모든 모드 아이템 허용).</summary>
    public HashSet<string>? Allowed { get; }
    public int Weapons { get; }
    public int Equipment { get; }
    public int Attachments { get; }

    public GearRaid(HashSet<string>? allowed, int w, int e, int a) { Allowed = allowed; Weapons = w; Equipment = e; Attachments = a; }

    public static GearRaid Pick(GearItems items, bool subset, int weapons, int equipment, int attachments, int seed)
    {
        if (!subset) return new GearRaid(null, items.ModWeapons.Count, items.ModEquipment.Count, items.ModAttachments.Count);
        var rnd = new Random(seed ^ 0x6EA2);
        IEnumerable<string> Take(IReadOnlyList<string> all, int n) => all.OrderBy(_ => rnd.Next()).Take(Math.Max(0, n));
        var w = Take(items.ModWeapons, weapons).ToList();
        var e = Take(items.ModEquipment, equipment).ToList();
        var a = Take(items.ModAttachments, attachments).ToList();
        return new GearRaid([.. w, .. e, .. a], w.Count, e.Count, a.Count);
    }

    public bool ModAllowed(string id) => Allowed == null || Allowed.Contains(id);
}
