using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gravity.Core;

/// <summary>
/// 1.1.3 PastPresentEventSelector(PPES) 설정 요약 — 웹이 "PPES가 이 판 확률을 바꿀 수 있는 것"을 안내하려고 읽기만 한다.
/// PPES는 Gravity와 같은 레이드 생성 함수 뒤에 과거 이벤트(직접 고른 것 + 자동 교체)와 사용자 스폰 편집을 적용하므로,
/// 켜진 것이 있으면 보스·특수 봇·팩션 확률이 웹 값과 다를 수 있다. 실행 순서는 정해져 있지 않다.
/// </summary>
public sealed record PpesInfo(bool Installed, bool ConfigRead, int Events, string Rotation, string SeasonalRotation, string[] SpawnEdits)
{
    public static readonly PpesInfo None = new(false, false, 0, "off", "off", []);

    /// <summary>켜진 것(이벤트·자동 교체·스폰 편집)이 하나라도 있는지.</summary>
    public bool Active => Events > 0 || Rotation != "off" || SeasonalRotation != "off" || SpawnEdits.Length > 0;

    private static readonly JsonDocumentOptions Opts = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>user/mods에서 PPES 폴더를 찾아 config.json을 요약한다. 없으면 None, 못 읽으면 설치됨·읽기 실패.</summary>
    public static PpesInfo Read(string? modsDir)
    {
        if (string.IsNullOrEmpty(modsDir) || !Directory.Exists(modsDir)) return None;
        string? dir;
        try
        {
            dir = Directory.GetDirectories(modsDir).FirstOrDefault(d =>
            {
                var n = Path.GetFileName(d);
                return n.Contains("pastpresenteventselector", StringComparison.OrdinalIgnoreCase) || n.Contains("pastpresent", StringComparison.OrdinalIgnoreCase);
            });
        }
        catch { return None; }
        if (dir == null) return None;
        try
        {
            var path = Path.Combine(dir, "config.json");
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : new PpesInfo(true, false, 0, "off", "off", []);
        }
        catch { return new PpesInfo(true, false, 0, "off", "off", []); }
    }

    /// <summary>config.json 글 → 요약. 형식이 틀린 칸은 꺼짐으로 본다.</summary>
    public static PpesInfo Parse(string json)
    {
        var root = JsonNode.Parse(json, documentOptions: Opts) as JsonObject;
        if (root == null) return new PpesInfo(true, false, 0, "off", "off", []);
        static string Mode(JsonNode? n) => n?["mode"] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : "off";
        var events = root["events"] is JsonArray ea ? ea.Count(x => x is JsonValue v && v.GetValueKind() == JsonValueKind.String) : 0;
        var edits = new List<string>();
        if (root["spawns"] is JsonObject sp)
            foreach (var (map, list) in sp)
                if (list is JsonArray arr)
                    foreach (var e in arr.OfType<JsonObject>())
                    {
                        var role = e["role"] is JsonValue rv && rv.GetValueKind() == JsonValueKind.String ? rv.GetValue<string>() : "";
                        var remove = e["remove"] is JsonValue bv && bv.GetValueKind() == JsonValueKind.True;
                        var changes = remove || e["chance"] is JsonValue { } || e["zones"] is JsonValue { } || e["escort"] is JsonValue { };
                        if (changes && role.Length > 0) edits.Add($"{map}/{role}");
                    }
        return new PpesInfo(true, true, events, Mode(root["rotation"]), Mode(root["seasonalRotation"]), edits.Distinct().ToArray());
    }
}
