using Gravity.Core;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Gravity.Spawn;

/// <summary>웹에 보낼 맵 정보.</summary>
public sealed record MapMeta(string Id, int RaidMin, int BotMax, int BasePmc, int BaseScav,
    string[] PmcZones, string[] ScavZones, Dictionary<string, double> Bosses, string[]? BossZones = null,
    bool HasVariant = false, string[]? OriginalPmcZones = null, string[]? OriginalScavZones = null,
    Dictionary<string, double>? Roster = null, int SvmAddMin = 0, IReadOnlyList<SpawnEntry>? Entries = null);

/// <summary>
/// 1.0.6 웹 타임라인·트리거 카드용 원래 항목 하나(서버 시작 때 DB 값, 다른 모드가 넣은 것 포함).
/// 특수 봇 역할 항목 전부 + 모든 역할의 트리거 항목. Trigger = TriggerName(빈 값 = 시각 무리, interactObject = 플레이어 조작, botEvent = 게임 이벤트).
/// </summary>
public sealed record SpawnEntry(string Role, double Chance, double Time, string Trigger, string TriggerId, string Zone,
    string Escort, string EscortType, double Delay);

/// <summary>서버 로드 뒤 DB에서 맵별 기본 정보를 한 번 모은다(원본은 읽기만).</summary>
public sealed class MapCatalog
{
    private readonly Dictionary<string, MapInfo> _info = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MapMeta> _meta = [];
    public IReadOnlyList<MapMeta> All => _meta;

    /// <summary>
    /// SVM 추가 레이드 시간(분)을 웹 맵 정보에 반영한다. SVM은 Gravity보다 늦게 DB를 바꿀 수 있어서,
    /// SPT 원본 길이(originalMin)와 지금 DB 길이가 같으면(아직 안 더해짐) 더하고, 이미 더해져 있으면 그대로 둔다.
    /// 레이드 중 계획은 SVM이 반영된 복제본 길이를 그대로 쓴다.
    /// </summary>
    public void ApplySvmRaidTime(int addMin, Func<string, int?> originalMin)
    {
        if (addMin <= 0) return;
        for (var i = 0; i < _meta.Count; i++)
        {
            var m = _meta[i];
            var orig = originalMin(m.Id);
            if (orig == null) continue;
            if (m.RaidMin == orig.Value) _meta[i] = m with { RaidMin = orig.Value + addMin, SvmAddMin = addMin };
            else if (m.RaidMin == orig.Value + addMin) _meta[i] = m with { SvmAddMin = addMin };
        }
    }
    public HashSet<string> AutomaticVariants { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MapInfo> _original = new(StringComparer.OrdinalIgnoreCase);
    public MapInfo? OriginalInfoFor(string id) => _original.GetValueOrDefault(id);

    /// <summary>모든 맵의 지점 id를 SPT 원본과 비교하고, 두 배치의 구역을 준비한다. 파일 오류는 맵별로 제한한다.</summary>
    public HashSet<string> DiscoverVariants(DetectResult detect, Func<string, string?> readOriginal, Func<string, LocationBase?> parseOriginal)
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AutomaticVariants.Clear();
        _original.Clear();
        foreach (var (id, loaded) in _loaded)
        {
            string? raw = null;
            try { raw = readOriginal(id); } catch { }
            var same = SameSpawnPoints(loaded, raw);
            if (same == false || (same == null && detect.VariantMaps.ContainsKey(id))) active.Add(id);
            if (same == false) AutomaticVariants.Add(id);
            if (active.Contains(id) && raw != null)
                try
                {
                    if (parseOriginal(raw) is { } original) _original[id] = Compute(id, original, _pmcConfig).Info;
                }
                catch { }
        }
        for (var i = 0; i < _meta.Count; i++)
        {
            var m = _meta[i];
            var original = OriginalInfoFor(m.Id);
            _meta[i] = m with { HasVariant = active.Contains(m.Id), OriginalPmcZones = original?.PmcZones, OriginalScavZones = original?.ScavZones };
        }
        return active;
    }

