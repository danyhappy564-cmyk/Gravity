using System.Text;
using System.Text.RegularExpressions;

namespace Gravity.Gear;

/// <summary>R_F의 APBS ItemImportHelper.cs:44~45,1580~1599 폴더 ID 스캔을 이식·확장한다.</summary>
public static class GearItemSource
{
    private static readonly Regex IdInText = new("[\"']([0-9a-fA-F]{24})[\"']", RegexOptions.Compiled);
    private static readonly Regex IdInBinary = new("(?<![0-9a-fA-F])([0-9a-fA-F]{24})(?![0-9a-fA-F])", RegexOptions.Compiled);
    public static string Resolve(string id, IReadOnlySet<string>? vanilla, IReadOnlyDictionary<string,string> sources) =>
        vanilla == null ? "출처 미확인" : vanilla.Contains(id) ? "vanilla" : sources.GetValueOrDefault(id) ?? "모드(출처 미상)";

    /// <summary>시작·명시적 재계산 때만. 설치 안 된 ID·바닐라는 제외하고 두 모드 이상 참조하면 출처 미상.</summary>
    public static IReadOnlyDictionary<string,string> Scan(string? modsDir, IReadOnlySet<string> modIds)
    {
        var hits = new Dictionary<string, HashSet<string>>();
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
                            foreach (Match m in (ext==".dll"?IdInBinary:IdInText).Matches(text))
                            {
                                var id = m.Groups[1].Value.ToLowerInvariant();
                                if (!modIds.Contains(id)) continue;
                                if (!hits.TryGetValue(id, out var owners)) hits[id] = owners = [];
                                owners.Add(name);
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
        return hits.Where(kv=>kv.Value.Count == 1).ToDictionary(kv=>kv.Key,kv=>kv.Value.Single());
    }
}
