"""참고 데이터 변환 결과가 원본 구조·설치 안 된 아이템을 포함하지 않는지 검사합니다."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

MODULE=Path(__file__).resolve().parents[1]/"tools/convert-realistic-preset.py"
spec=importlib.util.spec_from_file_location("converter",MODULE)
converter=importlib.util.module_from_spec(spec);spec.loader.exec_module(converter)

class RealisticConverterTests(unittest.TestCase):
    def test_converts_weights_classes_and_probabilities_and_removes_missing_mods(self):
        ar="1"*24;gun="2"*24;missing="3"*24
        items={ar:{"_name":"AssaultRifle"},gun:{"_name":"fixture","_parent":ar,"_props":{}}}
        with tempfile.TemporaryDirectory() as root:
            path=Path(root)
            for folder in ("Equipment","Ammo","Chances","Mods","Appearance"):(path/folder).mkdir()
            for t in range(1,8):
                files={"Equipment":{"pmcUSEC":{"equipment":{"FirstPrimaryWeapon_LongRange":{gun:4,missing:99},"FirstPrimaryWeapon_ShortRange":{gun:2},"SecondPrimaryWeapon_LongRange":{gun:8}}}},
                       "Ammo":{},"Mods":{},"Appearance":{"pmcUSEC":{"appearance":{"body":{"original":1}}}},
                       "Chances":{"pmcUSEC":{"chances":{"equipment":{"Headwear":37},"weaponMods":{"mod_scope":49},"generation":{"items":{"magazines":{"weights":{"1":80,"2":20}}}}}}}}
                for folder,data in files.items():(path/folder/f"Tier{t}_{folder.lower()}.json").write_text(json.dumps(data),encoding="utf-8")
            out=converter.convert(path,items)
        self.assertEqual(out["format"],"gravity-tier-preset")
        self.assertEqual(out["tables"]["usec"][0]["weapons"]["ar"],{gun:6})
        self.assertEqual(out["tables"]["usec"][0]["secondaryWeapons"]["ar"],{gun:8})
        self.assertEqual(out["tables"]["usec"][0]["chance"]["headwear"],37)
        self.assertEqual(out["tables"]["usec"][0]["genWeights"]["magazines"],{"1":80,"2":20})
        self.assertNotIn(missing,json.dumps(out));self.assertNotIn('"appearance":',json.dumps(out))
        self.assertEqual(out["conversion"]["missingItems"],1)

    def test_bundled_preset_has_seven_real_weighted_tiers_and_no_legacy_weapon_categories(self):
        path=MODULE.parents[1]/"src/Gravity.Server/wwwroot/presets/realistic.json"
        out=json.loads(path.read_text(encoding="utf-8"))
        self.assertEqual(len(out["tiers"]),7)
        for group in ("pmc","scavs","boss","usec","bear"):
            self.assertEqual(len(out["tables"][group]),7)
            for table in out["tables"][group]:
                self.assertNotIn("primaryShort",table["weapons"])
                self.assertTrue(any(table["weapons"].values()))
        self.assertEqual(out["tables"]["usec"][0]["weapons"]["ar"]["5447a9cd4bdc2dbd208b4567"],7)

if __name__=="__main__":unittest.main()
