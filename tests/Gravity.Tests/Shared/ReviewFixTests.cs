using System.Text.Json.Nodes;
using Gravity.Client;
using Gravity.Core;
using Gravity.Shared;
using Gravity.Spawn;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Gravity.Tests.Shared;

/// <summary>1.0 출시 전 Codex 전체 검토(높음 1·중간 4·낮음 1) 수정의 회귀 검사.</summary>
public class ReviewFixTests
{
    private static GravityConfig Read(string json) => ConfigModel.FromState(JsonNode.Parse(json), []);

    // ── 1번: 동시 상한 = 생존·로딩·대기·예약, 제외 팩션 뺌 ──

    [Theory]
    [InlineData(24, 0, 1, false)] // 생존 23 + 로딩 1
    [InlineData(25, 0, 1, false)] // 생존 23 + 로딩 1 + 대기 1 (게임 점유에 대기열 포함, 이미 넘침)
    [InlineData(24, 6, 1, true)]  // 전체 24 − 제외 팩션 6 → 자리 6
    [InlineData(24, 6, 6, true)]
    [InlineData(24, 6, 7, false)]
    public void CapLedger_CountsLoadingWaitingAndExcluded(int occupied, int excluded, int size, bool allowed)
    {
        var l = new CapLedger();
        Assert.Equal(allowed, l.TryReserve(new object(), size, 24, occupied, excluded, 0));
    }

    [Fact]
    public void CapLedger_OverlappingAsyncRequests_NeverExceedCap()
    {
        var l = new CapLedger();
        object extract = new(), near = new(), refill = new();
        // 상한 24, 게임 점유 20: 탈출 보충 3명 예약 뒤 근처 보충 2명은 자리가 1뿐이라 거부
        Assert.True(l.TryReserve(extract, 3, 24, 20, 0, 0));
        Assert.False(l.TryReserve(near, 2, 24, 20, 0, 0));
        Assert.True(l.TryReserve(refill, 1, 24, 20, 0, 0));
        Assert.Equal(4, l.Held(0));
        Assert.Equal(0, CapLedger.Room(24, 20, 0, l.Held(0)));
        // 탈출 분대가 게임 집계에 잡히면(점유 23) 예약을 풀어도 자리는 그대로 계산된다
        Assert.True(l.Release(extract));
        Assert.False(l.Release(extract));
        Assert.False(l.TryReserve(near, 2, 24, 23, 0, 0));
        Assert.True(l.TryReserve(near, 0, 24, 22, 0, 0)); // 크기 0은 1로 친다
    }

    [Fact]
    public void CapLedger_WaitingShareExpiresWithoutRetry_AndNoCapMeansUnlimited()
    {
        // 대기 중 예약만 기한이 있다: 대기열 재시도가 3초마다 늘리고, 재시도가 끊기면(대기열에서 사라짐) WaitTtl 뒤 풀린다.
        var l = new CapLedger();
        object a = new();
        Assert.True(l.TryReserve(a, 5, 10, 5, 0, 0));
        l.Shrink(a, 4, waiting: true, now: 0);
        Assert.False(l.TryReserve(new object(), 2, 10, 5, 0, 10));
        l.Hold(a, 4, true, 20); // 재시도로 기한 연장
        Assert.Equal(4, l.Held(20 + CapLedger.WaitTtl - 1));
        Assert.Equal(0, l.Held(20 + CapLedger.WaitTtl + 1));
        Assert.Equal(int.MaxValue, CapLedger.Room(0, 500, 0, 500));
    }

    [Fact]
    public void CapLedger_SquadKeepsFollowerShare_UntilSupportsDone()
    {
        // 재검토 1번: 상한 24, 기존 19. A 분대 5명 예약 → 분대장만 생성 처리(게임 점유 20) → 대원 4명 몫은 계속 잡혀 B 4명은 거부
        var l = new CapLedger();
        object a = new(), b = new();
        Assert.True(l.TryReserve(a, 5, 24, 19, 0, 0));
        l.Shrink(a, 4, waiting: false, now: 1);
        Assert.Equal(4, l.Held(1));
        Assert.False(l.TryReserve(b, 4, 24, 20, 0, 1));
        Assert.Equal(0, CapLedger.Room(24, 20, 0, l.Held(1)));
        // 대원 생성이 끝나면(점유 24) 예약을 풀어도 자리는 0 그대로 = 총원 24 유지
        Assert.True(l.Release(a));
        Assert.Equal(0, CapLedger.Room(24, 24, 0, l.Held(2)));
    }

