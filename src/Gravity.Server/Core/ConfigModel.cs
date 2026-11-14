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
    double Refill, int Pending, int SoftCap, int PerPlayer, double PscavChance, bool StartMarksman, double PlayerScavRemain = 60,
    bool StartOn = true, double StartPct = 20,
    string StartMode = "pct", int StartMin = 3, int StartMax = 6,
    string RemainMode = "pct", int RemainMin = 2, int RemainMax = 4);
// 1.0.2: StartMode "count"면 시작 스캐브를 StartMin~Max 사이에서 판마다 굴린다("pct"는 바닐라 스캐브 수 × StartPct).
// RemainMode "count"면 스캐브 레이드 때 PMC 무리를 RemainMin~Max개 남긴다("pct"는 PlayerScavRemain %).

public sealed record ProtectSettings(bool On, double Sec, double Radius);
public sealed record SpacingSettings(double Player, double Bots);

public sealed record SpawnSettings(
    double StartPmcPct, double FirstDelaySec, double IntervalMin, double RandomPct, double MinGapSec,
    double DistWave, double DistBots, bool Smooth, int WaveMin, int WaveMax, HardStop HardStop,
    bool IgnoreCapStart, bool IgnoreCapWaves, Intensity Intensity, double UsecPct,
    double[] Squad, GroupCap GroupCap, double[] Diff, ScavSettings Scav,
    ProtectSettings Protect, SpacingSettings Spacing, string Placement, double HotspotShare, bool HotspotBoost, bool AllowPmcLabyrinth, bool AllowScavLab,
    CapTable? Caps = null, string StartMode = "pct", int StartPmcMin = 8, int StartPmcMax = 12, bool StartPlayerOnly = false)
{
    // 1.0.1: StartMode "count"면 시작 PMC를 StartPmcMin~Max 사이에서 판마다 굴린다(맵별 덮어쓰기 가능, "pct"는 예전처럼 바닐라 PMC 수 × 비율).
    // StartPlayerOnly: 시작 분대는 플레이어 시작 지점에서만(봇 PMC 지점으로 안 넘어감, 공장 같은 작은 맵은 그중 가장 먼 지점).
    public CapTable CapsOrDefault => Caps ?? CapTable.Default;
}

/// <summary>상한 표의 한 줄(보스·특수·팩션·이벤트). Mode: separate·vanilla·respect(Gravity.Shared.CapKinds), Max 0 = 제한 없음.</summary>
public sealed record CapKindSettings(string Mode, int Max);

/// <summary>종류별 상한 표(1.0, 사용자 11-10). "limits" 카드라 맵별로도 정할 수 있다.</summary>
public sealed record CapTable(int PmcReserve, int PmcGroups, IReadOnlyDictionary<string, CapKindSettings> Kinds)
{
    public static readonly CapTable Default = new(0, 0, Gravity.Shared.CapKinds.Table.ToDictionary(k => k, _ => new CapKindSettings(Gravity.Shared.CapKinds.Separate, 0)));

    public CapKindSettings For(string kind) => Kinds.TryGetValue(kind, out var k) ? k : new CapKindSettings(Gravity.Shared.CapKinds.Separate, 0);
}

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
public sealed record ZoneLayout(IReadOnlyDictionary<string, bool> On,
    IReadOnlyDictionary<string, double> Pmc, IReadOnlyDictionary<string, double> Scav);
public sealed record CardOverrides(bool Scav, bool Near, bool Dist, bool Hotspot, bool Limits);

public sealed record MapSettings(
    bool Override, string Style, InfSettings Inf, double StartPmcPct, double IntervalMin, int WaveMin, int WaveMax,
    Intensity Intensity, bool Original, int Cap, IReadOnlyDictionary<string, bool> Zones,
    double PmcDist, double ScavDist, bool StartScav, IReadOnlyDictionary<string, double> ZoneWeights, bool Variant = true,
    string ScavStyle = "waves", IReadOnlyDictionary<string, double>? ScavZoneWeights = null, ZoneLayout? ZonesAlt = null,
    CardOverrides? Cards = null, SpawnSettings? CardSpawn = null, NearSettings? CardNear = null);

