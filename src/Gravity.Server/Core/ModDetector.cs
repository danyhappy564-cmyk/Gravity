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
    /// <summary>ISB Special Forces(1.0.7 안내용). Gravity는 ISB 항목을 관리하지 않고 웹 안내만 띄운다.</summary>
    public bool Isb { get; init; }
    /// <summary>WTT PackNStrap(1.0.7): 벨트를 봇 완장 칸 장비로 쓰고, 탄창을 벨트에 넣는다.</summary>
    public bool PackNStrap { get; init; }
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
            MapVariants = Has("mapvariants"), Blackout = Has("blackout"), Realism = Has("realism"), Isb = Has("isbspecialforces"),
            PackNStrap = Has("packnstrap", "wtt-packnstrapserver"),
            VariantMaps = variants.ToDictionary(kv => kv.Key, kv => kv.Value.Distinct().ToArray())
        };
    }

    /// <summary>알려진 변형 모드. 이 목록 밖의 변형은 MapCatalog의 SPT 원본 비교로 감지한다.</summary>
    /// <summary>
    /// 1.1.0 변형 판의 보스를 자기 설정으로 정하는 변형 모드: 라벨 → (맵, 역할). 그 판에서 Gravity는 이 역할 항목을 그대로 두고 새로 넣지도 않는다.
    /// 세관 클래식은 레쇼트·굿즈·컬티스트를 기본 0%로 빼고, 켜면 클래식에서 지점이 남은 구역에만 넣는다.
    /// </summary>
    internal static readonly (string Label, string Map, string[] Roles)[] VariantBossOwners =
    [
        ("CustomsClassic", "bigmap", ["bossBully", "followerBully", "bossKnight", "followerBigPipe", "followerBirdEye", "sectantPriest", "sectantWarrior"])
    ];

    internal static readonly (string Key, string Label, string[] Maps)[] VariantMods =
    [
        ("manimalinterchange", "Manimal", ["interchange"]),
        ("manimallighthouse", "Manimal", ["lighthouse"]),
        ("manimallabsboiler", "Manimal", ["laboratory"]),
        ("factoryclassic", "FactoryClassic", ["factory4_day", "factory4_night"]),
        // 1.1.0: 세관 클래식(LennoxP90-CustomsClassic, MapVariants 1.1.0+). 클래식 판의 보스는 그 모드 config/bosses.json이 맡는다(VariantBossOwners).
        ("customsclassic", "CustomsClassic", ["bigmap"]),
        ("interchangerework", "Interchange Rework", ["interchange"])
    ];
}
