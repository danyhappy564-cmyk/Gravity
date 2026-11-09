using Gravity.Core;

namespace Gravity.Gear;

/// <summary>모든 장비 화면이 공유하는 DB 카탈로그. 자동 티어와 이름은 재계산 때만 만든다.</summary>
public sealed record GearCatalogEntry(string Id, string Ko, string En, string Cat, string Section, string Slot,
    string GunKind, string[] EquipmentSlots, string Source, bool Mod, int AutoTier, int? ManualTier,
    int ArmorClass, string[] PlateSlots, double Pen, bool Thermal, double Price);

public static class GearCatalog
{
    // SPT 부모 클래스. 클래스 탐색은 Builder에서 최대 20단계로 제한한다.
    public static readonly (string Base, string Kind)[] GunClasses =
    [
        ("5447b5f14bdc2d61278b4567", "ar"), ("5447b5fc4bdc2d87278b4567", "carbine"),
        ("5447b5e04bdc2d62278b4567", "smg"), ("5447bed64bdc2d97278b4568", "lmg"),
        ("5447b6194bdc2d67278b4567", "dmr"), ("5447b6254bdc2dc3278b4568", "sniper"),
        ("5447b6094bdc2dc3278b4567", "shotgun"), ("5447b5cf4bdc2d65278b4567", "holster"),
        ("617f1ef5e8b54b0998387733", "holster"), ("5447bedf4bdc2d87278b4568", "launcher"),
        ("67446d4f04141c10630604e7", "launcher"), ("5447e1d04bdc2dff2f8b4567", "scabbard")
    ];
    public static (string Section, string Slot, string Cat, string[] Slots) Classify(Func<string, bool> isA, bool armoredRig, string caliber)
    {
        foreach (var (b, k) in GunClasses)
            if (isA(b)) return ("weapons", k, "weapon", k == "holster" ? ["Holster"] : k == "scabbard" ? ["Scabbard"] : ["FirstPrimaryWeapon", "SecondPrimaryWeapon"]);
        if (isA("5422acb9af1c889c16000029")) return ("weapons", "special", "weapon", ["FirstPrimaryWeapon", "SecondPrimaryWeapon"]);
        if (isA("5485a8684bdc2da71d8b4567")) return ("ammo", string.IsNullOrEmpty(caliber) ? "otherAmmo" : caliber, "ammo", []);
        foreach (var (b, slot, cat, eq) in Equipment)
            if (isA(b)) return ("armor", slot == "tacticalVest" && armoredRig ? "armouredRig" : slot, cat, [eq]);
        if (isA("644120aa86ffbe10ee032b6f")) return ("plates", "plate", "armor", []);
        if (isA("65649eb40bf0ed77b8044453")) return ("plates", "insert", "armor", []);
        foreach (var (b, slot) in Parts)
            if (isA(b)) return ("parts", slot, slot == "scope" ? "scope" : "gear", []);
        if (isA("5448fe124bdc2da5018b4567")) return ("parts", "other", "gear", []);
        if (isA("5448f3a64bdc2d60728b456a")) return ("meds", "stims", "med", []);
        if (isA("543be5664bdc2dd4348b4569")) return ("meds", "meds", "med", []);
        if (isA("543be6564bdc2df4348b4568")) return ("meds", "grenades", "gear", []);
        if (isA("5448e8d04bdc2ddf718b4569")) return ("meds", "food", "loot", []);
        if (isA("5448e8d64bdc2dce718b4568")) return ("meds", "drink", "loot", []);
        return ("loot", "loot", isA("543be5e94bdc2df1348b4568") ? "key" : "loot", []);
    }
    private static readonly (string Base, string Slot, string Cat, string Equipment)[] Equipment =
    [
        ("5a341c4086f77401f2541505","headwear","helmet","Headwear"),
        ("5448e54d4bdc2dcc718b4568","armorVest","armor","ArmorVest"),
        ("5448e5284bdc2dcb718b4567","tacticalVest","gear","TacticalVest"),
        ("5448e53e4bdc2d60728b4567","backpack","backpack","Backpack"),
        ("5645bcb74bdc2ded0b8b4578","earpiece","gear","Earpiece"),
        ("5a341c4686f77469e155819e","faceCover","gear","FaceCover"),
        ("5448e5724bdc2ddf718b4568","eyewear","gear","Eyewear"),
        ("5b3f15d486f77432d0509248","armband","gear","ArmBand")
    ];
    private static readonly (string Base, string Slot)[] Parts =
    [
        ("55818add4bdc2d5b648b456f","scope"),("55818ae44bdc2dde698b456c","scope"),("55818aeb4bdc2ddc698b456a","scope"),
        ("55818ad54bdc2ddc698b4569","scope"),("55818acf4bdc2dde698b456b","scope"),("55818ac54bdc2d5b648b456e","scope"),
        ("5d21f59b6dbe99052b54ef83","nvg"),("5a2c3a9486f774688b05e574","nvg"),
        ("5448fe394bdc2d0d028b456c","muzzle"),("55818af64bdc2d5b648b4570","foregrip"),
        ("55818a594bdc2db9688b456a","stock"),("5448bc234bdc2d3c308b4569","magazine"),
        ("55818b164bdc2ddc698b456c","tactical"),("55818b0e4bdc2dde698b456e","tactical"),("55818b084bdc2d5b648b4571","tactical"),
        ("55818b224bdc2dde698b456f","mount"),("555ef6e44bdc2de9068b457e","barrel"),("55818a104bdc2db9688b4569","handguard")
    ];
    public static IReadOnlyList<GearCatalogEntry> Build(GearItems items, IReadOnlyDictionary<string,string> ko, IReadOnlyDictionary<string,string> en) =>
        items.Info.Where(kv => kv.Value.Available).OrderBy(kv=>kv.Key, StringComparer.Ordinal).Select(kv =>
        {
            var (id, i) = kv;
            string eng = (en.GetValueOrDefault(id + " Name") ?? i.Name ?? id).Trim();
            return new GearCatalogEntry(id, ko.GetValueOrDefault(id + " Name")?.Trim() ?? eng, eng, i.Cat, i.Section, i.Slot,
                i.Section == "weapons" ? i.Slot : "", i.EquipmentSlots, i.Source, i.Mod,
                items.TierOf(i.EquipmentSlots.FirstOrDefault() ?? i.Section + "/" + i.Slot, id) + 1,
                null, i.ArmorClass, i.PlatePositions, i.Pen, i.Thermal, i.Price);
        }).ToArray();
}
