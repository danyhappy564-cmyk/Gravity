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

    public static void Apply(LocationBase loc, SpawnPlan plan, GravityConfig cfg, MapInfo info)
    {
        var ms = cfg.MapFor(info.Id);
        var pmcZones = Zones(info.PmcZones, ms);
        var scavZones = Zones(info.ScavZones, ms);
        var s = cfg.Spawn;

        var bosses = loc.BossLocationSpawn.Where(e => !MapCatalog.IsPmc(e)).ToList();
        foreach (var g in plan.Groups)
        {
            var side = g.Usec ? "pmcUSEC" : "pmcBEAR";
            bosses.Add(new BossLocationSpawn
            {
                BossName = side, BossEscortType = side, BossChance = 100,
                BossDifficulty = g.Difficulty, BossEscortDifficulty = g.Difficulty,
                BossEscortAmount = (g.Size - 1).ToString(), BossZone = pmcZones,
                IsBossPlayer = false, IsRandomTimeSpawn = false, Time = Math.Max(1, g.TimeSec),
                TriggerId = "", TriggerName = "", Delay = 0, ForceSpawn = false,
                IgnoreMaxBots = g.IsStart ? s.IgnoreCapStart : s.IgnoreCapWaves,
                Supports = null!, SpawnMode = Modes, SptId = "gravity"
            });
        }

        var waves = (loc.Waves ?? []).Where(w => w.WildSpawnType != WildSpawnType.assault).ToList();
        foreach (var w in plan.ScavWaves)
            waves.Add(new Wave
            {
                BotPreset = "normal", BotSide = "Savage", WildSpawnType = WildSpawnType.assault, IsPlayers = false,
                KeepZoneOnSpawn = false, SpawnPoints = scavZones, SlotsMin = w.SlotsMin, SlotsMax = w.SlotsMax,
                TimeMin = w.TimeMinSec, TimeMax = w.TimeMaxSec, SpawnMode = [.. Modes], SptId = "gravity"
            });
        for (var i = 0; i < waves.Count; i++) waves[i].Number = i;

        loc.BossLocationSpawn = bosses;
        loc.Waves = waves;
        loc.BotMax = plan.BotCap;
    }

    /// <summary>보스 켜기·확률·맵 끄기. 설정에 없는 역할(팩션·이벤트·모드 보스)은 건드리지 않는다.</summary>
    public static void ApplyBosses(LocationBase loc, GravityConfig cfg, string mapId)
    {
        foreach (var e in loc.BossLocationSpawn)
        {
            if (e.BossName is not { } role || !BossRoleToId.TryGetValue(role, out var id)) continue;
            if (!cfg.Bosses.TryGetValue(id, out var b)) continue;
            var mapOn = !b.Maps.TryGetValue(mapId, out var on) || on;
            if (!b.On || !mapOn) { e.BossChance = 0; continue; }
            e.BossChance = cfg.BossCommonOn && id != "zryachiy" ? cfg.BossCommonChance : b.Chance;
        }
    }

    /// <summary>맵 구역 중 웹에서 끈 것을 뺀다. 다 꺼지면 전부 쓴다.</summary>
    internal static string Zones(string[] all, MapSettings ms)
    {
        var on = all.Where(z => !ms.Zones.TryGetValue(z, out var v) || v).ToArray();
        return string.Join(",", on.Length > 0 ? on : all);
    }
}
