# 스캐브 웨이브 목록 + 1.0.6 마무리 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (사용자 규칙: 멀티 에이전트는 허락 없이 안 씀 → 이 세션에서 직접). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 스캐브 방식에 "웨이브 목록 (PMC처럼)"을 넣어 버블로 보고 끌어 고치게 하고, 1.0.5→1.0.6 전체를 한 번에 시험·빌드할 수 있게 마무리한다.

**Architecture:** 웹 state `maps.<id>.scavStyle="list"`·`scavList`를 서버가 읽어 `ScavDto.List`로 클라에 보낸다. 판마다의 시각·인원은 공용 `ScavWaveList`(순수 로직)가 시드로 정하고, 클라 `ScavDirector`가 2초 틱마다 낼 수를 받아 기존 `SpawnScavs`로 넣는다. 웹은 타임라인 스캐브 줄에 버블·드래그·생성기·복사·확대를 그린다.

**Tech Stack:** SPT 4.1 C# 서버 모드(net9/10), `Gravity.Shared`(netstandard2.1), BepInEx 클라, 단일 파일 웹 `wwwroot/index.html`, xUnit, Node `node --test`.

**Spec:** `docs/superpowers/specs/2026-11-14-scav-wave-list-design.md` (그리고 1.0.6 전체 계획 `docs/superpowers/plans/2026-11-14-1.0.6.md`, 검증 보고서 `docs/reports/2026-11-14-1.0.6-validation.md`)

## Global Constraints

- 기본 `scavStyle="waves"` 그대로 → 새 칸이 없으면 결과가 1.0.5·지금 main과 같다(테스트로 고정).
- 웹 표시 이름: `waves` = "활동·휴식 채우기 / Fill (active·quiet)", `list` = "웨이브 목록 (PMC처럼) / Wave list (like PMCs)". 값 이름은 바꾸지 않는다.
- 목록: 최대 60개, `t` 0~레이드 분, `min`·`max` 0~20(max ≥ min), 서버가 시각순 정렬. `scavListJitter` 0~120초 기본 20. 상한 재시도 창 60초. 드래그 맞춤 30초(4× 확대 10초).
- 옛 클라 보호: `list`일 때 `ScavDto.Style="start"` + 새 끝 칸 `List`.
- 새 반복문은 목록 길이(≤60)·인원(≤20)으로 끝이 정해져야 한다(PROJECT-RULES 2-5).
- 웹 새 글자는 모두 `T(ko,en)`, 한국어 보고서체. 커밋 작성자 R_F, Claude 트레일러 없음, main 직푸시.
- .NET 테스트는 `LANG=ko_KR.UTF-8 LC_ALL=ko_KR.UTF-8`로 돌린다.

## Review Focus

- 레이드가 짧은 맵(공장 20분)에 40분짜리 목록을 복사 → 길이 밖 웨이브는 버리고 버린 수를 알린다(Task 6 테스트).
- 스캐브 레이드(늦게 들어온 판, sim>0) → 이미 지난 웨이브는 내지 않고, 시각은 원래 레이드 기준(Task 2·4 테스트).
- 상한이 계속 차 있는 판 → 웨이브 인원이 60초 뒤 버려지고 다음 웨이브와 합쳐 한꺼번에 몰리지 않는다(Task 2 테스트).
- 목록 방식인데 목록이 비어 있음(사용자가 다 지움) → 시작 스캐브만, 오류 없음(Task 3·4 테스트).
- 하드 스톱 뒤 웨이브·인원 0~0 웨이브 → 건너뜀(Task 2 테스트).

---

### Task 1: 설정 읽기 (서버)

**Files:**
- Modify: `src/Gravity.Server/Core/ConfigModel.cs` (`MapSettings` 끝 칸 추가, `scavStyle` 허용값, 읽기 함수)
- Test: `tests/Gravity.Tests/ScavWaveListTests.cs` (새 파일, 클래스 `ScavListConfigTests`)

**Interfaces:**
- Produces: `public sealed record ScavListWave(double TMin, int Min, int Max);` · `MapSettings.ScavList : IReadOnlyList<ScavListWave>` (기본 빈 목록) · `MapSettings.ScavListJitterSec : double` (기본 20) · `MapSettings.ScavStyle`에 `"list"` 허용.

