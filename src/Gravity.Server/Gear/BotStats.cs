using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Gravity.Gear;

/// <summary>봇 묶음 하나의 체력·스킬 방식(1.0.6, Forge #2). Health: orig·pmc(440)·mult. Skills: orig·level·pct.</summary>
public sealed record StatGroup(string Health, double HealthMult, string Skills, int SkillLevel, double SkillPct)
{
    public static readonly StatGroup Original = new("orig", 1, "orig", 1, 100);
    public bool Changes => Health != "orig" || Skills != "orig";
}

public sealed record BotStatsSettings(IReadOnlyDictionary<string, StatGroup> Groups)
{
    public static readonly BotStatsSettings Default = new(BotStats.Groups.ToDictionary(g => g, _ => StatGroup.Original));
    public bool Any => Groups.Values.Any(g => g.Changes);
    public StatGroup For(string group) => Groups.TryGetValue(group, out var g) ? g : StatGroup.Original;
}

/// <summary>
/// 봇이 만들어진 직후(BotGenerator.GenerateBot 뒤) 체력·일반 스킬을 묶음 설정대로 고친다. DB 원본은 바꾸지 않는다.
/// 무기 숙련(Mastering)은 그대로. 플레이어 자신의 스캐브는 패치 쪽에서 뺀다.
/// </summary>
public static class BotStats
{
    public static readonly string[] Groups = ["scav", "pmc", "special", "boss", "follower"];

    /// <summary>PMC 기준 체력(부위별 최대, 합 440).</summary>
    public static readonly IReadOnlyDictionary<string, double> PmcHealth = new Dictionary<string, double>
    {
        ["Head"] = 35, ["Chest"] = 85, ["Stomach"] = 70, ["LeftArm"] = 60, ["RightArm"] = 60, ["LeftLeg"] = 65, ["RightLeg"] = 65
    };

    /// <summary>SPT 역할 → 묶음. 모르는 역할은 null(건드리지 않음).</summary>
    public static string? GroupOf(string? role, bool isPmc, string? side)
    {
        var id = GearTier.RoleId(role, isPmc, side);
        if (id == null) return null;
        if (id is "usec" or "bear") return "pmc";
        if (id is "scav" or "marksman" or "pscav") return "scav";
        if (id.StartsWith("boss_", StringComparison.Ordinal)) return "boss";
        if (id.StartsWith("f_", StringComparison.Ordinal)) return "follower";
        return "special";
    }

    /// <summary>체력·스킬을 고친다. 바꿀 것이 없으면 false. 칸이 없는 봇(다른 모드가 지운 경우)은 그 부분만 건너뛴다.</summary>
    public static bool Apply(BotBase bot, StatGroup g)
    {
        if (!g.Changes) return false;
        if (g.Health != "orig" && bot.Health?.BodyParts is { } parts)
            foreach (var (name, part) in parts)
            {
                if (part?.Health is not { } h) continue;
                if (g.Health == "pmc")
                {
                    if (!PmcHealth.TryGetValue(name, out var v)) continue;
                    h.Maximum = v;
                    h.Current = v;
                }
                else
                {
                    var max = Math.Max(1, Math.Round((h.Maximum ?? 0) * g.HealthMult));
                    h.Current = Math.Clamp(Math.Round((h.Current ?? max) * g.HealthMult), 1, max);
                    h.Maximum = max;
                }
            }
        if (g.Skills != "orig" && bot.Skills?.Common is { } skills)
            foreach (var s in skills)
                s.Progress = g.Skills == "level"
                    ? Math.Clamp(g.SkillLevel * 100.0, 0, CommonSkill.MaxSkillProgress)
                    : Math.Clamp(s.Progress * g.SkillPct / 100.0, 0, CommonSkill.MaxSkillProgress);
        return true;
    }
}
