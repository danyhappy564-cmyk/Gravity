namespace Gravity.Core;

public sealed record DetectResult(
    bool Abps, bool Apbs, bool Svm, bool Orbit, bool Sain, bool Ruaf, bool Untar, bool BlackDiv,
    bool MoreBots, bool QuestingBots, string? SvmFolder, bool Ppes = false)
{
    public bool LockWithQuestingBots { get; init; }
    /// <summary>ABPS는 잠금, Questing Bots는 사용자가 잠금을 선택했을 때만 잠근다.</summary>
    public bool SpawnLocked => Abps || (QuestingBots && LockWithQuestingBots);
    public bool GearLocked => Apbs;
    /// <summary>스폰 잠금 이유(웹·로그용). 안 잠겼으면 null.</summary>
    public string? SpawnLockReason => Abps ? "ABPS" : QuestingBots && LockWithQuestingBots ? "Questing Bots" : null;

    /// <summary>ORBIT 서버 모드 폴더 이름(설정 읽기용, 0.6). 없으면 null.</summary>
    public string? OrbitFolder { get; init; }
    public bool MapVariants { get; init; }
    public bool Blackout { get; init; }
    /// <summary>SPT-Realism(체력·스킬을 스스로 바꿈, 1.0.6 봇 체력·스킬 안내용).</summary>
    public bool Realism { get; init; }
    /// <summary>맵 id → 그 맵을 바꿔 끼우는 변형 모드 이름들(0.6, 판정 54).</summary>
    public IReadOnlyDictionary<string, string[]> VariantMaps { get; init; } = new Dictionary<string, string[]>();
}

/// <summary>다른 모드 감지: 로드된 어셈블리 이름 + user/mods 폴더 이름(대소문자 무시).</summary>
public static class ModDetector
{
    public static DetectResult Detect(IEnumerable<string> assemblyNames, IEnumerable<string> modFolderNames, CompatSettings? compat = null)
    {
        var asm = assemblyNames.Select(a => a.ToLowerInvariant()).ToHashSet();
        var folders = modFolderNames.ToList();
        var low = folders.Select(f => f.ToLowerInvariant()).ToList();
        bool Has(params string[] keys) => keys.Any(k => asm.Contains(k) || low.Any(f => f.Contains(k)));
        var svm = folders.FirstOrDefault(f => f.Contains("Server Value Modifier", StringComparison.OrdinalIgnoreCase) || f.StartsWith("[SVM]", StringComparison.OrdinalIgnoreCase));
        var variants = new Dictionary<string, List<string>>();
        foreach (var (key, label, maps) in VariantMods)
            if (Has(key))
                foreach (var m in maps)
                    (variants.TryGetValue(m, out var l) ? l : variants[m] = []).Add(label);
        foreach (var configured in compat?.VariantMods ?? [])
            if (folders.FirstOrDefault(f => f.Equals(configured, StringComparison.OrdinalIgnoreCase)) is { } folder)
                foreach (var id in ConfigModel.KnownMaps)
                    (variants.TryGetValue(id, out var list) ? list : variants[id] = []).Add(folder);
        return new DetectResult(
            Has("acidphantasm-botplacementsystem"), Has("acidphantasm-progressivebotsystem"), svm != null,
            Has("orbit"), Has("sain"), Has("ruafcomehome"), Has("untargh", "tacticaltoaster"), Has("blackdiv"),
            Has("morebots"), Has("questingbots", "skills_extended_questingbots"), svm, Has("pastpresenteventselector", "pastpresent"))
        {
            LockWithQuestingBots = compat?.LockWithQuestingBots ?? false,
            OrbitFolder = folders.FirstOrDefault(f => f.Equals("ORBIT", StringComparison.OrdinalIgnoreCase))
                ?? folders.FirstOrDefault(f => f.Contains("orbit", StringComparison.OrdinalIgnoreCase)),
            MapVariants = Has("mapvariants"), Blackout = Has("blackout"), Realism = Has("realism"),
            VariantMaps = variants.ToDictionary(kv => kv.Key, kv => kv.Value.Distinct().ToArray())
        };
    }

    /// <summary>알려진 변형 모드. 이 목록 밖의 변형은 MapCatalog의 SPT 원본 비교로 감지한다.</summary>
    internal static readonly (string Key, string Label, string[] Maps)[] VariantMods =
    [
        ("manimalinterchange", "Manimal", ["interchange"]),
        ("manimallighthouse", "Manimal", ["lighthouse"]),
        ("manimallabsboiler", "Manimal", ["laboratory"]),
        ("factoryclassic", "FactoryClassic", ["factory4_day", "factory4_night"]),
        ("interchangerework", "Interchange Rework", ["interchange"])
    ];
}
