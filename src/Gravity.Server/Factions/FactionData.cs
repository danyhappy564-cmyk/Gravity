using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace Gravity.Factions;

/// <summary>RUAF 설정 파일의 맵 하나: 순찰 구역, 좌표가 있는 검문소 구역, RUAF 사냥 구역(쉼표 묶음).</summary>
public sealed record RuafMap(string[] PatrolZones, string[] CheckpointZones, string? HuntZones);

/// <summary>
/// 0.4 팩션 관리·다른 모드 PMC 구분에 쓰는 읽기 전용 자료. 서버 시작 때 한 번 채운다.
/// 다른 모드의 설정 파일과 SPT 원본 파일을 읽기만 하고, 다른 모드의 코드는 부르지 않는다(Blackout 정전 여부만 리플렉션으로 읽음).
/// </summary>
public sealed class FactionData
{
    public static readonly FactionData Empty = new();

    /// <summary>RUAF 설정의 맵(=모드 기본 맵)별 구역.</summary>
    public IReadOnlyDictionary<string, RuafMap> Ruaf { get; init; } = new Dictionary<string, RuafMap>();
    /// <summary>BlackDiv 설정의 사냥조 맵(=모드 기본 맵).</summary>
    public IReadOnlyList<string> BlackDivMaps { get; init; } = [];
    /// <summary>Blackout 설정의 정전 확률(%). 없으면 null.</summary>
    public double? BlackoutChance { get; init; }
    /// <summary>이번 연구소 판이 정전인지(Blackout이 정해 둔 값). 모르면 null.</summary>
    public Func<bool?> BlackoutNow { get; init; } = () => null;
    /// <summary>SPT 원본 연구소 데이터의 게이트 트리거 항목(트리거 id → 항목들). 게이트 복원·BlackDiv 게이트 본보기.</summary>
    public IReadOnlyDictionary<string, List<BossLocationSpawn>> LabGateVanilla { get; init; } = new Dictionary<string, List<BossLocationSpawn>>();
    /// <summary>맵 → SPT 원본 PMC 항목 지문 개수(pmc.json 맞춤 웨이브 + 맵 base.json).</summary>
    public IReadOnlyDictionary<string, Dictionary<string, int>> SptPmc { get; init; } = new Dictionary<string, Dictionary<string, int>>();

    /// <summary>연구소 게이트 트리거 → 게이트 구역(SPT 원본 데이터 기준).</summary>
    public static readonly IReadOnlyDictionary<string, string> LabGates = new Dictionary<string, string>
    {
        ["autoId_00632_EXFIL"] = "BotZoneGate1", ["autoId_00014_EXFIL"] = "BotZoneGate2"
    };

    private static readonly JsonDocumentOptions Jsonc = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static JsonNode? ParseJsonc(string text) => JsonNode.Parse(text, documentOptions: Jsonc);

    private static string[] Strings(JsonNode? n) =>
        n is JsonArray a ? a.OfType<JsonValue>().Where(v => v.GetValueKind() == JsonValueKind.String).Select(v => v.GetValue<string>()).ToArray() : [];