/// <summary>ORBIT 연동(0.6). ORBIT 설정은 읽기만 한다.</summary>
public sealed record OrbitSettings(bool RefillAfterExtract, bool NearAsGhost, bool RespectTakeover,
    string? ExtractRefillMode = null, int ExtractRefillCap = 20)
{
    public string RefillMode => ExtractRefillMode ?? (RefillAfterExtract ? "infinite" : "off");
}

public sealed record CompatSettings(bool LockWithQuestingBots, IReadOnlyList<string> VariantMods);

/// <summary>진단(0.6). Log: off·summary·verbose.</summary>
public sealed record DiagSettings(string Log, bool Report, bool BotLog);

/// <summary>보스 하나. EscMin·EscMax 둘 다 0, TFrom·TTo 둘 다 0, Diff 빈 값, 구역 없음 = 바닐라 유지(0.3).</summary>
public sealed record BossSettings(bool On, double Chance, IReadOnlyDictionary<string, bool> Maps,
    bool Followers = true, int EscMin = 0, int EscMax = 0, double TFrom = 0, double TTo = 0, string Diff = "",
    bool Extra = false, IReadOnlyDictionary<string, string[]>? Zones = null,
    IReadOnlyDictionary<string, double>? MapChance = null);

/// <summary>특수 봇 무리(레이더·로그·컬티스트·블러드하운드). Chance는 맵별(없는 맵은 바닐라 확률 유지), Min·Max는 무리 전체 인원.</summary>
/// <summary>특수 봇 설정. 레이더·로그(ExtraModel)는 ABPS식(사용자 결정 11-10): 원래 항목(레버·탈출구 이벤트 포함)은 Vanilla면 확률·인원 그대로 두고,
/// Extra(맵별 확률)·ExtraMin~ExtraMax(인원)는 따로 넣는 "추가 스폰"에만 쓴다. 컬티스트·블러드하운드는 예전처럼 Chance·Min·Max가 원래 항목을 바꾼다.</summary>
public sealed record SpecialSettings(bool On, IReadOnlyDictionary<string, double> Chance, int Min, int Max,
    bool Vanilla = true, IReadOnlyDictionary<string, double>? Extra = null, int ExtraMin = 0, int ExtraMax = 0,
    IReadOnlyDictionary<string, bool>? Keep = null, double TFrom = 0, double TTo = 0, double XFrom = 0, double XTo = 0,
    int VanillaMax = 0, int ExtraCount = 1, double[]? ExtraCurve = null, double[]? ExtraSquad = null)
{
    // 1.0.6: ExtraCount = 추가 무리 수(1~6), ExtraCurve = 시각 창 초반·중반·후반 가중치(null = 고르게),
    // ExtraSquad = 무리 크기 1~8명 비율(null = ExtraMin~ExtraMax 고르게).
    // VanillaMax: 레이더·로그 원래 항목(이벤트 제외)의 한 판 총인원 상한, 0 = 그대로(1.0.1).
    /// <summary>레이더·로그 원래 항목을 이 맵에서 남기는지(맵별 등장 목록, 1.0). 없으면 남긴다.</summary>
    public bool KeepOn(string mapId) => Vanilla && (Keep == null || !Keep.TryGetValue(mapId, out var k) || k);

    /// <summary>원래 항목을 건드리지 않고 추가 스폰만 따로 넣는 역할(레이더 pmcBot·로그 exUsec).</summary>
    public static bool ExtraModel(string id) => id is "raiders" or "rogues";
}

/// <summary>
/// 트리거 스폰 하나(1.0.6): 맵 → "트리거id|원래 역할" → 규칙. Chance null = 원래 확률, Max 0 = 원래 호위 인원,
/// Role 빈 값 = 원래 역할(플레이어 조작 트리거의 레이더만 exUsec·blackDivAssault로 바꿀 수 있음).
/// 트리거·구역·Delay·Time은 바꾸지 않는다.
/// </summary>
public sealed record TriggerRule(double? Chance, int Min, int Max, string Role);

/// <summary>팩션 무리 종류 하나(순찰·검문소·사냥조, 0.4). TMin·TMax는 분(검문소·사냥조는 안 씀).</summary>
public sealed record FactionKind(bool On, double Chance, int Count, int Min, int Max, double TMin, double TMax);

