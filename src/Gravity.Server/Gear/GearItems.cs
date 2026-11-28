namespace Gravity.Gear;

/// <summary>장비 결정에 쓰는 아이템 한 개의 정보(서버 시작 때 DB에서 한 번 뽑음).</summary>
public sealed record ItemInfo(double Price, int ArmorClass, double Pen, bool Quest, bool Mod,
    bool Thermal = false, bool LongScope = false, bool Weapon = false, bool Plate = false, bool PlateSlots = false, double MaxDurability = 0,
    string Cat = "loot", IReadOnlySet<string>? RequiredSlots = null)
{
    public string Section { get; init; } = Weapon ? "weapons" : Pen > 0 ? "ammo" : Plate ? "plates" : "loot";
    public string Slot { get; init; } = "special";
    public string[] EquipmentSlots { get; init; } = [];
    public string[] PlatePositions { get; init; } = [];
    public string Source { get; init; } = "vanilla";
    public string? Name { get; init; }
    public bool Available { get; init; } = true;
    public string Caliber { get; init; } = "";
}

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
    /// <summary>자동 티어 근거: 칸마다 가격 순위(0부터)와 그 칸의 아이템 수.</summary>
    public IReadOnlyDictionary<string, Dictionary<string, (int Rank, int Size)>> SlotRank { get; }
    public IReadOnlyDictionary<string,string[]> PoolEquipment { get; }
    public IReadOnlyDictionary<string,IReadOnlyDictionary<string,string[]>> CompatibleMods { get; }
    /// <summary>모드 아이템 묶음(판마다 일부만 고르는 대상).</summary>
    public IReadOnlyList<string> ModWeapons { get; }
    public IReadOnlyList<string> ModEquipment { get; }
    public IReadOnlyList<string> ModAttachments { get; }
    /// <summary>1.1.0(#73): 기타 모드 아이템(의약품·수류탄·음식·전리품). 탄약은 무기를 따라가므로 판마다 고르지 않는다.</summary>
    public IReadOnlyList<string> ModOther { get; } = [];
    /// <summary>1.0.5 의상 탭: 모드 옷(Body·Feet) 목록.</summary>
    public IReadOnlyList<ClothingItem> Clothing { get; init; } = [];
    /// <summary>1.1.5 의상 레벨: SPT 원본 래그맨 의상(suits.json)이 쓰는 Body·Feet id → 그 의상 요구 프로필 레벨(가장 낮은 값). 모드 옷은 없다.</summary>
    public IReadOnlyDictionary<string, int> ClothingLevel { get; init; } = new Dictionary<string, int>();
    /// <summary>1.1.5 모드 옷 티어 규칙(옷 id → 규칙). 상인 판매 자료(모드가 넣은 Suits)로 정한다.</summary>
    public IReadOnlyDictionary<string, ModClothRule> ModClothRules { get; init; } = new Dictionary<string, ModClothRule>();
    /// <summary>1.0.9: 봇 원본 표의 부품 목록(부모 id → 칸 → 후보). USEC·BEAR 표가 먼저, 거기 없는 부모만 나머지 봇 표 합집합.
    /// 봇 표에 없던 총을 넣을 때 "맞는 부품 전부"(CompatibleMods) 대신 쓴다(GearCompatibility.AddMissing).</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string[]>> DonorMods { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, string[]>>();

    public static readonly string[] WeaponSlots = ["FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster"];

    /// <param name="slotPools">(칸 이름, 아이템 id): 모든 봇 종류의 장비 목록. 부품은 칸 이름 "mods".</param>
    public GearItems(IReadOnlyDictionary<string, ItemInfo> info, IEnumerable<(string Slot, string Id)> slotPools,
        IReadOnlyDictionary<string,IReadOnlyDictionary<string,string[]>>? compatibleMods = null)
    {
        Info = info;
        CompatibleMods = compatibleMods ?? new Dictionary<string,IReadOnlyDictionary<string,string[]>>();
        PoolEquipment = info.Values.GroupBy(i=>i.Section+"/"+i.Slot).ToDictionary(g=>g.Key,g=>g.SelectMany(i=>i.EquipmentSlots).Distinct().ToArray());
        var bySlot = slotPools.Where(p => info.ContainsKey(p.Id)).GroupBy(p => p.Slot)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Id).Distinct().ToList());
        var tiers = new Dictionary<string, Dictionary<string, int>>();
        var ranks = new Dictionary<string, Dictionary<string, (int, int)>>();
        foreach (var (slot, ids) in bySlot)
        {
            var sorted = ids.OrderBy(id => info[id].Price).ThenBy(id => id, StringComparer.Ordinal).ToList();
            var d = new Dictionary<string, int>();
            var r = new Dictionary<string, (int, int)>();
            for (int i = 0; i < sorted.Count; i++) { d[sorted[i]] = Math.Min(6, i * 7 / Math.Max(1, sorted.Count)); r[sorted[i]] = (i, sorted.Count); }
            tiers[slot] = d; ranks[slot] = r;
        }
        SlotTier = tiers; SlotRank = ranks;
        List<string> Mods(Func<string, bool> slotPick) => bySlot.Where(kv => slotPick(kv.Key)).SelectMany(kv => kv.Value)
            .Where(id => info[id].Mod).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
        ModWeapons = Mods(s => WeaponSlots.Contains(s)).Concat(info.Where(kv=>kv.Value.Available&&kv.Value.Mod&&kv.Value.Section=="weapons").Select(kv=>kv.Key)).Distinct().Order(StringComparer.Ordinal).ToArray();
        // PackNStrap 벨트는 판마다 고르는 모드 장비 묶음에 넣지 않는다(벨트 설정이 따로 정한다, 1.0.7).
        ModEquipment = Mods(s => s != "mods" && !WeaponSlots.Contains(s) && !s.Contains('/')).Concat(info.Where(kv=>kv.Value.Available&&kv.Value.Mod&&kv.Value.Section=="armor").Select(kv=>kv.Key))
            .Where(id => info[id].Slot != "belt").Distinct().Order(StringComparer.Ordinal).ToArray();
        ModAttachments = Mods(s => s == "mods").Concat(info.Where(kv=>kv.Value.Available&&kv.Value.Mod&&kv.Value.Section is "parts" or "plates").Select(kv=>kv.Key)).Distinct().Order(StringComparer.Ordinal).ToArray();
        ModOther = info.Where(kv => kv.Value.Mod && kv.Value.Available && kv.Value.Section is "meds" or "loot").Select(kv => kv.Key).Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>TierOf가 실제로 쓴 비교 묶음과 그 안의 가격 순위. 없으면 null.</summary>
    public (string Group, int Rank, int Size)? TierBasis(string slot, string id)
    {
        if (SlotRank.TryGetValue(slot, out var d) && d.TryGetValue(id, out var r)) return (slot, r.Rank, r.Size);
        if (Info.TryGetValue(id, out var i) && SlotRank.TryGetValue(i.Section + "/" + i.Slot, out var byKind) && byKind.TryGetValue(id, out r)) return (i.Section + "/" + i.Slot, r.Rank, r.Size);
        return null;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> _modFor = new();
    /// <summary>1.1.6 #106: 이 장비 칸에 들어가는 쓸 수 있는 모드 아이템(모드 자동 끼우기 후보).</summary>
    public string[] ModFor(string slot) => _modFor.GetOrAdd(slot, s => Info.Where(kv => kv.Value.Mod && kv.Value.Available && kv.Value.Section != "ammo" && kv.Value.EquipmentSlots.Contains(s)).Select(kv => kv.Key).Order(StringComparer.Ordinal).ToArray());

    public int TierOf(string slot, string id)
    {
        if(SlotTier.TryGetValue(slot,out var d)&&d.TryGetValue(id,out var t))return t;
        return Info.TryGetValue(id,out var i)&&SlotTier.TryGetValue(i.Section+"/"+i.Slot,out var byKind)&&byKind.TryGetValue(id,out t)?t:0;
    }
}

