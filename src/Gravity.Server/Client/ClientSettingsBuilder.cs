using Gravity.Core;
using Gravity.Shared;
using Gravity.Spawn;

namespace Gravity.Client;

/// <summary>서버 설정·계획 → 클라가 받을 레이드 설정. 비율(%)은 0~1로 바꿔 보낸다. 숫자는 ConfigModel에서 이미 Clamp됨.</summary>
public static class ClientSettingsBuilder
{
    private const int MaxRefill = 200;

    public static ClientRaidSettings Unmanaged(string mapId) => new(
        mapId, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, "vanilla", 0, new Dictionary<string, double>(), false,
        new GroupCapDto(false, "raid", "split", []), new ScavDto(false, 0, 0, 0, 0, 0, 0, 0, 0, 0), null, [],
        BuildNear(ConfigModel.Default().Near));

    public static ClientRaidSettings Build(GravityConfig cfg, MapInfo map, SpawnPlan plan, bool managed)
    {
        var s = cfg.Spawn;
        var ms = cfg.MapFor(map.Id);
        var raid = Math.Max(1, plan.RaidSec);

        var pmcSec = s.HardStop.Mode == "percent" ? raid * (1 - s.HardStop.PmcPct / 100) : raid - s.HardStop.BeforeEndMin * 60;
        var scavSec = raid * (1 - s.HardStop.ScavPct / 100);

        var weights = new Dictionary<string, double>();
        foreach (var (z, w) in ms.ZoneWeights)
            if (!ms.Zones.TryGetValue(z, out var on) || on) weights[z] = w;

        var rules = s.GroupCap.Rules.Select(r => new GroupCapRuleDto(r.Size, r.Cmp == "ge" ? "gte" : "eq", r.Count)).ToList();
        var sc = s.Scav;

        InfDto? inf = null;
        IReadOnlyList<RefillGroup> refill = [];
        if (ms.Style == "infinite")
        {
            inf = new InfDto(ms.Inf.DelaySec, ms.Inf.Batch, ms.Inf.PmcShare / 100, ms.Inf.Jitter / 100);
            // 미궁 PMC가 꺼져 있으면(allowPmcLabyrinth=false, 기본) 보충 PMC 분대도 보내지 않는다(스캐브 보충만).
            if (map.Id != "labyrinth" || s.AllowPmcLabyrinth)
            {
                var n = (int)Math.Min(MaxRefill, Math.Max(1, Math.Ceiling(raid / Math.Max(1.0, ms.Inf.DelaySec))));
                refill = SpawnPlanner.PlanRefill(cfg, plan.Seed, n);
            }
        }

        // 연구소는 바닐라에 일반 스캐브가 없다: allowScavLab(기본 false)이 꺼져 있으면 클라 스캐브 관리를 끈다(바닐라 그대로).
        var scavManaged = managed && (map.Id != "laboratory" || s.AllowScavLab);

        return new ClientRaidSettings(
            map.Id, managed, plan.Seed, raid, (int)Math.Round(Math.Max(1, pmcSec)), (int)Math.Round(Math.Max(1, scavSec)), plan.BotCap,
            ms.PmcDist, ms.ScavDist, s.Spacing.Player, s.Spacing.Bots,
            s.Protect.On, s.Protect.Sec, s.Protect.Radius,
            s.Placement, s.HotspotShare / 100, weights, s.AllowPmcLabyrinth,
            new GroupCapDto(s.GroupCap.On, s.GroupCap.Scope, s.GroupCap.Over, rules),
            new ScavDto(scavManaged, sc.ActiveMin, sc.ActiveMax, sc.QuietMin, sc.QuietMax, sc.Refill, sc.Pending, sc.SoftCap, sc.PerPlayer, sc.PscavChance / 100),
            inf, refill, BuildNear(cfg.Near));
    }

    private static NearDto BuildNear(NearSettings n) => new(
        n.On, n.FirstDelay, n.Interval, n.MinDist, n.MaxDist, n.Spacing, n.WaveMin, n.WaveMax,
        n.SlotChance / 100, n.PmcChance / 100, n.Attempts, n.Sight, n.StopAtCap, n.GroupScavOff,
        n.GroupChance / 100, n.GroupMin, n.GroupMax, n.CustomOn, n.CustomT1, n.CustomCap1, n.CustomT2, n.CustomCap2, n.CustomRatio2 / 100);
}
