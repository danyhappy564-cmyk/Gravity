using System.Text.Json;
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

    public static GearItems Build(TemplateTable templates, BotTable bots, HashSet<string>? vanilla)
    {
        var tpls = templates.Items;
        var prices = templates.Prices;
        var hb = templates.Handbook.Items.Where(h => h.Price.HasValue).GroupBy(h => h.Id.ToString()).ToDictionary(g => g.Key, g => g.First().Price!.Value);
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
        foreach (var (mid, t) in tpls)
        {
            var id = mid.ToString();
            var pr = t.Properties;
            double price = prices.TryGetValue(mid, out var p) ? p : hb.GetValueOrDefault(id);
            info[id] = new ItemInfo(price, pr?.ArmorClass ?? 0, pr?.PenetrationPower ?? 0, pr?.QuestItem ?? false,
                vanilla != null && !vanilla.Contains(id),
                Thermal: IsA(id, ThermalBase) || (parent.GetValueOrDefault(id) == SpecialScope && ThermalName.IsMatch(t.Name ?? "")), LongScope: LongScopeBases.Any(b => IsA(id, b)), Weapon: IsA(id, WeaponBase),
                Plate: PlateBases.Any(b => IsA(id, b)),
                PlateSlots: pr?.Slots?.Any(s => s.Name != null && PlateSlotNames.Contains(s.Name.ToLowerInvariant())) ?? false,
                MaxDurability: pr?.MaxDurability ?? 0, Cat: Category(id, IsA));
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
        return new GearItems(info, pools);
    }

    // 웹 블랙리스트 분류(ICAT)와 같은 이름.
    private static readonly (string Base, string Cat)[] Cats =
    [
        (WeaponBase, "weapon"), ("5485a8684bdc2da71d8b4567", "ammo"), ("55818b164bdc2ddc698b456c", "scope"),
        ("55818ae44bdc2dde698b456c", "scope"), ("55818add4bdc2d5b648b456f", "scope"), ("55818aeb4bdc2ddc698b456a", "scope"),
        ("55818ad54bdc2ddc698b4569", "scope"), ("55818acf4bdc2dde698b456b", "scope"),
        ("5a341c4086f77401f2541505", "helmet"), ("5448e54d4bdc2dcc718b4568", "armor"), ("644120aa86ffbe10ee032b6f", "armor"),
        ("5448e5284bdc2dcb718b4567", "gear"), ("5448e53e4bdc2d60728b4567", "backpack"), ("543be5e94bdc2df1388b4569", "key"),
        ("543be5664bdc2dd4348b4569", "med"), ("5448f3a64bdc2d60728b456a", "med"), ("57864a3d24597754843f8721", "gear"),
        ("5a341c4686f77469e155819e", "gear"), ("5645bcb74bdc2ded0b8b4578", "gear"), ("5448e5724bdc2ddf718b4568", "gear")
    ];

    private static string Category(string id, Func<string, string, bool> isA)
    {
        foreach (var (b, c) in Cats) if (isA(id, b)) return c;
        return "loot";
    }

    public static int[] ExpTable(GlobalTable globals) =>
        globals.Configuration.Exp.Level.ExperienceTable.Select(e => e.Experience).ToArray();
}