/// <summary>판마다 정하는 장비 상태: 이번 판 모드 아이템 묶음. 같은 판 봇은 같은 묶음을 쓴다.</summary>
public sealed class GearRaid
{
    /// <summary>null = 묶음 제한 없음(모든 모드 아이템 허용).</summary>
    public HashSet<string>? Allowed { get; }
    public int Weapons { get; }
    public int Equipment { get; }
    public int Attachments { get; }
    /// <summary>1.1.0: 뽑은 모드 무기가 꼭 달아야 하는 모드 부품(필수 칸)으로 자동 포함한 수.</summary>
    public int Required { get; init; }
    public int Other { get; init; }
    /// <summary>1.1.0: 이번 판 모드 의상(id). null = 의상 수 제한 없음.</summary>
    public HashSet<string>? Clothing { get; init; }

    public GearRaid(HashSet<string>? allowed, int w, int e, int a) { Allowed = allowed; Weapons = w; Equipment = e; Attachments = a; }

    /// <summary>1.0.x 호출 모양: 기타 0개, 의상 제한 없음.</summary>
    public static GearRaid Pick(GearItems items, bool subset, int weapons, int equipment, int attachments, int seed)
        => Pick(items, subset, weapons, equipment, attachments, 0, -1, seed);

    /// <param name="clothing">이번 판 모드 의상 수. 음수 = 제한 없음.</param>
    public static GearRaid Pick(GearItems items, bool subset, int weapons, int equipment, int attachments, int other, int clothing, int seed)
    {
        if (!subset) return new GearRaid(null, items.ModWeapons.Count, items.ModEquipment.Count, items.ModAttachments.Count) { Other = items.ModOther.Count };
        var rnd = new Random(seed ^ 0x6EA2);
        IEnumerable<string> Take(IReadOnlyList<string> all, int n) => all.OrderBy(_ => rnd.Next()).Take(Math.Max(0, n));
        var w = Take(items.ModWeapons, weapons).ToList();
        var e = Take(items.ModEquipment, equipment).ToList();
        // 1.1.0(#73): 뽑은 모드 무기·장비의 필수 칸 모드 부품은 개수와 별도로 넣는다 — 없으면 수신기·총열이 빠진 총이 되거나 총이 빠졌다.
        var need = RequiredMods(items, w.Concat(e), rnd);
        var a = Take(items.ModAttachments.Where(x => !need.Contains(x)).ToList(), attachments).ToList();
        var o = Take(items.ModOther, other).ToList();
        HashSet<string>? cl = clothing < 0 ? null : Take(items.Clothing.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList(), clothing).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new GearRaid([.. w, .. e, .. need, .. a, .. o], w.Count, e.Count, a.Count) { Required = need.Count, Other = o.Count, Clothing = cl };
    }