/// <summary>RUAF·UNTAR 같은 순찰형 팩션(0.4). Maps에는 웹에서 정한 맵만 있다(없는 맵 = 모드 설정 기본).</summary>
public sealed record FactionSettings(bool Managed, bool ShareCap, IReadOnlyDictionary<string, bool> Maps,
    FactionKind Patrol, FactionKind Checkpoint, FactionKind Hunt, string Style = "waves");

/// <summary>블랙 디비전(0.4). TMin·TMax는 레이드 %, 연구소 웨지·게이트, 블랙아웃 연동.</summary>
public sealed record BlackDivSettings(bool Managed, bool ShareCap, IReadOnlyDictionary<string, bool> Maps,
    double Chance, double TMin, double TMax, int HuntMin, int HuntMax, double LabsGate, bool Wedge, double WedgeChance,
    string BlackoutLink, double BlackoutMult, string Style = "waves");

public sealed record GravityConfig(
    bool SpawnModule, SpawnSettings Spawn, IReadOnlyDictionary<string, MapSettings> Maps,
    IReadOnlyDictionary<string, BossSettings> Bosses, bool BossCommonOn, double BossCommonChance, NearSettings Near,
    IReadOnlyDictionary<string, SpecialSettings>? Specials = null, EventSettings? Events = null,
    FactionSettings? Ruaf = null, BlackDivSettings? BlackDiv = null, string OtherPmc = "keep", GearSettings? Gear = null,
    OrbitSettings? Orbit = null, DiagSettings? Diag = null, CompatSettings? Compat = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, TriggerRule>>? Triggers = null)
{
    public OrbitSettings OrbitOrDefault => Orbit ?? new OrbitSettings(true, false, true);
    public DiagSettings DiagOrDefault => Diag ?? new DiagSettings("summary", true, false);
    public CompatSettings CompatOrDefault => Compat ?? new CompatSettings(false, []);

    public MapSettings MapFor(string id) => Maps.TryGetValue(id, out var m) ? m : ConfigModel.DefaultMap(id);

    /// <summary>켜진 카드만 맵 값으로 합친다. 다른 카드는 항상 최신 공통 값을 따른다.</summary>
    public SpawnSettings SpawnFor(string id)
    {
        var m = MapFor(id);
        var s = Spawn;
        var v = m.CardSpawn;
        if (m.Override)
            s = v == null ? s with { StartPmcPct = m.StartPmcPct, IntervalMin = m.IntervalMin,
                WaveMin = m.WaveMin, WaveMax = m.WaveMax, Intensity = m.Intensity } : s with
            {
                StartPmcPct = m.StartPmcPct, FirstDelaySec = v.FirstDelaySec, IntervalMin = m.IntervalMin,
                RandomPct = v.RandomPct, MinGapSec = v.MinGapSec, DistWave = v.DistWave, DistBots = v.DistBots,
                Smooth = v.Smooth, WaveMin = m.WaveMin, WaveMax = m.WaveMax, Intensity = m.Intensity,
                IgnoreCapStart = v.IgnoreCapStart, IgnoreCapWaves = v.IgnoreCapWaves,
                StartMode = v.StartMode, StartPmcMin = v.StartPmcMin, StartPmcMax = v.StartPmcMax, StartPlayerOnly = v.StartPlayerOnly,
                HardStop = s.HardStop with { Mode = v.HardStop.Mode, PmcPct = v.HardStop.PmcPct, BeforeEndMin = v.HardStop.BeforeEndMin }
            };
        if (v != null)
        {
            if (m.Cards?.Scav == true) s = s with { Scav = v.Scav, HardStop = s.HardStop with { ScavPct = v.HardStop.ScavPct } };
            if (m.Cards?.Dist == true) s = s with { Protect = v.Protect, Spacing = v.Spacing };
            if (m.Cards?.Hotspot == true) s = s with { Placement = v.Placement, HotspotShare = v.HotspotShare, HotspotBoost = v.HotspotBoost };
            if (m.Cards?.Limits == true) s = s with { GroupCap = v.GroupCap, Caps = v.Caps };
        }
        return s;
    }

    public NearSettings NearFor(string id) => MapFor(id) is { Cards.Near: true, CardNear: { } n } ? n : Near;

    /// <summary>변형이 있는 맵의 원본 판에는 zonesAlt만 쓴다. 없는 설정은 기본 구역·비중이다.</summary>
    public GravityConfig ForLayout(string id, bool variant, bool hasVariant)
    {
        if (variant || !hasVariant) return this;
        var m = MapFor(id);
        var z = m.ZonesAlt ?? new ZoneLayout(new Dictionary<string, bool>(), new Dictionary<string, double>(), new Dictionary<string, double>());
        var maps = Maps.ToDictionary(kv => kv.Key, kv => kv.Value);
        maps[id] = m with { Zones = z.On, ZoneWeights = z.Pmc, ScavZoneWeights = z.Scav };
        return this with { Maps = maps };
    }
}

