using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gravity.Core;

// 0.5 장비 모듈 설정. 기본값은 웹 makeDefaults()·roleDefaults()·tierDataDefaults()·lvDefaults()와 같아야 한다.

/// <summary>티어 하나의 상한·범위(웹 gear.tiers[i]). 확률은 %, 등급은 방탄 등급.</summary>
public sealed record TierCaps(int Min, int Max, double Pen, double Spare, int FMin, int FMax, int Side,
    double AMin, double AMax, double WMin, double WMax, int Mags, double LongScope, double HelmetAcc, int Meds, int Nades);

/// <summary>봇 레벨 기준(웹 gear.lv). Pick: tier(내 티어마다 폭)·flat(단순 폭)·fixed(고정 구간).</summary>
public sealed record LvRules(string Pick, int Below, int Above, int FixMin, int FixMax,
    (int B, int A)[] Deltas, bool ScavSame, (int B, int A)[] ScavDeltas, double[][] Weights);

/// <summary>봇 종류 하나(웹 roles.&lt;id&gt;).</summary>
public sealed record RoleGear(bool On, string Mode, int Tier, int Offset, double FrontPlate, double SidePlate,
    double WDurMin, double WDurMax, double ADurMin, double ADurMax, bool Loot, double Poverty);

/// <summary>티어 편집기 "개수·확률" 표 한 칸(웹 tierData.&lt;키&gt;[티어]의 gen·chance·mods).</summary>
public sealed record GearTable(IReadOnlyDictionary<string, (int Min, int Max)> Gen,
    IReadOnlyDictionary<string, double> Chance, IReadOnlyDictionary<string, double> Mods);

public sealed record BlacklistEntry(string Id, IReadOnlyList<string> Bots);

public sealed record GearSettings(
    bool Module, string TierMode, int FixedTier, int StartTier, LvRules Lv, IReadOnlyList<TierCaps> Tiers,
    IReadOnlyDictionary<string, RoleGear> Roles,
    bool ThermalsT7, bool NoQuest,
    bool ForceStock, bool ForceDust, bool ForceScope, bool ForceMuzzle, double NightNvg, double NightThermal,
    bool ModSubset, int ModWeapons, int ModAttachments, int ModEquipment, double ModMult, bool ModPmcOnly,
    IReadOnlyList<BlacklistEntry> Blacklist, double ScavPenCap, bool BlockQuest,
    IReadOnlyDictionary<string, GearTable[]> Tables)
{
    public RoleGear RoleFor(string id) => Roles.TryGetValue(id, out var r) ? r : GearDefaults.Role(id);

    /// <summary>봇의 표: 그 봇 전용 표가 켜져 있으면 그것, 아니면 묶음 표, 없으면 기본식.</summary>
    public GearTable TableFor(string roleId, int tier)
    {
        if (Tables.TryGetValue(roleId, out var own) && tier < own.Length) return own[tier];
        var g = GearDefaults.GroupOf(roleId);
        return Tables.TryGetValue(g, out var gt) && tier < gt.Length ? gt[tier] : GearDefaults.Table(g, tier);
    }
}

public static class GearDefaults
{
    public static readonly string[] Groups = ["pmc", "scavs", "boss", "follower", "special", "faction"];

    public static string GroupOf(string roleId) => roleId switch
    {
        "usec" or "bear" => "pmc",
        "scav" or "marksman" or "pscav" => "scavs",
        _ when roleId.StartsWith("boss_") => "boss",
        _ when roleId.StartsWith("f_") => "follower",
        "ruaf" or "untar" or "blackdiv" or "wedge" => "faction",
        _ => "special"
    };

    public static RoleGear Role(string id)
    {
        bool pmc = id is "usec" or "bear", boss = id.StartsWith("boss_"), fol = id.StartsWith("f_"),
            fac = id is "ruaf" or "untar" or "blackdiv" or "wedge";
        int tier = boss ? 6 : fol ? 5 : fac ? 5 : id is "scav" or "marksman" ? 2 : id == "pscav" ? 3 : 4;
        return new RoleGear(pmc, pmc ? "follow" : "fixed", tier, 0,
            pmc ? 90 : boss ? 100 : fol ? 85 : 40, pmc ? 35 : boss ? 60 : fol ? 30 : 10,
            pmc ? 75 : 60, 100, pmc ? 70 : 50, 100, true, pmc ? 5 : 0);
    }

