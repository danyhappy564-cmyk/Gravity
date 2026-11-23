using Gravity.Core;

namespace Gravity.Gear;

/// <summary>
/// 1.1.0(#66): 웹 무기 화면의 탄약·부품 개수·밤 손전등·맵별 무기 성향 설정.
/// 1.0.9까지는 화면에만 있고 서버가 읽지 않았다. 저장된 설정에는 그때의 화면 기본값이 들어 있으므로
/// 봇을 '줄이는' 설정(대용량 탄창 제한·개수 제한·손전등)은 새 스위치나 설정 판(weapons.ver 2)으로 켰을 때만 쓴다.
/// </summary>
/// <summary>1.1.2 SPT 원본 탄약 한 칸 크기: id별, 구경별 최대값(원본에 없는 모드 탄용).</summary>
public sealed record VanillaAmmo(IReadOnlyDictionary<string, int> ById, IReadOnlyDictionary<string, int> ByCaliber)
{
    public static readonly VanillaAmmo Empty = new(new Dictionary<string, int>(), new Dictionary<string, int>());
    public (int? Stack, int? CaliberStack) Lookup(string id, string? caliber) =>
        (ById.TryGetValue(id.ToLowerInvariant(), out var a) ? a : null, caliber != null && ByCaliber.TryGetValue(caliber, out var c) ? c : null);
}

public sealed record WeaponExtras(
    bool LooseOn = true, double LooseChance = 40, int LooseLimit = 2,
    bool BigMagOn = false, int BigMagCount = 1,
    bool ModLimitOn = false, int ScopeLimit = 2, int TacLimit = 1,
    bool NightLightOn = false, double NightLight = 40,
    bool LooseEftStack = true, double LooseMinPct = 50, double LooseMaxPct = 100)
{
    /// <summary>맵별 무기 성향(0 근거리 ~ 100 원거리). 웹에 저장된 맵만 들어 있다.</summary>
    public IReadOnlyDictionary<string, double> Range { get; init; } = new Dictionary<string, double>();

    /// <summary>이 정도 이상 들어가는 탄창을 대용량으로 본다(드럼·4열·베타 C 등). 45발 RPK·50발 P90 표준 탄창은 제외.</summary>
    public const int BigMagRounds = 60;

    /// <summary>웹 기본 맵별 무기 성향과 같은 값: 탁 트인 맵 65, 실내 맵 15, 나머지 40.</summary>
    public static double DefaultRange(string map) =>
        map is "woods" or "lighthouse" or "shoreline" ? 65
        : map.StartsWith("factory", StringComparison.Ordinal) || map is "laboratory" or "labyrinth" ? 15 : 40;

    /// <summary>
    /// 맵 기본값 대비 원거리(지정사수·저격) / 근거리(기관단총·산탄총) 무게 배율. 기본값 그대로면 (1, 1).
    /// 원거리 = 값/기본, 근거리 = (100-값)/(100-기본).
    /// </summary>
    public (double Long, double Short) RangeFactors(string? map)
    {
        if (string.IsNullOrEmpty(map)) return (1, 1);
        map = map.ToLowerInvariant();
        if (!Range.TryGetValue(map, out var v)) return (1, 1);
        double d = DefaultRange(map);
        v = Math.Clamp(v, 0, 100);
        if (Math.Abs(v - d) < 0.001) return (1, 1);
        return (v / d, (100 - v) / (100 - d));
    }

    public static string? RangeKind(string? gunKind) => gunKind switch
    {
        "dmr" or "sniper" => "long",
        "smg" or "shotgun" => "short",
        _ => null
    };

    /// <summary>여분 탄창 수 무게에서 max개를 넘는 경우를 뺀다. 다 빠지면 max개 하나만 남긴다.</summary>
    public static Dictionary<double, double> ClampMagWeights(IReadOnlyDictionary<double, double> weights, int max)
    {
        var o = weights.Where(kv => kv.Key <= max && kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (o.Count == 0) o[max] = 1;
        return o;
    }

    /// <summary>
    /// 낱개 탄약 묶음: 확률에 걸리면 1~최대 묶음, 묶음마다 기준 한 칸 크기의 최소~최대 %(기본 50~100%).
    /// stackMax는 이미 고른 기준(EFT 기본 또는 게임 값)의 한 칸 크기다(<see cref="LooseStackBase"/>).
    /// </summary>
    public List<int> LooseStacks(Random rnd, int stackMax)
    {
        var o = new List<int>();
        if (!LooseOn || stackMax <= 0 || LooseLimit <= 0 || rnd.NextDouble() * 100 >= LooseChance) return o;
        int n = rnd.Next(1, LooseLimit + 1);
        double a = Math.Min(LooseMinPct, LooseMaxPct), b = Math.Max(LooseMinPct, LooseMaxPct);
        int lo = Math.Clamp((int)Math.Ceiling(stackMax * a / 100 - 1e-9), 1, stackMax);
        int hi = Math.Clamp((int)Math.Floor(stackMax * b / 100 + 1e-9), lo, stackMax);
        for (int i = 0; i < n; i++) o.Add(rnd.Next(lo, hi + 1));
        return o;
    }

    /// <summary>
    /// 1.1.2: 낱개 탄약 한 칸 크기 기준. EFT 기본(기본값)이면 SPT 원본 items.json의 StackMaxSize — SVM 등이 탄 묶음 크기를 키워도
    /// 따라가지 않는다(외부 제보: 주머니에 400발 넘게). 원본에 없는 모드 탄은 같은 구경 원본 탄의 가장 큰 값, 그것도 없으면 게임 값.
    /// 게임 값이면 지금 게임(다른 모드가 바꾼 값 포함)의 StackMaxSize 그대로.
    /// </summary>
    public int LooseStackBase(int gameStack, int? vanillaStack, int? vanillaCaliberStack)
    {
        if (!LooseEftStack || gameStack <= 0) return gameStack;
        return vanillaStack ?? (vanillaCaliberStack is { } c ? Math.Min(gameStack, c) : gameStack);
    }

    internal static WeaponExtras Read(ConfigModel.Reader r)
    {
        // 설정 판 2 전에 저장된 파일의 bigMag.on은 그때 화면 기본값(켜짐)일 뿐 사용자가 고른 값이 아니다.
        bool v2 = r.Num("weapons.ver", 1, 1, 99) >= 2;
        var range = new Dictionary<string, double>();
        if (r.Node("weapons.range") is System.Text.Json.Nodes.JsonObject ro)
            foreach (var (map, _) in ro)
                if (map.Length is > 0 and <= 40 && map.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                    range[map.ToLowerInvariant()] = r.Num("weapons.range." + map, DefaultRange(map.ToLowerInvariant()), 0, 100);
        return new WeaponExtras(
            r.Bool("weapons.looseAmmo.on", true), r.Num("weapons.looseAmmo.chance", 40, 0, 100), (int)r.Num("weapons.looseAmmo.limit", 2, 1, 5),
            v2 && r.Bool("weapons.bigMag.on", false), (int)r.Num("weapons.bigMag.count", 1, 0, 4),
            r.Bool("weapons.modLimit", false), (int)r.Num("weapons.scopeLimit", 2, 1, 3), (int)r.Num("weapons.tacLimit", 1, 0, 3),
            r.Bool("weapons.night.lightOn", false), r.Num("weapons.night.light", 40, 0, 100),
            r.Str("weapons.looseAmmo.stack", "eft", "eft", "game") != "game", r.Num("weapons.looseAmmo.min", 50, 1, 100), r.Num("weapons.looseAmmo.max", 100, 1, 100))
        { Range = range };
    }
}
