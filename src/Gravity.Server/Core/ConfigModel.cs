using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gravity.Core;

// 서버가 실제로 쓰는 설정(범위 A). 웹 state 중 이 칸들만 읽고, 나머지는 저장소가 원문 그대로 보존한다.
// 기본값은 웹 목업 makeDefaults()와 같아야 한다(미리보기와 실제가 같은 숫자를 내도록).

public sealed record HardStop(string Mode, double PmcPct, double ScavPct, double BeforeEndMin);
public sealed record Intensity(double Early, double Mid, double Late);
public sealed record GroupCapRule(int Size, string Cmp, int Count);
public sealed record GroupCap(bool On, string Scope, string Over, IReadOnlyList<GroupCapRule> Rules);
public sealed record ScavSettings(
    bool SeparateStart, double DelayMin, double ActiveMin, double ActiveMax, double QuietMin, double QuietMax,
    double Refill, int Pending, int SoftCap, int PerPlayer, double PscavChance, bool StartMarksman, double PlayerScavRemain = 60);

public sealed record ProtectSettings(bool On, double Sec, double Radius);
public sealed record SpacingSettings(double Player, double Bots);

public sealed record SpawnSettings(
    double StartPmcPct, double FirstDelaySec, double IntervalMin, double RandomPct, double MinGapSec,
    double DistWave, double DistBots, bool Smooth, int WaveMin, int WaveMax, HardStop HardStop,
    bool IgnoreCapStart, bool IgnoreCapWaves, Intensity Intensity, double UsecPct,
    double[] Squad, GroupCap GroupCap, double[] Diff, ScavSettings Scav,
    ProtectSettings Protect, SpacingSettings Spacing, string Placement, double HotspotShare, bool HotspotBoost, bool AllowPmcLabyrinth, bool AllowScavLab);

/// <summary>근처 보충(0.2 클라). 값은 웹 단위 그대로(초·m·%).</summary>
public sealed record NearSettings(
    bool On, double FirstDelay, double Interval, double MinDist, double MaxDist, double Spacing,
    int WaveMin, int WaveMax, double SlotChance, double PmcChance, int Attempts, bool Sight, bool StopAtCap,
    bool GroupScavOff, double GroupChance, int GroupMin, int GroupMax,
    bool CustomOn, string CustomT1, int CustomCap1, string CustomT2, int CustomCap2, double CustomRatio2, bool UseDefault = true);

/// <summary>시즌 이벤트(0.3). Mode: auto(날짜)·on(항상)·off. 확률은 %.</summary>
public sealed record EventSettings(
    string Owner, bool Detect,
    string Halloween, double Zombies, double NightCult, bool Summon, bool HalloweenGear,
    string Christmas, double Santa, bool Gifts, bool Snow, bool ChristmasGear,
    string NewYears, string AprilFools);

public sealed record InfSettings(double DelaySec, int Batch, double PmcShare, double Jitter);

public sealed record MapSettings(
    bool Override, string Style, InfSettings Inf, double StartPmcPct, double IntervalMin, int WaveMin, int WaveMax,
    Intensity Intensity, bool Original, int Cap, IReadOnlyDictionary<string, bool> Zones,
    double PmcDist, double ScavDist, bool StartScav, IReadOnlyDictionary<string, double> ZoneWeights);

/// <summary>보스 하나. EscMin·EscMax 둘 다 0, TFrom·TTo 둘 다 0, Diff 빈 값, 구역 없음 = 바닐라 유지(0.3).</summary>
public sealed record BossSettings(bool On, double Chance, IReadOnlyDictionary<string, bool> Maps,
    bool Followers = true, int EscMin = 0, int EscMax = 0, double TFrom = 0, double TTo = 0, string Diff = "",
    bool Extra = false, IReadOnlyDictionary<string, string[]>? Zones = null);

/// <summary>특수 봇 무리(레이더·로그·컬티스트·블러드하운드). Chance는 맵별(없는 맵은 바닐라 확률 유지), Min·Max는 무리 전체 인원.</summary>
public sealed record SpecialSettings(bool On, IReadOnlyDictionary<string, double> Chance, int Min, int Max);

/// <summary>팩션 무리 종류 하나(순찰·검문소·사냥조, 0.4). TMin·TMax는 분(검문소·사냥조는 안 씀).</summary>
public sealed record FactionKind(bool On, double Chance, int Count, int Min, int Max, double TMin, double TMax);

/// <summary>RUAF·UNTAR 같은 순찰형 팩션(0.4). Maps에는 웹에서 정한 맵만 있다(없는 맵 = 모드 설정 기본).</summary>
public sealed record FactionSettings(bool Managed, bool ShareCap, IReadOnlyDictionary<string, bool> Maps,
    FactionKind Patrol, FactionKind Checkpoint, FactionKind Hunt);

