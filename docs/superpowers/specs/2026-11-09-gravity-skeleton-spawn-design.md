# Gravity 1·2단계 설계: 뼈대 + 스폰 핵심 (2026-11-09)

> **상태: 사용자 검토 대기.** 사용자가 범위 A(아래 1절)만 고르고 자러 가면서 "필요하면 Codex 쓰고, SlopBridge로 검증하며 이어서 진행"을 지시했다.
> 그래서 절별 승인 없이 Claude가 정한 결정이 들어 있다. 그런 결정은 **[검토 필요]**로 표시했다. 아침에 이 표시만 훑어보면 된다.

## 1. 목표와 범위

**목표**: 목업 v013의 스폰 설정을 웹에서 바꾸면 다음 레이드 스폰에 그대로 반영되게 한다. ORBIT·SAIN·팩션 모드(RUAF·UNTAR·BlackDiv)·SVM과 함께 깔아도 깨지지 않아야 한다.

**이번 판(범위 A)에 넣는 것**
- 뼈대: 서버 모드 틀, 설정 저장, 웹 화면, 다른 모드 감지
- PMC 시작 스폰 + 웨이브, 스캐브 웨이브
- 맵별 설정: 동시 봇 상한, 웨이브 방식 ↔ 무한 보충 방식, 맵 덮어쓰기
- 레이드 시간 따라가기(SVM·스캐브 레이드 단축 반영)
- 분대 크기 비율 + 그룹 개수 제한(v012)
- 보스: 보스별 켜기·확률·나오는 맵(원래 나오는 맵 안에서 끄기)

**다음 판(2-2)으로 미루는 것**: 근처 보충, 팩션 모드 관리, 시즌 이벤트, 특수 봇, 보스 태어날 구역·호위 수·새 맵 추가, 핫스팟 가중, 점증 보스 확률, 장비 모듈 전부.

## 2. 큰 구조

