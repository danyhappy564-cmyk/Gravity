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

    /// <summary>레이드 맵 복제본에 스폰 계획을 적용한다. 적용했으면 true. 예외는 잡아서 원본 유지.</summary>
    public bool TryApplyToRaid(string name, LocationBase loc, RaidChanges? changes, int? seed = null)
    {
        var id = name.ToLowerInvariant();
        try
        {
            var cfg = Store.Current;
            if (!cfg.SpawnModule || Detect.SpawnLocked) return Skip(id, Detect.SpawnLocked ? "ABPS 감지로 잠김" : "스폰 모듈 꺼짐");
            if (!Catalog.TryGet(id, out var info)) return Skip(id, "모르는 맵(커스텀 맵은 건드리지 않음)");
            if (!cfg.MapFor(id).Original) return Skip(id, "이 맵은 Gravity 적용 꺼짐");

            // 복제본의 EscapeTimeLimit(분)는 SVM·스캐브 단축까지 반영된 "남은" 길이다.
            // 스캐브 레이드는 원래 길이(남은 시간 + 시작 지연)로 계획한 뒤 지연보다 이른 항목을 빼고 당긴다(Codex 검토 반영).
            var simStart = (int)Math.Max(0, changes?.SimulatedRaidStartSeconds ?? 0);
            var raidSec = (int)Math.Round((loc.EscapeTimeLimit ?? info.DbRaidSec / 60.0) * 60) + simStart;
            var s = seed ?? Random.Shared.Next(1, int.MaxValue);
            // 이번 판은 스캐브를 바닐라로 둔다: 웨이브 밖 자동 스폰(OfflineNewSpawn)이 있어 웨이브만 바꿔서는 통제가 안 된다.
            var plan = SpawnPlanner.Plan(cfg, info, raidSec, simStart, s) with { ScavWaves = [] };
            RaidSpawnApplier.Apply(loc, plan, cfg, info);
            RaidSpawnApplier.ApplyBosses(loc, cfg, id);
            Last = new LastRaid(DateTime.Now, plan.Summary, plan, null);
            info_($"{GravityInfo.Tag} {plan.Summary}");
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
