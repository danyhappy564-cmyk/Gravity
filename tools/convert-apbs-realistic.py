"""APBS Realistic and Extended PMC Loadouts Preset(제작 mip01)의 값을 Gravity 장비 프리셋으로 옮깁니다.

원작 값은 고치지 않고 그대로 옮깁니다. 모드 아이템은 넣지 않습니다(바닐라 SPT 아이템만).
Gravity에 같은 기능이 없는 항목(맵별 근거리/원거리 비율, 등급별 방탄판 무게, 총 종류별 부품 확률, 외형)은
가장 가까운 값으로 바꾸고, 결과 파일 conversion.approximations에 적어 둡니다.

python tools/convert-apbs-realistic.py --source <Presets/realistic 폴더> --config <에드온 config.json>
  --tiers <APBS Data/Tiers/TierData.json> --catalog <api/gear/catalog JSON>
  --out src/Gravity.Server/wwwroot/presets/apbs-realistic-mip01.json
"""
import argparse
import json
from collections import defaultdict
from pathlib import Path

CREDITS = {
    "preset": "APBS Realistic and Extended PMC Loadouts Preset",
    "presetAuthor": "mip01",
    "base": "Acid's Progressive Bot System (APBS)",
    "baseAuthor": "acidphantasm",
    "note": "mip01님이 원작 APBS 프리셋 제작자의 허락을 받아 만든 에드온입니다. 값은 고치지 않고 Gravity 형식으로만 옮겼습니다.",
    "noteEn": "Made by mip01 with permission from the original APBS preset author. Values are carried over unchanged, only reformatted for Gravity.",
}

# Gravity 봇 종류 → 원작 파일의 봇 키. 원작에 없는 종류(팩션 모드 봇, 감염자)는 넣지 않는다(Gravity 기본 그대로).
ROLES = {
    "usec": "pmcUSEC", "bear": "pmcBEAR", "scav": "scav", "marksman": "scav", "pscav": "scav",
    "boss_reshala": "bossbully", "boss_shturman": "bosskojaniy", "boss_tagilla": "bosstagilla", "boss_glukhar": "bossgluhar",
    "boss_sanitar": "bosssanitar", "boss_killa": "bosskilla", "boss_kaban": "bossboar", "boss_kollontay": "bosskolontay",
    "boss_zryachiy": "bosszryachiy", "boss_partisan": "bosspartisan", "boss_goons": "bossknight",
    "f_reshala": "default", "f_glukhar": "default", "f_sanitar": "default", "f_shturman": "default", "f_kaban": "default",
    "f_kollontay": "default", "f_zryachiy": "default",
    "cultist": "sectantwarrior", "raider": "pmcbot", "rogue": "exusec", "bloodhound": "default",
}
# 묶음 표(전용 표가 없는 봇이 쓰는 표). special·faction 묶음은 감염자·팩션 봇이 쓰므로 건드리지 않는다.
GROUPS = {"pmc": "usec", "scavs": "scav", "boss": "boss_reshala", "follower": "f_reshala"}
# 원작 plateChances 묶음
PLATE_GROUP = lambda role: ("pmc" if role in ("usec", "bear") else "scav" if role in ("scav", "marksman", "pscav")
                            else "boss" if role.startswith("boss_") else "follower" if role.startswith("f_") else "special")
CHANCE = {"Headwear": "headwear", "Earpiece": "earpiece", "FaceCover": "faceCover", "ArmorVest": "armorVest",
          "Backpack": "backpack", "Holster": "holster", "Eyewear": "eyewear"}
GEN = {"magazines": "magazines", "healing": "meds", "stims": "stims", "grenades": "grenades", "food": "food",
       "drink": "drink", "backpackLoot": "backpackLoot", "pocketLoot": "pocketLoot", "vestLoot": "vestLoot"}
SKIP_SLOTS = ("Pockets", "SecuredContainer")


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def range_share(config):
    """원작은 맵마다 근거리/원거리 목록을 고르는 비율이 다르다. Gravity는 맵 구분이 없으므로 맵 평균을 쓴다."""
    maps = config.get("generalConfig", {}).get("mapRangeWeighting", {}) or {}
    longs = [m.get("LongRange", 0) / max(1, m.get("LongRange", 0) + m.get("ShortRange", 0)) for m in maps.values()]
    long = sum(longs) / len(longs) if longs else 0.25
    return 1 - long, long


def class_range(weights):
    """원작 등급별 무게 → 실제로 나오는 등급 범위(무게가 0보다 큰 등급)."""
    cls = [int(k) for k, v in (weights or {}).items() if float(v) > 0]
    return (min(cls), max(cls)) if cls else None