    private static readonly int[][] TierRows =
    [
        [1, 10, 20, 15, 2, 3, 2, 60, 85, 2, 20, 1, 1], [11, 20, 28, 22, 3, 3, 2, 65, 90, 3, 30, 2, 1],
        [21, 30, 35, 28, 3, 4, 3, 70, 95, 3, 40, 2, 2], [31, 40, 42, 35, 4, 5, 3, 75, 100, 4, 50, 3, 2],
        [41, 50, 50, 42, 4, 5, 4, 80, 100, 4, 60, 3, 3], [51, 65, 58, 50, 5, 6, 4, 85, 100, 5, 70, 4, 3],
        [66, 100, 65, 58, 5, 6, 5, 90, 100, 5, 80, 4, 4]
    ];

    public static TierCaps Tier(int i)
    {
        var t = TierRows[i];
        return new TierCaps(t[0], t[1], t[2], t[3], t[4], t[5], t[6], t[7], t[8], t[7] - 5, t[8], t[9], t[10], Math.Min(90, t[10] + 10), t[11], t[12]);
    }

    public static double[][] MixWeights(double n1, double n2) =>
        Enumerable.Range(0, 7).Select(i => Enumerable.Range(0, 7).Select(j => Math.Abs(i - j) switch { 0 => 100.0, 1 => n1, 2 => n2, _ => 0.0 }).ToArray()).ToArray();

    public static LvRules Lv() => new("tier", 10, 5, 15, 45,
        [(8, 4), (10, 5), (12, 6), (15, 8), (20, 10), (25, 12), (30, 15)], false,
        new[] { 10, 12, 15, 18, 20, 25, 30 }.Select(b => (b, 0)).ToArray(), MixWeights(35, 10));

    // 웹 GEN·CHSLOTS·MODCH: [티어1 값, 티어7 값]. GEN은 [최소, 최대] 쌍.
    public static readonly (string Key, int LoMin, int LoMax, int HiMin, int HiMax)[] Gen =
    [
        ("magazines", 1, 3, 3, 5), ("meds", 0, 1, 2, 4), ("stims", 0, 0, 1, 2), ("grenades", 0, 1, 2, 4), ("food", 0, 1, 1, 2),
        ("drink", 0, 1, 1, 2), ("backpackLoot", 0, 3, 4, 10), ("pocketLoot", 0, 2, 1, 4), ("vestLoot", 0, 2, 2, 5)
    ];
    public static readonly (string Key, double Lo, double Hi)[] Chance =
    [
        ("headwear", 50, 95), ("earpiece", 15, 85), ("faceCover", 30, 70), ("armorVest", 40, 95),
        ("armouredRig", 10, 60), ("backpack", 40, 90), ("holster", 20, 60), ("eyewear", 20, 60)
    ];
    public static readonly (string Key, double Lo, double Hi)[] ModChance =
    [
        ("scope", 40, 95), ("muzzle", 20, 80), ("tactical", 10, 70), ("foregrip", 20, 75), ("stock", 70, 100), ("mount", 30, 90)
    ];

    /// <summary>묶음의 티어 → 표에 쓰는 실효 티어(1~7). 스캐브는 두 단계 아래, 보스는 한 단계 위.</summary>
    public static int Eff(string group, int tier0) => group switch
    {
        "scavs" => Math.Max(1, tier0 + 1 - 2),
        "boss" => Math.Min(7, tier0 + 1 + 1),
        _ => tier0 + 1
    };

    private static double Lerp(double a, double z, int eff) => Math.Round(a + (z - a) * (eff - 1) / 6.0, MidpointRounding.AwayFromZero);

    public static GearTable Table(string group, int tier0)
    {
        var eff = Eff(group, tier0);
        return new GearTable(
            Gen.ToDictionary(g => g.Key, g => ((int)Lerp(g.LoMin, g.HiMin, eff), (int)Lerp(g.LoMax, g.HiMax, eff))),
            Chance.ToDictionary(c => c.Key, c => Lerp(c.Lo, c.Hi, eff)),
            ModChance.ToDictionary(c => c.Key, c => Lerp(c.Lo, c.Hi, eff)));
    }
}