    [Fact]
    public void CapLedger_WaitingSquad_KeepsFollowersOutOfNativeCheck()
    {
        var l = new CapLedger();
        object a = new(), b = new();
        Assert.True(l.TryReserve(a, 5, 24, 10, 0, 0));
        // 게임 대기열은 5인 분대를 1로 센다 → 대원 4명은 대기 중 예약으로 남는다(앞단 검사는 센다)
        l.Shrink(a, 4, waiting: true, now: 1);
        Assert.Equal(4, l.Held(1));
        Assert.Equal(0, l.HeldInFlight(1, null));
        // 진행 중인 다른 요청은 게임 자체 검사에도 더하되 자기 요청은 뺀다
        Assert.True(l.TryReserve(b, 3, 24, 11, 0, 1));
        Assert.Equal(3, l.HeldInFlight(1, a));
        Assert.Equal(0, l.HeldInFlight(1, b));
        // 없는 요청(서버 계획 웨이브)은 줄여도 생기지 않는다, 0이면 풀린다
        l.Shrink(new object(), 3, false, 1);
        l.Shrink(b, 0, false, 1);
        Assert.False(l.IsHeld(b));
        Assert.Equal(4, l.Held(1));
    }

    [Fact]
    public void CapLedger_InFlightRequest_NeverExpiresByTime()
    {
        // 3차 재검토 2번: 진행 중인 요청은 시간만으로 풀지 않는다(완료·실패·취소·대기열에서 버림·레이드 끝에만).
        var l = new CapLedger();
        object a = new();
        Assert.True(l.TryReserve(a, 5, 24, 19, 0, 0));
        Assert.Equal(5, l.Held(90));
        Assert.Equal(5, l.Held(100000));
        Assert.False(l.TryReserve(new object(), 1, 24, 19, 0, 100000));
        l.Shrink(a, 4, waiting: false, now: 100000); // 분대장 반영 → 대원 몫은 진행 중(기한 없음)
        Assert.Equal(4, l.Held(1e9f));
        Assert.True(l.Release(a));
    }

    [Fact]
    public void CapLedger_PlannedSquadWithoutPreReservation_HoldsWholeSquad()
    {
        // 3차 재검토 1번: 앞단 예약 없는 서버 계획 분대 A(5명)가 게임 상한 검사를 통과하면 그때 5명 전체를 잡는다(Hold).
        // 기존 19 + A 5 → 분대장만 반영(점유 20, 대원 4 진행 중) → 근처/무한 보충 B 4명은 거부
        var l = new CapLedger();
        object a = new(), b = new();
        l.Shrink(a, 4, false, 0);           // 예약이 없으면 Shrink는 아무것도 안 한다(바닐라 웨이브 보호)
        Assert.False(l.IsHeld(a));
        l.Hold(a, 5, false, 0);              // 계획 분대 통과 순간
        l.Shrink(a, 4, false, 1);            // 분대장 생성 처리
        Assert.False(l.TryReserve(b, 4, 24, 20, 0, 1));
        // 계획 분대가 대기열로 가면(게임은 1로 셈) 대원 4명은 대기 중 예약으로 남는다
        object q = new();
        l.Hold(q, 4, true, 1);
        Assert.Equal(8, l.Held(1));
        Assert.Equal(4, l.HeldInFlight(1, null));
    }

