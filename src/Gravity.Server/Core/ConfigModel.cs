using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gravity.Core;

// 서버가 실제로 쓰는 설정(범위 A). 웹 state 중 이 칸들만 읽고, 나머지는 저장소가 원문 그대로 보존한다.
// 기본값은 웹 목업 makeDefaults()와 같아야 한다(미리보기와 실제가 같은 숫자를 내도록).

public sealed record HardStop(string Mode, double PmcPct, double ScavPct, double BeforeEndMin);
public sealed record Intensity(double Early, double Mid, double Late);
public sealed record GroupCapRule(int Size, string Cmp, int Count);
public sealed record GroupCap(bool On, string Scope, string Over, IReadOnlyList<GroupCapRule> Rules);
public sealed record ScavSettings(bool SeparateStart, double DelayMin, double ActiveMin, double ActiveMax, double QuietMin, double QuietMax);

public sealed record SpawnSettings(
    double StartPmcPct, double FirstDelaySec, double IntervalMin, double RandomPct, double MinGapSec,
    double DistWave, double DistBots, bool Smooth, int WaveMin, int WaveMax, HardStop HardStop,
    bool IgnoreCapStart, bool IgnoreCapWaves, Intensity Intensity, double UsecPct,
    double[] Squad, GroupCap GroupCap, double[] Diff, ScavSettings Scav);

public sealed record InfSettings(double DelaySec, int Batch, double PmcShare, double Jitter);

public sealed record MapSettings(
    bool Override, string Style, InfSettings Inf, double StartPmcPct, double IntervalMin, int WaveMin, int WaveMax,
    Intensity Intensity, bool Original, int Cap, IReadOnlyDictionary<string, bool> Zones);

public sealed record BossSettings(bool On, double Chance, IReadOnlyDictionary<string, bool> Maps);

public sealed record GravityConfig(
    bool SpawnModule, SpawnSettings Spawn, IReadOnlyDictionary<string, MapSettings> Maps,
    IReadOnlyDictionary<string, BossSettings> Bosses, bool BossCommonOn, double BossCommonChance)
{
    public MapSettings MapFor(string id) => Maps.TryGetValue(id, out var m) ? m : ConfigModel.DefaultMap(id);
}

public static class ConfigModel
{
    public static readonly string[] KnownMaps =
    [
        "bigmap", "woods", "factory4_day", "factory4_night", "rezervbase", "shoreline", "interchange",
        "laboratory", "lighthouse", "tarkovstreets", "sandbox", "sandbox_high", "labyrinth"
    ];

    // 웹 보스 id → 기본 확률(목업 BOSSES와 같은 값)
    public static readonly IReadOnlyDictionary<string, double> BossDefaultChance = new Dictionary<string, double>
    {
        ["reshala"] = 38, ["shturman"] = 35, ["tagilla"] = 35, ["glukhar"] = 35, ["sanitar"] = 35, ["killa"] = 38,
        ["kaban"] = 35, ["kollontay"] = 30, ["zryachiy"] = 100, ["goons"] = 30, ["partisan"] = 25
    };

    public static int DefaultCap(string map) =>
        map == "labyrinth" ? 10 : map.StartsWith("factory") ? 14 : map == "tarkovstreets" ? 30 : 24;

    public static MapSettings DefaultMap(string id) => new(
        false, id == "labyrinth" ? "infinite" : "waves", new InfSettings(45, 2, 50, 30), 35, 6, 2, 4,
        new Intensity(1.2, 1, 0.6), true, DefaultCap(id), new Dictionary<string, bool>());

    public static GravityConfig Default() => FromState(null, []);

