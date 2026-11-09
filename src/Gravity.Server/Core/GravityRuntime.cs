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
    public DetectResult Detect { get; } = detect;
    public LastRaid? Last { get; private set; }
    public Dictionary<string, int>? MaxBotCap { get; set; }
    public ClientSessions Sessions { get; } = new();
    /// <summary>이번 판에 클라가 받을 설정(맵 id별). 클라가 있는 세션에서 적용했을 때만 채운다.</summary>
    public ConcurrentDictionary<string, ClientRaidSettings> LastClientSettings { get; } = new(StringComparer.OrdinalIgnoreCase);
    public RaidReport? LastReport { get; private set; }
    /// <summary>보고를 받은 시각과, 그때 비교할 계획(같은 맵의 마지막 적용 계획. 없으면 null).</summary>
    public DateTime? LastReportAt { get; private set; }
    public SpawnPlan? LastReportPlan { get; private set; }

    public HelloResponse OnHello(string sessionId, HelloRequest r)
    {
        var first = !Sessions.HasClient(sessionId);
        Sessions.Hello(sessionId, r);
        var ok = GravityVersion.Compatible(r.ClientVersion, GravityVersion.Current);
        var locked = Detect.SpawnLocked || r.AbpsClient;
        if (first)
        {
            if (!ok) warn($"{GravityInfo.Tag} 클라 플러그인 판({r.ClientVersion})이 서버({GravityVersion.Current})와 맞지 않음");
            else info($"{GravityInfo.Tag} 클라 플러그인 연결: {r.ClientVersion}" + (locked ? " (ABPS 감지, 스폰 잠금)" : ""));
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
        info($"{GravityInfo.Tag} 클라 보고 {r.MapId}: 생성 {string.Join(" ", r.Spawned.Select(kv => $"{kv.Key} {kv.Value}"))}"
            + (r.Dropped.Count > 0 ? $" / 버림 {string.Join(" ", r.Dropped.Select(kv => $"{kv.Key} {kv.Value}"))}" : "")
            + (r.Killed is { Count: > 0 } k ? $" / 사망 {string.Join(" ", k.Select(kv => $"{kv.Key} {kv.Value}"))}" : "")
            + $" / 정리 {r.Sanitized}");
    }

    /// <summary>레이드 맵 복제본에 스폰 계획을 적용한다. 적용했으면 true. 예외는 잡아서 원본 유지.</summary>
    public bool TryApplyToRaid(string name, LocationBase loc, RaidChanges? changes, string? sessionId, int? seed = null)
    {
        var id = name.ToLowerInvariant();
        try
        {
            var cfg = Store.Current;
            LastClientSettings.TryRemove(id, out _);
            var abpsClient = Sessions.AbpsClient(sessionId);
            if (!cfg.SpawnModule || Detect.SpawnLocked || abpsClient)
                return Skip(id, Detect.SpawnLocked || abpsClient ? "ABPS 감지로 잠김" : "스폰 모듈 꺼짐");
            var hasClient = Sessions.HasClient(sessionId);
            if (!Catalog.TryGet(id, out var info)) return Skip(id, "모르는 맵(커스텀 맵은 건드리지 않음)");
            if (!cfg.MapFor(id).Original) return Skip(id, "이 맵은 Gravity 적용 꺼짐");

            // 복제본의 EscapeTimeLimit(분)는 SVM·스캐브 단축까지 반영된 "남은" 길이다.
            // 스캐브 레이드는 원래 길이(남은 시간 + 시작 지연)로 계획한 뒤 지연보다 이른 항목을 빼고 당긴다(Codex 검토 반영).
            var simStart = (int)Math.Max(0, changes?.SimulatedRaidStartSeconds ?? 0);
            var raidSec = (int)Math.Round((loc.EscapeTimeLimit ?? info.DbRaidSec / 60.0) * 60) + simStart;
            var s = seed ?? Random.Shared.Next(1, int.MaxValue);
            var plan = SpawnPlanner.Plan(cfg, info, raidSec, simStart, s, clientAliveCap: hasClient);
            // 스캐브: 클라 플러그인이 없으면 바닐라 그대로(웨이브 밖 자동 스폰이 있어 웨이브만 바꿔서는 통제가 안 된다).
            // 클라가 있으면 시작 스캐브(첫 활동 구간)만 서버가 넣고 이후는 클라의 ScavDirector가 맡는다.
            plan = plan with { ScavWaves = hasClient && cfg.MapFor(id).StartScav ? plan.ScavWaves.Where(w => w.IsStart).ToList() : [] };
            // 공장·미궁은 PMC 구역이 없어 클라가 지점을 골라야 한다: 클라가 없으면(미궁은 allowPmcLabyrinth 꺼짐도) Gravity PMC를 넣지 않는다.
            string? pmcWhy = null;
            if (id.StartsWith("factory4") && !hasClient) pmcWhy = "클라 플러그인 없음";
            else if (id == "labyrinth" && !hasClient) pmcWhy = "클라 플러그인 없음";
            else if (id == "labyrinth" && !cfg.Spawn.AllowPmcLabyrinth) pmcWhy = "미궁 PMC 꺼짐";
            if (pmcWhy != null) plan = plan with { Groups = [] };
            // 무한 방식: 클라가 있으면 0.1의 촘촘한 PMC 웨이브 대신 시작 분대만 넣고, 이후 PMC는 클라가 RefillGroups로 보충한다(spec 4.6).
            else if (hasClient && cfg.MapFor(id).Style == "infinite") plan = plan with { Groups = plan.Groups.Where(g => g.IsStart).ToList() };
            RaidSpawnApplier.Apply(loc, plan, cfg, info, hasClient);
            RaidSpawnApplier.ApplyBosses(loc, cfg, id, s, raidSec - simStart);
            if (hasClient) LastClientSettings[id] = ClientSettingsBuilder.Build(cfg, info, plan, true);
            var summary = plan.Summary + (pmcWhy != null ? $" / PMC 항목 안 넣음({pmcWhy})" : "");
            Last = new LastRaid(DateTime.Now, summary, plan, null);
            info_($"{GravityInfo.Tag} {summary}");
            return true;
        }
        catch (Exception ex)
        {
            warn($"{GravityInfo.Tag} {id} 스폰 적용 실패, 바닐라로 진행: {ex.Message}");
            Last = new LastRaid(DateTime.Now, "실패: " + ex.Message, null, ex.ToString());
            return false;
        }
    }

    private void info_(string m) => info(m);

    private bool Skip(string id, string why)
    {
        Last = new LastRaid(DateTime.Now, $"{id}: 적용 안 함({why})", null, why);
        return false;
    }
}
