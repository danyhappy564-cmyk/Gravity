using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Gravity.Gear;

/// <summary>
/// 1.1.5 봇 체력·스킬 미리보기용: 봇 종류(웹 봇 id)마다 게임 자료의 원래 체력 합(변형 중 최소~최대)·머리 체력과
/// 일반 스킬 레벨 범위(진행도/100). 웹이 "원래 → 바뀐 값"을 보여 주는 데만 쓴다(봇 생성에는 안 씀).
/// </summary>
public sealed record BotBaseInfo(double HpMin, double HpMax, double Head, IReadOnlyDictionary<string, double[]> Skills);

public static class BotBaseCatalog
{
    /// <summary>SPT 봇 종류 표 → 웹 봇 id별 기본값. 한 id에 여러 역할이 있으면 이름이 짧은 역할(assault, bosskilla 등 본래 역할)을 쓴다.</summary>
    public static Dictionary<string, BotBaseInfo> Build(IEnumerable<KeyValuePair<string, BotType?>> types)
    {
        var res = new Dictionary<string, BotBaseInfo>(StringComparer.Ordinal);
        foreach (var (role, bt) in types.Where(kv => kv.Value != null).OrderBy(kv => kv.Key.Length).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var r = role.ToLowerInvariant();
            var id = r is "usec" or "bear" ? r : GearTier.RoleId(r, false, null);
            if (id == null || res.ContainsKey(id)) continue;
            var parts = bt!.BotHealth?.BodyParts?.ToList() ?? [];
            if (parts.Count == 0) continue;
            static double Sum(BodyPart p, bool max) => new[] { p.Head, p.Chest, p.Stomach, p.LeftArm, p.RightArm, p.LeftLeg, p.RightLeg }
                .Sum(m => m == null ? 0 : max ? m.Max : m.Min);
            var skills = (bt.BotSkills?.Common ?? new Dictionary<string, SPTarkov.Server.Core.Models.Common.MinMax<double>>())
                .Where(kv => kv.Value != null)
                .ToDictionary(kv => kv.Key, kv => new[] { Math.Round(kv.Value.Min / 100.0, 1), Math.Round(kv.Value.Max / 100.0, 1) }, StringComparer.Ordinal);
            res[id] = new BotBaseInfo(parts.Min(p => Sum(p, false)), parts.Max(p => Sum(p, true)), parts[0].Head?.Max ?? 0, skills);
        }
        return res;
    }
}