    /// <summary>
    /// 부모들의 필수 칸(RequiredSlots)을 채울 모드 부품을 모은다(봇 표 → 호환 목록 순). 바닐라 후보가 있는 칸은 바닐라로 채워지므로 넣지 않고,
    /// 모드 후보만 있는 칸은 칸마다 최대 <paramref name="perSlot"/>개를 골라 그 부품의 필수 칸도 따라간다(1.1.0 실측: 다 넣으면 무기 15개에 446개).
    /// </summary>
    public static HashSet<string> RequiredMods(GearItems items, IEnumerable<string> roots, Random? rnd = null, int perSlot = 2)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var q = new Queue<string>(roots);
        while (q.Count > 0 && found.Count < 2000)
        {
            var id = q.Dequeue();
            if (!seen.Add(id)) continue;
            var req = items.Info.GetValueOrDefault(id)?.RequiredSlots;
            if (req == null || req.Count == 0) continue;
            items.DonorMods.TryGetValue(id, out var donor);
            items.CompatibleMods.TryGetValue(id, out var compat);
            foreach (var slot in req)
            {
                string[]? cands = donor != null && donor.TryGetValue(slot, out var d) && d.Length > 0 ? d : compat != null && compat.TryGetValue(slot, out var c) ? c : null;
                if (cands == null || cands.Any(x => items.Info.TryGetValue(x, out var vi) && !vi.Mod)) continue;
                var mods = cands.Where(x => items.Info.TryGetValue(x, out var xi) && xi.Mod).ToList();
                if (mods.Any(found.Contains)) continue; // 다른 부모가 이미 이 칸 후보를 넣음
                foreach (var x in (rnd == null ? mods : mods.OrderBy(_ => rnd.Next()).ToList()).Take(perSlot))
                    if (found.Add(x)) q.Enqueue(x);
            }
        }
        return found;
    }

    public bool ModAllowed(string id) => Allowed == null || Allowed.Contains(id);
}
