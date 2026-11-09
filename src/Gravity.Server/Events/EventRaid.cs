using Gravity.Core;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace Gravity.Events;

/// <summary>
/// 0.3 판마다 이벤트 봇 확률. 이벤트가 실제로 켜져 레이드 복제본에 이벤트 항목이 있을 때만 바꾼다(담당이 누구든).
/// 감염자: 이번 판이 감염 판인지 굴려 아니면 감염 비율 0·감염자 항목 확률 0. 감염 정도는 SPT 값 그대로.
/// 밤 컬티스트 의식(sectantPredvestnik)·산타(gifter): 그 이벤트가 켜졌을 때만 항목 확률을 웹 값으로.
/// DB에는 이벤트가 아닐 때도 확률 0인 gifter 항목이 여러 맵에 있어, 이벤트 확인 없이 바꾸면 산타가 나온다(인게임 11-09).
/// </summary>
public static class EventRaid
{
    /// <param name="halloween">할로윈 또는 만우절이 켜짐(밤 컬티스트 의식이 들어감)</param>
    /// <param name="christmas">크리스마스·새해 또는 만우절이 켜짐(산타가 들어감)</param>
    public static string? Apply(LocationBase loc, GravityConfig cfg, string mapId, int seed, bool halloween = false, bool christmas = false)
    {
        var ev = cfg.Events;
        if (ev == null) return null;
        var notes = new List<string>();
        var rnd = new Random(seed ^ 0xE7E47);

        var h = loc.Events?.Halloween2024;
        if (h is { InfectionPercentage: > 0 })
        {
            var inf = cfg.Specials?.GetValueOrDefault("infected");
            double chance = inf is { On: true } ? inf.Chance.GetValueOrDefault(mapId, 0) : ev.Zombies;
            if (rnd.NextDouble() * 100 < chance)
                notes.Add($"감염 판(비율 {h.InfectionPercentage:0}%)");
            else
            {
                h.InfectionPercentage = 0;
                foreach (var b in loc.BossLocationSpawn ?? [])
                    if (b.BossName?.StartsWith("infected", StringComparison.OrdinalIgnoreCase) == true) b.BossChance = 0;
                notes.Add($"감염 없음(확률 {chance:0}%)");
            }
        }

        int cult = 0, santa = 0;
        foreach (var b in loc.BossLocationSpawn ?? [])
        {
            if (halloween && string.Equals(b.BossName, "sectantPredvestnik", StringComparison.OrdinalIgnoreCase)) { b.BossChance = ev.NightCult; cult++; }
            else if (christmas && string.Equals(b.BossName, "gifter", StringComparison.OrdinalIgnoreCase)) { b.BossChance = ev.Santa; santa++; }
        }
        if (cult > 0) notes.Add($"밤 컬티스트 {ev.NightCult:0}%");
        if (santa > 0) notes.Add($"산타 {ev.Santa:0}%");
        return notes.Count == 0 ? null : "이벤트: " + string.Join(", ", notes);
    }
}