/// <summary>블랙 디비전(0.4). TMin·TMax는 레이드 %, 연구소 웨지·게이트, 블랙아웃 연동.</summary>
public sealed record BlackDivSettings(bool Managed, bool ShareCap, IReadOnlyDictionary<string, bool> Maps,
    double Chance, double TMin, double TMax, int HuntMin, int HuntMax, double LabsGate, bool Wedge, double WedgeChance,
    string BlackoutLink, double BlackoutMult);

public sealed record GravityConfig(
    bool SpawnModule, SpawnSettings Spawn, IReadOnlyDictionary<string, MapSettings> Maps,
    IReadOnlyDictionary<string, BossSettings> Bosses, bool BossCommonOn, double BossCommonChance, NearSettings Near,
    IReadOnlyDictionary<string, SpecialSettings>? Specials = null, EventSettings? Events = null,
    FactionSettings? Ruaf = null, BlackDivSettings? BlackDiv = null, string OtherPmc = "keep")
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
    /// <summary>특수 봇 무리 id → 우두머리 역할(BossLocationSpawn.BossName). 저격 스캐브·감염자는 0.4.</summary>
    public static readonly IReadOnlyDictionary<string, string> SpecialRoles = new Dictionary<string, string>
    {
        ["raiders"] = "pmcBot", ["rogues"] = "exUsec", ["cultists"] = "sectantPriest", ["bloodhounds"] = "arenaFighterEvent"
    };

    public static readonly IReadOnlyDictionary<string, double> BossDefaultChance = new Dictionary<string, double>
    {
        ["reshala"] = 38, ["shturman"] = 35, ["tagilla"] = 35, ["glukhar"] = 35, ["sanitar"] = 35, ["killa"] = 38,
        ["kaban"] = 35, ["kollontay"] = 30, ["zryachiy"] = 100, ["goons"] = 30, ["partisan"] = 25
    };

    public static int DefaultCap(string map) =>
        map == "labyrinth" ? 10 : map.StartsWith("factory") ? 14 : map == "tarkovstreets" ? 30 : 24;

    // 공장·미궁은 좁아서 큰 맵 거리(120·80m)로는 지점이 남지 않는다(11-09 미궁 판: 스캐브 지점 9개 전부 80m 안).
    private static bool Small(string map) => map.StartsWith("factory") || map == "labyrinth";
    // 연구소는 중간 크기라 120m면 시작 분대가 지점을 못 찾아 만료된다(11-09 0.3 판: 7분에 만료 2) → 80m.
    public static double DefaultPmcDist(string map) => Small(map) ? 40 : map == "laboratory" ? 80 : 120;
    public static double DefaultScavDist(string map) => Small(map) ? 30 : 80;

    public static MapSettings DefaultMap(string id) => new(
        false, id == "labyrinth" ? "infinite" : "waves", new InfSettings(45, 2, 50, 30), 35, 6, 2, 4,
        new Intensity(1.2, 1, 0.6), true, DefaultCap(id), new Dictionary<string, bool>(),
        DefaultPmcDist(id), DefaultScavDist(id), true, new Dictionary<string, double>());

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
                r.Num(sp + "scav.quietMin", 1, 0, 60), r.Num(sp + "scav.quietMax", 3, 0, 60),
                r.Num(sp + "scav.refill", 45, 0, 3600), (int)r.Num(sp + "scav.pending", 3, 0, 20),
                (int)r.Num(sp + "scav.softCap", 18, 0, 60), (int)r.Num(sp + "scav.perPlayer", 20, 0, 100),
                r.Num(sp + "scav.pscavChance", 10, 0, 100), r.Bool(sp + "scav.startMarksman", true),
                r.Num(sp + "scav.playerScavRemain", 60, 0, 100)),
            new ProtectSettings(r.Bool(sp + "protect.on", true), r.Num(sp + "protect.sec", 90, 0, 3600), r.Num(sp + "protect.radius", 120, 5, 1000)),
            new SpacingSettings(r.Num(sp + "spacing.player", 80, 5, 1000), r.Num(sp + "spacing.bots", 25, 5, 1000)),
            r.Str(sp + "placement", "mixed", "mixed", "hotspot", "vanilla"), r.Num(sp + "hotspotShare", 60, 0, 100),
            r.Bool(sp + "hotspotBoost", true), r.Bool(sp + "allowPmcLabyrinth", false),
            r.Bool(sp + "allowScavLab", false));

        var maps = new Dictionary<string, MapSettings>();
        foreach (var id in KnownMaps)
        {
            var d = DefaultMap(id);
            var p = $"maps.{id}.";
            var zones = new Dictionary<string, bool>();
            var zoneW = new Dictionary<string, double>();
            if (r.Node(p + "zones") is JsonObject zo)
                foreach (var (k, v) in zo)
                {
                    zones[k] = v is JsonObject o && o["on"] is JsonValue jv && jv.TryGetValue<bool>(out var on) ? on : true;
                    zoneW[k] = r.Num($"{p}zones.{k}.w", 100, 0, 1000);
                }
            var wMin = (int)r.Num(p + "waveMin", d.WaveMin, 0, 20);
            maps[id] = new MapSettings(
                r.Bool(p + "override", d.Override), r.Str(p + "style", d.Style, "waves", "infinite", "start"),
                new InfSettings(r.Num(p + "inf.delay", 45, 10, 600), (int)r.Num(p + "inf.batch", 2, 1, 10),
                    r.Num(p + "inf.pmcShare", 50, 0, 100), r.Num(p + "inf.jitter", 30, 0, 100)),
                r.Num(p + "startPmcPct", d.StartPmcPct, 0, 100), r.Num(p + "interval", d.IntervalMin, 0.5, 60),
                wMin, (int)Math.Max(wMin, r.Num(p + "waveMax", d.WaveMax, 0, 20)),
                new Intensity(r.Num(p + "early", 1.2, 0, 3), r.Num(p + "mid", 1, 0, 3), r.Num(p + "late", 0.6, 0, 3)),
                r.Bool(p + "original", true), (int)r.Num(p + "cap", d.Cap, 1, 60), zones,
                r.Num(p + "pmcDist", d.PmcDist, 5, 1000), r.Num(p + "scavDist", d.ScavDist, 5, 1000),
                r.Bool(p + "start.scav", true), zoneW);
        }

        var bosses = new Dictionary<string, BossSettings>();
        foreach (var (id, ch) in BossDefaultChance)
        {
            var p = $"bosses.{id}.";
            var bm = new Dictionary<string, bool>();
            if (r.Node(p + "maps") is JsonObject mo)
                foreach (var (k, v) in mo)
                    bm[k] = v is JsonValue jv && jv.TryGetValue<bool>(out var b) && b;
            var zones = new Dictionary<string, string[]>();
            if (r.Node(p + "zones") is JsonObject zo)
                foreach (var (k, v) in zo)
                    if (v is JsonArray za)
                        zones[k] = za.OfType<JsonValue>().Select(x => x.TryGetValue<string>(out var sv) ? sv : null)
                            .Where(sv => sv is { Length: > 0 and <= 60 }).Select(sv => sv!).ToArray();
            var eMin = (int)r.Num(p + "escMin", 0, 0, 10);
            var tFrom = r.Num(p + "tFrom", 0, 0, 600);
            bosses[id] = new BossSettings(r.Bool(p + "on", true), r.Num(p + "chance", ch, 0, 100), bm,
                r.Bool(p + "followers", true), eMin, (int)Math.Max(eMin, r.Num(p + "escMax", 0, 0, 10)),
                tFrom, Math.Max(tFrom, r.Num(p + "tTo", 0, 0, 600)),
                r.Str(p + "diff", "", "easy", "normal", "hard", "impossible", "random"), r.Bool(p + "extra", false), zones);
        }

        var specials = new Dictionary<string, SpecialSettings>();
        foreach (var id in SpecialRoles.Keys.Append("snipers").Append("infected"))
        {
            var p = $"specials.{id}.";
            var chance = new Dictionary<string, double>();
            if (r.Node(p + "chance") is JsonObject co)
                foreach (var (k, _) in co)
                    chance[k] = r.Num(p + "chance." + k, 0, 0, 100);
            var sMin = (int)r.Num(p + "min", 0, 0, 10);
            specials[id] = new SpecialSettings(r.Bool(p + "on", id != "infected"), chance, sMin, (int)Math.Max(sMin, r.Num(p + "max", 0, 0, 10)));
        }

        var nMin = r.Num("near.minDist", 200, 5, 1000);
        var nWaveMin = (int)r.Num("near.waveMin", 3, 0, 20);
        var gMin = (int)r.Num("near.group.min", 2, 1, 10);
        var near = new NearSettings(
            r.Bool("near.on", false), r.Num("near.firstDelay", 15, 0, 3600), r.Num("near.interval", 180, 5, 3600),
            nMin, Math.Max(nMin, r.Num("near.maxDist", 300, 5, 1000)), r.Num("near.spacing", 15, 5, 1000),
            nWaveMin, (int)Math.Max(nWaveMin, r.Num("near.waveMax", 8, 0, 20)),
            r.Num("near.slotChance", 50, 0, 100), r.Num("near.pmcChance", 30, 0, 100), (int)r.Num("near.attempts", 3, 1, 20),
            r.Bool("near.sight", false), r.Bool("near.stopAtCap", true),
            r.Bool("near.group.scavOff", false), r.Num("near.group.chance", 25, 0, 100), gMin, (int)Math.Max(gMin, r.Num("near.group.max", 5, 1, 10)),
            r.Bool("near.custom.on", false), r.Role("near.custom.t1", "raider"), (int)r.Num("near.custom.cap1", 5, 0, 60),
            r.Role("near.custom.t2", "rogue"), (int)r.Num("near.custom.cap2", 5, 0, 60), r.Num("near.custom.ratio2", 50, 0, 100),
            r.Bool("near.useDefault", true));

        string[] modes = ["auto", "on", "off"];
        var events = new EventSettings(
            r.Str("eventsOwner", "auto", "auto", "ppes", "svm", "gravity", "spt"), r.Bool("events.detect", true),
            r.Str("events.halloween.mode", "auto", modes), r.Num("events.halloween.zombies", 30, 0, 100),
            r.Num("events.halloween.nightCult", 40, 0, 100), r.Bool("events.halloween.summon", true), r.Bool("events.halloween.gear", true),
            r.Str("events.christmas.mode", "auto", modes), r.Num("events.christmas.santa", 25, 0, 100),
            r.Bool("events.christmas.gifts", true), r.Bool("events.christmas.snow", true), r.Bool("events.christmas.gear", true),
            r.Str("events.newyears.mode", "auto", modes), r.Str("events.aprilfools.mode", "off", modes));

        FactionKind Kind(string p, double chance, int count, int min, int max, double tMin, double tMax)
        {
            var kMin = (int)r.Num(p + "min", min, 1, 10);
            var t0 = r.Num(p + "tMin", tMin, 0, 180);
            return new FactionKind(r.Bool(p + "on", true), r.Num(p + "chance", chance, 0, 100), (int)r.Num(p + "count", count, 0, 10),
                kMin, (int)Math.Max(kMin, r.Num(p + "max", max, 1, 10)), t0, Math.Max(t0, r.Num(p + "tMax", tMax, 0, 180)));
        }
        Dictionary<string, bool> FacMaps(string p)
        {
            var d = new Dictionary<string, bool>();
            if (r.Node(p) is JsonObject mo)
                foreach (var (k, _) in mo)
                    if (r.Node(p + "." + k) is JsonValue jv && jv.GetValueKind() is JsonValueKind.True or JsonValueKind.False) d[k] = jv.GetValue<bool>();
            return d;
        }
        const string fr = "factions.ruaf.";
        var ruaf = new FactionSettings(r.Bool(fr + "managed", false), r.Bool(fr + "shareCap", false), FacMaps(fr + "maps"),
            Kind(fr + "patrol.", 45, 2, 3, 6, 5, 28), Kind(fr + "checkpoint.", 45, 2, 3, 4, 0, 0), Kind(fr + "hunt.", 20, 1, 3, 4, 0, 0));
        const string fb = "factions.blackdiv.";
        var bdT0 = r.Num(fb + "bd.tMin", 10, 0, 100);
        var bdMin = (int)r.Num(fb + "bd.huntMin", 3, 1, 10);
        var blackDiv = new BlackDivSettings(r.Bool(fb + "managed", false), r.Bool(fb + "shareCap", false), FacMaps(fb + "maps"),
            r.Num(fb + "bd.chance", 25, 0, 100), bdT0, Math.Max(bdT0, r.Num(fb + "bd.tMax", 80, 0, 100)),
            bdMin, (int)Math.Max(bdMin, r.Num(fb + "bd.huntMax", 5, 1, 10)), r.Num(fb + "bd.labsGate", 20, 0, 100),
            r.Bool(fb + "bd.wedge", true), r.Num(fb + "bd.wedgeChance", 15, 0, 100),
            r.Str("lab.blackoutLink", "boost", "always", "only", "boost"), r.Num("lab.blackoutMult", 3, 1, 5));

        return new GravityConfig(r.Bool("modules.spawn", true), spawn, maps, bosses,
            r.Bool("bossCommon.on", false), r.Num("bossCommon.chance", 35, 0, 100), near, specials, events,
            ruaf, blackDiv, r.Str("compat.otherPmc", "keep", "keep", "remove"));
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

        /// <summary>역할 이름(영문·숫자·밑줄 40자까지). 그 밖은 기본값.</summary>
        public string Role(string path, string def) =>
            Node(path) is JsonValue jv && jv.GetValueKind() == JsonValueKind.String
            && jv.GetValue<string>() is { Length: > 0 and <= 40 } v && v.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? v : def;

        public string Str(string path, string def, params string[] allowed) =>
            Node(path) is JsonValue jv && jv.GetValueKind() == JsonValueKind.String && allowed.Contains(jv.GetValue<string>())
                ? jv.GetValue<string>() : def;
    }
}
