using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;

namespace Gravity.Spawn;

/// <summary>
/// 1.0.6 주간 보스(웹 weeklyBoss). SPT는 서버 시작 때(PostDbLoadService.FlagMapAsGuaranteedBoss) 이번 주 보스 하나를 골라
/// 그 맵 첫 항목을 BossChance 100·ShowOnTarkovMap(PvE) true로 바꾼다. Gravity는 판마다 보스 확률을 자기 값으로 덮으므로
/// 켜짐이면 그 항목을 다시 100%로, 꺼짐이면 Gravity 확률 + 해골 표시 끔으로 맞춘다. 고르는 공식은 SPT 4.1과 같다.
/// </summary>
public static class WeeklyBoss
{
    public sealed record Pick(string Role, string Map);

    /// <summary>SPT 4.1 FlagMapAsGuaranteedBoss의 보스 → 맵.</summary>
    public static string? MapOf(string role) => role switch
    {
        "bossBully" => "bigmap",
        "bossGluhar" => "rezervbase",
        "bossKilla" => "interchange",
        "bossKojaniy" => "woods",
        "bossSanitar" => "shoreline",
        "bossKolontay" => "tarkovstreets",
        "bossKnight" => "lighthouse",
        "bossTagilla" => "factory4_day",
        _ => null
    };

    /// <summary>SPT 4.1 GetWeeklyBoss: 가장 최근 초기화 요일(오늘 포함)의 연·연중 날짜로 시드.</summary>
    public static string? Choose(IReadOnlyList<WildSpawnType> pool, DayOfWeek reset, DateTime today)
    {
        if (pool.Count == 0) return null;
        var d = today.Date.AddDays(-(((int)today.DayOfWeek - (int)reset + 7) % 7));
        return pool[new Random(d.Year * 1009 + d.DayOfYear).Next(0, pool.Count)].ToString();
    }

    /// <summary>SPT 주간 보스가 켜져 있고 그 맵에 그 보스 항목이 있으면 이번 주 보스. 아니면 null.</summary>
    public static Pick? Detect(bool sptEnabled, IReadOnlyList<WildSpawnType>? pool, DayOfWeek reset, DateTime today, Func<string, LocationBase?> baseOf)
    {
        if (!sptEnabled || pool == null || Choose(pool, reset, today) is not { } role || MapOf(role) is not { } map) return null;
        return baseOf(map)?.BossLocationSpawn?.Any(e => e.BossName == role) == true ? new Pick(role, map) : null;
    }

    /// <summary>DB 맵 자료의 그 보스 첫 항목 해골 표시(맵 선택 화면)를 켜고 끈다. 항목이 없으면 false.</summary>
    public static bool SetFlag(LocationBase b, Pick w, bool on)
    {
        var e = b.BossLocationSpawn?.FirstOrDefault(x => x.BossName == w.Role);
        if (e == null) return false;
        e.ShowOnTarkovMap = on;
        e.ShowOnTarkovMapPvE = on;
        return true;
    }
}
