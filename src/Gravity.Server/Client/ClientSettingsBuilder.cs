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

    public static ClientRaidSettings Build(GravityConfig cfg, MapInfo map, SpawnPlan plan, bool managed,
        bool ruafInstalled = false, bool blackDivInstalled = false, OrbitInfo? orbit = null)
    {
        var s = cfg.Spawn;
        var ms = cfg.MapFor(map.Id);
        var raid = Math.Max(1, plan.RaidSec);
        // 클라 시계는 실제로 들어간 순간이 0이다. 스캐브 레이드(늦게 시작)는 계획 시각을 simStart만큼 당겨 보낸다.
        var sim = Math.Max(0, plan.SimStartSec);

        var pmcSec = (s.HardStop.Mode == "percent" ? raid * (1 - s.HardStop.PmcPct / 100) : raid - s.HardStop.BeforeEndMin * 60) - sim;
        var scavSec = raid * (1 - s.HardStop.ScavPct / 100) - sim;
        // 클라 스캐브 보충의 첫 시도: 서버 시작 스캐브와 같은 지연(scav.separateStart·scav.delay분).
        var scavStart = Math.Max(0, (s.Scav.SeparateStart ? s.Scav.DelayMin * 60 : 0) - sim);

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
            map.Id, managed, plan.Seed, Math.Max(1, raid - sim), (int)Math.Round(Math.Max(1, pmcSec)), (int)Math.Round(Math.Max(1, scavSec)), plan.BotCap,
            ms.PmcDist, ms.ScavDist, s.Spacing.Player, s.Spacing.Bots,
            s.Protect.On, s.Protect.Sec, s.Protect.Radius,
            s.Placement, s.HotspotShare / 100, weights, s.AllowPmcLabyrinth,
            new GroupCapDto(s.GroupCap.On, s.GroupCap.Scope, s.GroupCap.Over, rules),
            new ScavDto(scavManaged, sc.ActiveMin, sc.ActiveMax, sc.QuietMin, sc.QuietMax, sc.Refill, sc.Pending, sc.SoftCap, sc.PerPlayer, sc.PscavChance / 100, scavStart),
            // 좁은 맵(공장·미궁)은 ORBIT 수면 거리 밖 지점이 없어 근처 보충이 전부 버려진다(11-09 공장 판 noPoint 5) → 거리 보정 안 함.
            inf, refill, BuildNear(cfg.Near, ConfigModel.Small(map.Id) ? null : orbit, cfg.OrbitOrDefault.NearAsGhost), Gravity.Factions.FactionRaid.CapExcludeRoles(cfg, ruafInstalled, blackDivInstalled),
            cfg.OrbitOrDefault.RefillAfterExtract, cfg.DiagOrDefault.Log);
    }

    /// <summary>근처 보충 설정. ORBIT 봇 재우기와 맞추기(orbit.nearAsGhost)가 켜져 있으면 깨우는 거리 밖에서 시작(0.6).</summary>
    private static NearDto BuildNear(NearSettings n, OrbitInfo? orbit = null, bool nearAsGhost = false)
    {
        var (min, max) = (orbit ?? OrbitInfo.None).NearRange(n.MinDist, n.MaxDist, nearAsGhost);
        return BuildNear(n, min, max);
    }

    private static NearDto BuildNear(NearSettings n, double min, double max) => new(
        n.On, n.FirstDelay, n.Interval, min, max, n.Spacing, n.WaveMin, n.WaveMax,
        n.SlotChance / 100, n.PmcChance / 100, n.Attempts, n.Sight, n.StopAtCap, n.GroupScavOff,
        n.GroupChance / 100, n.GroupMin, n.GroupMax, n.CustomOn, NearRole(n.CustomT1), n.CustomCap1, NearRole(n.CustomT2), n.CustomCap2, n.CustomRatio2 / 100, n.UseDefault);

    /// <summary>
    /// 웹 화면 이름(near.custom.t1/t2) → 클라 WildSpawnType 이름. pmc는 클라가 자리마다 USEC/BEAR를 고른다.
    /// 컬티스트는 바닐라 보스 생성이 상한일 때 맵 전체 구역을 막는 특수 처리를 타므로 0.2에서는 막고,
    /// 감염자·보스·팩션(ruaf·untar·blackdiv)은 0.3 전까지 스캐브(assault)로 넣는다.
    /// </summary>
    public static string NearRole(string? web) => (web ?? "").ToLowerInvariant() switch
    {
        "pmc" => "pmc",
        "usec" => "pmcUSEC",
        "bear" => "pmcBEAR",
        "marksman" => "marksman",
        "raider" => "pmcBot",
        "rogue" => "exUsec",
        _ => "assault",
    };
}
