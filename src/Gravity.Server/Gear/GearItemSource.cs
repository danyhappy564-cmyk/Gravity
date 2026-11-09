using System.Text;
using System.Text.RegularExpressions;

namespace Gravity.Gear;

/// <summary>R_F의 APBS ItemImportHelper.cs:44~45,1580~1599 폴더 ID 스캔을 이식·확장한다.</summary>
public static class GearItemSource
{
    private static readonly Regex IdInText = new("[\"']([0-9a-fA-F]{24})[\"']", RegexOptions.Compiled);
    private static readonly Regex IdInBinary = new("(?<![0-9a-fA-F])([0-9a-fA-F]{24})(?![0-9a-fA-F])", RegexOptions.Compiled);
    // 정의 근거(강한 순): 아이템 정의 필드·CustomItems의 아이템 키(3) > 이름 번역 키 "<id> Name"(2) > 단순 참조(1).
    // 다른 모드가 호환 부품·전리품·퀘스트·번역으로 남의 아이템을 참조해도 정의한 모드를 출처로 고른다.
    private static readonly Regex IdDefField = new("\"(?:_id|newId|id|itemId|ItemId|NewId)\"\\s*:\\s*\"([0-9a-fA-F]{24})\"", RegexOptions.Compiled);
    private static readonly Regex IdAsItemKey = new("\"([0-9a-fA-F]{24})\"\\s*:\\s*\\{\\s*\"(?:itemTplToClone|ItemTplToClone|_id|_tpl|_name|_parent|overrideProperties)\"", RegexOptions.Compiled);
    private static readonly Regex IdLocaleName = new("\"([0-9a-fA-F]{24}) (?:Name|ShortName)\"", RegexOptions.Compiled);
    public static string Resolve(string id, IReadOnlySet<string>? vanilla, IReadOnlyDictionary<string,string> sources) =>
        vanilla == null ? "출처 미확인" : vanilla.Contains(id) ? "vanilla" : sources.GetValueOrDefault(id) ?? "모드(출처 미상)";

    /// <summary>시작·명시적 재계산 때만. 설치 안 된 ID·바닐라는 제외하고 두 모드 이상 참조하면 출처 미상.</summary>
    public static IReadOnlyDictionary<string,string> Scan(string? modsDir, IReadOnlySet<string> modIds)
    {
        var hits = new Dictionary<string, Dictionary<string, int>>();
        if (modsDir == null || !Directory.Exists(modsDir) || modIds.Count == 0) return new Dictionary<string,string>();
        foreach (var folder in Directory.EnumerateDirectories(modsDir).Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(folder);
            var pending = new Stack<(string Path, int Depth)>(); pending.Push((folder,0));
            int files = 0;
            while (pending.Count > 0 && files < 20000)
            {
                var (dir, depth) = pending.Pop();
                try
                {
                    if (depth < 10)
                        foreach (var sub in Directory.EnumerateDirectories(dir))
                            if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) == 0 && !new[]{"node_modules",".git","logs","cache"}.Contains(Path.GetFileName(sub))) pending.Push((sub,depth+1));
                    foreach (var file in Directory.EnumerateFiles(dir))
                    {
                        if (++files > 20000) break;
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext is not (".json" or ".cs" or ".js" or ".ts" or ".dll")) continue;
                        var f = new FileInfo(file);
                        if (f.Length > (ext == ".dll" ? 64 : 16) * 1024 * 1024) continue;
                        try
                        {
                            var bytes = File.ReadAllBytes(file);
                            var text = Encoding.UTF8.GetString(bytes);
                            if (ext == ".dll") text += Encoding.Unicode.GetString(bytes);
                            void Hit(Regex re, int score)
                            {
                                foreach (Match m in re.Matches(text))
                                {
                                    var id = m.Groups[1].Value.ToLowerInvariant();
                                    if (!modIds.Contains(id)) continue;
                                    if (!hits.TryGetValue(id, out var owners)) hits[id] = owners = new();
                                    if (owners.GetValueOrDefault(name) < score) owners[name] = score;
                                }
                            }
                            if (ext == ".dll") Hit(IdInBinary, 1);
                            else
                            {
                                Hit(IdInText, 1);
                                Hit(IdLocaleName, 2);
                                Hit(IdDefField, 3);
                                Hit(IdAsItemKey, 3);
                            }
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return Pick(hits);
    }

    /// <summary>가장 강한 근거를 가진 모드가 하나뿐이면 그 모드. 같은 근거가 둘 이상이면 출처 미상.</summary>
    internal static IReadOnlyDictionary<string,string> Pick(IReadOnlyDictionary<string, Dictionary<string,int>> hits)
    {
        var result = new Dictionary<string,string>();
        foreach (var (id, owners) in hits)
        {
            int best = owners.Values.Max();
            var top = owners.Where(kv => kv.Value == best).Select(kv => kv.Key).ToList();
            if (top.Count == 1) result[id] = top[0];
        }
        return result;
    }
}
