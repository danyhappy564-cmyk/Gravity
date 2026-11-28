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
    /// <summary>SPT 만우절이 넣는 이벤트 보스(seasonalevents.json halloweensummon·halloweennightcult, 산타). 연구소 태길라는 원래 보스와 이름이 같아 뺀다.</summary>
    public static readonly HashSet<string> AprilBosses = new(StringComparer.OrdinalIgnoreCase)
        { "peacefullZryachiyEvent", "ravangeZryachiyEvent", "sectactPriestEvent", "sectantPredvestnik", "gifter" };

    /// <param name="halloween">할로윈 또는 만우절이 켜짐(밤 컬티스트 의식이 들어감)</param>
    /// <param name="christmas">크리스마스·새해 또는 만우절이 켜짐(산타가 들어감)</param>
    /// <param name="newYearOnly">새해만 켜짐: 장식 칸을 끄면 춤추는 트리(호로보드) 확률 0(평소 DB 값)</param>
    /// <param name="aprilOnly">만우절만 켜짐: 만우절 봇 칸을 끄면 만우절이 넣은 이벤트 보스 확률 0</param>
    public static string? Apply(LocationBase loc, GravityConfig cfg, string mapId, int seed, bool halloween = false, bool christmas = false,
        bool newYearOnly = false, bool aprilOnly = false)
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
            {
                notes.Add(L.T($"감염 판(비율 {h.InfectionPercentage:0}%)", $"infected raid ({h.InfectionPercentage:0}%)"));
                // 1.1.6 좀비 타길라: 감염 판에서만 웹 값(default면 게임 확률 그대로)
                if (ev.ZTagilla != "default")
                    foreach (var b in loc.BossLocationSpawn ?? [])
                        if (string.Equals(b.BossName, "infectedTagilla", StringComparison.OrdinalIgnoreCase))
                            b.BossChance = ev.ZTagilla == "off" ? 0 : ev.ZTagillaChance;
            }
            else
            {
                h.InfectionPercentage = 0;
                foreach (var b in loc.BossLocationSpawn ?? [])
                    if (b.BossName?.StartsWith("infected", StringComparison.OrdinalIgnoreCase) == true) b.BossChance = 0;
                notes.Add(L.T($"감염 없음(확률 {chance:0}%)", $"no infection (chance {chance:0}%)"));
            }
        }

        if (newYearOnly && !ev.NewYearsDecor && loc.Events?.Khorovod is { Chance: > 0 } kh)
        {
            kh.Chance = 0;
            if (loc.BotLocationModifier != null) loc.BotLocationModifier.KhorovodChance = 0;
            notes.Add(L.T("춤추는 트리 끔", "dancing tree off"));
        }

        int cult = 0, santa = 0, april = 0;
        foreach (var b in loc.BossLocationSpawn ?? [])
        {
            if (aprilOnly && !ev.AprilBots && b.BossName is { } bn && AprilBosses.Contains(bn))
            {
                b.BossChance = 0; april++;
                continue;
            }
            if (halloween && string.Equals(b.BossName, "sectantPredvestnik", StringComparison.OrdinalIgnoreCase)) { b.BossChance = ev.NightCult; cult++; }
            else if (christmas && string.Equals(b.BossName, "gifter", StringComparison.OrdinalIgnoreCase)) { b.BossChance = ev.Santa; santa++; }
        }
        if (april > 0) notes.Add(L.T($"만우절 이벤트 보스 {april}개 끔", $"April Fools event bosses off ({april})"));
        if (cult > 0) notes.Add(L.T($"밤 컬티스트 {ev.NightCult:0}%", $"night cultists {ev.NightCult:0}%"));
        if (santa > 0) notes.Add(L.T($"산타 {ev.Santa:0}%", $"Santa {ev.Santa:0}%"));
        return notes.Count == 0 ? null : L.T("이벤트: ", "events: ") + string.Join(", ", notes);
    }
}
