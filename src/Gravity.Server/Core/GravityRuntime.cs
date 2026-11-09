using System.Collections.Concurrent;
using Gravity.Client;
using Gravity.Shared;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Location;

namespace Gravity.Core;

public sealed record LastRaid(DateTime At, string Summary, SpawnPlan? Plan, string? Skipped);

/// <summary>모드 전체가 공유하는 상태(로더가 한 번 채움). 하모니 패치는 정적이라 여기로 접근한다.</summary>
public sealed class GravityRuntime(GravityConfigStore store, MapCatalog catalog, DetectResult detect, Action<string> warn, Action<string> info)
{
    public static GravityRuntime? Instance { get; set; }

    public GravityConfigStore Store { get; } = store;
    public MapCatalog Catalog { get; } = catalog;
    public DetectResult Detect => detect with { LockWithQuestingBots = Store.Current.CompatOrDefault.LockWithQuestingBots };
    public LastRaid? Last { get; private set; }
    public Dictionary<string, int>? MaxBotCap { get; set; }
    public ClientSessions Sessions { get; } = new();
    /// <summary>이번 판에 클라가 받을 설정(맵 id별). 클라가 있는 세션에서 적용했을 때만 채운다.</summary>
    public ConcurrentDictionary<string, ClientRaidSettings> LastClientSettings { get; } = new(StringComparer.OrdinalIgnoreCase);
    public RaidReport? LastReport { get; private set; }
    /// <summary>보고를 받은 시각과, 그때 비교할 계획(같은 맵의 마지막 적용 계획. 없으면 null).</summary>
    public DateTime? LastReportAt { get; private set; }
    public SpawnPlan? LastReportPlan { get; private set; }
    /// <summary>마지막으로 저장한 보고 파일 이름(diag.report). 없으면 null.</summary>
    public string? LastReportFile { get; private set; }
    /// <summary>보고를 받을 때의 원본/변형(다음 판에 바뀌지 않게 따로 둔다).</summary>
    public string? LastReportLayout { get; private set; }
    /// <summary>내 프리셋 저장소(0.6). 시험에서는 null.</summary>
    public PresetLibrary? Library { get; set; }

    public HelloResponse OnHello(string sessionId, HelloRequest r)
    {
        var first = !Sessions.HasClient(sessionId);
        Sessions.Hello(sessionId, r);
        var ok = GravityVersion.Compatible(r.ClientVersion, GravityVersion.Current);
        var locked = Detect.SpawnLocked || r.AbpsClient;
        if (first)
        {
            if (!ok) warn($"{GravityInfo.Tag} " + L.T($"클라 플러그인 판({r.ClientVersion})이 서버({GravityVersion.Current})와 맞지 않음", $"client plugin version ({r.ClientVersion}) does not match the server ({GravityVersion.Current})"));
            else info($"{GravityInfo.Tag} {L.T("클라 플러그인 연결", "Client plugin connected")}: {r.ClientVersion}" + (locked ? L.T($" ({Detect.SpawnLockReason ?? "ABPS"} 감지, 스폰 잠금)", $" ({Detect.SpawnLockReason ?? "ABPS"} detected, spawns locked)") : ""));
        }
        return new HelloResponse(GravityVersion.Current, ok, locked);
    }

    public ClientRaidSettings ClientRaidFor(string mapId) =>
        LastClientSettings.TryGetValue((mapId ?? "").ToLowerInvariant(), out var s) && s.Managed ? s : ClientSettingsBuilder.Unmanaged(mapId ?? "");