    public bool TryGet(string id, out MapInfo info) => _info.TryGetValue(id, out info!);

    // 0.6 맵 변형: 서버 시작 때 걸려 있던 Base 참조(변형 모드가 있으면 변형 쪽, kb 1.28)와, Base 객체별로 다시 계산한 맵 정보.
    private readonly Dictionary<string, LocationBase> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<LocationBase, MapInfo> _byBase = new();
    private PmcConfig? _pmcConfig;

    /// <summary>서버 시작 때 이 맵에 걸려 있던 Base와 같은 객체인지(MapVariants가 원본을 고르면 다른 객체가 걸린다).</summary>
    public bool IsLoadedBase(string id, LocationBase? current) =>
        current is not null && _loaded.TryGetValue(id, out var b) && ReferenceEquals(b, current);

    internal void SetLoadedForTest(string id, LocationBase b) => _loaded[id] = b;

    /// <summary>지금 걸린 Base로 계산한 맵 정보(Base 객체별로 한 번). 서버 시작 때 Base면 Build 결과 그대로. 못 하면 null.</summary>
    public MapInfo? InfoFor(string id, LocationBase? current)
    {
        if (current is null || !_info.TryGetValue(id, out var loadedInfo)) return null;
        if (IsLoadedBase(id, current)) return loadedInfo;
        lock (_byBase)
        {
            if (_byBase.TryGetValue(current, out var cached)) return cached;
            var info = Compute(id, current, _pmcConfig).Info;
            _byBase.AddOrUpdate(current, info);
            return info;
        }
    }

    internal void AddForTest(MapInfo info) => _info[info.Id] = info;

    private readonly Dictionary<string, BossLocationSpawn> _bossTemplates = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>보스·특수 봇 역할 → DB에서 확률이 가장 높은 항목(원래 없던 맵에 넣을 때 본보기, 보스 0.4·특수 봇 1.0.6). 읽기 전용으로만 쓴다(복제해서 씀).</summary>
    public IReadOnlyDictionary<string, BossLocationSpawn> BossTemplates => _bossTemplates;
    internal void AddTemplateForTest(BossLocationSpawn e) => _bossTemplates[e.BossName!] = e;

    /// <summary>웹에 보낼 원래 항목 목록(특수 봇 + 트리거 항목, 최대 200개).</summary>
    internal static List<SpawnEntry> EntriesOf(IEnumerable<BossLocationSpawn>? list) =>
        (list ?? []).Where(e => e.BossName is { Length: > 0 } n && !IsPmc(e)
                && (!string.IsNullOrEmpty(e.TriggerName) || ConfigModel.SpecialRoles.Values.Contains(n, StringComparer.OrdinalIgnoreCase)))
            .Take(200)
            .Select(e => new SpawnEntry(e.BossName!, e.BossChance ?? 0, e.Time ?? -1, e.TriggerName ?? "", e.TriggerId ?? "", e.BossZone ?? "",
                e.BossEscortAmount ?? "0", e.BossEscortType ?? "", e.Delay ?? 0))
            .ToList();

    /// <summary>
    /// 본보기로 모을 항목: 보스 역할 전부, 특수 봇 역할(레이더·로그·컬티스트·블러드하운드, 1.0.6 #39)은 트리거 없는 항목만
    /// (레버·탈출구 항목을 본떠 다른 맵에 넣으면 트리거를 떼도 구역·대기가 그 맵에 맞지 않는다).
    /// </summary>
    internal static bool IsTemplateRole(BossLocationSpawn e, string role) =>
        RaidSpawnApplier.BossRoleToId.ContainsKey(role)
        || (ConfigModel.SpecialRoles.Values.Contains(role, StringComparer.OrdinalIgnoreCase) && string.IsNullOrEmpty(e.TriggerName));

