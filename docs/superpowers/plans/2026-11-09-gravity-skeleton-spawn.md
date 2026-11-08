# Gravity 뼈대 + 스폰 핵심 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 웹(목업 v013 화면)에서 고친 스폰 설정이 다음 레이드의 PMC·스캐브 웨이브·봇 상한·보스 확률에 반영되는 SPT 4.1 서버 모드.

**Architecture:** 서버 모드 하나. `GenerateLocationAndLoot` 앞뒤 하모니 패치에서 순수 함수 `SpawnPlanner`가 만든 계획을 맵 복제본에 쓴다. 설정은 웹 state 전체를 JSON 한 파일에 보존하고, `IHttpListener`가 `/gravity/` 화면과 API를 낸다.

**Tech Stack:** C# .NET 10, SPTarkov.* 4.1.0 NuGet(Common·DI·Reflection·Server.Core), xUnit, 단일 파일 HTML+JS(목업 v013).

**Spec:** `docs/superpowers/specs/2026-11-09-gravity-skeleton-spawn-design.md`

## Global Constraints

- 클린룸: ABPS·APBS·SVM·SWAG·Donuts 코드·데이터·문구 복사 금지(아이디어만).
- ORBIT 호환: 봇 생성·제거·행동 경로를 건드리지 않는다. 디스폰·컬링 없음.
- 게임 스레드 대기(`Thread.Sleep`) 금지. 레이드 패치 예외는 잡아서 바닐라로 진행.
- DB 원본(`LocationTable`의 맵 Base)을 바꾸지 않는다. 바꾸는 전역 값은 `BotConfig.MaxBotCap[맵]`뿐.
- 숫자 설정은 전부 Clamp(0이 무한대가 되지 않게).
- 화면 글 한국어(+영어 전환), 이모지 금지, 제목에 "—" 금지.
- 커밋 작성자 R_F, AI 트레일러 없음. 저장소 공개 푸시는 사용자 확인 뒤.

## Review Focus

1. 설정 파일이 없거나 깨졌을 때 → 기본값으로 서버가 뜨고 웹에 알림(Task 2 테스트).
2. 레이드 길이가 아주 짧을 때(스캐브 레이드 단축으로 5분 남음) → 계획이 비거나 음수 시각 없이 동작(Task 3 테스트 `ShortRaid_NoNegativeTimes`).
3. 그룹 제한 규칙이 모순(5인 이상 0개 + 분대 비율 5인 100%) → 무한 루프 없이 split/drop(Task 3 테스트 `GroupCap_AllForbidden_Terminates`).
4. 모르는 맵 id(커스텀 맵) → 손대지 않음(Task 5 테스트 `UnknownMap_LeavesLocationUntouched`).
5. 웹에서 숫자 칸에 문자열/null을 보냄 → 400 또는 기본값, 서버 예외 없음(Task 2 테스트 `Load_WrongTypes_FallsBackToDefaults`).

## 파일 구조

```
Gravity.sln
src/Gravity.Server/Gravity.Server.csproj      net10.0, AssemblyName Gravity
src/Gravity.Server/ModMetadata.cs              IModMetadata
src/Gravity.Server/Core/GravityPaths.cs        모드 폴더·config·wwwroot 경로
src/Gravity.Server/Core/ConfigModel.cs         범위 A 설정 형식 + Clamp
src/Gravity.Server/Core/GravityConfigStore.cs  읽기·검증·원자 저장·백업
src/Gravity.Server/Core/ModDetector.cs         다른 모드 감지
src/Gravity.Server/Core/GravityLoader.cs       IOnLoad: 설정·감지·카탈로그·패치 켜기
src/Gravity.Server/Spawn/MapCatalog.cs         맵 기본 정보
src/Gravity.Server/Spawn/SpawnPlanner.cs       순수 계획 함수
src/Gravity.Server/Spawn/SpawnPlan.cs          계획 레코드
src/Gravity.Server/Spawn/RaidSpawnApplier.cs   계획 → LocationBase
src/Gravity.Server/Spawn/RaidStartPatch.cs     Prefix/Postfix
src/Gravity.Server/Web/GravityWeb.cs           IHttpListener
src/Gravity.Server/wwwroot/index.html          목업 v013 → 실제 화면
tests/Gravity.Tests/Gravity.Tests.csproj       xUnit, Gravity.Server 참조
tests/Gravity.Tests/*Tests.cs
```

### Task 1: 솔루션 틀 + 빈 모드가 서버에 뜬다

**Files:** Create `Gravity.sln`, `src/Gravity.Server/Gravity.Server.csproj`, `ModMetadata.cs`, `Core/GravityPaths.cs`, `Core/GravityLoader.cs`, `tests/Gravity.Tests/Gravity.Tests.csproj`, `.gitignore`

