# 감사 / Credits

## 현재 기본 프리셋 (1.0.4부터)

**Realistic and Extended PMC Loadouts**는 R_F의 **Realistic Live-like 1.0.2** 저장본을 사용합니다. 7티어·33개 장비 표와 봇별 역할·무기·모드 설정을 함께 옮겼습니다. 기존 카드 이름과 프리셋 ID는 유지합니다. APBS 기반 변환에서 직접 옮기지 못한 기능은 프리셋의 `conversion.approximations`에 적었습니다. 커스텀 원본의 확인되지 않은 제작자 이름은 임의로 붙이지 않습니다.

From 1.0.4, **Realistic and Extended PMC Loadouts** contains R_F’s saved **Realistic Live-like 1.0.2** setup: 7 tiers, 33 tables, and role, weapon and mod settings. The existing card name and preset ID are retained. APBS conversion limitations are listed in `conversion.approximations`. An unverified author attribution is not assigned to this custom source.

## 이전 기본 프리셋 감사 (1.0.3 이하)

- 원작: **APBS Realistic and Extended PMC Loadouts Preset** — 제작 **mip01**
- 바탕: **Acid's Progressive Bot System (APBS)** — 제작 **acidphantasm**
- mip01님이 원작 APBS 프리셋 제작자의 허락을 받아 만든 에드온입니다. 당시 Gravity는 그 값을 고치지 않고 Gravity 장비 표 형식으로만 옮겼습니다(모드 아이템 제외).
- 파일: `src/Gravity.Server/wwwroot/presets/apbs-realistic-mip01.json`, 변환기: `tools/convert-apbs-realistic.py`
- Gravity에 같은 기능이 없는 몇 가지(맵별 근거리/원거리 비율, 총 종류별 부품 확률, 진통제 개수 등)는 가장 가까운 값으로 옮겼습니다. 목록은 프리셋 파일의 `conversion.approximations`에 있습니다.

좋은 프리셋을 만들어 주신 mip01님, 그리고 APBS를 만든 acidphantasm님께 감사드립니다.

---

- Original: **APBS Realistic and Extended PMC Loadouts Preset** by **mip01**
- Based on: **Acid's Progressive Bot System (APBS)** by **acidphantasm**
- Made by mip01 with permission from the original APBS preset author. Those releases carried its values over unchanged, only reformatted into Gravity loadout tables (mod items excluded).
- A few things Gravity has no direct equivalent for (per-map short/long range split, per-weapon-type attachment chances, painkiller counts, etc.) are carried over as the closest match. The list is in the preset file under `conversion.approximations`.

Thank you, mip01 and acidphantasm.
