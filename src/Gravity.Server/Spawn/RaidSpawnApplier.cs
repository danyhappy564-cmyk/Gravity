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

    /// <summary>[실험] 원인 가르기용 플래그(config/experiment.txt 한 줄, 쉼표: ignore,pve,t30,normal,zones). 확정 뒤 지운다.</summary>
    public static HashSet<string> Experiment { get; set; } = [];

    public static void Apply(LocationBase loc, SpawnPlan plan, GravityConfig cfg, MapInfo info)
    {
        var ms = cfg.MapFor(info.Id);
        var pmcZones = Zones(info.PmcZones, ms);
        var scavZones = Zones(info.ScavZones, ms);
        var s = cfg.Spawn;

        var bosses = loc.BossLocationSpawn.Where(e => !MapCatalog.IsPmc(e)).ToList();
        var x = Experiment;
        var sets = MapCatalog.ZoneSets.TryGetValue(info.Id, out var zs) && zs.Count > 0 ? zs : null;
        var gi = 0;
        foreach (var g in plan.Groups)
        {
            var side = g.Usec ? "pmcUSEC" : "pmcBEAR";
            var dif = x.Contains("normal") ? "normal" : g.Difficulty;
            var zone = x.Contains("zones") && sets is not null ? sets[gi % sets.Count] : pmcZones;
            var time = Math.Max(1, g.TimeSec);
            if (x.Contains("t30") && g.IsStart) time = Math.Max(30, time);
            gi++;
            bosses.Add(new BossLocationSpawn
            {
                BossName = side, BossEscortType = side, BossChance = 100,
                BossDifficulty = dif, BossEscortDifficulty = dif,
                BossEscortAmount = (g.Size - 1).ToString(), BossZone = zone,
                IsBossPlayer = false, IsRandomTimeSpawn = false, Time = time,
                TriggerId = "", TriggerName = "", Delay = 0, ForceSpawn = false,
                IgnoreMaxBots = x.Contains("ignore") || (g.IsStart ? s.IgnoreCapStart : s.IgnoreCapWaves),
                Supports = null!, SpawnMode = x.Contains("pve") ? ["pve"] : Modes, SptId = "gravity"
            });
        }

        // 스캐브 웨이브: 계획에 있을 때만 갈아 끼운다(이번 판은 비어 있음 → 바닐라 유지).
        var manageScavs = plan.ScavWaves.Count > 0;
        var waves = (loc.Waves ?? []).Where(w => !manageScavs || w.WildSpawnType != WildSpawnType.assault).ToList();
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

    /// <summary>맵 구역 중 웹에서 끈 것을 뺀다. 다 꺼지면 전부 쓴다.</summary>
    internal static string Zones(string[] all, MapSettings ms)
    {
        var on = all.Where(z => !ms.Zones.TryGetValue(z, out var v) || v).ToArray();
        return string.Join(",", on.Length > 0 ? on : all);
    }
}