    [Fact]
    public void PlannedPmcIndex_ClaimsOnlyServerSquads()
    {
        var idx = new PlannedPmcIndex([new PlannedPmcDto(true, 120, 3, "ZoneA"), new PlannedPmcDto(true, 120, 3, ""), new PlannedPmcDto(false, 1, 1, "")]);
        Assert.Equal(3, idx.Left);
        Assert.True(idx.TryClaim(true, 120.2f, 3, "ZoneA"));
        Assert.True(idx.TryClaim(true, 119.8f, 3, "other"));  // 구역 문자열은 보지 않는다
        Assert.False(idx.TryClaim(true, 120f, 3, "ZoneA"));   // 같은 서명은 계획 수만큼만
        Assert.False(idx.TryClaim(false, 120f, 3, ""));       // 진영이 다른 다른 모드 PMC
        Assert.False(idx.TryClaim(false, 1f, 2, ""));         // 인원이 다름
        Assert.True(idx.TryClaim(false, 0.4f, 1, ""));        // 서버는 시각을 최소 1초로 넣는다
        Assert.Equal(0, idx.Left);
        Assert.False(new PlannedPmcIndex(null).TryClaim(true, 1, 1, ""));
    }

    [Fact]
    public void Builder_SendsPlannedPmcSignatures_MatchingAppliedWaves()
    {
        var map = new MapInfo("bigmap", 16, 20, 2400, ["A", "B"], ["A"]);
        var cfg = Read("""{"maps":{"bigmap":{"zones":{"B":{"on":false}}}}}""");
        var plan = SpawnPlanner.Plan(cfg, map, 2400, 0, 9);
        Assert.NotEmpty(plan.Groups);
        var dto = ClientSettingsBuilder.Build(cfg, map, plan, true);
        Assert.Equal(plan.Groups.Count, dto.PlannedPmc!.Count);
        for (int i = 0; i < plan.Groups.Count; i++)
        {
            Assert.Equal(plan.Groups[i].Usec, dto.PlannedPmc[i].Usec);
            Assert.Equal(Math.Max(1, plan.Groups[i].TimeSec), dto.PlannedPmc[i].TimeSec);
            Assert.Equal(plan.Groups[i].Size, dto.PlannedPmc[i].Size);
        }
        var idx = new PlannedPmcIndex(dto.PlannedPmc);
        Assert.All(plan.Groups, g => Assert.True(idx.TryClaim(g.Usec, Math.Max(1, g.TimeSec), g.Size, null)));
    }

    // ── 2번: 레이드 전체 그룹 제한 장부 ──

    private static GroupCapDto Cap(string scope, int size, string cmp, int count) =>
        new(true, scope, "split", [new GroupCapRuleDto(size, cmp, count)]);

    [Fact]
    public void RaidGroupLedger_RaidScope_CountsPlannedAndNeverReturns()
    {
        // 1인 분대 0개: 탈출 보충 1인 분대도 막힌다
        Assert.False(new RaidGroupLedger(Cap("raid", 1, "eq", 0), []).CanAdd(1));
        // 1인 분대 1개, 계획에 이미 1개 → 탈출 보충 막힘
        Assert.False(new RaidGroupLedger(Cap("raid", 1, "eq", 1), [1, 3]).CanAdd(1));
        // 계획에 없으면 첫 탈출 보충만 허용, 그 분대가 탈출해도 두 번째는 막힘(누적)
        var l = new RaidGroupLedger(Cap("raid", 1, "eq", 1), [3]);
        Assert.True(l.TryUse(1));
        Assert.False(l.TryUse(1));
        Assert.Equal(1, l.Used(1));
        Assert.True(l.TryUse(2)); // 다른 크기는 규칙 밖
    }

    [Fact]
    public void RaidGroupLedger_GteRule_AndAliveScopeIsLeftToAliveRule()
    {
        var l = new RaidGroupLedger(Cap("raid", 4, "gte", 1), [5]);
        Assert.False(l.CanAdd(4));
        Assert.True(l.CanAdd(3));
        // scope=alive: 이 장부는 늘 허용(살아 있는 분대 기준은 AliveGroupRule)
        var alive = new RaidGroupLedger(Cap("alive", 1, "eq", 1), [1, 1]);
        Assert.False(alive.Active);
        Assert.True(alive.TryUse(1));
        Assert.True(alive.TryUse(1));
        // alive 규칙은 자리가 비면 다시 허용
        var cap = Cap("alive", 1, "eq", 1);
        Assert.Equal(CapVerdict.Wait, AliveGroupRule.Check([1], 1, cap));
        Assert.Equal(CapVerdict.Allow, AliveGroupRule.Check([], 1, cap));
    }

