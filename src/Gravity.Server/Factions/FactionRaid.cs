using Gravity.Core;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils.Json;

namespace Gravity.Factions;

/// <summary>
/// 0.4 팩션 관리: 웹에서 "Gravity가 관리"로 둔 팩션 모드의 항목을 그 판 레이드 복제본에서 다시 만든다.
/// 관리 끔이면 손대지 않는다. 새 목록을 다 만든 뒤 한 번에 바꿔 넣는다(중간 예외 시 원본 유지).
/// </summary>
public static class FactionRaid
{
    private static readonly string[] Modes = ["regular", "pve"];
    private static readonly string[] RuafSpecialists = ["ruafMarksman", "ruafMachinegunner", "ruafAutorifleman"];
    internal const string RuafLeader = "ruafRiflemanSenior", RuafRifleman = "ruafRifleman";
    private const string BdAssault = "blackDivAssault", BdWedge = "bossWedge", BdGuard = "blackDivIb";

    // 모드가 등록한 봇 종류(이름과 번호). 클라는 역할 이름이 번호로 보일 수도 있어 둘 다 보낸다.
    internal static readonly string[] RuafRoles =
        ["ruafRifleman", "ruafRiflemanSenior", "ruafAutorifleman", "ruafGrenadier", "ruafMarksman", "ruafMachinegunner", "remnantRifleman",
         "848400", "848401", "848402", "848403", "848404", "848405", "848406"];
    /// <summary>1.1.3 UNTAR 봇 종류(프리패치 번호 1170~1173: 소총수·분대장·저격수·장교).</summary>
    internal static readonly string[] UntarRoles =
        ["followeruntar", "bossuntarlead", "followeruntarmarksman", "bossuntarofficer", "1170", "1171", "1172", "1173"];
    internal const string UntarLead = "bossuntarlead", UntarOfficer = "bossuntarofficer", UntarFollower = "followeruntar";
    internal static readonly string[] BlackDivRoles =
        ["blackDivLead", "blackDivAssault", "blackDivBreacher", "blackDivSupport", "bossWedge", "blackDivIb",
         "848420", "848421", "848422", "848423", "848424", "848426"];

    /// <summary>
    /// 클라가 맵 상한을 셀 때 뺄 봇 역할(판정 41): "Gravity가 관리"이고 "PMC 동시 상한을 같이 씀"이 꺼진 팩션.
    /// 관리하지 않는 팩션은 모드 그대로라 바닐라처럼 다 센다.
    /// </summary>
    public static List<string>? CapExcludeRoles(GravityConfig cfg, bool ruafInstalled, bool blackDivInstalled, bool untarInstalled = false)
    {
        var l = new List<string>();
        if (ruafInstalled && cfg.Ruaf is { Managed: true, ShareCap: false }) l.AddRange(RuafRoles);
        if (untarInstalled && cfg.Untar is { Managed: true, ShareCap: false }) l.AddRange(UntarRoles);
        if (blackDivInstalled && cfg.BlackDiv is { Managed: true, ShareCap: false }) l.AddRange(BlackDivRoles);
        return l.Count == 0 ? null : l;
    }