    public static GravityConfig FromState(JsonNode? state, List<string> clamped)
    {
        var r = new Reader(state, clamped);
        var sp = "spawn.";
        var squad = new[]
        {
            r.Num(sp + "squad.s1", 40, 0, 100), r.Num(sp + "squad.s2", 30, 0, 100), r.Num(sp + "squad.s3", 20, 0, 100),
            r.Num(sp + "squad.s4", 10, 0, 100), r.Num(sp + "squad.s5", 0, 0, 100)
        };
        if (squad.Sum() <= 0) squad = [100, 0, 0, 0, 0];
        var diff = new[]
        {
            r.Num(sp + "diff.easy", 10, 0, 100), r.Num(sp + "diff.normal", 50, 0, 100),
            r.Num(sp + "diff.hard", 30, 0, 100), r.Num(sp + "diff.imp", 10, 0, 100)
        };
        if (diff.Sum() <= 0) diff = [0, 100, 0, 0];

        var rules = new List<GroupCapRule>();
        if (r.Node(sp + "groupCap.rules") is JsonArray arr)
            for (var i = 0; i < arr.Count && i < 20; i++)
                rules.Add(new GroupCapRule(
                    (int)r.Num($"{sp}groupCap.rules.{i}.size", 4, 1, 5),
                    r.Str($"{sp}groupCap.rules.{i}.cmp", "eq", "eq", "ge"),
                    (int)r.Num($"{sp}groupCap.rules.{i}.count", 1, 0, 20)));
        else
            rules.AddRange([new GroupCapRule(5, "eq", 1), new GroupCapRule(3, "eq", 1)]);

        var waveMin = (int)r.Num(sp + "waveMin", 2, 0, 20);
        var spawn = new SpawnSettings(
            r.Num(sp + "startPmcPct", 35, 0, 100), r.Num(sp + "firstDelay", 120, 0, 1800),
            r.Num(sp + "interval", 6, 0.5, 60), r.Num(sp + "randomPct", 30, 0, 100), r.Num(sp + "minGap", 60, 0, 1800),
            r.Num(sp + "dist.wave", 0, -100, 100), r.Num(sp + "dist.bots", 0, -100, 100), r.Bool(sp + "dist.smooth", true),
            waveMin, (int)Math.Max(waveMin, r.Num(sp + "waveMax", 4, 0, 20)),
            new HardStop(r.Str(sp + "hardStop.mode", "percent", "percent", "time"), r.Num(sp + "hardStop.pmcPct", 15, 0, 90),
                r.Num(sp + "hardStop.scavPct", 5, 0, 90), r.Num(sp + "stopBeforeEnd", 10, 0, 120)),
            r.Bool(sp + "ignoreCap.start", false), r.Bool(sp + "ignoreCap.waves", false),
            new Intensity(r.Num(sp + "intensity.early", 1.2, 0, 3), r.Num(sp + "intensity.mid", 1, 0, 3), r.Num(sp + "intensity.late", 0.6, 0, 3)),
            r.Num(sp + "usecPct", 50, 0, 100), squad,
            new GroupCap(r.Bool(sp + "groupCap.on", false), r.Str(sp + "groupCap.scope", "raid", "raid", "alive"),
                r.Str(sp + "groupCap.over", "split", "split", "drop"), rules),
            diff,
            new ScavSettings(r.Bool(sp + "scav.separateStart", true), r.Num(sp + "scav.delay", 4, 0, 60),
                r.Num(sp + "scav.activeMin", 3, 1, 60), r.Num(sp + "scav.activeMax", 7, 1, 60),
                r.Num(sp + "scav.quietMin", 1, 0, 60), r.Num(sp + "scav.quietMax", 3, 0, 60)));

        var maps = new Dictionary<string, MapSettings>();
        foreach (var id in KnownMaps)
        {
            var d = DefaultMap(id);
            var p = $"maps.{id}.";
            var zones = new Dictionary<string, bool>();
            if (r.Node(p + "zones") is JsonObject zo)
                foreach (var (k, v) in zo)
                    zones[k] = v is JsonObject o && o["on"] is JsonValue jv && jv.TryGetValue<bool>(out var on) ? on : true;
            var wMin = (int)r.Num(p + "waveMin", d.WaveMin, 0, 20);
            maps[id] = new MapSettings(
                r.Bool(p + "override", d.Override), r.Str(p + "style", d.Style, "waves", "infinite", "start"),
                new InfSettings(r.Num(p + "inf.delay", 45, 10, 600), (int)r.Num(p + "inf.batch", 2, 1, 10),
                    r.Num(p + "inf.pmcShare", 50, 0, 100), r.Num(p + "inf.jitter", 30, 0, 100)),
                r.Num(p + "startPmcPct", d.StartPmcPct, 0, 100), r.Num(p + "interval", d.IntervalMin, 0.5, 60),
                wMin, (int)Math.Max(wMin, r.Num(p + "waveMax", d.WaveMax, 0, 20)),
                new Intensity(r.Num(p + "early", 1.2, 0, 3), r.Num(p + "mid", 1, 0, 3), r.Num(p + "late", 0.6, 0, 3)),
                r.Bool(p + "original", true), (int)r.Num(p + "cap", d.Cap, 1, 60), zones);
        }

        var bosses = new Dictionary<string, BossSettings>();
        foreach (var (id, ch) in BossDefaultChance)
        {
            var p = $"bosses.{id}.";
            var bm = new Dictionary<string, bool>();
            if (r.Node(p + "maps") is JsonObject mo)
                foreach (var (k, v) in mo)
                    bm[k] = v is JsonValue jv && jv.TryGetValue<bool>(out var b) && b;
            bosses[id] = new BossSettings(r.Bool(p + "on", true), r.Num(p + "chance", ch, 0, 100), bm);
        }

        return new GravityConfig(r.Bool("modules.spawn", true), spawn, maps, bosses,
            r.Bool("bossCommon.on", false), r.Num("bossCommon.chance", 35, 0, 100));
    }

    /// <summary>JSON 경로를 읽는다. 형식이 틀리면 기본값, 범위를 넘으면 고정하고 경로를 기록.</summary>
    private sealed class Reader(JsonNode? root, List<string> clamped)
    {
        public JsonNode? Node(string path)
        {
            var n = root;
            foreach (var part in path.Split('.'))
            {
                if (n is null) return null;
                n = n switch
                {
                    JsonObject o => o.TryGetPropertyValue(part, out var c) ? c : null,
                    JsonArray a when int.TryParse(part, out var i) && i >= 0 && i < a.Count => a[i],
                    _ => null
                };
            }
            return n;
        }

        public double Num(string path, double def, double min, double max)
        {
            var v = def;
            if (Node(path) is JsonValue jv && jv.GetValueKind() == JsonValueKind.Number) v = jv.GetValue<double>();
            if (double.IsNaN(v) || double.IsInfinity(v)) v = def;
            if (v < min || v > max) { clamped.Add(path); v = Math.Clamp(v, min, max); }
            return v;
        }

        public bool Bool(string path, bool def) =>
            Node(path) is JsonValue jv && jv.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? jv.GetValue<bool>() : def;

        public string Str(string path, string def, params string[] allowed) =>
            Node(path) is JsonValue jv && jv.GetValueKind() == JsonValueKind.String && allowed.Contains(jv.GetValue<string>())
                ? jv.GetValue<string>() : def;
    }
}