- **서버 모드 하나**(`Gravity.Server`, C# .NET 10, SPT 4.1.x). **이번 판에는 클라이언트 플러그인이 없다.** **[검토 필요]**
  - 이유: 범위 A의 기능은 모두 "레이드 시작 때 서버가 클라이언트에 보내는 맵 데이터"를 고치는 것만으로 된다. 클라이언트 플러그인은 근처 보충(2-2)에서 처음 필요해진다.
  - 덕분에 ORBIT과 부딪칠 일이 거의 없다. 봇 생성·제거·행동은 게임이 원래 하던 대로 한다.
- 폴더 `SPT_Runtime/user/mods/Gravity/`: `Gravity.dll`, `wwwroot/`(웹 화면), `config/gravity.json`(설정, 처음 실행 때 생성), `config/backup/`.

### 2.1 단위(파일) 나누기

| 단위 | 하는 일 | 의존 |
|---|---|---|
| `GravityConfigStore` | 설정 JSON 읽기·검증·저장(원자적 쓰기, 백업 5개) | 없음(파일만) |
| `ConfigModel` | 서버가 쓰는 설정 값의 형식(목업 state의 일부) + 값 범위 고정(Clamp) | 없음 |
| `ModDetector` | 다른 모드 감지(로드된 어셈블리 이름 + mods 폴더 이름) | 없음 |
| `MapCatalog` | 서버 로드 뒤 맵별 기본 정보 수집(레이드 길이, 봇 상한, 구역 이름, 보스 목록) | SPT `LocationTable`·`BotConfig` |
| `SpawnPlanner` | **순수 함수**: (설정, 맵 정보, 레이드 길이, 시작 지연, 시드) → 스폰 계획(그룹 목록·스캐브 웨이브·상한) | 없음 → 단위 테스트 대상 |
| `RaidSpawnApplier` | 계획을 SPT 맵 복제본(`LocationBase`)에 쓰기 | SPT 모델 |
| `RaidStartPatch` | `LocationLifecycleService.GenerateLocationAndLoot` 앞뒤 패치 | SPTarkov.Reflection |
| `GravityWeb` | `IHttpListener`: `/gravity/` 정적 파일 + `/gravity/api/*` | ConfigStore·MapCatalog·ModDetector |

## 3. 설정 저장

- 파일은 **목업 state 전체를 그대로 담는 JSON 하나**(`{"format":"gravity-config","version":1,"state":{...}}`).
  - 웹 화면의 상태 객체를 그대로 저장한다. 서버가 아직 안 쓰는 칸(장비·팩션 등)도 버리지 않고 보존한다. 그래서 다음 판에서 기능을 붙여도 사용자 설정이 남는다.
  - 서버는 그중 범위 A에 해당하는 칸만 `ConfigModel`로 읽는다. 없는 칸은 기본값을 쓴다.
- **값 검증**: 숫자마다 최소·최대를 정해 둔다. 0이나 음수가 "무한대"로 바뀌는 일이 없게 한다(ABPS P5 회귀). 예를 들어 웨이브 간격은 0.5분 이상, 상한은 1~60이다. 고정된 값은 로그 한 줄로 알린다.
- **저장**: 임시 파일에 먼저 쓰고 바꿔 넣는다. 저장할 때마다 `config/backup/`에 이전 파일을 남기고, 최근 5개만 둔다.
- **적용 시점**: 저장하는 즉시 메모리 값이 바뀐다. **다음 레이드부터 적용**된다. 진행 중인 레이드는 그대로다.

## 4. 웹 화면

- 주소: `https://127.0.0.1:<SPT 포트>/gravity/`. SPT 서버 주소와 같고, 인증서도 SPT 것을 쓴다.
- **화면은 목업 v013을 그대로 쓴다**(사용자가 "완벽"하다고 한 화면). 바꾸는 것은 세 가지뿐이다. **[검토 필요]**
  1. 저장·불러오기: 브라우저 저장 대신 `GET/POST /gravity/api/state`.
  2. 가짜 상수를 서버 값으로 교체: 맵 목록, 레이드 길이(감지값), 맵 구역 이름, 보스 목록, 감지된 모드는 `GET /gravity/api/meta`에서 받는다.
  3. 이번 판에 게임에 반영되지 않는 페이지와 카드에는 "아직 게임에 적용되지 않음(다음 판)" 띠를 붙인다. 값은 저장되므로 미리 맞춰 둘 수 있다.
- API

| 경로 | 내용 |
|---|---|
| `GET /gravity/api/state` | 저장된 state(없으면 `null` → 화면이 자기 기본값 사용) |
| `POST /gravity/api/state` | state 저장. 응답에 고정된 값 목록 |
| `GET /gravity/api/meta` | 맵 정보, 보스 목록, 감지된 모드, 잠긴 모듈, Gravity 판 |
| `GET /gravity/api/lastraid` | 마지막 레이드에 실제로 넣은 계획 요약(진단 페이지용) |

- **보안**: `POST`는 루프백 주소(127.0.0.1·::1)에서 온 요청만 받는다. 본문은 최대 2MB다.
- 정적 파일은 `wwwroot` 안 경로만 내준다. `..` 경로는 막는다.

## 5. 스폰 적용(레이드 시작)

### 5.1 넣는 자리

SPT의 `LocationLifecycleService.GenerateLocationAndLoot(sessionId, name, generateLoot)`가 다음 순서로 일한다.

1. 맵 데이터를 복제한다.
2. SPT 기본 PMC 웨이브를 붙인다.
3. 스캐브 레이드면 시간 단축을 적용한다(`RaidChanges`).
4. 루트를 만든다.

결과(복제본)는 그대로 클라이언트에 간다.
- **앞 패치(Prefix)**: `ProfileActivityService`에서 이번 레이드의 `RaidChanges`(시작 지연 `SimulatedRaidStartSeconds` 등)를 미리 잡아 둔다. 원래 함수가 끝나면 이 값이 지워지기 때문이다.
- **뒤 패치(Postfix)**: `generateLoot`가 참이고 은신처가 아니며, 스폰 모듈이 켜져 있고 잠겨 있지 않으면 복제본을 고친다.
  - 레이드 길이: 복제본의 `EscapeTimeLimit`를 쓴다. SVM이 늘린 값과 스캐브 단축이 모두 반영된 최종값이다.
  - DB 원본(`LocationTable`)은 건드리지 않는다. 바꾸는 전역 값은 `BotConfig.MaxBotCap[맵]` 하나뿐이다. 클라이언트는 레이드 때 `/singleplayer/settings/bot/maxCap`으로 이 값을 받아 간다.
- **예외 처리**: 뒤 패치 전체를 try/catch로 감싼다. 실패하면 복제본을 SPT 원래 결과 그대로 두고 경고 한 줄을 남긴다. 그 레이드는 바닐라 스폰으로 진행된다.

### 5.2 지우는 것과 남기는 것 **[검토 필요]**
- **지움**(그 맵을 Gravity가 관리할 때):
  - `BossLocationSpawn` 중 `BossName`이 `pmcUSEC`/`pmcBEAR`인 항목
  - `Waves` 중 `WildSpawnType`이 `assault`인 항목
- **맵 끄기**: 맵 설정 `original=false`이면 그 맵은 통째로 건드리지 않는다(바닐라 그대로). 목업의 "게임 기본 맵 데이터에 Gravity 적용" 스위치다. `variant`(다른 모드가 바꿔 끼운 맵에도 적용)는 이번 판에서 구분하지 않고 항상 적용한다. **[검토 필요]**
- **남김**: 보스·호위·레이더·로그·컬티스트·저격 스캐브(`marksman`)·팩션 모드 항목(RUAF·UNTAR·BlackDiv가 넣은 것)·이벤트 항목. 보스는 5.5절의 확률 조정만 한다.

### 5.3 계획 만들기(`SpawnPlanner`)
목업의 `plan()`·`gcEstimate()`와 **같은 규칙**을 C#으로 옮긴다. 웹 미리보기와 실제 스폰이 같은 숫자를 내게 하기 위해서다.
- **입력**:
  - 맵 설정: 전역 `spawn` + 맵 `override` 덮어쓰기
  - 레이드 길이(초), 시작 지연(초)
  - 맵 기준 PMC 수: `MapCatalog`에서 받는다. 맵 기본 웨이브 인원을 근거로 하고, 웹 미리보기와 같은 값이다.
  - 시드: 레이드마다 새로 뽑는다(로그에 남김).
- **PMC**
  - 시작 인원 = 기준 PMC 수 × `startPmcPct`. 레이드 시작 1~5초 사이에 나눠 넣는다(1.0 3차 점검: 게임 웨이브 시계가 로딩 끝 무렵부터 돌아, 로딩 화면 안에 불린 분대만 모델을 미리 읽고 시작과 함께 나온다. 예전 5~20초는 뒤쪽 분대가 2~4분 늦었다).
  - 웨이브 시각: 목업 규칙을 따른다. 첫 지연 → 간격, 무작위 폭, 최소 간격, 앞뒤 쏠림, 초반·중반·후반 세기.
  - 하드 스톱: 레이드 끝에서 `hardStop` 이전까지만 넣는다.
- **무한 보충 방식**: 간격 `inf.delay`초마다 작은 웨이브를 레이드 끝까지 촘촘히 넣는다(PMC 비율 `inf.pmcShare`).
  - 동시 상한은 게임이 지킨다. `IgnoreMaxBots=false`이면 상한이 찼을 때 게임이 기다린다고 본다.
  - 이 동작은 **인게임 검증 항목**이다(9절). 상한이 찼을 때 기다리지 않고 버린다면 2-2에서 클라이언트 보충으로 바꾼다.
- **그룹 나누기**
  - 인원을 분대 비율(`squad.s1~s5`)로 그룹에 나눈다.
  - `groupCap` 규칙을 적용한다: 같은 크기 또는 그 이상 크기의 그룹 최대 개수. 넘치면 더 작은 그룹으로 나누거나(`split`) 뺀다(`drop`).
  - 세는 범위 "동시에 살아 있는"(`alive`)은 서버만으로는 알 수 없다. 이번 판에서는 "레이드 전체"로 센다. 웹에 이미 그렇게 안내하고 있다. **[검토 필요]**
- **진영·난이도**: 그룹마다 `usecPct`로 USEC/BEAR를 정하고, `diff` 비율로 난이도를 정한다.
- **스캐브**
  - 맵 기준 스캐브 수 × 레이드 길이 비율로 웨이브 수를 정한다.
  - `scav.separateStart`이면 시작 지연 `scav.delay`분 뒤에 첫 웨이브를 넣는다.
  - 활동·휴식 시간(`activeMin/Max`·`quietMin/Max`)으로 웨이브 시각을 묶는다.
- **상한**: 맵 `cap`.
- **출력**: `SpawnPlan { groups[{timeSec, side, size, difficulty, zones}], scavWaves[{timeMinSec, timeMaxSec, slotsMin, slotsMax, zones}], botCap, summary }`.
- **스캐브 레이드 시작 지연**: 지연보다 이른 항목은 빼고, 남은 항목 시각에서 지연만큼 뺀다. SPT의 기본 웨이브 처리 방식과 맞춘다. 인게임에서 확인한다.

### 5.4 계획을 맵에 쓰기(`RaidSpawnApplier`)
- **PMC 그룹** → `BossLocationSpawn` 하나:
  - `BossName`=`BossEscortType`=`pmcUSEC|pmcBEAR`
  - `BossEscortAmount`=`"인원-1"`, `BossChance=100`, `Time`=초
  - `BossZone`: **쉼표 목록은 쓰지 않는다** — 클라는 목록에서 한 구역만 무작위로 골라 고정하고 그 구역만 재시도한다(kb 2.62). 바닐라 묶음에는 봇 PMC 지점이 없는 구역이 섞여 있어(세관 12개 중 6개) 그 구역을 고른 분대는 영원히 대기했다(11-09 세관: 대기열 16·PMC 1~2명). 구역을 다 켰으면 `""`(클라가 자기 PMC 구역 전부를 섞어 차례로 시도, 세관 확인 판 18/18). 일부만 켰으면 켠 구역 이름 하나씩을 분대 순서대로 돌려 넣는다. 웹의 PMC 구역 목록 = DB `SpawnPointParams`에서 `BotPmc` 지점이 있는 구역.
  - **공장 한계(11-09)**: 클라의 공장 `BotZone` 지점 19개는 모두 일반 `Bot`이라 PMC 구역이 0개(`AvailableZonesPmc=0`) → 보스 웨이브 PMC는 바닐라 항목까지 `can't find zones to spawn boss`로 버려진다(코드상 판단). `""`이면 바로 버려지고, 이름을 넣으면 3초마다 영원히 재시도한다. 공장·미궁 PMC는 0.2 클라 플러그인(사람 스폰 지점 사용, ABPS 방식 원리만 참고)에서 해결한다.
  - `BossDifficulty`=`BossEscortDifficulty`=난이도
  - `IgnoreMaxBots`=`ignoreCap.start`(시작) 또는 `ignoreCap.waves`(웨이브)
  - `SpawnMode`=`["regular","pve"]`
- **스캐브 웨이브** → `Wave` 하나:
  - `WildSpawnType=assault`, `BotSide=Savage`, `BotPreset=normal`
  - `TimeMin/TimeMax`, `SlotsMin/SlotsMax`
  - `SpawnPoints`=스캐브 구역
  - `number`는 0부터 다시 매긴다.
- **봇 상한**: `BotMax`=cap, `BotConfig.MaxBotCap[맵]`=cap.

### 5.5 보스(범위 A)
- 맵의 `BossLocationSpawn` 중 이름이 알려진 보스 역할(`bossBully`·`bossKojaniy`·`bossTagilla`·`bossGluhar`·`bossSanitar`·`bossKilla`·`bossBoar`·`bossKolontay`·`bossZryachiy`·`bossKnight`·`bossPartisan`)인 항목을 조정한다.
  - 보스가 꺼져 있거나 그 맵이 꺼져 있으면 `BossChance=0`이다.
  - 아니면 `BossChance`=설정 확률이다. `bossCommon.on`이면 공통 확률을 쓰고, 즈랴치는 공통 확률에서 제외한다.
- 실제 역할 이름과 맵별 존재 여부는 `MapCatalog`가 DB에서 읽는다. 웹 보스 목록도 이 값으로 만든다.
- **SVM이 보스 확률을 바꾸고 있으면** 웹에 알린다. SVM은 서버 로드 때 DB를 바꾸고, Gravity는 레이드 시작 때 바꾸므로 Gravity 값이 나중에 적용된다.

### 5.6 로그
레이드마다 한 줄을 남긴다. 예: `[Gravity] bigmap 레이드 100분(시작 0초) PMC 시작 6(3그룹)+웨이브 11개(23명) 스캐브 웨이브 14개 상한 24 시드 81723`
같은 내용은 `/gravity/api/lastraid`에도 남는다.

## 6. 다른 모드 감지와 잠금

| 감지 대상 | 방법 | 처리 |
|---|---|---|
| ABPS(`acidphantasm-botplacementsystem`) | 로드된 어셈블리 이름 | **스폰 모듈 잠금**(꺼짐 고정, 웹에 이유 표시) |
| APBS(`acidphantasm-progressivebotsystem`) | 〃 | 장비 모듈 잠금(이번 판엔 장비 없음, 표시만) |
| SVM(`[SVM] Server Value Modifier` 폴더) | 폴더 + 선택된 프리셋 JSON 읽기(읽기 전용) | 레이드 시간 출처 표시, 보스 확률 겹침 경고 |
| ORBIT·SAIN·RUAF·UNTAR·BlackDiv·MoreBots | 폴더/어셈블리 이름 | 표시만(이번 판은 건드리지 않음) |
| Questing Bots | 〃 | "지원 안 함" 경고만 |

## 7. 처음부터 막는 함정(개조판 회귀 기록)
- 게임 스레드 대기 금지(`Thread.Sleep` 없음). 서버 패치는 동기 계산만 하고, 계획 계산은 1ms 수준이다.
- 0 입력이 무한대가 되는 문제는 3절의 Clamp로 막는다.
- 다른 모드의 맵 데이터를 통째로 바꾸지 않는다. 복제본에서 PMC·assault 항목만 갈아 끼운다.
- 커스텀 맵(모르는 맵 id)은 건드리지 않는다. 기본 정보가 없는 맵은 계획을 만들지 않고 바닐라로 둔다.
- 남의 모드 항목에서 예외가 나도 Gravity 처리는 항목 단위로 넘어간다.
- 이른 로딩 단계에서 설정을 쓰지 않는다. 설정은 `OnLoad` 이후에 읽고, 패치는 레이드 때만 돈다.
- **ABPS를 대신할 때 생기는 빈자리(11-09 확인)**: ABPS 클라 플러그인에는 봇 프로필 정리 패치(`Profile.GetAllPrefabPaths` 직전, 클라가 모르는 외형은 진영 기본 외형으로, 모델 없는 아이템은 제거)가 들어 있다. ABPS 없이 APBS 모드 옷 허용(`enableAllowlistedModClothing`)을 쓰면 `ConvertResourceInfo` NRE로 봇 생성이 통째로 실패한다(kb 2.17). 0.1은 서버 전용이라 막지 못한다 → 0.2 클라 플러그인에 같은 역할을 새로 작성한다(ABPS 코드는 읽기만).

## 8. 오류 처리 요약
- 설정 파일 손상 → 백업 폴더에 `broken-<시각>.json`으로 옮기고 기본값으로 시작하며, 웹에 알린다.
- 웹 POST 형식 오류 → 400과 이유를 돌려주고 저장하지 않는다.
- 레이드 패치 예외 → 바닐라 스폰으로 진행하고 경고 로그를 남긴다(5.1).

## 9. 시험

- **단위 테스트**(xUnit, `Gravity.Tests`): `SpawnPlanner`
  - 하드 스톱 뒤 항목 없음
  - 시작 지연 처리
  - 그룹 제한(split·drop)
  - Clamp(0 간격 → 최소값)
  - 같은 시드 → 같은 계획
  - 무한 보충 항목 수 상한
  - 목업 `plan()`과 같은 입력에서 같은 웨이브 수
- **빌드 확인**: SPT 4.1.0 패키지로 Release 빌드한다.
- **인게임 검증**(SlopBridge, 게임 `E:\SPT 4.1`)
  - 설치 전 백업한다. 시험 동안 ABPS를 잠시 빼고(`probe-env.ps1 set -Hide`), 끝나면 되돌린다.
  1. 서버 로그에 Gravity 로드와 감지 목록이 나온다.
  2. `/gravity/` 화면이 열리고, 저장이 파일에 남는다.
  3. 세관 레이드: 시작 로그의 계획과 레이드 중 살아 있는 PMC·스캐브 수·그룹 크기를 비교한다(브릿지로 30초마다 읽음).
  4. 상한이 찼을 때 웨이브가 기다리는지 버려지는지 확인한다(5.3).
  5. 오류 로그(`errors.log`·서버 콘솔)가 0이다.
  - 끝나면 SlopBridge를 빼고 ABPS를 되돌린다. Gravity는 게임에 남길지 사용자에게 묻는다.

## 10. 정한 것 목록(아침 검토용)
1. 이번 판은 서버 모드만 만든다(클라이언트 없음).
2. 웹은 목업 v013을 그대로 쓰고, 미구현 칸에는 띠를 붙인다.
3. 설정은 웹 state 전체를 한 파일에 저장한다(미구현 칸 보존).
4. 지우는 것은 PMC 항목과 assault 웨이브뿐이고, 나머지는 남긴다. 맵 `original` 스위치를 끄면 그 맵은 바닐라로 둔다.
5. 그룹 제한의 "동시에" 범위는 이번 판에서 "레이드 전체"로 센다.
6. 무한 보충은 촘촘한 웨이브로 흉내 낸다. 상한이 찼을 때의 동작은 인게임에서 확인한다.
7. 보스는 확률·맵 끄기만 한다(구역·호위·새 맵은 2-2).
8. ABPS가 있으면 스폰 모듈을 잠근다(검증 때만 ABPS를 잠시 뺌).
9. Gravity 저장소는 로컬 커밋만 하고, 공개 푸시는 사용자 확인 뒤에 한다.

## 11. Codex 검토 반영(2026-11-09 새벽, 결과 `F:\코딩\Index\코덱스방\gravity-spec-review\RESULT.md`)

위 본문보다 이 절이 우선한다.

1. **스캐브 레이드 시간**: 복제본 `EscapeTimeLimit`(분)은 이미 단축된 "남은" 길이다. 계획은 원래 길이(남은 시간 + `SimulatedRaidStartSeconds`)로 만들고, 지연보다 이른 항목을 빼고 그만큼 당긴다(5.3 수정).
2. **봇 상한**: 4.1 클라이언트는 전체 상한을 자기 설정(`MaxBotsAliveOnMap`·봇 수 설정)으로 정한다. `LocationBase.BotMax`는 웨이브 밖 자동 스폰의 목표치다. 그래서 이번 판은 **`BotMax`를 바꾸지 않는다.** 맵별 상한은 클라이언트 플러그인이 필요한 2-2로 넘긴다(5.4 수정, 범위 A에서 빠짐). **[검토 필요]**
3. **스캐브**: 세관 등은 `OfflineNewSpawn`(웨이브 밖 자동 스폰)이 켜져 있다. 그래서 `Waves`만 바꿔서는 스캐브를 통제할 수 없고, 웨이브를 더하면 넘칠 수 있다. **이번 판은 스캐브를 바닐라 그대로 두고 PMC만 관리한다.** `SpawnPlanner`의 스캐브 계획 코드는 남겨 두고, 적용하지 않는다. 스캐브 관리는 2-2에서 자동 스폰 처리와 함께 한다. **[검토 필요]**
4. **상한이 찼을 때 PMC**: 게임 코드상 대기열에서 다시 시도한다. 그래서 하드 스톱 이후에도 밀린 그룹이 나올 수 있다. 인게임에서 확인한다.
5. **원자성**: 적용기는 새 목록을 다 만든 뒤 한 번에 바꾼다. 보스 확률도 다 계산한 뒤 한 번에 쓴다.
6. **그룹 제한 "동시에 살아 있는" 기준**: 이번 판은 레이드 전체로 센다. 웹 안내 띠에 이 점을 밝혔다.
7. **같은 역할을 쓰는 다른 모드 항목**: `pmcUSEC`/`pmcBEAR` 이름만으로 지우므로, 다른 모드가 같은 이름으로 넣은 PMC도 지워진다. 지금 설치본의 팩션 모드(RUAF·UNTAR·BlackDiv)는 다른 역할을 쓴다. 출처를 구분하는 일은 2-2에서 한다.