    public void Build(LocationTable locations, PmcConfig pmcConfig, BotConfig botConfig)
    {
        _info.Clear();
        _meta.Clear();
        _loaded.Clear();
        _pmcConfig = pmcConfig;
        foreach (var id in ConfigModel.KnownMaps)
        {
            var b = locations.GetLocation(id)?.Base;
            if (b is null) continue;
            _loaded[id] = b;
            var (info, basePmc, baseScav, raidSec, pmcZones, scavZones) = Compute(id, b, pmcConfig);
            _info[id] = info;
            foreach (var e in b.BossLocationSpawn)
                if (e.BossName is { } bn && IsTemplateRole(e, bn)
                    && (!_bossTemplates.TryGetValue(bn, out var cur) || (e.BossChance ?? 0) > (cur.BossChance ?? 0)))
                    _bossTemplates[bn] = e;
            var bosses = b.BossLocationSpawn
                .Where(e => e.BossName is { } n && n.StartsWith("boss", StringComparison.OrdinalIgnoreCase))
                .GroupBy(e => e.BossName!)
                .ToDictionary(g => g.Key, g => g.Max(e => e.BossChance ?? 0));
            var cap = botConfig.MaxBotCap.TryGetValue(id, out var c) ? c : b.BotMax;
            // 보스·특수 봇 항목의 실제 구역 이름(웹 보스 페이지가 고를 목록, 0.3).
            var bossZones = Split(b.BossLocationSpawn.Where(e => e.BossName is { } n2 && !IsPmc(e)
                && (n2.StartsWith("boss", StringComparison.OrdinalIgnoreCase) || ConfigModel.SpecialRoles.Values.Contains(n2))).Select(e => e.BossZone));
            // 맵별 등장 목록(1.0): 서버 시작 때 맵 자료에 있는 PMC 밖 역할 → 최대 확률. 저격 스캐브 웨이브가 있으면 marksman.
            var roster = b.BossLocationSpawn.Where(e => e.BossName is { Length: > 0 } && !IsPmc(e))
                .GroupBy(e => e.BossName!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(e => e.BossChance ?? 0), StringComparer.OrdinalIgnoreCase);
            if (b.Waves?.Any(w => w.WildSpawnType == WildSpawnType.marksman) == true) roster["marksman"] = 100;
            _meta.Add(new MapMeta(id, raidSec / 60, cap, basePmc, baseScav, pmcZones, scavZones, bosses, bossZones, Roster: roster,
                Entries: EntriesOf(b.BossLocationSpawn)));
        }
    }

