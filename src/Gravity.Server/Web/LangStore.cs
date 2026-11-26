using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Gravity.Web;

/// <summary>
/// 1.1.5 언어 팩(INBOX #100): 사용자가 직접 만든 웹 화면 번역 파일 `lang/&lt;코드&gt;.json`.
/// 열쇠 = 영어 원문, 값 = 번역(빈 칸이면 영어). 한국어·영어는 화면에 들어 있어 파일로 받지 않는다.
/// </summary>
public static class LangStore
{
    public const int MaxBytes = 8 * 1024 * 1024;
    private static readonly Regex CodeRx = new("^[a-z]{2,3}(-[a-z0-9]{2,8})?$", RegexOptions.Compiled);
    private static readonly Regex SptRx = new("^[a-z]{2}(-[a-z]{2})?$", RegexOptions.Compiled);

    /// <summary>파일 이름으로 쓸 수 있는 언어 코드인지(소문자 2~3글자, 붙임 하나까지). ko·en은 안 된다.</summary>
    public static bool ValidCode(string? code) => code != null && CodeRx.IsMatch(code) && code is not ("ko" or "en");

    /// <summary>SPT 언어 자료 코드(예: ch, ru, jp, es-mx).</summary>
    public static bool ValidSpt(string? code) => code != null && SptRx.IsMatch(code);

    public sealed record LangInfo(string Code, string Language, string? ItemNames, int Translated, string? Error);

    public static List<LangInfo> List(string dir)
    {
        var list = new List<LangInfo>();
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var code = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
            if (!ValidCode(code)) continue;
            try
            {
                var n = JsonNode.Parse(File.ReadAllText(f, Encoding.UTF8)) as JsonObject;
                var s = n?["strings"] as JsonObject;
                int done = s?.Count(kv => kv.Value is JsonValue v && v.TryGetValue<string>(out var t) && t.Length > 0) ?? 0;
                list.Add(new LangInfo(code, Str(n?["language"]) ?? code, Str(n?["itemNames"]), done, s == null ? "strings 없음 / no strings" : null));
            }
            catch (Exception ex) { list.Add(new LangInfo(code, code, null, 0, ex.Message)); }
        }
        return list;
    }

    public static JsonObject? Read(string dir, string code)
    {
        if (!ValidCode(code)) return null;
        var f = Path.Combine(dir, code + ".json");
        if (!File.Exists(f)) return null;
        try { return JsonNode.Parse(File.ReadAllText(f, Encoding.UTF8)) as JsonObject; } catch { return null; }
    }

    /// <summary>검사 후 저장. 실패하면 이유(한/영), 성공하면 null.</summary>
    public static string? Save(string dir, string code, JsonNode? node)
    {
        if (!ValidCode(code)) return "언어 코드가 올바르지 않습니다(예: zh, ru, pt-br) / Invalid language code (e.g. zh, ru, pt-br)";
        if (node is not JsonObject o || o["strings"] is not JsonObject s) return "strings 칸이 없습니다 / Missing \"strings\" object";
        foreach (var (k, v) in s)
            if (v is not null && !(v is JsonValue jv && jv.TryGetValue<string>(out _)))
                return $"번역은 글자여야 합니다 / Translation must be text: {k}";
        if (o["unused"] is { } u && u is not JsonObject) return "unused 칸은 객체여야 합니다 / \"unused\" must be an object";
        o["code"] = code;
        if (Str(o["language"]) is not { Length: > 0 }) o["language"] = code;
        if (o["itemNames"] is { } i && !ValidSpt(Str(i))) o.Remove("itemNames");
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, code + ".json");
        var tmp = f + ".tmp";
        File.WriteAllText(tmp, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), new UTF8Encoding(false));
        File.Move(tmp, f, true);
        return null;
    }

    /// <summary>SPT 언어 자료에서 아이템 이름만(`&lt;id&gt; Name`).</summary>
    public static Dictionary<string, string> ItemNames(Dictionary<string, string>? locale)
    {
        var d = new Dictionary<string, string>();
        if (locale == null) return d;
        foreach (var (k, v) in locale)
            if (k.Length == 29 && k.EndsWith(" Name", StringComparison.Ordinal) && !string.IsNullOrEmpty(v)) d[k[..24]] = v;
        return d;
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