    [Theory]
    [InlineData("split", new[] { 3, 2 })]
    [InlineData("drop", new int[0])]
    public void RaidGroupLedger_Split_MatchesServerPlanner(string over, int[] expected)
    {
        // 재검토 2번: 5인 분대 100%, "4인 이상 0개" → split이면 3+2, drop이면 없음. 서버 계획과 같은 결과여야 한다.
        var cap = new GroupCapDto(true, "raid", over, [new GroupCapRuleDto(4, "gte", 0)]);
        var l = new RaidGroupLedger(cap, []);
        Assert.False(l.CanAdd(5));
        Assert.Equal(expected, l.Split(5));
        Assert.Equal(0, l.Used(3)); // 나누기는 장부에 적지 않는다(각 조각이 자기 차례에 다시 확인)

        var map = new MapInfo("bigmap", 16, 20, 2400, ["A"], ["A"]);
        var cfg = Read($$$$"""{"spawn":{"squad":{"s1":0,"s2":0,"s3":0,"s4":0,"s5":100},"startPmcPct":40,"groupCap":{"on":true,"scope":"raid","over":"{{{{over}}}}","rules":[{"size":4,"cmp":"ge","count":0}]}}}""");
        var plan = SpawnPlanner.Plan(cfg, map, 2400, 0, 3);
        Assert.All(plan.Groups, g => Assert.True(g.Size <= 3));
        // split은 5인을 3+2로 나눠 넣고, drop은 5인 분대를 버린다(남은 자투리 인원만 작은 분대로 남을 수 있음)
        if (over == "split") Assert.Contains(plan.Groups, g => g.Size == 3);
        else Assert.DoesNotContain(plan.Groups, g => g.Size == 3);
    }

    [Fact]
    public void RaidGroupLedger_SplitPieces_RespectCumulativeLimit()
    {
        // "3인 1개" 규칙: 5인을 나누면 3+2, 3인을 하나 쓴 뒤에는 2+2+1
        var cap = new GroupCapDto(true, "raid", "split", [new GroupCapRuleDto(3, "eq", 1), new GroupCapRuleDto(4, "gte", 0)]);
        var l = new RaidGroupLedger(cap, []);
        Assert.Equal(new[] { 3, 2 }, l.Split(5));
        Assert.True(l.TryUse(3));
        Assert.Equal(new[] { 2, 2, 1 }, l.Split(5));
        Assert.False(l.TryUse(3));
    }

    [Fact]
    public void Builder_SendsPlannedGroupSizes_ForRaidScope()
    {
        var map = new MapInfo("bigmap", 16, 20, 2400, ["A"], ["A"]);
        var cfg = Read("""{"maps":{"bigmap":{"style":"infinite","scavStyle":"start"}},"spawn":{"groupCap":{"on":true,"scope":"raid","over":"drop","rules":[{"size":1,"cmp":"eq","count":1}]}}}""");
        var plan = SpawnPlanner.Plan(cfg, map, 2400, 0, 5);
        var dto = ClientSettingsBuilder.Build(cfg, map, plan, true);
        Assert.Equal(plan.Groups.Select(g => g.Size), dto.PlannedGroups!);
        var ledger = new RaidGroupLedger(dto.GroupCap, dto.PlannedGroups);
        Assert.Equal(plan.Groups.Count(g => g.Size == 1) == 0, ledger.CanAdd(1));
    }

    // ── 3번: PMC 첫 보충 시각은 스캐브 방식과 무관 ──

    private static ScavDto Scav(string style, double start) => new(true, 3, 3, 1, 1, 30, 3, 18, 20, 0, start, style);

    private static double FirstPmcTurn(ScavSchedule s)
    {
        for (int t = 0; t <= 2000; t += 2)
        {
            s.Tick(t, 24, 0, 0, 0, 0, 1, out bool pmc);
            if (pmc) return t;
        }
        return -1;
    }