def convert(source, config, tier_data, catalog):
    vanilla = {x["id"]: x for x in catalog if not x.get("mod")}
    stats = {"missing": set(), "modded": set()}
    mods_in_catalog = {x["id"] for x in catalog if x.get("mod")}

    def usable(id):
        if id in vanilla: return True
        (stats["modded"] if id in mods_in_catalog else stats["missing"]).add(id)
        return False

    short_share, long_share = range_share(config)
    gc = config.get("generalConfig", {})
    plate_classes = gc.get("plateClasses", {}).get("pmc", {})
    plate_chances = gc.get("plateChances", {})
    pmc_cfg = config.get("pmcBots", {})

    # 1) 티어 상한: 레벨 범위는 원작 TierData, 방탄판 등급은 원작 PMC plateClasses에서 실제로 나오는 등급.
    tiers = []
    for i, td in enumerate(sorted(tier_data, key=lambda t: t["Tier"])[:7]):
        pc = plate_classes.get(f"tier{i + 1}", {})
        front = class_range(pc.get("front_plate")) or (2, 6)
        side = class_range(pc.get("left_side_plate")) or front
        wd, ad = pmc_cfg.get("weaponDurability", {}), pmc_cfg.get("armourDurability", {})
        tiers.append(dict(min=td["PlayerMinLevel"], max=td["PlayerMaxLevel"], pen=100, spare=100,
                          fMin=front[0], fMax=front[1], sMin=side[0], side=side[1],
                          aMin=ad.get("min", 95), aMax=ad.get("max", 100), wMin=wd.get("min", 95), wMax=wd.get("max", 100),
                          mags=5, longScope=50, helmetAcc=50, meds=6, nades=6))

    # 2) 봇 종류별 표
    tables = defaultdict(list)
    for ti in range(1, 8):
        load = lambda folder, suffix: read(source / folder / f"Tier{ti}_{suffix}.json")
        equipment, ammo, chances = load("Equipment", "equipment"), load("Ammo", "ammo"), load("Chances", "chances")
        for role, key in ROLES.items():
            eq = equipment.get(key, equipment.get("default", {})).get("equipment", {})
            ch = chances.get(key, chances.get("default", {})).get("chances", {})
            d = {"only": True, "weapons": {}, "secondaryWeapons": {}, "armor": {}, "ammo": {}, "meds": {}, "gen": {},
                 "genWeights": {}, "chance": {}, "mods": {}, "slotChances": {}, "partChances": {}}

            def add(sec, id, w, slot=None):
                info = vanilla[id]
                if slot:  # 원작이 권총·칼 칸에 넣은 총(예: MP-43 쌍열 단축형)은 그 칸 목록에 둔다. 총 종류(shotgun 등)로 보내면 주무기 후보가 된다.
                    pool = d.setdefault(sec, {}).setdefault(slot, {})
                    pool[id] = round(min(10000, pool.get(id, 0) + w), 2)
                    return
                if info["section"] == "weapons": out_sec = sec
                elif info["section"] == "armor" and sec == "weapons": return  # 무기 칸에 방어구가 들어간 경우 없음(방어)
                else: out_sec = info["section"]
                pool = d.setdefault(out_sec, {}).setdefault(info["slot"], {})
                pool[id] = round(min(10000, pool.get(id, 0) + w), 2)

            # 주무기·보조 주무기: 원작은 근거리/원거리 목록을 맵 비율로 고른 뒤 그 안에서 무게로 고른다 → 실제 확률을 합쳐 1000점으로.
            for slot, sec in (("FirstPrimaryWeapon", "weapons"), ("SecondPrimaryWeapon", "secondaryWeapons")):
                parts = [(eq.get(slot + "_ShortRange", {}), short_share), (eq.get(slot + "_LongRange", {}), long_share)]
                for pool, share in parts:
                    clean = {id: float(w) for id, w in pool.items() if float(w) > 0 and usable(id)}
                    total = sum(clean.values())
                    for id, w in clean.items(): add(sec, id, 1000 * share * w / total)
            for slot, pool in eq.items():
                if slot.startswith(("FirstPrimaryWeapon", "SecondPrimaryWeapon")) or slot in SKIP_SLOTS: continue
                for id, w in pool.items():
                    if float(w) > 0 and usable(id):
                        if slot in ("Holster", "Scabbard"): add("weapons", id, float(w), slot.lower())
                        else: add("armor", id, float(w))

            ammo_key = "pmcAmmo" if role in ("usec", "bear") else "scavAmmo" if key == "scav" else "bossAmmo"
            for caliber, pool in ammo.get(ammo_key, {}).items():
                clean = {id: float(w) for id, w in pool.items() if float(w) > 0 and usable(id)}
                if clean: d["ammo"][caliber] = clean

            gen = ch.get("generation", {}).get("items", {})
            for name, cfg in gen.items():
                k = GEN.get(name)
                if not k: continue
                weights = {str(n): float(v) for n, v in cfg.get("weights", {}).items() if float(v) > 0 and 0 <= int(n) <= 30}
                if weights:
                    ns = [int(n) for n in weights]
                    d["gen"][k] = {"min": min(ns), "max": max(ns)}
                    d["genWeights"][k] = weights
                white = {id: float(w) for id, w in cfg.get("whitelist", {}).items() if float(w) > 0 and usable(id)}
                if k in ("meds", "stims", "grenades", "food", "drink") and white: d["meds"][k] = white

            d["slotChances"] = {k: max(0, min(100, float(v))) for k, v in ch.get("equipment", {}).items()}
            d["chance"] = {CHANCE[k]: v for k, v in d["slotChances"].items() if k in CHANCE}
            d["chance"]["armouredRig"] = 100
            for kind in ("equipmentMods", "weaponMods"):
                d["partChances"].update({k: max(0, min(100, float(v))) for k, v in ch.get(kind, {}).items()})
            # 원작 config의 plateChances가 켜져 있으면 판 확률은 그 값이 우선한다(봇 묶음·티어별).
            if plate_chances.get("enable"):
                g = PLATE_GROUP(role)
                main, side = plate_chances.get(g + "MainPlateChance"), plate_chances.get(g + "SidePlateChance")
                if main and side:
                    d["partChances"].update({"front_plate": main[ti - 1], "back_plate": main[ti - 1],
                                             "left_side_plate": side[ti - 1], "right_side_plate": side[ti - 1]})
            d["mods"] = {k: float(ch.get("weaponMods", {}).get("mod_" + k, 50)) for k in ("scope", "muzzle", "tactical", "foregrip", "stock", "mount")}
            tables[role].append(d)
    for group, role in GROUPS.items():
        tables[group] = json.loads(json.dumps(tables[role]))

    # 3) 레벨 선택: 원작 TierData의 레벨 폭과 티어 섞기 무게
    td = sorted(tier_data, key=lambda t: t["Tier"])[:7]
    lv = {"pick": "tier", "scavSame": False,
          "deltas": [{"b": t["BotMinLevelVariance"], "a": t["BotMaxLevelVariance"]} for t in td],
          "scavDeltas": [{"b": t["ScavMinLevelVariance"], "a": t["ScavMaxLevelVariance"]} for t in td],
          "weights": [[w["weight"] for w in sorted(t["PmcLevelWeights"], key=lambda x: x["tier"])] for t in td]}

    return {
        "format": "gravity-tier-preset", "version": 2, "name": "APBS Realistic (mip01)",
        "credits": CREDITS,
        "tiers": tiers, "tables": dict(tables), "ownTables": {r: True for r in ROLES}, "items": {},
        "settings": {
            "gear": {"tiers": tiers, "lv": lv},
            "weapons": {"stock": bool(gc.get("forceStock", True)), "dust": bool(gc.get("forceDustCover", True)),
                        "scope": bool(gc.get("forceScopeSlot", True)), "muzzle": {"force": bool(gc.get("forceMuzzle", False))}},
            "mods": {"subset": True, "weapons": 0, "attachments": 0, "equipment": 0},
        },
        "conversion": {
            "rangeShare": {"short": round(short_share, 3), "long": round(long_share, 3)},
            "skippedModItems": len(stats["modded"]), "missingItems": len(stats["missing"]),
            "approximations": [
                "근거리/원거리 주무기 목록: 맵별 비율 대신 맵 평균 비율로 합침",
                "방탄판 등급: 원작 PMC 등급별 무게 중 0보다 큰 등급 범위(모든 봇 공통)",
                "총 종류별 부품 확률: 공통 weaponMods 확률만 사용",
                "외형(Appearance)·부품 조합 트리(Mods): 설치된 SPT DB 사용",
                "권총 칸의 MP-43 쌍열 단축형(보스·추종자 무게 3): Gravity 권총 칸은 권총만 받아 나오지 않음",
                "진통제(drugs)·현금·특수 아이템 개수: SPT 기본값",
                "원작 config의 봇 묶음 켬/끔·자원 무작위·느슨한 탄·대용량 탄창 제한·내구도(PMC 값만 모든 봇): Gravity 설정을 따름",
            ],
        },
    }


def main():
    p = argparse.ArgumentParser(description=__doc__)
    for a in ("source", "config", "tiers", "catalog", "out"): p.add_argument("--" + a, type=Path, required=True)
    a = p.parse_args()
    catalog = read(a.catalog).get("items", [])
    result = convert(a.source, read(a.config), read(a.tiers), catalog)
    a.out.parent.mkdir(parents=True, exist_ok=True)
    a.out.write_text(json.dumps(result, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(json.dumps({"tables": len(result["tables"]), "bytes": a.out.stat().st_size, **result["conversion"]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
