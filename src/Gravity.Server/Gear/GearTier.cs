using Gravity.Core;

namespace Gravity.Gear;

/// <summary>봇 하나의 장비 결정 결과: 웹 봇 id, 묶음, 티어(0~6), PMC 여부.</summary>
public sealed record GearPick(string RoleId, string Group, int Tier, bool Pmc);

/// <summary>티어·레벨 고르기(웹 lvRange·tierDist와 같은 규칙). 난수만 받으면 되는 순수 함수.</summary>
public static class GearTier
{
    public const int LvMax = 79;

    public static int TierOfLevel(IReadOnlyList<TierCaps> tiers, int level)
    {
        for (int i = 0; i < tiers.Count; i++)
            if (level >= tiers[i].Min && level <= tiers[i].Max) return i;
        return level < tiers[0].Min ? 0 : tiers.Count - 1;
    }

    /// <summary>내 레벨 기준 봇 레벨 구간(웹 lvRange). scav면 스캐브 폭(scavSame이 아니면).</summary>
    public static (int Lo, int Hi) LevelRange(GearSettings g, int my, bool scav)
    {
        var v = g.Lv;
        if (v.Pick == "fixed") return (v.FixMin, Math.Max(v.FixMin, v.FixMax));
        int b, a;
        if (v.Pick == "flat") { b = v.Below; a = v.Above; }
        else
        {
            var d = (scav && !v.ScavSame ? v.ScavDeltas : v.Deltas)[TierOfLevel(g.Tiers, my)];
            b = d.B; a = d.A;
        }
        int lo = Math.Clamp(my - b, 1, LvMax);
        return (lo, Math.Max(lo, Math.Clamp(my + a, 1, LvMax)));
    }

    /// <summary>섞기 표의 행(레벨 티어)에서 실제 티어를 뽑는다. 행 합이 0이면 그대로.</summary>
    public static int Mix(GearSettings g, int t, Random rnd)
    {
        var w = g.Lv.Weights[t];
        double sum = w.Sum();
        if (sum <= 0) return t;
        double x = rnd.NextDouble() * sum;
        for (int j = 0; j < w.Length; j++)
        {
            x -= w[j];
            if (x < 0) return j;
        }
        return t;
    }

    /// <summary>1.0.6 티어 분포: 비중대로 티어 번호(0~)를 뽑는다. 합이 0이면 -1. 반복은 칸 수(7)로 끝.</summary>
    public static int Weighted(double[] w, Random rnd)
    {
        double sum = 0;
        foreach (var x in w) if (x > 0) sum += x;
        if (sum <= 0) return -1;
        double v = rnd.NextDouble() * sum;
        int last = -1;
        for (int j = 0; j < w.Length; j++)
        {
            if (w[j] <= 0) continue;
            last = j;
            v -= w[j];
            if (v < 0) return j;
        }
        return last;
    }

    /// <summary>
    /// 봇 티어(0~6). level은 PMC면 실제 레벨, 아니면 null(내 레벨 구간에서 가상 레벨을 뽑음).
    /// </summary>
    public static int Pick(GearSettings g, RoleGear role, string group, int playerLevel, int? level, Random rnd)
    {
        int st = Math.Clamp(g.StartTier - 1, 0, 6);
        int t;
        if (role.Mode == "spread") t = role.Spread is { } sw && Weighted(sw, rnd) is var k && k >= 0 ? k : role.Tier - 1;
        else if (role.Mode == "fixed") t = role.Tier - 1;
        else if (g.TierMode == "fixed") t = g.FixedTier - 1;
        else
        {
            int L = level ?? RandomLevel(g, playerLevel, group == "scavs", rnd);
            t = TierOfLevel(g.Tiers, L);
            if (g.TierMode == "mixed") t = Mix(g, t, rnd);
            if (role.Mode == "offset") t += role.Offset;
        }
        t = Math.Clamp(Math.Max(st, t), 0, 6);
        if (role.Poverty > 0 && rnd.NextDouble() * 100 < role.Poverty) t = Math.Max(0, t - 2);
        return t;
    }

    public static int RandomLevel(GearSettings g, int playerLevel, bool scav, Random rnd)
    {
        var (lo, hi) = LevelRange(g, Math.Clamp(playerLevel, 1, LvMax), scav);
        return rnd.Next(lo, hi + 1);
    }

    /// <summary>SPT 역할 이름 → 웹 봇 id. 모르는 역할은 null(손대지 않음).</summary>
    public static string? RoleId(string? sptRole, bool isPmc, string? side)
    {
        if (isPmc) return string.Equals(side, "Bear", StringComparison.OrdinalIgnoreCase) || (sptRole ?? "").Contains("bear", StringComparison.OrdinalIgnoreCase) ? "bear" : "usec";
        var r = (sptRole ?? "").ToLowerInvariant();
        if (r.StartsWith("ruaf")) return "ruaf";
        if (r == "blackdivib") return "wedge";
        if (r.StartsWith("blackdiv")) return "blackdiv";
        if (r.StartsWith("infected")) return "infected";
        if (r.StartsWith("sectant")) return "cultist";
        if (r.StartsWith("followergluhar")) return "f_glukhar";
        if (r.StartsWith("followerkolontay")) return "f_kollontay";
        if (r.StartsWith("followerboar")) return "f_kaban";
        return r switch
        {
            "assault" or "assaultgroup" or "crazyassaultevent" or "cursedassault" => "scav",
            "marksman" => "marksman",
            "bossbully" => "boss_reshala",
            "bosskojaniy" => "boss_shturman",
            "bosstagilla" or "bosstagillaagro" => "boss_tagilla",
            "bossgluhar" => "boss_glukhar",
            "bosssanitar" => "boss_sanitar",
            "bosskilla" or "bosskillaagro" => "boss_killa",
            "bossboar" => "boss_kaban",
            "bossboarsniper" => "f_kaban",
            "bosskolontay" => "boss_kollontay",
            "bosszryachiy" => "boss_zryachiy",
            "bosspartisan" => "boss_partisan",
            "bossknight" or "followerbigpipe" or "followerbirdeye" => "boss_goons",
            "followerbully" => "f_reshala",
            "followersanitar" => "f_sanitar",
            "followerkojaniy" => "f_shturman",
            "followerzryachiy" => "f_zryachiy",
            "pmcbot" => "raider",
            "exusec" => "rogue",
            "arenafighter" or "arenafighterevent" => "bloodhound",
            "bosswedge" => "wedge",
            _ => null
        };
    }
}
