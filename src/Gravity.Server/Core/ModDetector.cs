namespace Gravity.Core;

public sealed record DetectResult(
    bool Abps, bool Apbs, bool Svm, bool Orbit, bool Sain, bool Ruaf, bool Untar, bool BlackDiv,
    bool MoreBots, bool QuestingBots, string? SvmFolder, bool Ppes = false)
{
    /// <summary>ABPS가 있으면 스폰 모듈을 잠근다(둘이 같은 웨이브를 서로 덮어씀).</summary>
    public bool SpawnLocked => Abps;
    public bool GearLocked => Apbs;
}

/// <summary>다른 모드 감지: 로드된 어셈블리 이름 + user/mods 폴더 이름(대소문자 무시).</summary>
public static class ModDetector
{
    public static DetectResult Detect(IEnumerable<string> assemblyNames, IEnumerable<string> modFolderNames)
    {
        var asm = assemblyNames.Select(a => a.ToLowerInvariant()).ToHashSet();
        var folders = modFolderNames.ToList();
        var low = folders.Select(f => f.ToLowerInvariant()).ToList();
        bool Has(params string[] keys) => keys.Any(k => asm.Contains(k) || low.Any(f => f.Contains(k)));
        var svm = folders.FirstOrDefault(f => f.Contains("Server Value Modifier", StringComparison.OrdinalIgnoreCase) || f.StartsWith("[SVM]", StringComparison.OrdinalIgnoreCase));
        return new DetectResult(
            Has("acidphantasm-botplacementsystem"), Has("acidphantasm-progressivebotsystem"), svm != null,
            Has("orbit"), Has("sain"), Has("ruafcomehome"), Has("untargh", "tacticaltoaster"), Has("blackdiv"),
            Has("morebots"), Has("questingbots", "skills_extended_questingbots"), svm, Has("pastpresenteventselector", "pastpresent"));
    }
}
