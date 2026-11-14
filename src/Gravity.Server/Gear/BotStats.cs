using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Gravity.Core;

namespace Gravity.Gear;

/// <summary>티어 하나의 체력·스킬 방식(1.0.6, Forge #2). Health: orig·pmc(440)·mult. Skills: orig·level·pct.</summary>
public sealed record StatRule(string Health, double HealthMult, string Skills, int SkillLevel, double SkillPct)
{
    public static readonly StatRule Original = new("orig", 1, "orig", 1, 100);
    public bool Changes => Health != "orig" || Skills != "orig";
}

/// <summary>티어(T1~T7)별 규칙 + 규칙을 따를 봇 묶음.</summary>
public sealed record BotStatsSettings(IReadOnlyList<StatRule> Tiers, IReadOnlyDictionary<string, bool> Groups)
{
    public static readonly BotStatsSettings Default = new(Enumerable.Repeat(StatRule.Original, 7).ToArray(), BotStats.Groups.ToDictionary(g => g, _ => true));
    public bool Any => Tiers.Any(t => t.Changes) && Groups.Values.Any(on => on);

    /// <summary>그 묶음·티어(0~6)의 규칙. 묶음이 꺼져 있거나 바꿀 것이 없으면 null.</summary>
    public StatRule? RuleFor(string group, int tier)
    {
        if (!Groups.TryGetValue(group, out var on) || !on) return null;
        var r = Tiers[Math.Clamp(tier, 0, Tiers.Count - 1)];
        return r.Changes ? r : null;
    }
}

/// <summary>
/// 봇이 만들어진 직후(BotGenerator.GenerateBot 뒤) 체력·일반 스킬을 그 봇 티어의 규칙대로 고친다. DB 원본은 바꾸지 않는다.
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

    /// <summary>웹 봇 id → 묶음.</summary>
    public static string GroupOfId(string id) =>
        id is "usec" or "bear" ? "pmc"
        : id is "scav" or "marksman" or "pscav" ? "scav"
        : id.StartsWith("boss_", StringComparison.Ordinal) ? "boss"
        : id.StartsWith("f_", StringComparison.Ordinal) ? "follower"
        : "special";

    /// <summary>SPT 역할 → 묶음. 모르는 역할은 null(건드리지 않음).</summary>
    public static string? GroupOf(string? role, bool isPmc, string? side) =>
        GearTier.RoleId(role, isPmc, side) is { } id ? GroupOfId(id) : null;

    /// <summary>
    /// 봇 티어(0~6): Gravity가 장비를 입혔으면 그 티어, 아니면 봇별 장비의 티어 방식대로(고정·분포는 고정 티어, 레벨 따라가기·오프셋은 봇 레벨의 티어).
    /// </summary>
    public static int TierOf(GearSettings g, string roleId, int? dressedTier, int level)
    {
        if (dressedTier is { } t) return Math.Clamp(t, 0, 6);
        var role = g.RoleFor(roleId);
        if (role.Mode is "fixed" or "spread") return Math.Clamp(role.Tier - 1, 0, 6);
        return Math.Clamp(GearTier.TierOfLevel(g.Tiers, Math.Max(1, level)) + (role.Mode == "offset" ? role.Offset : 0), 0, 6);
    }

    /// <summary>체력·스킬을 고친다. 바꿀 것이 없으면 false. 칸이 없는 봇(다른 모드가 지운 경우)은 그 부분만 건너뛴다.</summary>
    public static bool Apply(BotBase bot, StatRule g)
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