- [ ] Step 1: 테스트 `Default_IsWavesAndEmptyList`(기본 `ScavStyle=="waves"`, `ScavList.Count==0`, 지터 20), `List_SortedClampedCapped`(입력 순서 섞인 3개 → 시각순, `min 30→20`, `max<min → max=min`, `t -5 → 0`, 70개 입력 → 60개), `BadEntries_Ignored`(숫자 아닌 칸·배열 아님 → 건너뜀, 예외 없음), `Jitter_Clamped`(500 → 120).
- [ ] Step 2: 실패 확인 `dotnet test tests/Gravity.Tests --filter ScavListConfigTests`.
- [ ] Step 3: 구현. 경로 `maps.<id>.scavList`(배열)·`maps.<id>.scavListJitter`. 맵 칸 그대로(카드 덮어쓰기 아님). `t`는 레이드 길이로 자르지 않는다(서버 계획에서 자름).
- [ ] Step 4: 통과 확인, 전체 테스트도 그대로 통과.
- [ ] Step 5: 커밋 `feat(config): 스캐브 웨이브 목록 설정 읽기(scavStyle=list, scavList, scavListJitter)`.

### Task 2: 공용 판 일정 `ScavWaveList`

**Files:**
- Create: `src/Gravity.Shared/ScavWaveList.cs`
- Modify: `src/Gravity.Shared/ClientDto.cs` (`ScavDto` 끝 칸)
- Test: `tests/Gravity.Tests/Shared/ScavWaveListTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ScavWaveDto(double TimeSec, int Min, int Max);`
  - `ScavDto` 끝 칸 추가: `ScavWaveDto[]? List = null, double ListJitterSec = 0` (원래 레이드 기준 초).
  - `public sealed class ScavWaveList { public ScavWaveList(ScavWaveDto[] list, double jitterSec, double offsetSec, double hardStopSec, Random rng); public int Due(double elapsed, int room); public int Dropped { get; } public int Count { get; } public string Describe(); }`
    - 생성 때 각 웨이브 실제 시각 = `TimeSec ± jitter`(0 이상) − `offsetSec`(클라 경과 기준), 인원 = `rng.Next(Min, Max+1)`. `offsetSec` 지난 웨이브(실제 시각 < 0)·`hardStopSec` 이후·인원 0은 버린다(버림 수에 안 셈, 계획 단계 제외).
    - `Due(elapsed, room)`: 시각이 된 웨이브의 남은 인원을 모아 `room`까지 낸다(남은 건 웨이브에 남김). 시각 뒤 60초가 지난 웨이브의 남은 인원은 `Dropped`에 더하고 없앤다. 한 번 호출의 반복은 웨이브 수로 끝.

- [ ] Step 1: 테스트 `Due_OnTime_WithJitterRange`(지터 20, 시드 고정, 실제 시각이 ±20초 안), `Due_RoomLimited_CarriesThenDropsAfter60s`(인원 5, room 2 → 2 낸 뒤 61초에 남은 3 버림, `Dropped==3`), `Waves_NotMergedAfterDrop`(앞 웨이브 버린 뒤 다음 웨이브는 자기 인원만), `ScavRaid_PastWavesSkipped`(offset 600 → 시각 300 웨이브 없음, 900 웨이브는 경과 300에), `HardStop_And_Zero_Skipped`, `SameSeed_SameSchedule`.
- [ ] Step 2: 실패 확인 `--filter ScavWaveListTests`.
- [ ] Step 3: 구현(위 Interfaces). `ScavDto` 칸 추가는 끝에, 기본값 있게. `DtoJsonTests`처럼 옛 JSON(칸 없음) 역직렬화 테스트 1개 추가: `Old_ScavDto_Json_ListNull`.
- [ ] Step 4: 통과 확인.
- [ ] Step 5: 커밋 `feat(shared): 스캐브 웨이브 목록 판 일정(ScavWaveList) — 지터·60초 재시도·스캐브 레이드·하드 스톱`.

### Task 3: 서버 계획·클라 설정

**Files:**
- Modify: `src/Gravity.Server/Spawn/SpawnPlanner.cs` (`PlanScavs`), `src/Gravity.Server/Spawn/RaidSpawnApplier.cs` (클라 없을 때 목록 → 게임 웨이브), `src/Gravity.Server/Client/ClientSettingsBuilder.cs`, `src/Gravity.Server/Core/GravityRuntime.cs` (판 시작 로그 한 줄)
- Test: `tests/Gravity.Tests/ScavWaveListTests.cs` (클래스 `ScavListPlanTests`)

