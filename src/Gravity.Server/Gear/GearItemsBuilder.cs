using System.Text.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Gravity.Gear;

/// <summary>서버 시작 때 SPT DB에서 아이템 정보·자동 티어를 만든다(읽기만).</summary>
public static class GearItemsBuilder
{
    private const string WeaponBase = "5422acb9af1c889c16000029";
    private const string ThermalBase = "5d21f59b6dbe99052b54ef83"; // 열화상 고글(T-7)
    private const string SpecialScope = "55818aeb4bdc2ddc698b456a"; // 열화상·야간 조준경(야간 조준경·야투경은 열화상 아님)
    // 특수 조준경 바로 아래 중 열화상(SPT 데이터에 열화상 표시 칸이 없어 이름으로 가름, 판정 48).
    private static readonly System.Text.RegularExpressions.Regex ThermalName = new("reap|flir|thermal|t12|zeus|ultima|shakhin|torrey", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static readonly string[] LongScopeBases = ["55818ae44bdc2dde698b456c", "55818add4bdc2d5b648b456f", "55818aeb4bdc2ddc698b456a"];
    private static readonly string[] PlateBases = ["644120aa86ffbe10ee032b6f", "65649eb40bf0ed77b8044453"];
    private static readonly HashSet<string> PlateSlotNames = ["front_plate", "back_plate", "left_side_plate", "right_side_plate"];

    /// <summary>SPT 원본 items.json의 아이템 id(모드 아이템 구분용). 못 읽으면 null(모드 구분 끔).</summary>
    public static HashSet<string>? VanillaIds(string? sptDataDir)
    {
        if (sptDataDir == null) return null;
        var p = System.IO.Path.Combine(sptDataDir, "database", "templates", "items.json");
        if (!File.Exists(p)) return null;
        using var fs = File.OpenRead(p);
        using var doc = JsonDocument.Parse(fs);
        return doc.RootElement.EnumerateObject().Select(o => o.Name.ToLowerInvariant()).ToHashSet();
    }

    /// <summary>1.0.5: SPT 원본 customization.json의 외형 id(모드 옷 구분용). 못 읽으면 null(의상 목록 끔).</summary>
    public static HashSet<string>? VanillaCustomizationIds(string? sptDataDir)
    {
        if (sptDataDir == null) return null;
        var p = System.IO.Path.Combine(sptDataDir, "database", "templates", "customization.json");
        if (!File.Exists(p)) return null;
        using var fs = File.OpenRead(p);
        using var doc = JsonDocument.Parse(fs);
        return doc.RootElement.EnumerateObject().Select(o => o.Name.ToLowerInvariant()).ToHashSet();
    }

    public static GearItems Build(TemplateTable templates, BotTable bots, HashSet<string>? vanilla, string? modsDir = null,
        IEnumerable<(string Item, IEnumerable<string> Parts)>? defaultPresets = null, HashSet<string>? vanillaCustomization = null)
    {
        var tpls = templates.Items;
        var prices = templates.Prices;
        var hb = templates.Handbook.Items.Where(h => h.Price.HasValue).GroupBy(h => h.Id.ToString()).ToDictionary(g => g.Key, g => g.First().Price!.Value);
        double PriceOf(string id) => prices.TryGetValue(new MongoId(id), out var pp) ? pp : hb.GetValueOrDefault(id);
        // 총은 몸통만의 가격이 매우 낮다(SCAR-H 2만대 → 자동 T1). 기본 조립(기본 프리셋) 부품 가격을 모두 더한 값을 쓴다.
        var assembled = new Dictionary<string, double>(StringComparer.Ordinal);
        if (defaultPresets != null)
            foreach (var (item, parts) in defaultPresets)
            {
                double sum = 0;
                foreach (var part in parts) sum += PriceOf(part);
                if (sum > assembled.GetValueOrDefault(item)) assembled[item] = sum;
            }
        var parent = tpls.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.Parent.ToString());
        bool IsA(string id, string baseId)
        {
            for (int i = 0; i < 20 && !string.IsNullOrEmpty(id); i++)
            {
                if (id == baseId) return true;
                if (!parent.TryGetValue(id, out var p) || p == id) return false;
                id = p;
            }
            return false;
        }
        var info = new Dictionary<string, ItemInfo>();
        // 1.0.5 의상: 원본에 없는 상의·하의도 같은 한 번의 폴더 검색에서 출처를 찾는다.
        var custom = (templates.Customization ?? new()).Where(kv => kv.Value?.Properties?.BodyPart is "Body" or "Feet")
            .Select(kv => (Id: kv.Key.ToString().ToLowerInvariant(), Part: kv.Value.Properties.BodyPart, Sides: (IEnumerable<string>?)kv.Value.Properties.Side)).ToList();
        var modCustom = vanillaCustomization == null ? [] : custom.Where(c => !vanillaCustomization.Contains(c.Id)).Select(c => c.Id).ToList();
        var sources = GearItemSource.Scan(modsDir, tpls.Keys.Select(k=>k.ToString()).Where(id=>vanilla != null && !vanilla.Contains(id)).Concat(modCustom).ToHashSet());
        var positions = new Dictionary<string,HashSet<string>>();
        foreach (var t in tpls.Values)
            if (t.Properties?.Slots != null)
                foreach (var slot in t.Properties.Slots)
                    if (slot.Name != null && slot.Properties?.Filters != null)
                        foreach (var filter in slot.Properties.Filters)
                            if (filter.Filter != null)
                                foreach (var plate in filter.Filter)
                                {
                                    var id = plate.ToString();
                                    if(!PlateBases.Any(b=>IsA(id,b)))continue;
                                    if (!positions.TryGetValue(id,out var pos)) positions[id] = pos = [];
                                    pos.Add(slot.Name.ToLowerInvariant());
                                }
        foreach (var (mid, t) in tpls)
        {
            var id = mid.ToString();
            var pr = t.Properties;
            var plateSlots = pr?.Slots?.Any(s => s.Name != null && PlateSlotNames.Contains(s.Name.ToLowerInvariant())) ?? false;
            var cls = GearCatalog.Classify(b=>IsA(id,b), plateSlots || (pr?.ArmorClass ?? 0) > 0, pr?.Caliber ?? pr?.AmmoCaliber ?? "");
            double price = prices.TryGetValue(mid, out var p) ? p : hb.GetValueOrDefault(id);
            if (assembled.TryGetValue(id, out var full) && full > price) price = full;
            info[id] = new ItemInfo(price, pr?.ArmorClass ?? 0, pr?.PenetrationPower ?? 0, pr?.QuestItem ?? false,
                vanilla != null && !vanilla.Contains(id),
                Thermal: IsA(id, ThermalBase) || (parent.GetValueOrDefault(id) == SpecialScope && ThermalName.IsMatch(t.Name ?? "")), LongScope: LongScopeBases.Any(b => IsA(id, b)), Weapon: IsA(id, WeaponBase),
                Plate: PlateBases.Any(b => IsA(id, b)),
                PlateSlots: plateSlots,
                MaxDurability: pr?.MaxDurability ?? 0, Cat: cls.Cat,
                RequiredSlots: RequiredOf(pr?.Slots))
            {
                Section = cls.Section, Slot = cls.Slot, EquipmentSlots = cls.Slots,
                PlatePositions = positions.TryGetValue(id,out var pos) ? pos.Order(StringComparer.Ordinal).ToArray() : [],
                Source = GearItemSource.Resolve(id,vanilla,sources), Name = t.Name,
                Caliber = pr?.Caliber ?? pr?.AmmoCaliber ?? "",
                Available = string.Equals(t.Type, "Item", StringComparison.OrdinalIgnoreCase)
            };
        }
        var pools = new List<(string, string)>();
        foreach (var (_, bt) in bots.Types)
        {
            var inv = bt?.BotInventory;
            if (inv == null) continue;
            if (inv.Equipment != null)
                foreach (var (slot, pool) in inv.Equipment)
                    if (pool != null) pools.AddRange(pool.Keys.Select(k => (slot.ToString(), k.ToString())));
            if (inv.Mods != null)
                foreach (var (_, slots) in inv.Mods)
                    if (slots != null)
                        foreach (var (_, set) in slots)
                            if (set != null) pools.AddRange(set.Select(k => ("mods", k.ToString())));
        }
        // 장비 칸은 기존 봇 목록 그대로 가격 7등분한다. 목록 밖 아이템·탄약·부품은 같은 분류로 보충한다.
        foreach (var (id,i) in info.Where(kv=>kv.Value.Available))
        {
            pools.Add((i.Section + "/" + i.Slot,id));
        }
        // 봇 원본 목록에 없던 무기/장비를 직접 추가할 때 필요한 호환 부품 그래프도 시작 때 한 번 만든다.
        var compatibility = new Dictionary<string,IReadOnlyDictionary<string,string[]>>();
        foreach(var(mid,t) in tpls)
        {
            if(t.Properties?.Slots == null)continue;
            var parentId=mid.ToString();
            var slots=new Dictionary<string,string[]>();
            foreach(var slot in t.Properties.Slots)
            {
                if(string.IsNullOrEmpty(slot.Name)||slot.Properties?.Filters==null)continue;
                var filters=slot.Properties.Filters.SelectMany(f=>f.Filter??[]).Select(m=>m.ToString()).ToHashSet();
                // 대부분 필터는 실제 ID다. 부모 클래스 필터만 실제 자식 아이템으로 펼친다(탐색 최대 20단계).
                var exact=filters.Where(id=>info.TryGetValue(id,out var i)&&i.Available).ToHashSet();
                var bases=filters.Where(id=>info.TryGetValue(id,out var i)&&!i.Available).ToArray();
                if(bases.Length>0)
                    exact.UnionWith(info.Where(kv=>kv.Value.Available&&bases.Any(b=>IsA(kv.Key,b))).Select(kv=>kv.Key));
                exact.Remove(parentId);
                slots[slot.Name]=exact.Order(StringComparer.Ordinal).ToArray();
            }
            if(slots.Count>0)compatibility[parentId]=slots;
        }
        return new GearItems(info, pools,compatibility) { Clothing = GearClothing.Build(custom, vanillaCustomization, sources) };
    }

    /// <summary>필수 부품 칸 이름(소문자). 없으면 null(대부분 아이템).</summary>
    private static IReadOnlySet<string>? RequiredOf(IEnumerable<Slot>? slots)
    {
        if (slots == null) return null;
        HashSet<string>? set = null;
        foreach (var s in slots)
            if (s?.Required == true && !string.IsNullOrEmpty(s.Name)) (set ??= new(StringComparer.Ordinal)).Add(s.Name.ToLowerInvariant());
        return set;
    }

    public static int[] ExpTable(GlobalTable globals) =>
        globals.Configuration.Exp.Level.ExperienceTable.Select(e => e.Experience).ToArray();
}