public static partial class ConfigModel
{
    public static readonly string[] GearRoleIds =
    [
        "usec", "bear", "scav", "marksman", "pscav",
        "boss_reshala", "boss_shturman", "boss_tagilla", "boss_glukhar", "boss_sanitar", "boss_killa", "boss_kaban",
        "boss_kollontay", "boss_zryachiy", "boss_partisan", "boss_goons",
        "f_reshala", "f_glukhar", "f_sanitar", "f_shturman", "f_kaban", "f_kollontay", "f_zryachiy",
        "cultist", "raider", "rogue", "bloodhound", "infected", "ruaf", "untar", "blackdiv", "wedge"
    ];

    internal static GearSettings ParseGear(Reader r)
    {
        const string g = "gear.";
        var tiers = new List<TierCaps>();
        for (int i = 0; i < 7; i++)
        {
            var d = GearDefaults.Tier(i);
            var p = $"{g}tiers.{i}.";
            int fMin = (int)r.Num(p + "fMin", d.FMin, 1, 6);
            double aMin = r.Num(p + "aMin", d.AMin, 1, 100), wMin = r.Num(p + "wMin", d.WMin, 1, 100);
            int lvMin = (int)r.Num(p + "min", d.Min, 1, 100);
            tiers.Add(new TierCaps(lvMin, (int)Math.Max(lvMin, r.Num(p + "max", d.Max, 1, 100)),
                r.Num(p + "pen", d.Pen, 1, 100), r.Num(p + "spare", d.Spare, 1, 100),
                fMin, (int)Math.Max(fMin, r.Num(p + "fMax", d.FMax, 1, 6)), (int)r.Num(p + "side", d.Side, 1, 6),
                aMin, Math.Max(aMin, r.Num(p + "aMax", d.AMax, 1, 100)), wMin, Math.Max(wMin, r.Num(p + "wMax", d.WMax, 1, 100)),
                (int)r.Num(p + "mags", d.Mags, 0, 10), r.Num(p + "longScope", d.LongScope, 0, 100),
                r.Num(p + "helmetAcc", d.HelmetAcc, 0, 100), (int)r.Num(p + "meds", d.Meds, 0, 10), (int)r.Num(p + "nades", d.Nades, 0, 10)));
        }

        var ld = GearDefaults.Lv();
        (int, int)[] Deltas(string p, (int B, int A)[] def) =>
            def.Select((x, i) => ((int)r.Num($"{p}.{i}.b", x.B, 0, 100), (int)r.Num($"{p}.{i}.a", x.A, -30, 100))).ToArray();
        var weights = ld.Weights.Select((row, i) => row.Select((w, j) => r.Num($"{g}lv.weights.{i}.{j}", w, 0, 1000)).ToArray()).ToArray();
        var fixMin = (int)r.Num(g + "lv.fixMin", ld.FixMin, 1, 100);
        var lv = new LvRules(r.Str(g + "lv.pick", "tier", "tier", "flat", "fixed"),
            (int)r.Num(g + "lv.below", ld.Below, 0, 100), (int)r.Num(g + "lv.above", ld.Above, -30, 100),
            fixMin, (int)Math.Max(fixMin, r.Num(g + "lv.fixMax", ld.FixMax, 1, 100)),
            Deltas(g + "lv.deltas", ld.Deltas), r.Bool(g + "lv.scavSame", false), Deltas(g + "lv.scavDeltas", ld.ScavDeltas), weights);

        var roles = new Dictionary<string, RoleGear>();
        foreach (var id in GearRoleIds)
        {
            var d = GearDefaults.Role(id);
            var p = $"roles.{id}.";
            double wd = r.Num(p + "wDurMin", d.WDurMin, 1, 100), ad = r.Num(p + "aDurMin", d.ADurMin, 1, 100);
            roles[id] = new RoleGear(r.Bool(p + "on", d.On), r.Str(p + "mode", d.Mode, "follow", "fixed", "offset"),
                (int)r.Num(p + "tier", d.Tier, 1, 7), (int)r.Num(p + "offset", 0, -3, 3),
                r.Num(p + "frontPlate", d.FrontPlate, 0, 100), r.Num(p + "sidePlate", d.SidePlate, 0, 100),
                wd, Math.Max(wd, r.Num(p + "wDurMax", d.WDurMax, 1, 100)), ad, Math.Max(ad, r.Num(p + "aDurMax", d.ADurMax, 1, 100)),
                r.Bool(p + "loot", true), r.Num(p + "poverty", d.Poverty, 0, 100));
        }

        // 티어 편집기 개수·확률 표: 묶음 표는 늘, 봇 전용 표는 tierOwn.<봇>이 켜졌을 때만.
        var tables = new Dictionary<string, GearTable[]>();
        var keys = GearDefaults.Groups.Concat(GearRoleIds.Where(id => r.Bool("tierOwn." + id, false)));
        foreach (var key in keys)
        {
            if (r.Node("tierData." + key) is not JsonArray) continue;
            var group = GearDefaults.GroupOf(key) is var gg && GearDefaults.Groups.Contains(key) ? key : gg;
            tables[key] = Enumerable.Range(0, 7).Select(t =>
            {
                var d = GearDefaults.Table(group, t);
                var p = $"tierData.{key}.{t}.";
                var gen = d.Gen.ToDictionary(kv => kv.Key, kv =>
                {
                    var mn = (int)r.Num($"{p}gen.{kv.Key}.min", kv.Value.Min, 0, 30);
                    return (mn, (int)Math.Max(mn, r.Num($"{p}gen.{kv.Key}.max", kv.Value.Max, 0, 30)));
                });
                return new GearTable(gen,
                    d.Chance.ToDictionary(kv => kv.Key, kv => r.Num($"{p}chance.{kv.Key}", kv.Value, 0, 100)),
                    d.Mods.ToDictionary(kv => kv.Key, kv => r.Num($"{p}mods.{kv.Key}", kv.Value, 0, 100)));
            }).ToArray();
        }

        var bl = new List<BlacklistEntry>();
        // 칸이 아예 없으면 웹 기본 목록(스캐브 M995, 모두 T-7 열화상 고글·연구소 빨간 카드). 사용자가 비운 배열은 그대로 둔다.
        if (r.Node("blacklist.entries") is null)
            bl.AddRange([new("59e690b686f7746c9f75e848", ["scavs"]), new("5c110624d174af029e69734c", ["all"]), new("5c1d0efb86f7744baf2e7b7b", ["all"])]);
        else if (r.Node("blacklist.entries") is JsonArray ea)
            for (int i = 0; i < ea.Count; i++)
            {
                if (r.Node($"blacklist.entries.{i}.id") is not JsonValue iv || iv.GetValueKind() != JsonValueKind.String) continue;
                var id = iv.GetValue<string>();
                if (id.Length != 24 || !id.All(char.IsAsciiHexDigit)) continue; // 웹 예시 아이템(가짜 id)은 건너뜀
                var bots = r.Node($"blacklist.entries.{i}.bots") is JsonArray ba
                    ? ba.OfType<JsonValue>().Where(v => v.GetValueKind() == JsonValueKind.String).Select(v => v.GetValue<string>()).ToList()
                    : ["all"];
                bl.Add(new BlacklistEntry(id.ToLowerInvariant(), bots.Count > 0 ? bots : ["all"]));
            }

        int fixedTier = (int)r.Num(g + "fixedTier", 4, 1, 7);
        return new GearSettings(r.Bool("modules.gear", true), r.Str(g + "tierMode", "mixed", "level", "mixed", "fixed"),
            fixedTier, (int)r.Num(g + "startTier", 1, 1, 7), lv, tiers, roles,
            r.Bool(g + "thermalsT7", true), r.Bool(g + "noQuest", true),
            r.Bool("weapons.stock", true), r.Bool("weapons.dust", true), r.Bool("weapons.scope", true), r.Bool("weapons.muzzle.force", false),
            r.Num("weapons.night.nvg", 60, 0, 100), r.Num("weapons.night.thermal", 8, 0, 100),
            r.Bool("mods.subset", true), (int)r.Num("mods.weapons", 30, 0, 1000), (int)r.Num("mods.attachments", 150, 0, 5000),
            (int)r.Num("mods.equipment", 30, 0, 1000), r.Num("mods.mult", 100, 0, 200), r.Bool("mods.pmcOnly", true),
            bl, r.Num("blacklist.scavPenCap", 40, 1, 100), r.Bool("blacklist.blockQuest", true), tables);
    }
}
