using Gravity.Core;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Gravity.Spawn;

/// <summary>계획을 레이드용 맵 복제본에 쓴다. 새 목록을 다 만든 뒤 마지막에 한 번에 바꿔 넣는다(중간 예외 시 원본 유지).</summary>
public static class RaidSpawnApplier
{
    // SPT 보스 역할 → 웹 보스 id
    public static readonly IReadOnlyDictionary<string, string> BossRoleToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["bossBully"] = "reshala", ["bossKojaniy"] = "shturman", ["bossTagilla"] = "tagilla", ["bossGluhar"] = "glukhar",
        ["bossSanitar"] = "sanitar", ["bossKilla"] = "killa", ["bossBoar"] = "kaban", ["bossKolontay"] = "kollontay",
        ["bossZryachiy"] = "zryachiy", ["bossKnight"] = "goons", ["bossPartisan"] = "partisan"
    };

    private static readonly string[] Modes = ["regular", "pve"];

    /// <param name="hasClient">클라 플러그인이 있는 세션이면 true: 스캐브 웨이브(assault)를 계획의 시작 스캐브로 바꾸고,
    /// 저격(marksman)은 scav.startMarksman이 꺼졌을 때만 지운다. 없으면 스캐브·저격 웨이브는 바닐라 그대로.</param>
    public static void Apply(LocationBase loc, SpawnPlan plan, GravityConfig cfg, MapInfo info, bool hasClient = false)
    {
        var ms = cfg.MapFor(info.Id);
        var scavZones = Zones(info.ScavZones, ms);
        var s = cfg.Spawn;

        var bosses = loc.BossLocationSpawn.Where(e => !MapCatalog.IsPmc(e)).ToList();
        for (var i = 0; i < plan.Groups.Count; i++)
        {
            var g = plan.Groups[i];
            var side = g.Usec ? "pmcUSEC" : "pmcBEAR";
            bosses.Add(new BossLocationSpawn
            {
                BossName = side, BossEscortType = side, BossChance = 100,
                BossDifficulty = g.Difficulty, BossEscortDifficulty = g.Difficulty,
                BossEscortAmount = (g.Size - 1).ToString(), BossZone = PmcZoneFor(info.PmcZones, ms, i),
                IsBossPlayer = false, IsRandomTimeSpawn = false, Time = Math.Max(1, g.TimeSec),
                TriggerId = "", TriggerName = "", Delay = 0, ForceSpawn = false,
                IgnoreMaxBots = g.IsStart ? s.IgnoreCapStart : s.IgnoreCapWaves,
                Supports = null!, SpawnMode = Modes, SptId = "gravity"
            });
        }

        // 스캐브 웨이브: 클라가 있을 때만 갈아 끼운다(시작 스캐브가 0개여도 바닐라 assault는 지운다).
        var manageScavs = hasClient;
        var waves = (loc.Waves ?? []).Where(w => !manageScavs
            || (w.WildSpawnType != WildSpawnType.assault && (s.Scav.StartMarksman || w.WildSpawnType != WildSpawnType.marksman))).ToList();
        foreach (var w in plan.ScavWaves)
            waves.Add(new Wave
            {
                BotPreset = "normal", BotSide = "Savage", WildSpawnType = WildSpawnType.assault, IsPlayers = false,
                KeepZoneOnSpawn = false, SpawnPoints = scavZones, SlotsMin = w.SlotsMin, SlotsMax = w.SlotsMax,
                TimeMin = w.TimeMinSec, TimeMax = w.TimeMaxSec, SpawnMode = [.. Modes], SptId = "gravity"
            });
        if (manageScavs) for (var i = 0; i < waves.Count; i++) waves[i].Number = i;

        // BotMax는 웨이브 밖 자동 스폰의 목표치라 건드리지 않는다(4.1 클라 전체 상한은 클라 설정 — 2-2에서 클라 플러그인으로).
        loc.BossLocationSpawn = bosses;
        if (manageScavs) loc.Waves = waves;
    }

    /// <summary>보스 켜기·확률·맵 끄기. 설정에 없는 역할(팩션·이벤트·모드 보스)은 건드리지 않는다.</summary>
    public static void ApplyBosses(LocationBase loc, GravityConfig cfg, string mapId)
    {
        // 새 확률을 먼저 다 계산한 뒤 한 번에 쓴다(중간 예외 시 일부만 바뀌지 않게).
        var updates = new List<(BossLocationSpawn e, double chance)>();
        foreach (var e in loc.BossLocationSpawn)
        {
            if (e.BossName is not { } role || !BossRoleToId.TryGetValue(role, out var id)) continue;
            if (!cfg.Bosses.TryGetValue(id, out var b)) continue;
            var mapOn = !b.Maps.TryGetValue(mapId, out var on) || on;
            updates.Add((e, !b.On || !mapOn ? 0 : cfg.BossCommonOn && id != "zryachiy" ? cfg.BossCommonChance : b.Chance));
        }
        foreach (var (e, c) in updates) e.BossChance = c;
    }

    /// <summary>
    /// PMC 분대 하나의 BossZone. 쉼표 목록은 쓰지 않는다: 클라는 목록에서 구역 하나를 무작위로 골라 레이드 끝까지
    /// 그 구역만 재시도한다(kb 2.62).
    /// - 구역을 다 켰으면(다 껐으면 다 켠 것으로 봄) "" = 클라가 자기 PMC 구역(HasPmcBotSpawns) 전부를 섞어 차례로 시도
    ///   (11-09 세관 확인 판 18/18). 클라에 PMC 구역이 없는 맵(공장: BotZone 지점이 전부 일반 Bot)은 대기열에 쌓이지 않고 바로 버려진다
    ///   — 이름을 직접 넣으면 같은 구역을 3초마다 영원히 재시도하므로 그보다 낫다(11-09 공장 시험). 그런 맵의 PMC는 0.2 클라 플러그인 몫.
    /// - 일부만 켰으면 켠 구역 이름 하나씩을 분대 순서대로 돌려 준다(all은 BotPmc 지점이 있는 구역 목록, MapCatalog).
    /// </summary>
    internal static string PmcZoneFor(string[] all, MapSettings ms, int groupIndex)
    {
        var on = all.Where(z => !ms.Zones.TryGetValue(z, out var v) || v).ToArray();
        if (on.Length == 0 || on.Length == all.Length) return "";
        return on[groupIndex % on.Length];
    }

    /// <summary>맵 구역 중 웹에서 끈 것을 뺀다. 다 꺼지면 전부 쓴다.</summary>
    internal static string Zones(string[] all, MapSettings ms)
    {
        var on = all.Where(z => !ms.Zones.TryGetValue(z, out var v) || v).ToArray();
        return string.Join(",", on.Length > 0 ? on : all);
    }
}