**Interfaces:**
- Consumes: Task 1 `MapSettings.ScavList/ScavListJitterSec`, Task 2 `ScavWaveDto`·`ScavDto.List`.
- Produces: `list`일 때 `plan.ScavWaves`엔 시작 스캐브만(IsStart). 클라 DTO `Style="start"`, `List`=레이드 길이 안 웨이브(초), `ListJitterSec`. 클라 없음이면 `RaidSpawnApplier`가 목록을 게임 웨이브로(웨이브당 1명씩, 시작 스캐브와 같은 구역 순환 방식, `TimeMin=TimeMax=초`, 하드 스톱 뒤 제외).

- [ ] Step 1: 테스트 `List_PlanHasOnlyStartScavs`, `List_Dto_StyleStart_WithList`(세관 목록 3개 → `Style=="start"`, `List.Length==3`, 초 단위), `List_Dto_DropsBeyondRaid`(공장 20분에 t=30 → 빠짐), `List_Empty_NoError`, `Waves_Default_Unchanged`(기본 설정 DTO·계획이 지금과 같음: Style "waves", List null), `NoClient_List_BecomesNativeWaves`(hasClient=false → `loc.Waves`에 목록 인원만큼 1명 웨이브).
- [ ] Step 2: 실패 확인.
- [ ] Step 3: 구현. 판 시작 로그(diag): `스캐브 웨이브 목록 {n}개(총 {a}~{b}마리)` / `scav wave list {n} ({a}–{b})`.
- [ ] Step 4: 통과 + 전체 테스트.
- [ ] Step 5: 커밋 `feat(spawn): 스캐브 웨이브 목록을 클라로 보내고(옛 클라는 시작만), 클라 없으면 게임 웨이브로`.

### Task 4: 클라 `ScavDirector` (클라우드 빌드 불가 — 코드만, 로컬 빌드)

**Files:**
- Modify: `src/Gravity.Client/Directors/ScavDirector.cs` (`State`에 `ScavWaveList? List`, `Tick`)

**Interfaces:**
- Consumes: Task 2 `ScavWaveList`, `ScavDto.List/ListJitterSec/CurveOffsetSec`.
- Produces: `List != null`이면 스캐브 채우기 차례를 쓰지 않고(`ScavSchedule`은 PMC 무한 차례만 — 서버가 Style "start"를 보내므로 그대로 StartOnly), 2초 틱마다 `room = cap − 살아있는·로딩·대기 − PMC 예약`으로 `List.Due(elapsed, room)`만큼 `SpawnScavs`. 낸 수가 0이고 남은 인원이 있으면 `MarkCap`. 판 끝/2분마다 자세히 로그: `스캐브 웨이브 {i}/{n}: 낸 {k} · 상한으로 버림 {Dropped}`.

- [ ] Step 1: 구현(시드 = `c.Settings.Seed ^ 0x5CA1`, hardStop은 기존 `_hardStop`과 같은 값, offset = `CurveOffsetSec`).
- [ ] Step 2: 서버·공용 빌드 확인 `dotnet build src/Gravity.Server -c Release`(클라는 로컬 Task 9).
- [ ] Step 3: 커밋 `feat(client): 스캐브 웨이브 목록대로 내기(채우기 끔), 진단 줄`.

### Task 5: 웹 — 방식·버블·드래그·창

**Files:**
- Modify: `src/Gravity.Server/wwwroot/index.html`
- Test: `tests/web/ux.test.cjs`

**Interfaces:**
- Produces (JS):
  - `scavListOf(mid) → [{t,min,max}]` (시각순), `scavGen(mid, opts?) → [{t,min,max}]` 생성기(첫 웨이브 `first`분, 간격 `interval`분, `min`·`max`, 인원 × `scav.curve` 시간대 배수 반올림, 하드 스톱 전까지, 최대 60).
  - 방식 seg에 `['list', T('웨이브 목록 (PMC처럼)','Wave list (like PMCs)')]`, `waves` 이름 변경. `list`를 처음 고르고 목록이 비면 `scavGen`으로 채움.
  - 타임라인 스캐브 줄(`list`): 버블 `data-pop="scavw|<i>"`, 편집 중 `data-drag="scavw|<i>"`; 드래그 값 함수 `dragVals('scavw', i, t, raid, m)` → `[['maps.<id>.scavList.<i>.t', 맞춘 분]]`(30초, 확대 4×면 10초); 드래그 중 말풍선 `<g class="tl-drag-tip">` 글자 `mm:ss · a~b마리`.
  - 팝업 `scavw`: 시각·최소·최대 행 + 삭제·"뒤에 추가"(`data-act="scavwDel|scavwAdd" data-v="<mid>|<i>"`). 팝업 `scavgen`(줄 머리): 생성기 값 + "다시 채우기"(창 안 확인 두 번 누름).
  - 예상 인원: `estimate`의 스캐브 = 시작 + 목록 합, `capNeed.scav` = 가장 큰 웨이브 최대 + 시작 스캐브.