    /// <summary>Base 하나에서 맵 정보(기준 인원·PMC 구역·스캐브 구역·레이드 길이)를 계산한다. 원본은 읽기만.</summary>
    internal static (MapInfo Info, int BasePmc, int BaseScav, int RaidSec, string[] PmcZones, string[] ScavZones) Compute(string id, LocationBase b, PmcConfig? pmcConfig)
    {
        {
            var custom = pmcConfig != null && pmcConfig.CustomPmcWaves.TryGetValue(id, out var cw) ? cw : [];
            var pmcEntries = (pmcConfig is { RemoveExistingPmcWaves: true } && custom.Count > 0 ? custom : (b.BossLocationSpawn ?? []).Where(IsPmc).Concat(custom)).ToList();
            var basePmc = (int)Math.Round(pmcEntries.Sum(e => (e.BossChance ?? 100) / 100 * (1 + AvgEscort(e.BossEscortAmount))));
            var assault = (b.Waves ?? []).Where(w => w.WildSpawnType == WildSpawnType.assault).ToList();
            var baseScav = (int)Math.Round(assault.Sum(w => ((w.SlotsMin ?? 0) + (w.SlotsMax ?? 0)) / 2.0));
            // 웹에 보이는 PMC 구역 = 봇 PMC 지점(BotPmc)이 있는 구역. 바닐라 항목의 구역 묶음에는 PMC 지점이 없는 구역이
            // 섞여 있다(세관 12개 중 6개: 웨이드·기숙사·주유소 등) — 그런 구역을 하나 고르면 그 분대는 영원히 대기한다(kb 2.62).
            var pmcPointZones = (b.SpawnPointParams ?? [])
                .Where(p => p.Categories?.Contains("BotPmc") == true && !string.IsNullOrEmpty(p.BotZoneName))
                .Select(p => p.BotZoneName!).Distinct().OrderBy(z => z, StringComparer.Ordinal).ToArray();
            var pmcZones = pmcPointZones.Length > 0 ? pmcPointZones : Split(pmcEntries.Select(e => e.BossZone));
            if (pmcZones.Length == 0) pmcZones = Split([b.OpenZones]);
            var scavZones = ScavZonesOf(b, Split(assault.Select(w => w.SpawnPoints)));
            var raidSec = (int)Math.Round((b.EscapeTimeLimit ?? 40) * 60);
            // 바닐라 스캐브 웨이브는 대부분 "시작 때 한 번"이라 DB 합계가 실제 인원보다 훨씬 작다.
            // 맵별 기준 인원(웹 목업과 같은 값, 40분당)을 최소값으로 쓴다. BaseScav는 DB 레이드 길이 기준으로 맞춘다.
            var (refPmc, refScav40) = Reference.TryGetValue(id, out var rf) ? rf : (8, 16);
            basePmc = Math.Max(basePmc, refPmc);
            baseScav = Math.Max(baseScav, (int)Math.Round(refScav40 * raidSec / 60.0 / 40.0));
            return (new MapInfo(id, Math.Max(1, basePmc), Math.Max(0, baseScav), raidSec, pmcZones, scavZones), basePmc, baseScav, raidSec, pmcZones, scavZones);
        }
    }

    /// <summary>
    /// 이 Base의 스폰 지점 id 묶음이 SPT 원본 base.json과 같은지(변형 모드가 설치만 되고 배치를 안 바꾼 경우, Codex 0.6 지적 3).
    /// 원본을 못 읽으면 null(판단 못 함).
    /// </summary>
    public static bool? SameSpawnPoints(LocationBase b, string? originalBaseJson)
    {
        if (string.IsNullOrEmpty(originalBaseJson)) return null;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(originalBaseJson)?["SpawnPointParams"] is not System.Text.Json.Nodes.JsonArray arr) return null;
            var orig = arr.Select(n => n?["Id"]?.GetValue<string>()).Where(x => x != null).ToHashSet(StringComparer.Ordinal);
            var cur = (b.SpawnPointParams ?? []).Select(p => p.Id).Where(x => x != null).ToHashSet(StringComparer.Ordinal);
            return orig.SetEquals(cur!);
        }
        catch { return null; }
    }

    /// <summary>
    /// 스캐브 구역: 웨이브 구역 중 실제 스캐브 지점(Bot + Savage/All)이 있는 것. 그런 구역이 없으면(변형 맵이 구역 이름을 바꾸고 웨이브는 그대로 둔 경우)
    /// 지점이 있는 구역 ∩ OpenZones, 그것도 없으면 웨이브 구역 그대로(판정 55). 바닐라 맵에서도 지점 없는 웨이브 구역은 빠진다
    /// (설치 DB 기준 등대 Zone_OldHouse 등 — 그 구역을 고르면 스캐브가 나올 자리가 없다).
    /// </summary>
    internal static string[] ScavZonesOf(LocationBase b, string[] waveZones)
    {
        var withPoints = (b.SpawnPointParams ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.BotZoneName) && p.Categories?.Contains("Bot") == true && (p.Sides?.Contains("Savage") == true || p.Sides?.Contains("All") == true))
            .Select(p => p.BotZoneName!).ToHashSet(StringComparer.Ordinal);
        if (withPoints.Count == 0) return waveZones;
        var keep = waveZones.Where(withPoints.Contains).ToArray();
        if (keep.Length > 0) return keep;
        var open = Split([b.OpenZones]).Where(withPoints.Contains).ToArray();
        return open.Length > 0 ? open : waveZones;
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
