using System.Text.Json.Nodes;

namespace Gravity.Core;

/// <summary>
/// ORBIT 서버 설정(user/mods/ORBIT/config.json)에서 Gravity가 맞춰야 할 값만 읽는다(0.6). 파일은 고치지 않는다.
/// 값이 없거나 형식이 다르면 ORBIT 기본값(3.1 기준)을 쓴다.
/// </summary>
public sealed record OrbitInfo(
    bool Installed, string? Version, bool ConfigRead, bool Limiter, double SleepDistance, double WakeDistance, int MinAwake,
    bool ExtractPmc, bool PlayerScav, string[] FactionTakeover, string[] VanillaTakeover, bool FullSleep = true)
{
    public static readonly OrbitInfo None = new(false, null, false, false, 0, 0, 0, false, false, [], []);

    /// <summary>실제로 늘 깨어 있는 봇 수: full_sleep이면 0(ORBIT DormancySystem과 같음).</summary>
    public int EffectiveMinAwake => FullSleep ? 0 : MinAwake;

    /// <summary>ORBIT이 장악한 팩션인지(웹 팩션 id: ruaf·untar·blackdiv).</summary>
    public bool TakesOver(string faction) => FactionTakeover.Contains(faction, StringComparer.OrdinalIgnoreCase);

    // ORBIT config.json factions.take_over_* 키 → 웹 팩션 id(Gravity가 관리하는 팩션만 이름을 맞춘다).
    private static readonly (string Key, string Id)[] FactionKeys =
    [
        ("take_over_ruaf", "ruaf"), ("take_over_untar", "untar"), ("take_over_black_division", "blackdiv"),
        ("take_over_isb", "isb"), ("take_over_combine_soldiers", "combine")
    ];

    // vanilla_* = true면 그 종류는 바닐라 행동을 유지(ORBIT이 안 맡음). 기본값은 ORBIT 3.1 OrbitServerConfig와 같다.
    private static readonly (string Key, string Id, bool Def)[] VanillaKeys =
    [
        ("vanilla_scavs", "scavs", false), ("vanilla_goons", "goons", false), ("vanilla_cultists", "cultists", false),
        ("vanilla_raiders", "raiders", true), ("vanilla_rogues", "rogues", true), ("vanilla_bloodhounds", "bloodhounds", false)
    ];

    /// <summary>config.json 내용(없으면 null)으로 만든다. installed는 폴더 감지 결과.</summary>
    public static OrbitInfo Parse(bool installed, string? version, string? json)
    {
        if (!installed) return None;
        JsonNode? root = null;
        try { if (json != null) root = JsonNode.Parse(json); } catch { root = null; }
        bool B(string path, bool def) => Get(root, path) is JsonValue v && v.TryGetValue<bool>(out var b) ? b : def;
        double N(string path, double def) => Get(root, path) is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : def;
        return new OrbitInfo(true, version, root is JsonObject,
            B("ai_limiter.enabled", true), N("ai_limiter.sleep_distance", 250), N("ai_limiter.wake_distance", 200),
            (int)N("ai_limiter.min_awake_bots", 6), B("loot.extract_pmc", true), B("player_scav.enabled", true),
            FactionKeys.Where(k => B("factions." + k.Key, k.Id == "isb")).Select(k => k.Id).ToArray(),
            VanillaKeys.Where(k => !B("factions." + k.Key, k.Def)).Select(k => k.Id).ToArray(),
            B("ai_limiter.full_sleep", true));
    }

    /// <summary>
    /// 근처 보충 거리(orbit.nearAsGhost): ORBIT 봇 재우기가 켜져 있으면 잠드는 거리(깨우는 거리보다 작으면 깨우는 거리) + 10m 밖에서 시작.
    /// ORBIT은 PMC를 max(잠드는, 깨우는 거리) 밖에서 재우고, 스캐브는 그보다 안쪽에서도 재운다(Codex 0.6 지적 6).
    /// </summary>
    public double GhostDistance => Math.Max(SleepDistance, WakeDistance) + 10;

    public (double Min, double Max) NearRange(double min, double max, bool nearAsGhost)
    {
        if (!Installed || !nearAsGhost || !Limiter || WakeDistance <= 0) return (min, max);
        var m = Math.Max(min, GhostDistance);
        return (m, Math.Max(max, m + 40));
    }

    private static JsonNode? Get(JsonNode? root, string path)
    {
        var n = root;
        foreach (var part in path.Split('.'))
            n = n is JsonObject o && o.TryGetPropertyValue(part, out var c) ? c : null;
        return n;
    }
}