    private static string? Str(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    /// <summary>RUAF config.jsonc → 맵별 구역. 형식이 틀린 맵은 건너뛴다.</summary>
    public static Dictionary<string, RuafMap> ReadRuaf(string jsonc)
    {
        var d = new Dictionary<string, RuafMap>(StringComparer.OrdinalIgnoreCase);
        if (ParseJsonc(jsonc)?["locations"] is not JsonObject locs) return d;
        foreach (var (map, node) in locs)
        {
            if (node is not JsonObject m) continue;
            // RUAF가 이 맵 검문소를 끈 경우(enableCheckpoints=false)는 좌표가 있어도 세우지 않는다(1.0 전 사용자 요청 확인 중 발견: 해안선).
            var cpOn = m["checkpoint"]?["enableCheckpoints"] is not JsonValue ev || !ev.TryGetValue<bool>(out var en) || en;
            var cps = cpOn && m["checkpoint"]?["checkpointZones"] is JsonArray ca
                ? ca.OfType<JsonObject>().Select(z => Str(z["checkpointZone"])).OfType<string>().Where(z => z.Length > 0).ToArray() : [];
            d[map.ToLowerInvariant()] = new RuafMap(Strings(m["patrol"]?["patrolZones"]), cps, Str(m["hunt"]?["hunts"]?["ruaf"]?["huntZones"]));
        }
        return d;
    }

    /// <summary>BlackDiv config.jsonc → 사냥조 맵.</summary>
    public static string[] ReadBlackDivMaps(string jsonc) =>
        Strings(ParseJsonc(jsonc)?["spawns"]?["huntMaps"]).Select(x => x.ToLowerInvariant()).ToArray();

    /// <summary>Blackout config.json → 정전 확률.</summary>
    public static double? ReadBlackoutChance(string jsonc) =>
        ParseJsonc(jsonc)?["blackoutChance"] is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? v.GetValue<double>() : null;

    /// <summary>PMC 항목 지문: 역할·구역·확률·호위 수·트리거. 시각은 스캐브 레이드 때 SPT가 당기므로 넣지 않는다.</summary>
    public static string PmcSig(string? name, string? zone, double? chance, string? escorts, string? trigger) =>
        $"{name}|{zone}|{(chance ?? 100):0.###}|{escorts}|{trigger}";

    public static string PmcSig(BossLocationSpawn e) => PmcSig(e.BossName, e.BossZone, e.BossChance, e.BossEscortAmount, e.TriggerId);

    private static double? Num(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? v.GetValue<double>() : null;

    /// <summary>원본 JSON 항목 배열에서 PMC 지문을 더한다.</summary>
    public static void AddPmcSigs(Dictionary<string, int> bag, JsonNode? entries)
    {
        if (entries is not JsonArray a) return;
        foreach (var e in a.OfType<JsonObject>())
        {
            var name = Str(e["BossName"]);
            if (name is not ("pmcUSEC" or "pmcBEAR")) continue;
            var sig = PmcSig(name, Str(e["BossZone"]), Num(e["BossChance"]), Str(e["BossEscortAmount"]), Str(e["TriggerId"]));
            bag[sig] = bag.GetValueOrDefault(sig) + 1;
        }
    }

    /// <summary>
    /// 서버 시작 때: 다른 모드 설정 파일과 SPT 원본 파일을 읽는다. 못 읽은 것은 비워 둔다(그 기능만 0.3처럼 동작).
    /// </summary>
    /// <param name="deserialize">JSON 문자열 → SPT 항목 목록(SPT JsonUtil). 게이트 항목을 SPT 모양 그대로 되살리는 데만 쓴다.</param>
    public static FactionData Load(string modsDir, string? sptDataDir, IEnumerable<string> maps,
        Func<string, List<BossLocationSpawn>?> deserialize, Action<string> warn)
    {
        string? Read(params string[] parts)
        {
            var p = Path.Combine(parts);
            try { return File.Exists(p) ? File.ReadAllText(p) : null; }
            catch (Exception ex) { warn($"{p} 읽기 실패: {ex.Message}"); return null; }
        }
        T? Try<T>(string what, Func<T?> f) where T : class
        {
            try { return f(); }
            catch (Exception ex) { warn($"{what} 읽기 실패: {ex.Message}"); return null; }
        }

        var folders = Directory.Exists(modsDir) ? Directory.GetDirectories(modsDir).Select(Path.GetFileName).OfType<string>().ToList() : [];
        string? Folder(string key) => folders.FirstOrDefault(f => f.Contains(key, StringComparison.OrdinalIgnoreCase));

        var ruafDir = Folder("ruafcomehome");
        var ruaf = ruafDir != null && Read(modsDir, ruafDir, "config.jsonc") is { } rj ? Try("RUAF 설정", () => ReadRuaf(rj)) : null;
        var bdDir = Folder("blackdiv");
        var bd = bdDir != null && Read(modsDir, bdDir, "config.jsonc") is { } bj ? Try("BlackDiv 설정", () => ReadBlackDivMaps(bj)) : null;
        var boDir = folders.FirstOrDefault(f => f.Equals("Blackout", StringComparison.OrdinalIgnoreCase)) ?? Folder("blackout");
        double? boChance = null;
        if (boDir != null && Read(modsDir, boDir, "config.json") is { } boj)
            try { boChance = ReadBlackoutChance(boj); } catch (Exception ex) { warn($"Blackout 설정 읽기 실패: {ex.Message}"); }

        var gates = new Dictionary<string, List<BossLocationSpawn>>();
        var sptPmc = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        if (sptDataDir != null)
        {
            var pmcJson = Read(sptDataDir, "configs", "pmc.json") is { } pj ? Try("SPT pmc.json", () => JsonNode.Parse(pj)) : null;
            foreach (var map in maps)
            {
                var bag = new Dictionary<string, int>();
                AddPmcSigs(bag, pmcJson?["customPmcWaves"]?[map]);
                var baseText = Read(sptDataDir, "database", "locations", map, "base.json");
                var bossNode = baseText != null ? Try($"SPT {map} base.json", () => JsonNode.Parse(baseText)?["BossLocationSpawn"]) : null;
                AddPmcSigs(bag, bossNode);
                if (pmcJson != null || bossNode != null) sptPmc[map] = bag;
                if (map == "laboratory" && bossNode != null)
                {
                    var all = Try("SPT 연구소 항목", () => deserialize(bossNode.ToJsonString())) ?? [];
                    foreach (var trig in LabGates.Keys)
                        gates[trig] = all.Where(e => e.TriggerId == trig).ToList();
                }
            }
        }

        return new FactionData
        {
            Ruaf = ruaf ?? new Dictionary<string, RuafMap>(), BlackDivMaps = bd ?? [], BlackoutChance = boChance,
            BlackoutNow = BlackoutReader(), LabGateVanilla = gates, SptPmc = sptPmc
        };
    }

    /// <summary>Blackout 모드의 이번 판 정전 여부를 읽는 함수(읽기만). 모드가 없거나 모양이 바뀌었으면 늘 null.</summary>
    private static Func<bool?> BlackoutReader()
    {
        try
        {
            var router = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType("BlackoutServer.BlackoutStateRouter", false); } catch { return null; } })
                .FirstOrDefault(t => t != null);
            var field = router?.GetField("_controller", BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null) return () => null;
            return () =>
            {
                try
                {
                    var c = field.GetValue(null);
                    return c?.GetType().GetProperty("CurrentRaidBlackout")?.GetValue(c) as bool?;
                }
                catch { return null; }
            };
        }
        catch { return () => null; }
    }
}
