"""로컬 참고 프리셋의 수치만 Gravity로 변환합니다. 원본 코드/파일은 배포하지 않습니다.

python tools/convert-realistic-preset.py --source <realistic 폴더> --items <SPT items.json>
  --out src/Gravity.Server/wwwroot/presets/realistic.json [--catalog <api/gear/catalog JSON>]
"""
import argparse
import json
from collections import defaultdict
from pathlib import Path

GUNS = {"AssaultRifle":"ar", "AssaultCarbine":"carbine", "Smg":"smg", "MachineGun":"lmg",
        "MarksmanRifle":"dmr", "SniperRifle":"sniper", "Shotgun":"shotgun", "Pistol":"holster",
        "Revolver":"holster", "GrenadeLauncher":"launcher", "RocketLauncher":"launcher", "Knife":"scabbard"}
EQUIP = {"Headwear":"headwear", "ArmorVest":"armorVest", "TacticalVest":"tacticalVest", "Backpack":"backpack",
         "Earpiece":"earpiece", "FaceCover":"faceCover", "Eyewear":"eyewear", "ArmBand":"armband", "Holster":"holster", "Scabbard":"scabbard"}
ROLES = {"usec":"pmcUSEC", "bear":"pmcBEAR", "scav":"scav", "marksman":"scav", "pscav":"scav",
         "boss_reshala":"bossbully", "boss_shturman":"bosskojaniy", "boss_tagilla":"bosstagilla", "boss_glukhar":"bossgluhar",
         "boss_sanitar":"bosssanitar", "boss_killa":"bosskilla", "boss_kaban":"bossboar", "boss_kollontay":"bosskolontay",
         "boss_zryachiy":"bosszryachiy", "boss_partisan":"bosspartisan", "boss_goons":"bossknight",
         "cultist":"sectantwarrior", "raider":"pmcbot", "rogue":"exusec"}
CHANCE = {"Headwear":"headwear", "Earpiece":"earpiece", "FaceCover":"faceCover", "ArmorVest":"armorVest", "Backpack":"backpack", "Holster":"holster", "Eyewear":"eyewear"}
GEN = {"magazines":"magazines", "healing":"meds", "stims":"stims", "grenades":"grenades", "food":"food", "drink":"drink", "backpackLoot":"backpackLoot", "pocketLoot":"pocketLoot", "vestLoot":"vestLoot"}

def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))

