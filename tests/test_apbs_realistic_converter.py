"""APBS Realistic(mip01) 변환: 원작 무게를 그대로 옮기고, 모드 아이템은 빼고, 감사 표시를 남기는지 검사합니다."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

MODULE = Path(__file__).resolve().parents[1] / "tools/convert-apbs-realistic.py"
spec = importlib.util.spec_from_file_location("converter", MODULE)
converter = importlib.util.module_from_spec(spec); spec.loader.exec_module(converter)


class ApbsRealisticConverterTests(unittest.TestCase):
    def test_keeps_original_values_drops_mod_items_and_credits_author(self):
        short, long, pistol, mod, helmet = "1" * 24, "2" * 24, "3" * 24, "4" * 24, "5" * 24
        catalog = [
            {"id": short, "section": "weapons", "slot": "smg", "mod": False},
            {"id": long, "section": "weapons", "slot": "sniper", "mod": False},
            {"id": pistol, "section": "weapons", "slot": "holster", "mod": False},
            {"id": helmet, "section": "armor", "slot": "headwear", "mod": False},
            {"id": mod, "section": "weapons", "slot": "ar", "mod": True},
        ]
        config = {"generalConfig": {"mapRangeWeighting": {"a": {"LongRange": 20, "ShortRange": 80}},
                                    "plateClasses": {"pmc": {f"tier{t}": {"front_plate": {"3": 90, "4": 10, "5": 0}, "left_side_plate": {"3": 100}} for t in range(1, 8)}},
                                    "plateChances": {"enable": True, "pmcMainPlateChance": [65] * 7, "pmcSidePlateChance": [15] * 7},
                                    "forceStock": True, "forceMuzzle": False}}
        tiers = [{"Tier": t, "PlayerMinLevel": t * 10 - 9, "PlayerMaxLevel": t * 10, "BotMinLevelVariance": 10, "BotMaxLevelVariance": 5,
                  "ScavMinLevelVariance": 10, "ScavMaxLevelVariance": 5, "PmcLevelWeights": [{"tier": i, "weight": 100 if i == t else 0} for i in range(1, 8)]}
                 for t in range(1, 8)]
        with tempfile.TemporaryDirectory() as root:
            path = Path(root)
            for folder in ("Equipment", "Ammo", "Chances"): (path / folder).mkdir()
            for t in range(1, 8):
                eq = {"pmcUSEC": {"equipment": {"FirstPrimaryWeapon_ShortRange": {short: 3, mod: 50}, "FirstPrimaryWeapon_LongRange": {long: 1},
                                                "Holster": {pistol: 7}, "Headwear": {helmet: 4}}}}
                ch = {"pmcUSEC": {"chances": {"equipment": {"Headwear": 37}, "equipmentMods": {"front_plate": 20},
                                              "generation": {"items": {"magazines": {"weights": {"1": 80, "2": 20}}}}}}}
                (path / "Equipment" / f"Tier{t}_equipment.json").write_text(json.dumps(eq), encoding="utf-8")
                (path / "Chances" / f"Tier{t}_chances.json").write_text(json.dumps(ch), encoding="utf-8")
                (path / "Ammo" / f"Tier{t}_ammo.json").write_text("{}", encoding="utf-8")
            out = converter.convert(path, config, tiers, catalog)
        usec = out["tables"]["usec"][0]
        self.assertTrue(usec["only"])
        self.assertEqual(usec["weapons"]["smg"], {short: 800.0})    # 근거리 80% 전부
        self.assertEqual(usec["weapons"]["sniper"], {long: 200.0})  # 원거리 20% 전부
        self.assertEqual(usec["weapons"]["holster"], {pistol: 7.0})
        self.assertEqual(usec["armor"]["headwear"], {helmet: 4.0})
        self.assertNotIn(mod, json.dumps(out))
        self.assertEqual(out["conversion"]["skippedModItems"], 1)
        self.assertEqual(usec["slotChances"]["Headwear"], 37)
        self.assertEqual(usec["partChances"]["front_plate"], 65)  # plateChances가 원작에서 우선
        self.assertEqual(usec["genWeights"]["magazines"], {"1": 80.0, "2": 20.0})
        self.assertEqual((out["tiers"][0]["fMin"], out["tiers"][0]["fMax"]), (3, 4))
        self.assertEqual(out["settings"]["mods"]["weapons"], 0)
        self.assertEqual(out["credits"]["presetAuthor"], "mip01")
        self.assertNotIn("infected", out["tables"]); self.assertNotIn("faction", out["tables"])

    def test_bundled_preset_is_credited_and_vanilla_only(self):
        path = MODULE.parents[1] / "src/Gravity.Server/wwwroot/presets/apbs-realistic-mip01.json"
        out = json.loads(path.read_text(encoding="utf-8"))
        self.assertEqual(out["credits"]["presetAuthor"], "mip01")
        self.assertEqual(out["conversion"]["skippedModItems"], 0)
        self.assertEqual([t["min"] for t in out["tiers"]], [1, 11, 21, 31, 41, 51, 61])
        for group in ("pmc", "scavs", "boss", "usec", "bear"):
            self.assertEqual(len(out["tables"][group]), 7)
            for table in out["tables"][group]:
                self.assertTrue(table["only"]); self.assertTrue(any(table["weapons"].values()))


if __name__ == "__main__":
    unittest.main()