    public void OnReport(RaidReport r)
    {
        LastReport = r;
        LastReportAt = DateTime.Now;
        var plan = Last?.Plan;
        LastReportPlan = plan is not null && string.Equals(plan.MapId, r.MapId, StringComparison.OrdinalIgnoreCase) ? plan : null;
        DiagLog.FlushGear();
        LastReportLayout = LastLayout.GetValueOrDefault(r.MapId ?? "");
        if (Store.Current.DiagOrDefault.Report)
            LastReportFile = DiagLog.SaveReport(r.MapId ?? "map", new
            {
                format = "gravity-raid-report", version = GravityInfo.Version, at = LastReportAt,
                layout = LastReportLayout, summary = LastReportPlan != null ? Last?.Summary : null,
                plan = LastReportPlan, report = r
            }, LastReportAt);
        info($"{GravityInfo.Tag} {L.T("클라 보고", "Client report")} {r.MapId}: {L.T("생성", "spawned")} {string.Join(" ", r.Spawned.Select(kv => $"{kv.Key} {kv.Value}"))}"
            + (r.Dropped.Count > 0 ? $" / {L.T("버림", "dropped")} {string.Join(" ", r.Dropped.Select(kv => $"{kv.Key} {kv.Value}"))}" : "")
            + (r.Killed is { Count: > 0 } k ? $" / {L.T("사망", "killed")} {string.Join(" ", k.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key} {kv.Value}"))}" : "")
            + (r.Extracted is { } x && x.Values.Sum() > 0 ? $" / {L.T("탈출", "extracted")} {string.Join(" ", x.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key} {kv.Value}"))}" + (r.ExtractRefill > 0 ? $"({L.T("보충", "refilled")} {r.ExtractRefill})" : "") : "")
            + $" / {L.T("정리", "sanitized")} {r.Sanitized}");
    }

    /// <summary>레이드 맵 복제본에 스폰 계획을 적용한다. 적용했으면 true. 예외는 잡아서 원본 유지.</summary>
    /// <summary>SPT 봇 설정(플레이어 스캐브 이름 확률을 판마다 웹 값으로). 시험에서는 null.</summary>
    public SPTarkov.Server.Core.Models.Spt.Config.BotConfig? BotCfg { get; set; }

    /// <summary>지금 켜진 SPT 시즌 이벤트(할로윈 계열, 크리스마스 계열). 시험에서는 둘 다 꺼짐.</summary>
    public Func<(bool Halloween, bool Christmas)> ActiveEvents { get; set; } = () => (false, false);

    /// <summary>팩션 모드 설정·SPT 원본 PMC 지문(0.4). 시험에서는 비어 있음.</summary>
    public Gravity.Factions.FactionData Factions { get; set; } = Gravity.Factions.FactionData.Empty;

    /// <summary>마지막 판에 남긴 다른 모드 PMC 항목 수(웹 진단용).</summary>
    public int LastOtherPmc { get; private set; }

    /// <summary>ORBIT 설정에서 읽은 값(0.6). 없으면 None.</summary>
    public OrbitInfo Orbit { get; set; } = OrbitInfo.None;

    /// <summary>지금 맵 표에 걸린 Base(MapVariants가 판마다 바꿔 끼움, kb 1.28). 시험에서는 null.</summary>
    public Func<string, LocationBase?>? CurrentBase { get; set; }

    /// <summary>DB Base의 깊은 복제(개발 페이지 시뮬레이션용). 시험에서는 null.</summary>
    public Func<string, LocationBase?>? CloneBase { get; set; }

    /// <summary>자세한 로그(diag.log = verbose일 때만 남음).</summary>
    public Action<string> Verbose { get; set; } = _ => { };

    /// <summary>맵 id → 마지막 판이 원본(original)인지 변형(variant)인지(웹 표시용).</summary>
    public ConcurrentDictionary<string, string> LastLayout { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>이 맵의 지금 판이 변형 배치인지: 변형 모드가 있는 맵이고, 서버 시작 때 걸려 있던 Base가 그대로 걸려 있음(판정 54).</summary>
    public bool IsVariant(string id, LocationBase? current) =>
        (ActiveVariants?.Contains(id) ?? Detect.VariantMaps.ContainsKey(id)) && Catalog.IsLoadedBase(id, current);

    /// <summary>변형 모드가 실제로 배치를 바꾼 맵(서버 시작 때 SPT 원본과 비교). null이면 감지 목록 그대로(시험).</summary>
    public HashSet<string>? ActiveVariants { get; set; }

    /// <summary>클라 플러그인이 붙어 있고 판이 맞는지(마지막 Hello 기준, 개발 페이지용).</summary>
    public bool AnyCompatibleClient => Sessions.LastVersion is { } v && GravityVersion.Compatible(v, GravityVersion.Current);

    /// <summary>스폰을 건너뛸 이유(레이드·개발 페이지 공통). 적용하면 null.</summary>
    public string? SkipReason(GravityConfig cfg, string id, LocationBase? current, bool abpsClient = false)
    {
        if (!cfg.SpawnModule) return L.T("스폰 모듈 꺼짐", "spawn module off");
        if (Detect.SpawnLocked || abpsClient) return L.T($"{Detect.SpawnLockReason ?? "ABPS"} 감지로 잠김", $"locked ({Detect.SpawnLockReason ?? "ABPS"} detected)");
        if (!Catalog.TryGet(id, out _)) return L.T("모르는 맵(커스텀 맵은 건드리지 않음)", "unknown map (custom maps are left alone)");
        var variant = IsVariant(id, current);
        var ms = cfg.MapFor(id);
        if (variant ? !ms.Variant : !ms.Original)
            return variant
                ? L.T($"변형 맵({string.Join("·", Detect.VariantMaps.GetValueOrDefault(id) ?? ["자동 감지"])})에 Gravity 적용 꺼짐", $"Gravity is off for the map variant ({string.Join("·", Detect.VariantMaps.GetValueOrDefault(id) ?? ["auto-detected"])})")
                : L.T("이 맵은 Gravity 적용 꺼짐", "Gravity is off for this map");
        return null;
    }

    /// <summary>팩션의 웹 관리 선택이 최종값이다. 옛 respectTakeover는 읽기 호환만 유지한다.</summary>
    public GravityConfig EffectiveConfig(GravityConfig cfg) => cfg;

    public GravityConfig ConfigForLayout(GravityConfig cfg, string id, LocationBase? current) =>
        cfg.ForLayout(id, IsVariant(id, current), ActiveVariants?.Contains(id) ?? Detect.VariantMaps.ContainsKey(id));

    /// <summary>한 판 계산 결과(복제본에 이미 적용됨).</summary>
    public sealed record RaidBuild(SpawnPlan Plan, MapInfo Info, string? PmcWhy, string? ScavNote, int OtherPmc, string? FacNote, string? EvNote);

    /// <summary>
    /// 복제본 loc에 스폰 계획·팩션·보스·이벤트를 적용한다(바깥 상태는 안 바꿈 — 레이드와 개발 페이지 시뮬레이션이 같이 쓴다).
    /// </summary>
    public RaidBuild BuildRaid(string id, LocationBase loc, GravityConfig cfg, MapInfo info, int raidSec, int simStart, int s, bool hasClient, bool scavRaid)
    {
        var plan = SpawnPlanner.Plan(cfg, info, raidSec, simStart, s, clientAliveCap: hasClient);
        // 스캐브: 클라 플러그인이 없으면 바닐라 그대로(웨이브 밖 자동 스폰이 있어 웨이브만 바꿔서는 통제가 안 된다).
        // 클라가 있으면 시작 스캐브(첫 활동 구간)만 서버가 넣고 이후는 클라의 ScavDirector가 맡는다.
        plan = plan with { ScavWaves = hasClient && cfg.MapFor(id).StartScav ? plan.ScavWaves.Where(w => w.IsStart).ToList() : [] };
        // 공장·미궁은 PMC 구역이 없어 클라가 지점을 골라야 한다: 클라가 없으면(미궁은 allowPmcLabyrinth 꺼짐도) Gravity PMC를 넣지 않는다.
        string? pmcWhy = null;
        if (id.StartsWith("factory4") && !hasClient) pmcWhy = L.T("클라 플러그인 없음", "no client plugin");
        else if (id == "labyrinth" && !hasClient) pmcWhy = L.T("클라 플러그인 없음", "no client plugin");
        else if (id == "labyrinth" && !cfg.Spawn.AllowPmcLabyrinth) pmcWhy = L.T("미궁 PMC 꺼짐", "Labyrinth PMCs off");
        if (pmcWhy != null) plan = plan with { Groups = [] };
        // 무한 방식: 클라가 있으면 0.1의 촘촘한 PMC 웨이브 대신 시작 분대만 넣고, 이후 PMC는 클라가 RefillGroups로 보충한다(spec 4.6).
        else if (hasClient && cfg.MapFor(id).Style == "infinite") plan = plan with { Groups = plan.Groups.Where(g => g.IsStart).ToList() };
        string? scavNote = null;
        if (scavRaid && plan.Groups.Count > 0)
        {
            var keep = KeepForScavRaid(plan.Groups, cfg.SpawnFor(id).Scav.PlayerScavRemain, s);
            scavNote = " / " + L.T($"스캐브 레이드: PMC 무리 {plan.Groups.Count}개 중 {keep.Count}개 남김({cfg.SpawnFor(id).Scav.PlayerScavRemain:0}%)",
                $"scav raid: kept {keep.Count} of {plan.Groups.Count} PMC groups ({cfg.SpawnFor(id).Scav.PlayerScavRemain:0}%)");
            plan = plan with { Groups = keep };
        }
        var otherPmc = RaidSpawnApplier.Apply(loc, plan, cfg, info, hasClient, Factions.SptPmc.GetValueOrDefault(id));
        // 팩션을 보스보다 먼저: 되살린 연구소 게이트 레이더도 특수 봇 설정을 받게 한다.
        var facNote = Gravity.Factions.FactionRaid.Apply(loc, cfg, id, s, raidSec - simStart, Factions, Detect.Ruaf, Detect.BlackDiv, info.PmcZones);
        RaidSpawnApplier.ApplyBosses(loc, cfg, id, s, raidSec - simStart, Catalog.BossTemplates);
        var (hw, xm) = ActiveEvents();
        var evNote = Gravity.Events.EventRaid.Apply(loc, cfg, id, s, hw, xm);
        return new RaidBuild(plan, info, pmcWhy, scavNote, otherPmc, facNote, evNote);
    }

    public bool TryApplyToRaid(string name, LocationBase loc, RaidChanges? changes, string? sessionId, int? seed = null, bool scavRaid = false)
    {
        var id = name.ToLowerInvariant();
        try
        {
            var cfg = EffectiveConfig(Store.Current);
            LastClientSettings.TryRemove(id, out _);
            // 장비는 스폰 모듈과 따로: 스폰이 잠기거나 꺼진 판에도 이번 판 모드 아이템 묶음을 새로 고른다.
            var gearNote = PrepareGear(seed ?? Random.Shared.Next(1, int.MaxValue), id);
            var abpsClient = Sessions.AbpsClient(sessionId);
            var hasClient = Sessions.HasClient(sessionId);
            // 0.6 맵 변형: 지금 걸린 Base로 원본/변형을 가르고, 맵 정보(구역)도 그 Base에서 다시 계산한다(판정 54·55).
            var current = CurrentBase?.Invoke(id);
            var variant = IsVariant(id, current);
            cfg = ConfigForLayout(cfg, id, current);
            if (Catalog.TryGet(id, out _)) LastLayout[id] = variant ? "variant" : "original";
            if (SkipReason(cfg, id, current, abpsClient) is { } why) return Skip(id, why, gearNote);
            Catalog.TryGet(id, out var info);
            if (current != null && Catalog.InfoFor(id, current) is { } curInfo) info = curInfo;
            if (BotCfg != null) BotCfg.ChanceAssaultScavHasPlayerScavName = (int)Math.Round(cfg.SpawnFor(id).Scav.PscavChance);

            // 복제본의 EscapeTimeLimit(분)는 SVM·스캐브 단축까지 반영된 "남은" 길이다.
            // 스캐브 레이드는 원래 길이(남은 시간 + 시작 지연)로 계획한 뒤 지연보다 이른 항목을 빼고 당긴다(Codex 검토 반영).
            var simStart = (int)Math.Max(0, changes?.SimulatedRaidStartSeconds ?? 0);
            var raidSec = (int)Math.Round((loc.EscapeTimeLimit ?? info.DbRaidSec / 60.0) * 60) + simStart;
            var s = seed ?? Random.Shared.Next(1, int.MaxValue);
            var b = BuildRaid(id, loc, cfg, info, raidSec, simStart, s, hasClient, scavRaid);
            var plan = b.Plan;
            LastOtherPmc = b.OtherPmc;
            if (hasClient) LastClientSettings[id] = ClientSettingsBuilder.Build(cfg, info, plan, true, Detect.Ruaf, Detect.BlackDiv, Orbit);
            var summary = plan.Summary + (variant ? L.T(" / 변형 맵", " / map variant") : "") + (b.PmcWhy != null ? L.T($" / PMC 항목 안 넣음({b.PmcWhy})", $" / no PMC entries ({b.PmcWhy})") : "") + b.ScavNote
                + (b.OtherPmc > 0 ? L.T($" / 다른 모드 PMC 항목 {b.OtherPmc}개 유지", $" / kept {b.OtherPmc} other-mod PMC entries") : "")
                + (b.FacNote != null ? " / " + b.FacNote : "") + (b.EvNote != null ? " / " + b.EvNote : "") + (gearNote != null ? " / " + gearNote : "");
            Last = new LastRaid(DateTime.Now, summary, plan, null);
            info_($"{GravityInfo.Tag} {summary}");
            foreach (var g in plan.Groups)
                Verbose($"{GravityInfo.Tag} {id} PMC " + L.T($"{(g.IsStart ? "시작" : "웨이브")} {g.TimeSec}초 {(g.Usec ? "USEC" : "BEAR")} {g.Size}명 {g.Difficulty}",
                    $"{(g.IsStart ? "start" : "wave")} {g.TimeSec}s {(g.Usec ? "USEC" : "BEAR")} x{g.Size} {g.Difficulty}"));
            foreach (var e in loc.BossLocationSpawn ?? [])
                if (e.BossName is { } bn && !MapCatalog.IsPmc(e))
                    Verbose($"{GravityInfo.Tag} {id} " + L.T($"보스·특수 {bn} 확률 {e.BossChance ?? 0:0}% 구역 {(string.IsNullOrEmpty(e.BossZone) ? "(아무 데)" : e.BossZone)} 시각 {e.Time ?? -1:0}",
                        $"boss/special {bn} chance {e.BossChance ?? 0:0}% zone {(string.IsNullOrEmpty(e.BossZone) ? "(any)" : e.BossZone)} time {e.Time ?? -1:0}"));
            return true;
        }
        catch (Exception ex)
        {
            warn($"{GravityInfo.Tag} {id} {L.T("스폰 적용 실패, 바닐라로 진행", "failed to apply spawns, raid runs vanilla")}: {ex.Message}");
            Last = new LastRaid(DateTime.Now, L.T("실패: ", "Failed: ") + ex.Message, null, ex.ToString());
            return false;
        }
    }

    /// <summary>개발 페이지 한 줄: PMC·스캐브 계획 수와 나올 보스·특수 봇(확률 굴림).</summary>
    public sealed record SimRow(int Pmc, int Scav, string[] Bosses, string[] Specials, string Layout);

    /// <summary>시뮬레이션 결과: 줄들과, 실제 레이드라면 건너뛸 이유(없으면 null).</summary>
    public sealed record SimResult(List<SimRow> Rows, string? Skip, bool Client);

    /// <summary>
    /// 이 맵 DB 복제본에 지금 설정을 n번(다른 시드) 적용해 본다(바깥 상태 안 바꿈, 판정 60). 보스·특수 봇은 항목 확률만 굴린다.
    /// 스캐브는 서버 계획 웨이브 인원(최대) + 클라가 있으면 보충 한도(소프트 캡). 맵을 못 찾으면 null.
    /// </summary>
    public SimResult? Simulate(string mapId, int n, int seed0)
    {
        var id = (mapId ?? "").ToLowerInvariant();
        if (CloneBase == null || !Catalog.TryGet(id, out var info)) return null;
        var cfg = EffectiveConfig(Store.Current);
        var hasClient = AnyCompatibleClient;
        var current = CurrentBase?.Invoke(id);
        cfg = ConfigForLayout(cfg, id, current);
        var layout = IsVariant(id, current) ? "variant" : "original";
        var mi = current != null && Catalog.InfoFor(id, current) is { } ci ? ci : info;
        var rows = new List<SimRow>();
        var rnd = new Random(seed0);
        for (int i = 0; i < Math.Clamp(n, 1, 50); i++)
        {
            var loc = CloneBase(id);
            if (loc == null) return null;
            var raidSec = (int)Math.Round((loc.EscapeTimeLimit ?? mi.DbRaidSec / 60.0) * 60);
            var b = BuildRaid(id, loc, cfg, mi, raidSec, 0, rnd.Next(1, int.MaxValue), hasClient, false);
            rows.Add(Roll(loc, b, cfg, id, hasClient, layout, rnd));
        }
        return new SimResult(rows, SkipReason(cfg, id, current), hasClient);
    }

    /// <summary>적용한 복제본에서 보스·특수 봇 항목 확률을 굴려 한 줄을 만든다.</summary>
    internal static SimRow Roll(LocationBase loc, RaidBuild b, GravityConfig cfg, string id, bool hasClient, string layout, Random rnd)
    {
        var bosses = new List<string>();
        var specials = new List<string>();
        foreach (var e in loc.BossLocationSpawn ?? [])
        {
            if (e.BossName is not { } bn || MapCatalog.IsPmc(e)) continue;
            if (rnd.NextDouble() * 100 >= (e.BossChance ?? 0)) continue;
            if (RaidSpawnApplier.BossRoleToId.TryGetValue(bn, out var bid)) bosses.Add(bid);
            else if (ConfigModel.SpecialRoles.FirstOrDefault(kv => kv.Value == bn).Key is { } sid) specials.Add(sid);
            else if (Gravity.Factions.FactionRaid.RuafRoles.Contains(bn)) specials.Add("ruaf");
            else if (Gravity.Factions.FactionRaid.BlackDivRoles.Contains(bn)) specials.Add("blackdiv");
            else specials.Add(bn);
        }
        // 클라 스캐브 관리 조건은 ClientSettingsBuilder와 같다(연구소는 allowScavLab일 때만, 방식과 상관없이).
        var scavManaged = hasClient && (id != "laboratory" || cfg.Spawn.AllowScavLab);
        var scav = b.Plan.ScavWaves.Sum(w => w.SlotsMax)
            + (scavManaged && cfg.MapFor(id).ScavStyle != "start" ? cfg.SpawnFor(id).Scav.SoftCap : 0);
        return new SimRow(b.Plan.PmcTotal, scav, bosses.Distinct().ToArray(), specials.Distinct().ToArray(), layout);
    }

    /// <summary>개발 페이지 "웨이브 표": DB 복제본에 한 번 적용한 계획과 BossLocationSpawn 항목(바깥 상태 안 바꿈).</summary>
    public object? PlanPreview(string mapId, int seed)
    {
        var id = (mapId ?? "").ToLowerInvariant();
        if (CloneBase == null || !Catalog.TryGet(id, out var info)) return null;
        var loc = CloneBase(id);
        if (loc == null) return null;
        var cfg = EffectiveConfig(Store.Current);
        var current = CurrentBase?.Invoke(id);
        cfg = ConfigForLayout(cfg, id, current);
        var mi = current != null && Catalog.InfoFor(id, current) is { } ci ? ci : info;
        var raidSec = (int)Math.Round((loc.EscapeTimeLimit ?? mi.DbRaidSec / 60.0) * 60);
        var b = BuildRaid(id, loc, cfg, mi, raidSec, 0, seed, AnyCompatibleClient, false);
        return new
        {
            map = id, layout = IsVariant(id, current) ? "variant" : "original", seed, summary = b.Plan.Summary,
            skip = SkipReason(cfg, id, current), client = AnyCompatibleClient,
            pmcZones = mi.PmcZones, scavZones = mi.ScavZones,
            pmc = b.Plan.Groups, scavWaves = b.Plan.ScavWaves,
            entries = (loc.BossLocationSpawn ?? []).Select(e => new
            {
                role = e.BossName, chance = e.BossChance, zone = e.BossZone, time = e.Time, escorts = e.BossEscortAmount,
                escortType = e.BossEscortType, difficulty = e.BossDifficulty
            }).ToList()
        };
    }

    /// <summary>스캐브 레이드: 계획 PMC 무리 중 pct%만 무작위로 남긴다(시드 고정, 반올림, 원래 순서 유지).</summary>
    public static List<PmcGroup> KeepForScavRaid(List<PmcGroup> groups, double pct, int seed)
    {
        int keep = (int)Math.Round(groups.Count * Math.Clamp(pct, 0, 100) / 100.0, MidpointRounding.AwayFromZero);
        var rnd = new Random(seed ^ 0x5CA7);
        var pick = Enumerable.Range(0, groups.Count).OrderBy(_ => rnd.Next()).Take(keep).ToHashSet();
        return groups.Where((_, i) => pick.Contains(i)).ToList();
    }

    private void info_(string m) => info(m);

    /// <summary>바깥(웹 등)에서 남기는 알림(로그 수준·최근 목록을 따름).</summary>
    public void Note(string m) => info(m);

    /// <summary>장비 모듈이 켜져 있으면 이번 판 상태(모드 아이템 묶음·통계)를 새로 만든다. 요약 한 줄 또는 null.</summary>
    private string? PrepareGear(int seed, string mapId)
    {
        if (Gravity.Gear.GearRuntime.Active() is not { } gear) return null;
        Gravity.Gear.GearRuntime.NewRaid(gear, seed);
        Gravity.Gear.GearRuntime.LogBots = Store.Current.DiagOrDefault.BotLog;
        if (Gravity.Gear.GearRuntime.LogBots) DiagLog.NewGearRaid($"{DateTime.Now:s} {mapId} " + L.T("봇 장비 기록(역할 티어 레벨 주무기 방탄)", "bot gear log (role tier level primary armor)"));
        var gr = Gravity.Gear.GearRuntime.Raid;
        return gr.Allowed != null ? L.T($"장비: 모드 아이템 무기 {gr.Weapons}·장비 {gr.Equipment}·부품 {gr.Attachments}", $"gear: mod items {gr.Weapons} weapons · {gr.Equipment} gear · {gr.Attachments} attachments") : L.T("장비: Gravity", "gear: Gravity");
    }

    private bool Skip(string id, string why, string? gearNote = null)
    {
        if (gearNote != null) info($"{GravityInfo.Tag} {id}: " + L.T($"스폰 적용 안 함({why})", $"spawns not applied ({why})") + $" / {gearNote}");
        Last = new LastRaid(DateTime.Now, $"{id}: " + L.T($"적용 안 함({why})", $"not applied ({why})") + (gearNote != null ? " / " + gearNote : ""), null, why);
        return false;
    }
}
