using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gravity.Core;

/// <summary>
/// 내 프리셋 저장소(0.6, 판정 61): config/presets.json = { raid:[], gear:[], spawn:[] }(웹 목록 그대로).
/// 설정 파일과 따로 저장한다. 저장은 임시 파일 → 교체, 백업 5개(presets-*.json). 목록마다 200개까지.
/// </summary>
public sealed class PresetLibrary(string file, string backupDir)
{
    public const int MaxPerList = 200;
    private const int MaxBackups = 5;
    internal static readonly string[] Lists = ["raid", "gear", "spawn"];
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private readonly object _lock = new();

    /// <summary>지금 목록(없으면 빈 목록 셋). 바깥에서 고치지 말 것(복제해서 씀).</summary>
    public JsonObject Current { get; private set; } = Empty();
    public string? LoadWarning { get; private set; }

    public static JsonObject Empty() => new() { ["raid"] = new JsonArray(), ["gear"] = new JsonArray(), ["spawn"] = new JsonArray() };

    public void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(file)) { Current = Empty(); return; }
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(file, Encoding.UTF8)) as JsonObject ?? throw new JsonException("객체가 아닙니다");
                var (ok, err, lib) = Validate(root);
                if (!ok) throw new JsonException(err);
                Current = lib!;
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(backupDir);
                var broken = Path.Combine(backupDir, $"presets-broken-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
                File.Move(file, broken);
                Current = Empty();
                LoadWarning = L.T($"프리셋 파일이 깨져 빈 목록으로 시작했습니다. 원래 파일: {broken} ({ex.Message})", $"Presets file was broken; started with an empty list. Original file: {broken} ({ex.Message})");
            }
        }
    }

    /// <summary>raid·gear·spawn 세 칸이 배열이고 항목이 객체(id 문자열)인지 본다. 통과하면 세 칸만 남긴 복제본.</summary>
    public static (bool Ok, string? Error, JsonObject? Lib) Validate(JsonNode? node)
    {
        if (node is not JsonObject o) return (false, "객체가 필요합니다", null);
        var lib = new JsonObject();
        foreach (var k in Lists)
        {
            var arr = o[k];
            if (arr is null) { lib[k] = new JsonArray(); continue; }
            if (arr is not JsonArray a) return (false, $"{k}는 배열이어야 합니다", null);
            if (a.Count > MaxPerList) return (false, $"{k} 프리셋이 너무 많습니다({a.Count}, 최대 {MaxPerList})", null);
            var outA = new JsonArray();
            foreach (var item in a)
            {
                if (item is not JsonObject io || io["id"] is not JsonValue idv || !idv.TryGetValue<string>(out var id) || id.Length is 0 or > 40)
                    return (false, $"{k} 항목에 id가 없습니다", null);
                if (ShapeError(k, io) is { } why) return (false, $"{k} 프리셋 '{id}': {why}", null);
                outA.Add(io.DeepClone());
            }
            lib[k] = outA;
        }
        return (true, null, lib);
    }

    /// <summary>웹이 그릴 수 있는 모양인지(Codex 0.6 지적 9): 레이드 = snap 객체, 장비 = tiers 7칸, 스폰 = kind(map·all)와 data 객체.</summary>
    internal static string? ShapeError(string list, JsonObject o) => list switch
    {
        "raid" => o["snap"] is JsonObject ? null : "snap 객체가 없습니다",
        "gear" => o["tiers"] is JsonArray { Count: 7 } t && t.All(x => x is JsonObject) ? null : "tiers(7칸)가 없습니다",
        "spawn" => o["kind"] is JsonValue kv && kv.TryGetValue<string>(out var kind) && kind is "map" or "all" && o["data"] is JsonObject ? null : "kind·data가 없습니다",
        _ => null
    };

    public (bool Ok, string? Error) Save(JsonNode? node)
    {
        var (ok, err, lib) = Validate(node);
        if (!ok) return (false, err);
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            Directory.CreateDirectory(backupDir);
            if (File.Exists(file))
            {
                File.Copy(file, Path.Combine(backupDir, $"presets-{DateTime.Now:yyyyMMdd-HHmmss-fffffff}.json"), true);
                foreach (var old in Directory.GetFiles(backupDir, "presets-2*.json").OrderByDescending(f => f, StringComparer.Ordinal).Skip(MaxBackups))
                    File.Delete(old);
            }
            var doc = new JsonObject { ["format"] = "gravity-library", ["version"] = 1, ["saved"] = DateTime.Now.ToString("s") };
            foreach (var k in Lists) doc[k] = lib![k]!.DeepClone();
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, doc.ToJsonString(Pretty), new UTF8Encoding(false));
            File.Move(tmp, file, true);
            Current = lib!;
            LoadWarning = null;
        }
        return (true, null);
    }
}