    [Theory]
    [InlineData(240, 600)]
    [InlineData(600, 120)]
    [InlineData(0, 300)]
    public void FirstPmcTurn_SameForAllScavStyles(double scavStart, double pmcFirst)
    {
        var inf = new InfDto(45, 2, 1.0, 0.0, pmcFirst);
        foreach (var style in new[] { "waves", "infinite", "start" })
            Assert.Equal(pmcFirst, FirstPmcTurn(new ScavSchedule(Scav(style, scavStart), inf, 100000, new Random(3))));
    }

    [Fact]
    public void WavesStyle_PmcClockOpenBeforeScavStart_DoesNotSpawnScavsEarly()
    {
        var s = new ScavSchedule(Scav("waves", 240), new InfDto(45, 2, 0.0, 0.0, 0), 100000, new Random(1));
        for (int t = 0; t < 240; t += 2) Assert.Equal(0, s.Tick(t, 24, 0, 0, 0, 0, 1, out _));
        int n = 0;
        for (int t = 240; t < 400; t += 2) n += s.Tick(t, 24, 0, 0, 0, 0, 1, out _);
        Assert.True(n > 0);
    }

    [Fact]
    public void OldServerWithoutFirstDelay_KeepsOldBehaviour()
    {
        var inf = new InfDto(45, 2, 1.0, 0.0);
        Assert.Equal(0, FirstPmcTurn(new ScavSchedule(Scav("infinite", 240), inf, 100000, new Random(3))));
        Assert.Equal(240, FirstPmcTurn(new ScavSchedule(Scav("waves", 240), inf, 100000, new Random(3))));
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(200, 400)]
    [InlineData(900, 0)]
    public void Builder_SendsPmcFirstDelay_MinusScavRaidStart(int simStart, double expected)
    {
        var map = new MapInfo("bigmap", 16, 20, 2400, ["A"], ["A"]);
        var cfg = Read("""{"maps":{"bigmap":{"style":"infinite"}},"spawn":{"firstDelay":600}}""");
        Assert.Equal(600, cfg.SpawnFor("bigmap").FirstDelaySec);
        foreach (var scavStyle in new[] { "waves", "infinite", "start" })
        {
            var c = Read($$$"""{"maps":{"bigmap":{"style":"infinite","scavStyle":"{{{scavStyle}}}"}},"spawn":{"firstDelay":600}}""");
            var dto = ClientSettingsBuilder.Build(c, map, SpawnPlanner.Plan(c, map, 2400 + simStart, simStart, 1), true);
            Assert.Equal(expected, dto.Inf!.FirstDelaySec);
        }
    }

    // ── 4번: 누락 구역 비중은 100 ──

    [Fact]
    public void ZoneWeight_MissingIs100_ExplicitZeroKept()
    {
        var w = new Dictionary<string, double> { ["A"] = 0, ["B"] = 200 };
        Assert.Equal(0, ZoneWeight.Of(w, "A"));
        Assert.Equal(200, ZoneWeight.Of(w, "B"));
        Assert.Equal(100, ZoneWeight.Of(w, "C"));
        Assert.Equal(100, ZoneWeight.Of(null, "C"));
    }

    [Theory]
    [InlineData("""{"A":{"wPmc":100,"w":100}}""", 100, 100)]
    [InlineData("""{"A":{"wPmc":200}}""", 200, 100)]
    [InlineData("""{"A":{"wPmc":0}}""", 0, 100)]
    [InlineData("""{"A":{"w":400}}""", 400, 400)]
    [InlineData("""{"A":{"wScav":0}}""", 100, 0)]
    public void OriginalLayout_EditOneZone_OthersStayAtDefault(string alt, double aPmc, double aScav)
    {
        var map = new MapInfo("bigmap", 16, 20, 2400, ["A", "B"], ["A", "B"]);
        var cfg = Read($$$$"""{"maps":{"bigmap":{"zonesAlt":{{{{alt}}}}}}}""").ForLayout("bigmap", variant: false, hasVariant: true);
        var dto = ClientSettingsBuilder.Build(cfg, map, SpawnPlanner.Plan(cfg, map, 2400, 0, 1), true);
        Assert.Equal(aPmc, dto.ZoneWeights["A"]);
        Assert.Equal(100, dto.ZoneWeights["B"]);
        Assert.Equal(aScav, dto.ScavZoneWeights!["A"]);
        Assert.Equal(100, dto.ScavZoneWeights["B"]);
        // 클라 쪽 읽기도 같은 값
        Assert.Equal(ZoneWeight.Of(dto.ZoneWeights, "A"), dto.ZoneWeights["A"]);
    }