**Interfaces:** Produces `GravityPaths.ModDir`, `.ConfigFile`, `.BackupDir`, `.WwwRoot`(string), `GravityLoader : IOnLoad`(TypePriority `OnLoadOrder.PostSptModLoader + 1`)

- [ ] csproj: Sdk `Microsoft.NET.Sdk`, net10.0, PackageReference SPTarkov.Common/DI/Reflection/Server.Core 4.1.0, `wwwroot\**` CopyToOutputDirectory, Release에서 pdb·deps 없음. 빌드 뒤 `Build\SPT_Runtime\user\mods\Gravity\`로 모으는 Target.
- [ ] `ModMetadata`: ModGuid `com.rf.gravity`, Name `Gravity`, Author `R_F`, Version `0.1.0`, SptVersion `~4.1.0`, License `MIT`.
- [ ] `GravityLoader.OnLoadAsync`: 로그 `[Gravity] 0.1.0 로드`.
- [ ] Run `dotnet build -c Release` → 성공, `Build\...\Gravity\Gravity.dll` 있음.
- [ ] Commit `틀: 솔루션·빈 서버 모드`.

### Task 2: 설정 모델·저장소

**Files:** Create `Core/ConfigModel.cs`, `Core/GravityConfigStore.cs`, Test `tests/Gravity.Tests/ConfigStoreTests.cs`

**Interfaces:**
- Produces `record SpawnSettings`(목업 `state.spawn`의 범위 A 칸: `StartPmcPct, FirstDelaySec, IntervalMin, RandomPct, MinGapSec, DistWave, DistBots, Smooth, WaveMin, WaveMax, StopBeforeEndMin, HardStop(Mode, PmcPct, ScavPct), IgnoreCapStart, IgnoreCapWaves, Intensity(Early,Mid,Late), UsecPct, Squad(S1..S5), GroupCap(On, Scope, Over, Rules[{Size,Cmp,Count}]), Diff(Easy,Normal,Hard,Imp), Scav(SeparateStart, DelayMin, ActiveMin, ActiveMax, QuietMin, QuietMax)`)
- `record MapSettings(Override, Style, Inf(DelaySec,Batch,PmcShare,Jitter), StartPmcPct, IntervalMin, WaveMin, WaveMax, Early, Mid, Late, Original, Cap, Zones: Dictionary<string,(bool On,int W)>)`
- `record BossSettings(On, Chance, Maps: Dictionary<string,bool>)`, `record GravityConfig(bool SpawnModule, SpawnSettings Spawn, Dictionary<string,MapSettings> Maps, Dictionary<string,BossSettings> Bosses, (bool On,int Chance) BossCommon)`
- `static GravityConfig ConfigModel.FromState(JsonNode? state, List<string> clamped)` / `GravityConfigStore(string configFile, string backupDir)`: `JsonNode? RawState`, `GravityConfig Current`, `LoadResult Load()`, `SaveResult Save(JsonNode state)`; `SaveResult(bool Ok, string? Error, List<string> Clamped)`.
- 값 범위(Clamp): IntervalMin 0.5–60, FirstDelaySec 0–1800, WaveMin/Max 0–20(Max≥Min), Cap 1–60, Inf.DelaySec 10–600, Inf.Batch 1–10, 백분율 0–100, Squad 합 0이면 S1=100, GroupCap.Count 0–20, Size 1–5.

- [ ] 테스트: `Load_MissingFile_UsesDefaults`, `Load_BrokenJson_MovesToBrokenAndDefaults`(파일이 `backup/broken-*.json`으로 옮겨짐), `Load_WrongTypes_FallsBackToDefaults`(`interval:"abc"` → 6), `Save_ZeroInterval_ClampedTo0_5`(Clamped에 `spawn.interval` 포함), `Save_KeepsUnknownKeys`(`gear.x` 보존), `Save_KeepsFiveBackups`.
- [ ] `dotnet test` 실패 확인 → 구현 → 통과.
- [ ] Commit `설정: 모델·저장소`.

### Task 3: SpawnPlanner(순수 함수)

**Files:** Create `Spawn/SpawnPlan.cs`, `Spawn/SpawnPlanner.cs`, Test `tests/Gravity.Tests/SpawnPlannerTests.cs`

**Interfaces:**
- Consumes `GravityConfig`, `MapSettings`(Task 2)
- Produces `record MapInfo(string Id, int BasePmc, int BaseScav, string[] PmcZones, string[] ScavZones)`,
  `record PmcGroup(int TimeSec, bool Usec, int Size, string Difficulty, bool IsStart)`,
  `record ScavWave(int TimeMinSec, int TimeMaxSec, int SlotsMin, int SlotsMax)`,
  `record SpawnPlan(List<PmcGroup> Groups, List<ScavWave> ScavWaves, int BotCap, string Summary)`,
  `static SpawnPlan SpawnPlanner.Plan(GravityConfig cfg, MapInfo map, int raidSec, int simStartSec, int seed)`
- 규칙: 목업 `plan()`(웨이브 시각·세기)·`gcEstimate()`(그룹 제한) 그대로. 시각은 분 → 초. 스캐브: 첫 웨이브 = `separateStart ? delayMin*60 : 0`, 이후 활동 `active` 분 동안 60–120초 간격 웨이브, 휴식 `quiet` 분 쉼, 레이드 끝 − `hardStop.scavPct`까지; 웨이브당 slots = max(1, round(BaseScav × 웨이브간격/총시간 비례)). 시작 지연: `TimeSec < simStartSec` 항목 제거 후 `TimeSec -= simStartSec`.
- [ ] 테스트: `SameSeed_SamePlan`, `NoGroupAfterHardStop`, `SimStart_RemovesEarlyAndShifts`, `ShortRaid_NoNegativeTimes`(raid 300s, simStart 280s), `GroupCap_Split`(목업 v012 검증값: s1..s5 20% 균등, PMC 46 → 5인 1·3인 1, 넘친 16명은 4인 4개), `GroupCap_Drop_LosesPlayers`, `GroupCap_AllForbidden_Terminates`, `Infinite_StyleProducesWavesUntilEnd_Max400`, `UsecPct100_AllUsec`, `GroupSizesBetween1And5`.
- [ ] 실패 확인 → 구현 → 통과. Commit `스폰: 계획 함수`.

### Task 4: MapCatalog·ModDetector

**Files:** Create `Spawn/MapCatalog.cs`, `Core/ModDetector.cs`, Test `tests/Gravity.Tests/ModDetectorTests.cs`

**Interfaces:**
- `MapCatalog(LocationTable, BotConfig)`: `void Build()`(OnLoad 때), `bool TryGet(string id, out MapInfo info)`, `IReadOnlyList<MapMeta> All` where `record MapMeta(string Id, int RaidMin, int BotMax, string[] PmcZones, string[] ScavZones, Dictionary<string,double> Bosses /*역할→바닐라 확률*/)`.
  - BasePmc = 맵 `BossLocationSpawn` 중 pmcUSEC/pmcBEAR 항목의 (1+평균 호위수) 합 + `PmcConfig.CustomPmcWaves[맵]` 같은 계산; BaseScav = assault `Waves`의 (SlotsMin+SlotsMax)/2 합. PmcZones = PMC 항목 BossZone 합집합(없으면 `OpenZones`), ScavZones = assault Waves SpawnPoints 합집합.
  - 대상 맵: bigmap, woods, factory4_day, factory4_night, rezervbase, shoreline, interchange, laboratory, lighthouse, tarkovstreets, sandbox, sandbox_high, labyrinth.
- `ModDetector`: `static DetectResult Detect(IEnumerable<string> assemblyNames, IEnumerable<string> modFolderNames)`, `record DetectResult(bool Abps, bool Apbs, bool Svm, bool Orbit, bool Sain, bool Ruaf, bool Untar, bool BlackDiv, bool MoreBots, bool QuestingBots, string? SvmFolder)`; `SpawnLocked => Abps`.
- [ ] 테스트: `Abps_LocksSpawn`, `NoMods_NothingLocked`, `SvmFolderBracketsDetected`(`[SVM] Server Value Modifier`).
- [ ] 구현 → 통과. Commit `감지·맵 정보`.

### Task 5: RaidSpawnApplier + RaidStartPatch

**Files:** Create `Spawn/RaidSpawnApplier.cs`, `Spawn/RaidStartPatch.cs`, Test `tests/Gravity.Tests/ApplierTests.cs`; Modify `Core/GravityLoader.cs`(패치 Enable)

**Interfaces:**
- `static void RaidSpawnApplier.Apply(LocationBase loc, SpawnPlan plan, GravityConfig cfg, MapSettings map, MapInfo info)`
- `static void RaidSpawnApplier.ApplyBosses(LocationBase loc, GravityConfig cfg)`; 역할→id 표: bossBully=reshala, bossKojaniy=shturman, bossTagilla=tagilla, bossGluhar=glukhar, bossSanitar=sanitar, bossKilla=killa, bossBoar=kaban, bossKolontay=kollontay, bossZryachiy=zryachiy, bossKnight=goons, bossPartisan=partisan.
- `RaidStartPatch : AbstractPatch` 대상 `LocationLifecycleService.GenerateLocationAndLoot`; Prefix(`MongoId sessionId`) → `[ThreadStatic] RaidChanges? _pending`; Postfix(`string name, bool generateLoot, ref LocationBase __result`).
- `GravityState`(정적 싱글턴 대신 DI Singleton `GravityRuntime`): `GravityConfig Config`, `DetectResult Detect`, `MapCatalog Catalog`, `LastRaid? LastRaid`.
- [ ] 테스트(LocationBase를 직접 만들어서): `Apply_RemovesPmcAndAssault_KeepsBossesAndFactions`(bossBully·`exUsec`·RUAF 역할 항목 남음, marksman 웨이브 남음), `Apply_OriginalTrue_KeepsAssaultWaves`, `Apply_WritesGroupFields`(BossEscortAmount "2" for size 3, BossChance 100, IgnoreMaxBots, SpawnMode), `Apply_SetsBotMax`, `ApplyBosses_OffMap_ChanceZero`, `UnknownMap_LeavesLocationUntouched`(Postfix 로직 함수 `TryApplyToRaid(name, loc, raidChanges)`로 분리해 시험).
- [ ] 구현 → 통과. `BotConfig.MaxBotCap[name]=cap` 설정은 Postfix 안. 예외 → `logger.Warning` + 원래 `__result` 유지(복제본을 먼저 만들어 고친 뒤 성공 시에만 바꿔 넣음).
- [ ] 레이드 요약 로그 한 줄(spec 5.6 형식) + `LastRaid` 저장.
- [ ] Commit `스폰: 레이드 시작 적용`.

### Task 6: 웹 리스너

**Files:** Create `Web/GravityWeb.cs`, Test `tests/Gravity.Tests/WebPathTests.cs`

**Interfaces:** `GravityWeb : IHttpListener`, `[Injectable(TypePriority = 0)]`; `CanHandle`: 경로가 `/gravity` 또는 `/gravity/`로 시작. `static string? ResolveStatic(string wwwroot, string requestPath)`(`..` 거부, `/gravity/` → index.html).
- 라우트: spec 4절 표. POST는 `context.Connection.RemoteIpAddress`가 루프백일 때만, 본문 2MB 상한. 응답 JSON UTF-8, `Cache-Control: no-store`.
- `/gravity/api/meta` 내용: `{version, detect, locked:{spawn,gear}, maps:[MapMeta...], svm:{raidAddMin?}}`.
- [ ] 테스트: `Resolve_Root_IsIndex`, `Resolve_DotDot_Null`, `Resolve_Asset`.
- [ ] 구현 → 통과. Commit `웹: 리스너·API`.

### Task 7: 웹 화면(목업 v013 → 실제)

**Files:** Create `src/Gravity.Server/wwwroot/index.html`(v013 복사 후 수정)

- [ ] 시작: `fetch('api/meta')`·`fetch('api/state')` → meta로 `MAPS`(raid 감지값·cap 기본)·`ZONES`(실제 구역 id, 이름은 id 그대로)·`BOSSES`(맵 존재 여부) 덮어쓴 뒤 `DEFAULTS` 재계산, state가 있으면 덮어씀. 실패 시 화면 위 오류 띠.
- [ ] 저장 버튼 → `POST api/state`; 응답 Clamped 목록을 toast로.
- [ ] 잠금: `locked.spawn`이면 스폰 페이지 상단 "ABPS가 설치되어 스폰 모듈이 꺼져 있습니다" 띠 + 스폰 모듈 스위치 비활성.
- [ ] 미구현 띠: 페이지 near·specials·factions·events·gear 계열·blacklist·orbit 연동 + 카드(핫스팟·근처, 보스 구역·호위) → "아직 게임에 적용되지 않음(다음 판)".
- [ ] 진단 페이지에 `api/lastraid` 요약.
- [ ] 브라우저(SPT 서버 켜고 `https://127.0.0.1:6969/gravity/`)에서 21페이지 렌더 오류 0, 저장 → 파일 반영 확인. Commit `웹: 실제 화면`.

### Task 8: 인게임 검증(SlopBridge)

- [ ] 게임·서버 꺼짐 확인 → `user/mods/Gravity` 설치(새 폴더라 백업 대상 없음, 프로필 백업) → `probe-env.ps1 set -Hide acidphantasm-botplacementsystem`(ABPS만) → `bridge-kit.ps1 install`.
- [ ] 서버 로그: `[Gravity] 0.1.0 로드`, 감지 목록, 오류 없음.
- [ ] 세관 레이드(`probe-raid.ps1 -Map bigmap`): 시작 로그 계획 vs 30초 간격 살아 있는 봇(역할·그룹) 10분 기록. 상한 도달 때 웨이브 대기/버림 판정.
- [ ] 공장 레이드 1판(짧은 맵).
- [ ] `errors.log`·LogOutput·서버 콘솔 확인.
- [ ] 되돌리기: `bridge-kit.ps1 remove`, `probe-env.ps1 restore`. Gravity는 남기되 결과 보고 때 사용자에게 유지 여부 확인.
- [ ] 결과를 `docs/superpowers/specs` 옆 `docs/verification/2026-11-09-ingame.md`에 기록, Commit.