def convert(source, items, installed=None):
    """주무기 두 거리 목록은 종류별 합산, 숫자 개수 분포·장비/부품 확률은 보존합니다."""
    def ancestry(id):
        seen=set()
        for _ in range(20):
            if id in seen or id not in items: return
            seen.add(id); t=items[id];yield t.get("_name", "")
            id=t.get("_parent", "")

    def weapon(id):
        return next((GUNS[n] for n in ancestry(id) if n in GUNS), "special")

    def allowed(id):
        return id in items or id in (installed or {})

    def valid_weights(pool):
        return {id:max(0,min(10000,float(w))) for id,w in pool.items() if allowed(id) and isinstance(w,(float,int))}

    tiers=[];tables=defaultdict(list);own={};stats={"missingItems":set(), "appearanceRead":0}
    for ti in range(1,8):
        def load(folder,suffix):return read(source/folder/f"Tier{ti}_{suffix}.json")
        equipment,ammo,chances,mods,appearance=load("Equipment","equipment"),load("Ammo","ammo"),load("Chances","chances"),load("Mods","mods"),load("Appearance","appearance")
        stats["appearanceRead"]+=len(appearance)
        # Mods는 호환·허용 ID만 참고한다. 전체 부품 그래프는 가져오지 않고 설치된 SPT가 관리한다.
        plate_classes={"front":set(),"side":set()}
        for parent,slots in mods.items():
            if not allowed(parent):continue
            for slot,ids in slots.items():
                pos="front" if slot.lower() in ("front_plate","back_plate") else "side" if "side_plate" in slot.lower() else None
                if pos:
                    for id in ids:
                        cl=int(items.get(id,{}).get("_props",{}).get("armorClass",0) or 0)
                        if 1<=cl<=6:plate_classes[pos].add(cl)
        # 참고 자료는 모든 티어의 호환 판 목록이 동일하다. Gravity의 진행 등급에 호환 상한을 적용한다.
        fmin,fmax,side=[(2,3,2),(3,3,2),(3,4,3),(4,5,3),(4,5,4),(5,6,4),(5,6,5)][ti-1]
        low=[1,11,21,31,41,51,66][ti-1];hi=[10,20,30,40,50,65,100][ti-1]
        pen=max((float(items[id].get("_props",{}).get("PenetrationPower",0) or 0) for pool in ammo.get("pmcAmmo",{}).values() for id,w in pool.items() if id in items and w>0),default=20)
        tiers.append(dict(min=low,max=hi,pen=pen,spare=pen,fMin=fmin,fMax=min(fmax,max(plate_classes["front"],default=6)),sMin=1,side=side,
                          aMin=60+(ti-1)*5,aMax=100,wMin=55+(ti-1)*5,wMax=100,mags=5,longScope=min(80,20+ti*8),helmetAcc=50,meds=6,nades=6))
        for role,original in ROLES.items():
            eq=equipment.get(original,equipment.get("default",{})).get("equipment",{})
            ch=chances.get(original,chances.get("default",{})).get("chances",{})
            d={"weapons":{},"armor":{},"ammo":{},"meds":{},"parts":{},"gen":{},"genWeights":{},"chance":{},"mods":{},"slotChances":{},"partChances":{}}
            for slot,pool in eq.items():
                clean=valid_weights(pool)
                stats["missingItems"].update(id for id in pool if not allowed(id))
                # 각 보조 주무기별 무게도 별도로 남겨 주무기와 서로 덮어쓰지 않는다.
                sec="secondaryWeapons" if slot.startswith("SecondPrimaryWeapon") else "weapons" if slot.startswith("FirstPrimaryWeapon") or slot in ("Holster","Scabbard") else "armor"
                for id,w in clean.items():
                    kind=weapon(id) if sec in ("weapons","secondaryWeapons") else EQUIP.get(slot)
                    if not kind:continue
                    if kind=="tacticalVest":
                        props=items.get(id,{}).get("_props",{})
                        if int(props.get("armorClass",0) or 0)>0 or any(s.get("_name", "").lower() in ("front_plate","back_plate") for s in props.get("Slots",[])):kind="armouredRig"
                    out=d.setdefault(sec,{}).setdefault(kind,{})
                    out[id]=min(10000,out.get(id,0)+w)
            for caliber,pool in ammo.get("scavAmmo" if original=="scav" else "pmcAmmo" if role in ("usec","bear") else "bossAmmo",{}).items():
                d["ammo"][caliber]=valid_weights(pool)
            for name,config in ch.get("generation",{}).get("items",ch.get("generation",{})).items():
                key=GEN.get(name)
                if not key:continue
                weights={str(k):float(v) for k,v in config.get("weights",{}).items() if float(v)>0 and 0<=int(k)<=30}
                if weights:
                    ns=[int(k) for k in weights];d["gen"][key]={"min":min(ns),"max":max(ns)};d["genWeights"][key]=weights
                whitelist=valid_weights(config.get("whitelist",{}))
                if key in ("meds","stims","grenades","food","drink") and whitelist:d["meds"][key]=whitelist
            d["slotChances"]={k:max(0,min(100,float(v))) for k,v in ch.get("equipment",{}).items()}
            d["chance"]={CHANCE[k]:v for k,v in d["slotChances"].items() if k in CHANCE}
            d["chance"]["armouredRig"]=100
            for kind in ("equipmentMods","weaponMods"):
                d["partChances"].update({k:max(0,min(100,float(v))) for k,v in ch.get(kind,{}).items()})
            d["mods"]={key:float(ch.get("weaponMods",{}).get("mod_"+key,50)) for key in ("scope","muzzle","tactical","foregrip","stock","mount")}
            tables[role].append(d);own[role]=True
    # 그룹 표는 대응 역할의 복사본. 개별 역할은 각자의 실제 참고 값을 유지한다.
    for group,role in {"pmc":"usec","scavs":"scav","boss":"boss_reshala","follower":"raider","special":"raider","faction":"raider"}.items():
        tables[group]=json.loads(json.dumps(tables[role]))
    used_mods={id:{"source":row.get("source","모드(출처 미상)"),"code":id} for id,row in (installed or {}).items() if id not in items}
    return {"format":"gravity-tier-preset","version":2,"name":"실전형(본섭 비슷)","tiers":tiers,"tables":dict(tables),"ownTables":own,"items":used_mods,
            "settings":{"gear":{"tiers":tiers},"weapons":{"stock":False,"dust":False,"scope":False,"muzzle":{"force":False}},"mods":{"subset":False}},
            "conversion":{"source":"realistic reference values","missingItems":len(stats["missingItems"]),"appearanceEntriesRead":stats["appearanceRead"],
                          "note":"의상은 외형 변경 기능이 없어 가져오지 않음. 호환 부품은 설치된 SPT DB 사용. 동일한 판 목록 대신 Gravity 진행 등급 사용."}}

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument("--source",type=Path,required=True);p.add_argument("--items",type=Path,required=True);p.add_argument("--catalog",type=Path);p.add_argument("--out",type=Path,required=True)
    a=p.parse_args();catalog=read(a.catalog).get("items",[]) if a.catalog else []
    result=convert(a.source,read(a.items),{x["id"]:x for x in catalog})
    a.out.parent.mkdir(parents=True,exist_ok=True);a.out.write_text(json.dumps(result,ensure_ascii=False,separators=(",",":"))+"\n",encoding="utf-8")
    print(json.dumps({"tiers":7,"tables":len(result["tables"]),"bytes":a.out.stat().st_size,**result["conversion"]},ensure_ascii=False))

if __name__=="__main__":main()