public static partial class ConfigModel
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
    public static bool Small(string map) => map.StartsWith("factory") || map == "labyrinth";
    // 연구소는 중간 크기라 120m면 시작 분대가 지점을 못 찾아 만료된다(11-09 0.3 판: 7분에 만료 2) → 80m.
    public static double DefaultPmcDist(string map) => Small(map) ? 40 : map == "laboratory" ? 80 : 120;
    public static double DefaultScavDist(string map) => Small(map) ? 30 : 80;

    public static MapSettings DefaultMap(string id) => new(
        false, id == "labyrinth" ? "infinite" : "waves", new InfSettings(45, 2, 50, 30), 35, 6, 2, 4,
        new Intensity(1.2, 1, 0.6), true, DefaultCap(id), new Dictionary<string, bool>(),
        DefaultPmcDist(id), DefaultScavDist(id), true, new Dictionary<string, double>(), DefaultVariant(id));

    /// <summary>변형 맵 적용 기본값(웹 makeDefaults와 같음): 등대만 끔(Manimal 등대는 스폰 모드를 스스로 끈다).</summary>
    public static bool DefaultVariant(string id) => id != "lighthouse";

    public static GravityConfig Default() => FromState(null, []);

    public static GravityConfig FromState(JsonNode? state, List<string> clamped)
    {
        var r = new Reader(state, clamped);
        var spawn = ReadSpawn(r);

        var maps = new Dictionary<string, MapSettings>();
        foreach (var id in KnownMaps)
        {
            var d = DefaultMap(id);
            var p = $"maps.{id}.";
            var zones = new Dictionary<string, bool>();
            var zoneW = new Dictionary<string, double>();
            var scavZoneW = new Dictionary<string, double>();
            if (r.Node(p + "zones") is JsonObject zo)
                foreach (var (k, v) in zo)
                {
                    zones[k] = v is JsonObject o && o["on"] is JsonValue jv && jv.TryGetValue<bool>(out var on) ? on : true;
                    var legacy = r.Num($"{p}zones.{k}.w", 100, 0, 1000);
                    zoneW[k] = r.Node($"{p}zones.{k}.wPmc") == null ? legacy : r.Num($"{p}zones.{k}.wPmc", legacy, 0, 200);
                    scavZoneW[k] = r.Node($"{p}zones.{k}.wScav") == null ? legacy : r.Num($"{p}zones.{k}.wScav", legacy, 0, 200);
                }
            var altOn = new Dictionary<string, bool>();
            var altPmc = new Dictionary<string, double>();
            var altScav = new Dictionary<string, double>();
            if (r.Node(p + "zonesAlt") is JsonObject alt)
                foreach (var (k, v) in alt)
                {
                    altOn[k] = v is JsonObject o && o["on"] is JsonValue jv && jv.TryGetValue<bool>(out var on) ? on : true;
                    var legacy = r.Num($"{p}zonesAlt.{k}.w", 100, 0, 1000);
                    altPmc[k] = r.Node($"{p}zonesAlt.{k}.wPmc") == null ? legacy : r.Num($"{p}zonesAlt.{k}.wPmc", legacy, 0, 200);
                    altScav[k] = r.Node($"{p}zonesAlt.{k}.wScav") == null ? legacy : r.Num($"{p}zonesAlt.{k}.wScav", legacy, 0, 200);
                }
            var mr = r.ForMap(id);
            var effective = ReadSpawn(mr);
            maps[id] = new MapSettings(
                mr.CardOn("flow"), r.Str(p + "style", d.Style, "waves", "infinite", "start"),
                new InfSettings(r.Num(p + "inf.delay", 45, 10, 600), (int)r.Num(p + "inf.batch", 2, 1, 10),
                    r.Num(p + "inf.pmcShare", 50, 0, 100), r.Num(p + "inf.jitter", 30, 0, 100)),
                effective.StartPmcPct, effective.IntervalMin, effective.WaveMin, effective.WaveMax,
                effective.Intensity,
                r.Bool(p + "original", true), EffectiveCap(r, p, d.Cap), zones,
                mr.Num("spawn.pmcDist", d.PmcDist, 5, 1000), mr.Num("spawn.scavDist", d.ScavDist, 5, 1000),
                mr.Bool("spawn.scav.startOn", true), zoneW, r.Bool(p + "variant", DefaultVariant(id)),
                r.Str(p + "scavStyle", "waves", "waves", "infinite", "start"), scavZoneW, new ZoneLayout(altOn, altPmc, altScav),
                new CardOverrides(mr.CardOn("scav"), mr.CardOn("near"), mr.CardOn("dist"), mr.CardOn("hotspot"), mr.CardOn("limits")),
                effective, ReadNear(mr));
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
            // 1.0.5: 맵별 등장 확률(없는 맵 = 보스 기본/공통 확률). 0이면 그 맵에서 안 나온다.
            var mapChance = new Dictionary<string, double>();
            if (r.Node(p + "mapChance") is JsonObject mco)
                foreach (var (k, v) in mco)
                    if (k.Length is > 0 and <= 40 && v is JsonValue mv && mv.GetValueKind() == JsonValueKind.Number)
                        mapChance[k] = r.Num(p + "mapChance." + k, 0, 0, 100);
            var eMin = (int)r.Num(p + "escMin", 0, 0, 10);
            var tFrom = r.Num(p + "tFrom", 0, 0, 600);
            bosses[id] = new BossSettings(r.Bool(p + "on", true), r.Num(p + "chance", ch, 0, 100), bm,
                r.Bool(p + "followers", true), eMin, (int)Math.Max(eMin, r.Num(p + "escMax", 0, 0, 10)),
                tFrom, Math.Max(tFrom, r.Num(p + "tTo", 0, 0, 600)),
                r.Str(p + "diff", "", "easy", "normal", "hard", "impossible", "random"), r.Bool(p + "extra", false), zones, mapChance);
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
            var extra = new Dictionary<string, double>();
            if (r.Node(p + "extra") is JsonObject eo)
                foreach (var (k, _) in eo)
                    extra[k] = r.Num(p + "extra." + k, 0, 0, 100);
            var eMin = (int)r.Num(p + "extraMin", 1, 0, 10);
            double tFrom = r.Num(p + "tFrom", 0, 0, 240), tTo = r.Num(p + "tTo", 0, 0, 240), xFrom = r.Num(p + "xFrom", 0, 0, 240), xTo = r.Num(p + "xTo", 0, 0, 240);
            var keep = new Dictionary<string, bool>();
            if (r.Node(p + "keep") is JsonObject ko)
                foreach (var (k, _) in ko)
                    keep[k] = r.Bool(p + "keep." + k, true);
            specials[id] = new SpecialSettings(r.Bool(p + "on", id != "infected"), chance, sMin, (int)Math.Max(sMin, r.Num(p + "max", 0, 0, 10)),
                r.Bool(p + "vanilla", true), extra, eMin, (int)Math.Max(eMin, r.Num(p + "extraMax", eMin, 0, 10)), keep,
                tFrom, Math.Max(tFrom, tTo), xFrom, Math.Max(xFrom, xTo), (int)r.Num(p + "vanillaMax", 0, 0, 40),
                (int)r.Num(p + "extraCount", 1, 1, 6), Curve(r, p + "extraCurve."), Squad(r, p + "extraSquad.", 8));
        }

        var near = ReadNear(r);

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
            Kind(fr + "patrol.", 45, 2, 3, 6, 5, 28), Kind(fr + "checkpoint.", 45, 2, 3, 4, 0, 0), Kind(fr + "hunt.", 20, 1, 3, 4, 0, 0),
            r.Str(fr + "style", "waves", "waves", "start"));
        const string fb = "factions.blackdiv.";
        var bdT0 = r.Num(fb + "bd.tMin", 10, 0, 100);
        var bdMin = (int)r.Num(fb + "bd.huntMin", 3, 1, 10);
        var blackDiv = new BlackDivSettings(r.Bool(fb + "managed", false), r.Bool(fb + "shareCap", false), FacMaps(fb + "maps"),
            r.Num(fb + "bd.chance", 25, 0, 100), bdT0, Math.Max(bdT0, r.Num(fb + "bd.tMax", 80, 0, 100)),
            bdMin, (int)Math.Max(bdMin, r.Num(fb + "bd.huntMax", 5, 1, 10)), r.Num(fb + "bd.labsGate", 20, 0, 100),
            r.Bool(fb + "bd.wedge", true), r.Num(fb + "bd.wedgeChance", 15, 0, 100),
            r.Str("lab.blackoutLink", "boost", "always", "only", "boost"), r.Num("lab.blackoutMult", 3, 1, 5),
            r.Str(fb + "style", "waves", "waves", "start"));

        return new GravityConfig(r.Bool("modules.spawn", true), spawn, maps, bosses,
            r.Bool("bossCommon.on", false), r.Num("bossCommon.chance", 35, 0, 100), near, specials, events,
            ruaf, blackDiv, r.Str("compat.otherPmc", "keep", "keep", "remove"), ParseGear(r),
            new OrbitSettings(r.Bool("orbit.refillAfterExtract", true), r.Bool("orbit.nearAsGhost", false), r.Bool("orbit.respectTakeover", true),
                r.Node("orbit.extractRefillMode") == null ? null : r.Str("orbit.extractRefillMode", r.Bool("orbit.refillAfterExtract", true) ? "infinite" : "off", "off", "infinite", "all"),
                (int)r.Num("orbit.extractRefillCap", 20, 0, 100)),
            new DiagSettings(r.Str("diag.log", "summary", "off", "summary", "verbose"), r.Bool("diag.report", true), r.Bool("diag.botLog", false)),
            new CompatSettings(r.Bool("compat.lockWithQuestingBots", false), r.StringList("compat.variantMods")),
            ReadTriggers(r));
    }

    /// <summary>초반·중반·후반 배수(0~3). 칸이 하나도 없거나 모두 1이면 null(= 1.0.5와 같게 고르게).</summary>
    internal static double[]? Curve(Reader r, string p)
    {
        if (r.Node(p + "early") == null && r.Node(p + "mid") == null && r.Node(p + "late") == null) return null;
        var c = new[] { r.Num(p + "early", 1, 0, 3), r.Num(p + "mid", 1, 0, 3), r.Num(p + "late", 1, 0, 3) };
        return c.All(x => Math.Abs(x - 1) < 1e-9) ? null : c;
    }

    /// <summary>무리 크기 1~n명 비율(s1~sn, 0~100). 칸이 없거나 합이 0이면 null.</summary>
    internal static double[]? Squad(Reader r, string p, int n)
    {
        if (r.Node(p.TrimEnd('.')) is not JsonObject) return null;
        var w = Enumerable.Range(1, n).Select(k => r.Num(p + "s" + k, 0, 0, 100)).ToArray();
        return w.Sum() > 0 ? w : null;
    }

    /// <summary>웹 triggers.&lt;맵&gt;.&lt;트리거id&gt;.&lt;역할&gt;.{chance,min,max,role}(1.0.6). 이름이 이상한 키는 버린다.</summary>
    private static Dictionary<string, IReadOnlyDictionary<string, TriggerRule>>? ReadTriggers(Reader r)
    {
        if (r.Node("triggers") is not JsonObject root) return null;
        static bool Key(string k) => k.Length is > 0 and <= 60 && k.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
        var maps = new Dictionary<string, IReadOnlyDictionary<string, TriggerRule>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (map, mn) in root)
        {
            if (!Key(map) || mn is not JsonObject mo) continue;
            var rules = new Dictionary<string, TriggerRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var (trig, tn) in mo)
            {
                if (!Key(trig) || tn is not JsonObject to) continue;
                foreach (var (role, rn) in to)
                {
                    if (!Key(role) || rn is not JsonObject) continue;
                    var p = $"triggers.{map}.{trig}.{role}.";
                    double? chance = r.Node(p + "chance") is JsonValue cv && cv.GetValueKind() == JsonValueKind.Number ? r.Num(p + "chance", 0, 0, 100) : null;
                    var min = (int)r.Num(p + "min", 0, 0, 10);
                    var max = (int)Math.Max(min, r.Num(p + "max", 0, 0, 10));
                    var rule = new TriggerRule(chance, min, max, r.Str(p + "role", "", "", "pmcBot", "exUsec", "blackDivAssault"));
                    if (rule.Chance != null || rule.Max > 0 || rule.Role.Length > 0) rules[$"{trig}|{role}"] = rule;
                }
            }
            if (rules.Count > 0) maps[map] = rules;
        }
        return maps.Count > 0 ? maps : null;
    }

    /// <summary>
    /// <summary>
    /// 1.0 동시 봇 상한: maps.&lt;id&gt;.capOn이면 그 맵 값, 아니면 spawn.botCap(0이면 맵 기본값).
    /// capOn이 없는 옛 설정은 맵 값이 기본값과 다르면 따로 정한 것으로 본다.
    /// </summary>
    internal static int EffectiveCap(Reader r, string p, int mapDefault)
    {
        var own = (int)r.Num(p + "cap", mapDefault, 1, 60);
        bool on = r.Node(p + "capOn") is JsonValue ? r.Bool(p + "capOn", false) : r.Node(p + "cap") is JsonValue && own != mapDefault;
        if (on) return own;
        var shared = (int)r.Num("spawn.botCap", 0, 0, 60);
        return shared > 0 ? shared : mapDefault;
    }

    /// 상한 표(spawn.caps.*). 팩션 줄 기본값은 옛 "PMC 동시 상한을 같이 씀"(팩션별 shareCap)에서 이어받는다:
    /// 하나라도 켜져 있었으면 respect, 아니면 separate(옛 기본과 같은 동작).
    /// </summary>
    private static CapTable ReadCaps(Reader r, string sp)
    {
        var c = sp + "caps.";
        bool oldShare = r.Bool("factions.ruaf.shareCap", false) || r.Bool("factions.blackdiv.shareCap", false) || r.Bool("factions.untar.shareCap", false);
        var kinds = new Dictionary<string, CapKindSettings>();
        foreach (var k in Gravity.Shared.CapKinds.Table)
        {
            var def = k == Gravity.Shared.CapKinds.Faction && oldShare ? Gravity.Shared.CapKinds.Respect : Gravity.Shared.CapKinds.Separate;
            kinds[k] = new CapKindSettings(
                r.Str(c + k + ".mode", def, Gravity.Shared.CapKinds.Separate, Gravity.Shared.CapKinds.Vanilla, Gravity.Shared.CapKinds.Respect),
                (int)r.Num(c + k + ".max", 0, 0, 60));
        }
        return new CapTable((int)r.Num(c + "pmcReserve", 0, 0, 60), (int)r.Num(c + "pmcGroups", 0, 0, 30), kinds);
    }

    private static SpawnSettings ReadSpawn(Reader r)
    {
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
        var startMin = (int)r.Num(sp + "startPmcMin", 8, 0, 40);
        var scavStartMin = (int)r.Num(sp + "scav.startMin", 3, 0, 40);
        var remainMin = (int)r.Num(sp + "scav.remainMin", 2, 0, 30);
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
                r.Num(sp + "scav.playerScavRemain", 60, 0, 100), r.Bool(sp + "scav.startOn", true), r.Num(sp + "scav.startPct", 20, 0, 100),
                r.Str(sp + "scav.startMode", "pct", "pct", "count"), scavStartMin, (int)Math.Max(scavStartMin, r.Num(sp + "scav.startMax", 6, 0, 40)),
                r.Str(sp + "scav.remainMode", "pct", "pct", "count"), remainMin, (int)Math.Max(remainMin, r.Num(sp + "scav.remainMax", 4, 0, 30))),
            new ProtectSettings(r.Bool(sp + "protect.on", true), r.Num(sp + "protect.sec", 90, 0, 3600), r.Num(sp + "protect.radius", 120, 5, 1000)),
            new SpacingSettings(r.Num(sp + "spacing.player", 80, 5, 1000), r.Num(sp + "spacing.bots", 25, 5, 1000)),
            r.Str(sp + "placement", "mixed", "mixed", "hotspot", "vanilla"), r.Num(sp + "hotspotShare", 60, 0, 100),
            r.Bool(sp + "hotspotBoost", true), r.Bool(sp + "allowPmcLabyrinth", false),
            r.Bool(sp + "allowScavLab", false), ReadCaps(r, sp),
            r.Str(sp + "startMode", "pct", "pct", "count"), startMin, (int)Math.Max(startMin, r.Num(sp + "startPmcMax", 12, 0, 40)),
            r.Bool(sp + "startPlayerOnly", false));
        return spawn;
    }

    private static NearSettings ReadNear(Reader r)
    {
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
        return near;
    }

    /// <summary>JSON 경로를 읽는다. 형식이 틀리면 기본값, 범위를 넘으면 고정하고 경로를 기록.</summary>
    internal sealed class Reader(JsonNode? root, List<string> clamped, string? mapId = null)
    {
        public string[] StringList(string path) => Node(path) is JsonArray a
            ? a.OfType<JsonValue>().Where(v => v.GetValueKind() == JsonValueKind.String)
                .Select(v => v.GetValue<string>().Trim()).Where(v => v.Length is > 0 and <= 120)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(1000).ToArray() : [];

        public Reader ForMap(string id) => new(root, clamped, id);

        public bool CardOn(string card)
        {
            var key = $"maps.{mapId}.ovr.{card}";
            if (RawNode(key) is JsonValue jv && jv.TryGetValue<bool>(out var on)) return on;
            return card == "flow" && RawNode($"maps.{mapId}.override") is JsonValue old && old.TryGetValue<bool>(out var legacy) && legacy;
        }

        public string MapPath(string path)
        {
            if (mapId == null) return path;
            var card = CardFor(path);
            if (card == "common") return path;
            var prefix = $"maps.{mapId}.";
            var legacyDistance = path is "spawn.pmcDist" or "spawn.scavDist";
            var legacyStart = path == "spawn.scav.startOn";
            if (!CardOn(card) && !(RawNode(prefix + "ovr." + card) == null && (legacyDistance || legacyStart))) return path;
            var suffix = path.StartsWith("spawn.", StringComparison.Ordinal) ? path[6..] : path;
            var canonical = prefix + suffix;
            if (RawNode(canonical) != null) return canonical;
            var alias = suffix.StartsWith("intensity.", StringComparison.Ordinal) ? prefix + suffix[10..]
                : legacyStart ? prefix + "start.scav" : canonical;
            return RawNode(alias) != null ? alias : path;
        }

        private static string CardFor(string path) => path switch
        {
            var p when p.StartsWith("near.", StringComparison.Ordinal) => "near",
            var p when p.StartsWith("spawn.scav.", StringComparison.Ordinal) || p == "spawn.hardStop.scavPct" => "scav",
            var p when p.StartsWith("spawn.protect.", StringComparison.Ordinal) || p.StartsWith("spawn.spacing.", StringComparison.Ordinal) || p is "spawn.pmcDist" or "spawn.scavDist" => "dist",
            "spawn.placement" or "spawn.hotspotShare" or "spawn.hotspotBoost" => "hotspot",
            var p when p.StartsWith("spawn.groupCap.", StringComparison.Ordinal) || p.StartsWith("spawn.caps.", StringComparison.Ordinal) => "limits",
            var p when p.StartsWith("spawn.squad.", StringComparison.Ordinal) || p.StartsWith("spawn.diff.", StringComparison.Ordinal) || p is "spawn.usecPct" or "spawn.allowPmcLabyrinth" or "spawn.allowScavLab" => "common",
            _ => "flow"
        };

        public JsonNode? Node(string path) => RawNode(MapPath(path));

        private JsonNode? RawNode(string path)
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
            if (v < min || v > max) { clamped.Add(MapPath(path)); v = Math.Clamp(v, min, max); }
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