    /// <summary>상한 표의 "팩션" 종류로 셀 역할(설치된 팩션 모드, 관리 여부와 상관없이, 1.0).</summary>
    public static HashSet<string> FactionRoleSet(bool ruafInstalled, bool blackDivInstalled, bool untarInstalled = false)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ruafInstalled) set.UnionWith(RuafRoles);
        if (untarInstalled) set.UnionWith(UntarRoles);
        if (blackDivInstalled) set.UnionWith(BlackDivRoles);
        return set;
    }

    public static bool IsRuaf(BossLocationSpawn e) => e.BossName?.StartsWith("ruaf", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>RUAF 모드가 넣는 다른 사냥조(레넌트·전 USEC). 맵을 끌 때만 같이 지운다.</summary>
    public static bool IsRuafOtherHunt(BossLocationSpawn e) => e.TriggerId == "hunt" && e.BossName is "remnantRifleman" or "exUsec";

    /// <summary>1.1.3 UNTAR 모드 무리(UNTAR가 지우는 기준과 같게: 역할 이름에 "untar").</summary>
    public static bool IsUntar(BossLocationSpawn e) => e.BossName?.Contains("untar", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>1.1.3 UNTAR가 넣는 레이더 사냥조(바닐라 레이더 사냥조를 지우고 대신 넣음, TriggerId "hunt").</summary>
    public static bool IsUntarHunt(BossLocationSpawn e) => e.TriggerId == "hunt" && e.BossName?.Contains("pmcBot", StringComparison.Ordinal) == true;

    public static bool IsBlackDiv(BossLocationSpawn e) =>
        e.BossName is { } n && (n.Contains("blackDiv", StringComparison.OrdinalIgnoreCase) || n.Equals(BdWedge, StringComparison.OrdinalIgnoreCase));

    /// <param name="raidSec">이번 판 남은 레이드 길이(초).</param>
    /// <param name="fallbackZones">RUAF 설정에 없는 맵을 켰을 때 순찰 구역(맵 PMC 구역).</param>
    /// <returns>로그용 한 줄. 아무것도 관리하지 않았으면 null.</returns>
    public static string? Apply(LocationBase loc, GravityConfig cfg, string mapId, int seed, int raidSec, FactionData data,
        bool ruafInstalled, bool blackDivInstalled, string[] fallbackZones, bool untarInstalled = false)
    {
        var notes = new List<string>();
        var rnd = new Random(seed ^ 0xFAC7);
        var list = loc.BossLocationSpawn.ToList();
        if (ruafInstalled && cfg.Ruaf is { Managed: true } r) notes.Add(ApplyRuaf(list, r, mapId, rnd, raidSec, data, fallbackZones));
        // 1.1.3: UNTAR는 따로 굴린다(RUAF·BlackDiv 판과 같은 난수 순서를 지키려고 다른 시드).
        if (untarInstalled && cfg.Untar is { Managed: true } u) notes.Add(ApplyUntar(list, u, mapId, new Random(seed ^ 0x47A2), raidSec, data, fallbackZones));
        if (blackDivInstalled && cfg.BlackDiv is { Managed: true } b) notes.Add(ApplyBlackDiv(list, b, mapId, rnd, raidSec, data));
        if (notes.Count == 0) return null;
        loc.BossLocationSpawn = list;
        return string.Join(" / ", notes);
    }

    private static int Between(Random rnd, int min, int max) => rnd.Next(min, Math.Max(min, max) + 1);

    /// <summary>초 단위 창 [min, max]를 3등분해 가중치(초반·중반·후반)로 한 칸을 고르고 그 안에서 고르게.</summary>
    internal static int CurveSec(Random rnd, int min, int max, double[] curve)
    {
        max = Math.Max(min, max);
        var w = curve.Length >= 3 ? curve.Take(3).Select(x => Math.Max(0, x)).ToArray() : [1, 1, 1];
        var total = w.Sum();
        var part = 0;
        if (total > 0) { var x = rnd.NextDouble() * total; while (part < 2 && (x -= w[part]) >= 0) part++; }
        else part = rnd.Next(3);
        var span = max - min;
        return (int)Math.Round(min + span * (part + rnd.NextDouble()) / 3);
    }

    private static bool MapOn(IReadOnlyDictionary<string, bool> maps, string mapId, bool modDefault) =>
        maps.TryGetValue(mapId, out var on) ? on : modDefault;

    private static string[] SplitZones(string? s) => (s ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>구역을 겹치지 않게 차례로 뽑고, 다 쓰면 다시 채운다.</summary>
    private sealed class ZoneBag(string[] all, Random rnd)
    {
        private readonly List<string> _left = [];
        public string Next()
        {
            if (all.Length == 0) return "";
            if (_left.Count == 0) _left.AddRange(all);
            var i = rnd.Next(_left.Count);
            var z = _left[i];
            _left.RemoveAt(i);
            return z;
        }
    }

    // ---------------- RUAF ----------------

    private static string ApplyRuaf(List<BossLocationSpawn> list, FactionSettings r, string mapId, Random rnd, int raidSec,
        FactionData data, string[] fallbackZones)
    {
        data.Ruaf.TryGetValue(mapId, out var md);
        var existing = list.Where(IsRuaf).ToList();
        list.RemoveAll(IsRuaf);
        if (!MapOn(r.Maps, mapId, md != null))
        {
            var others = list.RemoveAll(IsRuafOtherHunt);
            return existing.Count + others > 0 ? L.T($"RUAF: 이 맵 끔(항목 {existing.Count + others}개 지움)", $"RUAF: off on this map (removed {existing.Count + others} entries)") : L.T("RUAF: 이 맵 끔", "RUAF: off on this map");
        }

        var patrolZones = md is { PatrolZones.Length: > 0 } ? md.PatrolZones
            : existing.Where(e => e.TriggerId != "hunt").SelectMany(e => SplitZones(e.BossZone)).Distinct().ToArray() is { Length: > 0 } ez ? ez
            : fallbackZones;
        var ignoreCap = !r.ShareCap;
        int patrols = 0, checkpoints = 0, hunts = 0;

        if (r.Patrol.On && r.Patrol.Chance > 0 && patrolZones.Length > 0)
        {
            var bag = new ZoneBag(patrolZones, rnd);
            var tMax = Math.Max(1, raidSec - 60);
            for (var i = 0; i < r.Patrol.Count; i++)
            {
                var lo = (int)Math.Min(r.Patrol.TMin * 60, tMax);
                var hi = (int)Math.Min(r.Patrol.TMax * 60, tMax);
                // 1.0.6: 곡선이 있으면 창을 3등분한 가중치로(없으면 1.0.5와 같은 난수 순서).
                var t = r.Style == "start" ? -1 : r.Patrol.Curve is { } cv ? CurveSec(rnd, Math.Max(1, lo), hi, cv) : Between(rnd, Math.Max(1, lo), hi);
                list.Add(RuafGroup(Between(rnd, r.Patrol.Min, r.Patrol.Max), r.Patrol.Chance, bag.Next(), t, rnd, ignoreCap));
                patrols++;
            }
        }
        if (r.Checkpoint.On && r.Checkpoint.Chance > 0 && md is { CheckpointZones.Length: > 0 })
        {
            var bag = new ZoneBag(md.CheckpointZones, rnd);
            for (var i = 0; i < r.Checkpoint.Count; i++)
            {
                list.Add(RuafGroup(Between(rnd, r.Checkpoint.Min, r.Checkpoint.Max), r.Checkpoint.Chance, bag.Next(), -1, rnd, ignoreCap));
                checkpoints++;
            }
        }
        if (r.Hunt.On && r.Hunt.Chance > 0)
        {
            var zones = md?.HuntZones is { Length: > 0 } hz ? hz : string.Join(",", patrolZones);
            var t = r.Style == "start" ? -1 : Math.Max(1, (int)Math.Round(raidSec * (0.1 + rnd.NextDouble() * 0.8)));
            list.Add(RuafGroup(Between(rnd, r.Hunt.Min, r.Hunt.Max), r.Hunt.Chance, zones, t, rnd, ignoreCap) with
            {
                TriggerId = "hunt", ForceSpawn = true
            });
            hunts++;
        }
        return L.T($"RUAF: 순찰 {patrols}·검문소 {checkpoints}·사냥조 {hunts}", $"RUAF: patrols {patrols} · checkpoints {checkpoints} · hunters {hunts}")
            + (r.Checkpoint.On && md is not { CheckpointZones.Length: > 0 } ? L.T("(검문소 좌표 없는 맵)", " (no checkpoint coordinates on this map)") : "");
    }

    /// <summary>RUAF 무리: 선임 소총수 1명 + 3명마다 전문병 1명 + 나머지 소총수. 항목 모양은 RUAF 모드와 같게(Supports).</summary>
    internal static BossLocationSpawn RuafGroup(int size, double chance, string zone, double time, Random rnd, bool ignoreCap)
    {
        size = Math.Max(1, size);
        var specialists = size / 3;
        var riflemen = Math.Max(0, size - 1 - specialists);
        var supports = new List<BossSupport>();
        for (var i = 0; i < specialists; i++) supports.Add(Support(RuafSpecialists[rnd.Next(RuafSpecialists.Length)], 1));
        supports.Add(Support(RuafRifleman, riflemen));
        return new BossLocationSpawn
        {
            BossName = RuafLeader, BossChance = chance, BossDifficulty = "normal", BossEscortDifficulty = "normal",
            BossEscortType = RuafRifleman, BossEscortAmount = "1", BossZone = zone, Time = time,
            IsBossPlayer = false, IsRandomTimeSpawn = false, ForceSpawn = false, IgnoreMaxBots = ignoreCap,
            TriggerId = "", TriggerName = "", Delay = 0, Supports = supports, SpawnMode = Modes, SptId = "gravity"
        };
    }

    private static BossSupport Support(string type, int amount) => new()
    {
        BossEscortType = type, BossEscortAmount = amount.ToString(), BossEscortDifficulty = new ListOrT<string>(["normal"], null)
    };

    // ---------------- UNTAR (1.1.3) ----------------

    /// <summary>
    /// UNTAR 모드 항목(역할에 "untar" + UNTAR 레이더 사냥조)을 지우고 웹 값으로 다시 만든다. 무리 모양은 UNTAR GeneratePatrol과 같게
    /// (분대장 또는 장교 + 두 번째 분대장 + 소총수 호위). 검문소 구역은 UNTAR 설정 이름 그대로 — UNTAR 클라가 그 이름으로 검문소 좌표를 찾는다.
    /// 사냥조는 UNTAR처럼 바닐라 레이더 사냥조 자리에 레이더 무리(TriggerId "hunt", ForceSpawn).
    /// </summary>
    internal static string ApplyUntar(List<BossLocationSpawn> list, FactionSettings u, string mapId, Random rnd, int raidSec,
        FactionData data, string[] fallbackZones)
    {
        UntarMap? md = null;
        data.Untar?.Maps.TryGetValue(mapId, out md);
        var existing = list.Where(IsUntar).ToList();
        list.RemoveAll(IsUntar);
        // UNTAR 사냥조는 UNTAR가 그 맵에 레이더 사냥을 둔 경우만 UNTAR 것이다 — 그 맵만 지운다.
        var hadHunt = md?.HuntZones != null ? list.RemoveAll(IsUntarHunt) : 0;
        if (!MapOn(u.Maps, mapId, md != null))
            return existing.Count + hadHunt > 0
                ? L.T($"UNTAR: 이 맵 끔(항목 {existing.Count + hadHunt}개 지움)", $"UNTAR: off on this map (removed {existing.Count + hadHunt} entries)")
                : L.T("UNTAR: 이 맵 끔", "UNTAR: off on this map");

        var rules = data.Untar ?? new UntarConfig(new Dictionary<string, UntarMap>(), UntarSquadRule.PatrolDefault, UntarSquadRule.CheckpointDefault);
        var patrolZones = md is { PatrolZones.Length: > 0 } ? md.PatrolZones
            : existing.SelectMany(e => SplitZones(e.BossZone)).Distinct().ToArray() is { Length: > 0 } ez ? ez
            : fallbackZones;
        var ignoreCap = !u.ShareCap;
        int patrols = 0, checkpoints = 0, hunts = 0;

        if (u.Patrol.On && u.Patrol.Chance > 0 && patrolZones.Length > 0)
        {
            var bag = new ZoneBag(patrolZones, rnd);
            var tMax = Math.Max(1, raidSec - 60);
            for (var i = 0; i < u.Patrol.Count; i++)
            {
                var lo = (int)Math.Min(u.Patrol.TMin * 60, tMax);
                var hi = (int)Math.Min(u.Patrol.TMax * 60, tMax);
                var t = u.Style == "start" ? -1 : u.Patrol.Curve is { } cv ? CurveSec(rnd, Math.Max(1, lo), hi, cv) : Between(rnd, Math.Max(1, lo), hi);
                list.Add(UntarGroup(Between(rnd, u.Patrol.Min, u.Patrol.Max), u.Patrol.Chance, bag.Next(), t, rnd, ignoreCap, rules.Patrol));
                patrols++;
            }
        }
        if (u.Checkpoint.On && u.Checkpoint.Chance > 0 && md is { CheckpointZones.Length: > 0 })
        {
            var bag = new ZoneBag(md.CheckpointZones, rnd);
            for (var i = 0; i < u.Checkpoint.Count; i++)
            {
                list.Add(UntarGroup(Between(rnd, u.Checkpoint.Min, u.Checkpoint.Max), u.Checkpoint.Chance, bag.Next(), -1, rnd, ignoreCap, rules.Checkpoint));
                checkpoints++;
            }
        }
        if (u.Hunt.On && u.Hunt.Chance > 0 && md?.HuntZones is { } hz)
        {
            var size = Math.Max(1, Between(rnd, u.Hunt.Min, u.Hunt.Max));
            var t = u.Style == "start" ? -1 : Math.Max(1, (int)Math.Round(raidSec * (0.1 + rnd.NextDouble() * 0.8)));
            list.Add(new BossLocationSpawn
            {
                BossName = "pmcBot", BossChance = u.Hunt.Chance, BossDifficulty = "normal", BossEscortDifficulty = "normal",
                BossEscortType = "pmcBot", BossEscortAmount = (size - 1).ToString(), BossZone = hz, Time = t,
                IsBossPlayer = false, IsRandomTimeSpawn = false, ForceSpawn = true, IgnoreMaxBots = false,
                TriggerId = "hunt", TriggerName = "", Delay = 0, Supports = [], SpawnMode = Modes, SptId = "gravity"
            });
            hunts++;
        }
        return L.T($"UNTAR: 순찰 {patrols}·검문소 {checkpoints}·사냥조 {hunts}", $"UNTAR: patrols {patrols} · checkpoints {checkpoints} · hunters {hunts}")
            + (u.Checkpoint.On && md is not { CheckpointZones.Length: > 0 } ? L.T("(검문소 구역 없는 맵)", " (no checkpoint zones on this map)") : "");
    }

    /// <summary>UNTAR 무리: 크기가 넘으면 규칙 확률로 장교 분대장·두 번째 분대장, 나머지는 소총수(UNTAR GeneratePatrol과 같은 모양).</summary>
    internal static BossLocationSpawn UntarGroup(int size, double chance, string zone, double time, Random rnd, bool ignoreCap, UntarSquadRule rule)
    {
        size = Math.Max(1, size);
        var lead = size >= rule.MinOfficerSize && rnd.NextDouble() * 100 < rule.OfficerChance ? UntarOfficer : UntarLead;
        var second = size >= rule.MinSecondLeaderSize && rnd.NextDouble() * 100 < rule.SecondLeaderChance;
        var followers = Math.Max(0, size - 1 - (second ? 1 : 0));
        var supports = new List<BossSupport>();
        if (second) supports.Add(Support(UntarLead, 1));
        supports.Add(Support(UntarFollower, followers));
        return new BossLocationSpawn
        {
            BossName = lead, BossChance = chance, BossDifficulty = "normal", BossEscortDifficulty = "normal",
            BossEscortType = UntarFollower, BossEscortAmount = "1", BossZone = zone, Time = time,
            IsBossPlayer = false, IsRandomTimeSpawn = false, ForceSpawn = false, IgnoreMaxBots = ignoreCap,
            TriggerId = "", TriggerName = "", Delay = 0, Supports = supports, SpawnMode = Modes, SptId = "gravity"
        };
    }

    // ---------------- BlackDiv ----------------

    private static string ApplyBlackDiv(List<BossLocationSpawn> list, BlackDivSettings b, string mapId, Random rnd, int raidSec, FactionData data)
    {
        var parts = new List<string>();
        var ignoreCap = !b.ShareCap;

        // 사냥조(맵마다 하나)
        list.RemoveAll(e => IsBlackDiv(e) && e.TriggerId == "hunt" && !e.BossName!.Equals(BdWedge, StringComparison.OrdinalIgnoreCase));
        if (MapOn(b.Maps, mapId, data.BlackDivMaps.Contains(mapId)) && b.Chance > 0)
        {
            var frac = (b.TMin + rnd.NextDouble() * (b.TMax - b.TMin)) / 100.0;
            var size = Between(rnd, b.HuntMin, b.HuntMax);
            list.Add(new BossLocationSpawn
            {
                BossName = BdAssault, BossEscortType = BdAssault, BossChance = b.Chance, BossEscortAmount = (size - 1).ToString(),
                BossDifficulty = "normal", BossEscortDifficulty = "normal", BossZone = "", Time = b.Style == "start" ? -1 : Math.Max(1, Math.Round(raidSec * frac)),
                IsBossPlayer = false, IsRandomTimeSpawn = false, ForceSpawn = true, IgnoreMaxBots = ignoreCap,
                TriggerId = "hunt", TriggerName = "", Delay = 0, Supports = null!, SpawnMode = Modes, SptId = "gravity"
            });
            parts.Add(L.T($"사냥조 {b.Chance:0}% {size}명", $"hunters {b.Chance:0}% x{size}"));
        }
        else parts.Add(L.T("사냥조 없음", "no hunters"));

        if (mapId == "laboratory") parts.Add(ApplyLab(list, b, rnd, data, ignoreCap));
        return "BlackDiv: " + string.Join(", ", parts);
    }

    private static string ApplyLab(List<BossLocationSpawn> list, BlackDivSettings b, Random rnd, FactionData data, bool ignoreCap)
    {
        var blackout = b.BlackoutLink == "always" ? null : data.BlackoutNow();
        var allowed = !(b.BlackoutLink == "only" && blackout == false);
        var mult = b.BlackoutLink == "boost" && blackout == true ? b.BlackoutMult : 1;
        var link = blackout == null ? "" : blackout.Value ? L.T("(정전 판", " (blackout raid") + (mult > 1 ? $" ×{mult:0.#}" : "") + ")" : L.T("(정전 아님)", " (no blackout)");

        // 웨지 무리
        var wedgeChance = Math.Min(100, b.WedgeChance * mult);
        var wedge = list.FindIndex(e => e.BossName?.Equals(BdWedge, StringComparison.OrdinalIgnoreCase) == true);
        string wedgeNote;
        if (!b.Wedge || !allowed || wedgeChance <= 0)
        {
            list.RemoveAll(e => e.BossName?.Equals(BdWedge, StringComparison.OrdinalIgnoreCase) == true);
            wedgeNote = L.T("웨지 없음", "no Wedge");
        }
        else
        {
            if (wedge >= 0) list[wedge] = list[wedge] with { BossChance = wedgeChance, IgnoreMaxBots = ignoreCap };
            else list.Add(new BossLocationSpawn
            {
                BossName = BdWedge, BossEscortType = BdGuard, BossChance = wedgeChance, BossEscortAmount = RaidSpawnApplier.Range(b.HuntMin - 1, b.HuntMax - 1),
                BossDifficulty = "normal", BossEscortDifficulty = "normal", BossZone = "BotZoneFloor2,BotZoneFloor1,BotZoneBasement", Time = -1,
                IsBossPlayer = false, IsRandomTimeSpawn = false, ForceSpawn = true, IgnoreMaxBots = ignoreCap,
                TriggerId = "hunt", TriggerName = "", Delay = 0, Supports = null!, SpawnMode = Modes, SptId = "gravity"
            });
            wedgeNote = L.T($"웨지 {wedgeChance:0}%", $"Wedge {wedgeChance:0}%");
        }

        // 게이트: 트리거마다 원본 항목으로 되돌린 뒤, 굴려서 되면 게이트 구역 항목만 BlackDiv 무리로 바꾼다.
        var gateChance = Math.Min(100, b.LabsGate * mult);
        var taken = 0;
        foreach (var (trig, zone) in FactionData.LabGates)
        {
            if (!data.LabGateVanilla.TryGetValue(trig, out var vanilla) || vanilla.Count == 0) continue;
            var want = allowed && rnd.NextDouble() * 100 < gateChance;
            list.RemoveAll(e => e.TriggerId == trig);
            foreach (var v in vanilla)
            {
                if (want && v.BossZone == zone)
                {
                    list.Add(v with
                    {
                        BossName = BdAssault, BossEscortType = BdAssault, BossChance = 100, BossEscortAmount = RaidSpawnApplier.Range(b.HuntMin - 1, b.HuntMax - 1),
                        BossDifficulty = "normal", BossEscortDifficulty = "normal", ForceSpawn = true, IgnoreMaxBots = ignoreCap, Supports = null!, SptId = "gravity"
                    });
                    taken++;
                }
                else list.Add(v with { });
            }
        }
        var gateNote = data.LabGateVanilla.Count == 0 ? L.T("게이트 원본 없음(그대로)", "no original gate entries (unchanged)") : L.T($"게이트 {taken}곳({gateChance:0}%)", $"gates {taken} ({gateChance:0}%)");
        return L.T("연구소 ", "Labs ") + $"{wedgeNote}, {gateNote}{link}";
    }
}
