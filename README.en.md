<p align="center">
  <a href="README.md"><img alt="한국어" src="https://img.shields.io/badge/%ED%95%9C%EA%B5%AD%EC%96%B4-%ED%95%9C%EA%B5%AD%EC%96%B4%EB%A1%9C%20%EB%B3%B4%EA%B8%B0-6e7681?style=for-the-badge"></a>
  <a href="README.en.md"><img alt="English (this page)" src="https://img.shields.io/badge/English-This%20page-1f6feb?style=for-the-badge"></a>
</p>

<p align="center">
  <a href="https://danyhappy564-cmyk.github.io/Gravity/?lang=en"><img src="site/img/og.jpg" alt="Gravity project site" width="820"></a>
</p>

<h2 align="center">📖 <a href="https://danyhappy564-cmyk.github.io/Gravity/?lang=en">Everything is explained on the project site →</a></h2>
<p align="center">Spawns · bosses · loadout tiers · presets · other mods · install and notes, all on one page with screenshots of the web UI.</p>

<p align="center">
  <a href="https://danyhappy564-cmyk.github.io/Gravity/?lang=en"><img alt="Open the project site" src="https://img.shields.io/badge/Project%20site-Open-3d7be0?style=for-the-badge"></a>
  <a href="https://github.com/danyhappy564-cmyk/Gravity/releases/latest"><img alt="Download latest" src="https://img.shields.io/badge/Latest-Download-2ea043?style=for-the-badge&logo=github&logoColor=white"></a>
</p>

## Changelog

The full changelog (3 entries) is in the [`changelog/`](changelog/) folder, 10 per file (higher number = newer, latest: [`CHANGELOG-001.md`](changelog/CHANGELOG-001.md)). Per-release summaries: [Releases](../../releases).

# Gravity 1.0.1

[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20Me%20a%20Coffee-Support-FFDD00?logo=buymeacoffee&logoColor=black)](https://buymeacoffee.com/real_feels)

If you like the mod, you can support it here: https://buymeacoffee.com/real_feels

A server + client mod for SPT **4.1**. It decides in one place **who spawns, when, where, and with what gear**. Everything is set from a web page you open in your browser while the game server is running.

## What it does

**Spawn module**

- PMCs: starting squads plus waves or continuous refills, squad-size mix, faction mix, difficulty and level range, stop near the end of the raid.
- Scavs: starting count, active/quiet cycles, map caps (one shared value or per map), protection radius around the player for refills.
- Bosses and special bots (sniper scavs, Raiders, Rogues, Cultists, …): chance, count and timing on a timeline; 0 means they never appear.
- Faction mods (RUAF, BlackDiv, …) and events (Halloween, Christmas, …): detected, each can be turned on or off.
- Per-map spawn zones, raid-wide group limits, SVM extra raid time detection, raid presets you can save and import.
- Each map shows an estimate of how many PMCs and scavs to expect.

**Loadout module**

- Bots get a tier T1–T7 from their level; each tier picks weapons, armor, plate classes, ammo, meds and attachments.
- **Item tiers**: items are ranked by price (flea, or handbook when missing) within the same slot or type and split into 7 tiers, with the reason shown. Click to override.
- **Tier editor**: per tier, the items and their share, counts and chances, caps and plates, attachments, and a **Tier summary** tab showing what that tier actually carries.
- Attachments follow tiers too. If every part that fits a gun is above the tier, the lowest-tier one is used, so guns never come out bare (can be turned off in Weapon build).
- Mod items: on/off per installed mod, use only part of them each raid, PMC-only.
- Loadout presets: **Gravity auto** and **Realistic and Extended PMC Loadouts** (see Credits) are built in; you can save your own.

## Install

1. Extract the archive into your SPT 4.1 folder. You should get `BepInEx\plugins\Gravity` and `SPT_Runtime\user\mods\Gravity`.
2. Start the server and open `https://127.0.0.1:6969/gravity/` in a browser (also listed on the SPT launcher's mod page).
3. Change settings and press **Save**; they apply from the next raid.

## With other mods

- With **ABPS** (bot placement) installed, Gravity's spawn module locks itself; with **APBS** (bot gear), the loadout module locks. Those features are left to that mod.
- SAIN, ORBIT, SVM, MapVariants (variant maps) and others are detected and handled. See the **Compatibility** page in the web UI.

## Credits

The **Realistic and Extended PMC Loadouts** preset carries over the values of *APBS Realistic and Extended PMC Loadouts Preset* by **mip01** unchanged (mod items excluded; a few features Gravity lacks use the closest match). mip01 made it with permission from the original APBS preset author; it is based on APBS by **acidphantasm**. Details in [CREDITS.md](CREDITS.md).

## License

MIT — [LICENSE](LICENSE)
