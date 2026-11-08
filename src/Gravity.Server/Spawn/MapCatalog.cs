using Gravity.Core;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Gravity.Spawn;

/// <summary>웹에 보낼 맵 정보.</summary>
public sealed record MapMeta(string Id, int RaidMin, int BotMax, int BasePmc, int BaseScav,
    string[] PmcZones, string[] ScavZones, Dictionary<string, double> Bosses);

/// <summary>서버 로드 뒤 DB에서 맵별 기본 정보를 한 번 모은다(원본은 읽기만).</summary>
public sealed class MapCatalog
{
    /// <summary>[실험] 맵별 바닐라 PMC 항목의 구역 묶음(쉼표 문자열).</summary>
    public static readonly Dictionary<string, List<string>> ZoneSets = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, MapInfo> _info = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MapMeta> _meta = [];
    public IReadOnlyList<MapMeta> All => _meta;

    public bool TryGet(string id, out MapInfo info) => _info.TryGetValue(id, out info!);

    internal void AddForTest(MapInfo info) => _info[info.Id] = info;

    public void Build(LocationTable locations, PmcConfig pmcConfig, BotConfig botConfig)
    {
        _info.Clear();
        _meta.Clear();
        foreach (var id in ConfigModel.KnownMaps)
        {
            var b = locations.GetLocation(id)?.Base;
            if (b is null) continue;
            var custom = pmcConfig.CustomPmcWaves.TryGetValue(id, out var cw) ? cw : [];
            var pmcEntries = (pmcConfig.RemoveExistingPmcWaves && custom.Count > 0 ? custom : b.BossLocationSpawn.Where(IsPmc).Concat(custom)).ToList();
            var basePmc = (int)Math.Round(pmcEntries.Sum(e => (e.BossChance ?? 100) / 100 * (1 + AvgEscort(e.BossEscortAmount))));
            var assault = (b.Waves ?? []).Where(w => w.WildSpawnType == WildSpawnType.assault).ToList();
            var baseScav = (int)Math.Round(assault.Sum(w => ((w.SlotsMin ?? 0) + (w.SlotsMax ?? 0)) / 2.0));
            var pmcZones = Split(pmcEntries.Select(e => e.BossZone));
            ZoneSets[id] = pmcEntries.Select(e => e.BossZone ?? "").Where(z => z.Length > 0).Distinct().ToList();
            if (pmcZones.Length == 0) pmcZones = Split([b.OpenZones]);
            var scavZones = Split(assault.Select(w => w.SpawnPoints));
            var raidSec = (int)Math.Round((b.EscapeTimeLimit ?? 40) * 60);
            // 바닐라 스캐브 웨이브는 대부분 "시작 때 한 번"이라 DB 합계가 실제 인원보다 훨씬 작다.
            // 맵별 기준 인원(웹 목업과 같은 값, 40분당)을 최소값으로 쓴다. BaseScav는 DB 레이드 길이 기준으로 맞춘다.
            var (refPmc, refScav40) = Reference.TryGetValue(id, out var rf) ? rf : (8, 16);
            basePmc = Math.Max(basePmc, refPmc);
            baseScav = Math.Max(baseScav, (int)Math.Round(refScav40 * raidSec / 60.0 / 40.0));
            _info[id] = new MapInfo(id, Math.Max(1, basePmc), Math.Max(0, baseScav), raidSec, pmcZones, scavZones);
            var bosses = b.BossLocationSpawn
                .Where(e => e.BossName is { } n && n.StartsWith("boss", StringComparison.OrdinalIgnoreCase))
                .GroupBy(e => e.BossName!)
                .ToDictionary(g => g.Key, g => g.Max(e => e.BossChance ?? 0));
            var cap = botConfig.MaxBotCap.TryGetValue(id, out var c) ? c : b.BotMax;
            _meta.Add(new MapMeta(id, raidSec / 60, cap, basePmc, baseScav, pmcZones, scavZones, bosses));
        }
    }

    // 맵별 기준 인원: (PMC 한 판, 스캐브 40분당). 웹 목업 MAPS 값과 같다.
    internal static readonly Dictionary<string, (int Pmc, int Scav40)> Reference = new()
    {
        ["bigmap"] = (16, 22), ["woods"] = (14, 20), ["factory4_day"] = (6, 10), ["factory4_night"] = (6, 10),
        ["rezervbase"] = (13, 20), ["shoreline"] = (16, 22), ["interchange"] = (15, 22), ["laboratory"] = (10, 0),
        ["lighthouse"] = (13, 20), ["tarkovstreets"] = (18, 24), ["sandbox"] = (8, 12), ["sandbox_high"] = (10, 12), ["labyrinth"] = (4, 6)
    };

    internal static bool IsPmc(BossLocationSpawn e) => e.BossName is "pmcUSEC" or "pmcBEAR";

    internal static double AvgEscort(string? amounts)
    {
        if (string.IsNullOrWhiteSpace(amounts)) return 0;
        var v = amounts.Split(',').Select(x => int.TryParse(x.Trim(), out var n) ? n : 0).ToList();
        return v.Count == 0 ? 0 : v.Average();
    }

    internal static string[] Split(IEnumerable<string?> lists) =>
        lists.Where(s => !string.IsNullOrWhiteSpace(s))
            .SelectMany(s => s!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal).OrderBy(z => z, StringComparer.Ordinal).ToArray();
}