- [ ] Step 1: 테스트 `list 방식: 처음 고르면 생성기로 채움`, `버블·인원 표시`(`3~5`), `드래그 값 30초 맞춤·범위·4× 10초`, `scavw 창 추가·삭제·정렬`, `생성기에 PMC 반대로 곡선 반영`(0.5·1·1.5 → 후반 웨이브 인원이 큼), `waves 기본은 그대로`(기존 타임라인 문자열 유지), `estimate·capNeed가 목록을 셈`.
- [ ] Step 2: 실패 확인 `node --test tests/web/ux.test.cjs`.
- [ ] Step 3: 구현.
- [ ] Step 4: 통과.
- [ ] Step 5: 커밋 `feat(web): 스캐브 웨이브 목록 — 버블·드래그 말풍선·창·생성기`.

### Task 6: 웹 — 다른 맵에 복사·확대

**Files:**
- Modify: `src/Gravity.Server/wwwroot/index.html`
- Test: `tests/web/ux.test.cjs`

**Interfaces:**
- Produces: `copyScavList(from, toIds) → {copied:n, dropped:{<mid>:k}}` (대상 맵 `scavStyle=list`, `scavList`(그 맵 레이드 길이 밖 제외), `scavListJitter` 덮어씀) · 생성기 창의 맵 칩 다중 선택 + "복사" 버튼(`data-act="scavCopy"`) · 토스트 `{n}개 맵에 복사, 길이 밖 {k}개 뺌`.
  - 확대: `tlZoom` ∈ {1,2,4}, localStorage `gravity.tlZoom`, 타임라인 위 버튼 `data-act="tlZoom" data-v="2"`, SVG `style="width:<배수×100>%"`·`.tl` 가로 스크롤.

- [ ] Step 1: 테스트 `복사: 공장(짧은 맵)엔 길이 밖 웨이브를 빼고 개수를 알림`, `복사 대상 방식이 list로 바뀜`, `확대 배수 저장·SVG 폭`, `확대 4×에서 드래그 맞춤 10초`.
- [ ] Step 2~4: 실패 확인 → 구현 → 통과.
- [ ] Step 5: 커밋 `feat(web): 스캐브 웨이브 목록 다른 맵에 복사, 타임라인 확대 1·2·4배`.

### Task 7: 문서·버전 기록·시연

**Files:**
- Modify: `README.md`, `README.en.md` (1.0.6 문단에 한 줄), `changelog/` (KB `tools/changelog.py add`), `docs/reports/2026-11-14-1.0.6-validation.md`, `#demo` 예시 자료(세관 `scavStyle:'list'` 대신 리저브에 예시 목록 — 세관 예시는 그대로)
- KB: `projects/Gravity/NOTES.md` 상태 절, INBOX #43 note, 방명록.

- [ ] Step 1: 문서 갱신, 웹 테스트(시연 자료) 통과.
- [ ] Step 2: 커밋·푸시, 아티팩트 시연 페이지 재게시(같은 파일 경로).

### Task 8: 1.0.6 전체 점검 (클라우드)

- [ ] Step 1: `LANG=ko_KR.UTF-8 LC_ALL=ko_KR.UTF-8 dotnet test tests/Gravity.Tests -c Release` 전부 통과, `node --test tests/web/` 전부 통과, `dotnet build src/Gravity.Server -c Release` 경고 0.
- [ ] Step 2: 1.0.5 태그 대비 바뀐 것 목록 `git log v1.0.5..HEAD --oneline`을 검증 보고서 "커밋" 표에 맞춤.
- [ ] Step 3: Playwright 폰·데스크톱 캡처 1장씩(세관 목록 방식 펼친 타임라인) 눈으로 확인.

### Task 9: 로컬 (사용자 PC — 집)

- [ ] Step 1: `git pull` → `dotnet build Gravity.sln -c Release`(TarkovDir) 클라 포함 경고·오류 0 → zip·SHA256.
- [ ] Step 2: 실제 판(사용자): 1.0.6 검증 보고서 "남은 것" 전부 + 세관 목록 방식 긴 판(로그 웨이브별 시각·인원 = 버블, 웨이브 사이 채우기 없음, 상한 막힘 60초 뒤 버림), 기존 채우기 방식 한 판(예전과 같은지).
- [ ] Step 3: 통과 뒤 R-1: 노트 4종(GitHub·아카·디코·Forge) 묻기.