    [Fact]
    public void DisabledZone_IsNotFilledWithDefault()
    {
        var map = new MapInfo("bigmap", 16, 20, 2400, ["A", "B"], ["A", "B"]);
        var cfg = Read("""{"maps":{"bigmap":{"zonesAlt":{"B":{"on":false}}}}}""").ForLayout("bigmap", false, true);
        var dto = ClientSettingsBuilder.Build(cfg, map, SpawnPlanner.Plan(cfg, map, 2400, 0, 1), true);
        Assert.False(dto.ZoneWeights.ContainsKey("B"));
        Assert.Contains("B", dto.DisabledZones!);
        Assert.Equal(100, dto.ZoneWeights["A"]);
    }

    // ── 6번: 끈 구역을 지운 최종 목록으로 생성/실패 집계 ──

    [Fact]
    public void PmcTally_EmptyFinalList_IsNoPointNotSpawned()
    {
        // 후보 구역 하나가 꺼져 지워지면 최종 목록 0개 → 바닐라는 true(처리 끝)지만 생성 아님
        var zones = new List<string?> { "A" };
        zones.RemoveAll(z => z == "A");
        bool ok = PmcTally.ZonesUsable(zones.Count, zones.Count > 0 && zones[0] == null);
        Assert.False(ok);
        Assert.Equal(PmcOutcome.NoPoint, PmcTally.Classify(true, true, ok));
        Assert.Equal(PmcOutcome.Spawned, PmcTally.Classify(true, true, true));
        Assert.Equal(PmcOutcome.NoPoint, PmcTally.Classify(true, false, true));
        Assert.Equal(PmcOutcome.Failed, PmcTally.Classify(false, true, true));
        Assert.False(PmcTally.ZonesUsable(2, true));
    }

    // ── 클라 실제 파서(Newtonsoft)로 옛 서버 응답 읽기 ──

    private static readonly JsonSerializerSettings Wire = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false, OverrideSpecifiedNames = true }
        }
    };

    [Fact]
    public void Newtonsoft_OldServerDto_WithoutNewFields()
    {
        var map = new MapInfo("bigmap", 16, 20, 2400, ["A"], ["A"]);
        var cfg = Read("""{"maps":{"bigmap":{"style":"infinite"}}}""");
        var json = JsonNode.Parse(JsonConvert.SerializeObject(ClientSettingsBuilder.Build(cfg, map, SpawnPlanner.Plan(cfg, map, 2400, 0, 1), true), Wire))!.AsObject();
        Assert.NotNull(json["plannedGroups"]);
        json.Remove("plannedGroups");
        json["inf"]!.AsObject().Remove("firstDelaySec");
        var back = JsonConvert.DeserializeObject<ClientRaidSettings>(json.ToJsonString(), Wire)!;
        Assert.Null(back.PlannedGroups);
        Assert.Null(back.Inf!.FirstDelaySec);
        Assert.True(new RaidGroupLedger(back.GroupCap, back.PlannedGroups).CanAdd(1) || back.GroupCap.On);

        var round = JsonConvert.DeserializeObject<ClientRaidSettings>(
            JsonConvert.SerializeObject(ClientSettingsBuilder.Build(cfg, map, SpawnPlanner.Plan(cfg, map, 2400, 0, 1), true), Wire), Wire)!;
        Assert.NotNull(round.PlannedGroups);
        Assert.NotNull(round.Inf!.FirstDelaySec);
    }
}
