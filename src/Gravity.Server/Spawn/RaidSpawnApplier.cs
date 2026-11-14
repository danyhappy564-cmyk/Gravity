using Gravity.Core;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Gravity.Spawn;

/// <summary>계획을 레이드용 맵 복제본에 쓴다. 새 목록을 다 만든 뒤 마지막에 한 번에 바꿔 넣는다(중간 예외 시 원본 유지).</summary>
public static class RaidSpawnApplier
{
    // SPT 보스 역할 → 웹 보스 id
    public static readonly IReadOnlyDictionary<string, string> BossRoleToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["bossBully"] = "reshala", ["bossKojaniy"] = "shturman", ["bossTagilla"] = "tagilla", ["bossGluhar"] = "glukhar",
        ["bossSanitar"] = "sanitar", ["bossKilla"] = "killa", ["bossBoar"] = "kaban", ["bossKolontay"] = "kollontay",
        ["bossZryachiy"] = "zryachiy", ["bossKnight"] = "goons", ["bossPartisan"] = "partisan"
    };

    private static readonly string[] Modes = ["regular", "pve"];

    /// <param name="hasClient">클라 플러그인이 있는 세션이면 true: 스캐브 웨이브(assault)를 계획의 시작 스캐브로 바꾸고,
    /// 저격(marksman)은 scav.startMarksman이 꺼졌을 때만 지운다. 없으면 스캐브·저격 웨이브는 바닐라 그대로.</param>
    /// <param name="sptPmc">SPT 원본 PMC 항목 지문(맵 단위, 0.4). 있으면 지문이 안 맞는 PMC 항목을 다른 모드 것으로 보고
    /// <c>compat.otherPmc=keep</c>일 때 남긴다. null이면 0.3처럼 PMC 항목을 다 지운다.</param>
    /// <returns>남긴 다른 모드 PMC 항목 수.</returns>
    public static int Apply(LocationBase loc, SpawnPlan plan, GravityConfig cfg, MapInfo info, bool hasClient = false,
        IReadOnlyDictionary<string, int>? sptPmc = null)
    {
        var ms = cfg.MapFor(info.Id);
        var scavZones = Zones(info.ScavZones, ms);
        var s = cfg.SpawnFor(info.Id);

        var keepOther = sptPmc != null && cfg.OtherPmc == "keep";
        var left = keepOther ? new Dictionary<string, int>(sptPmc!) : null;
        var otherKept = 0;
        bool KeepPmc(BossLocationSpawn e)
        {
            if (left == null) return false;
            var sig = Gravity.Factions.FactionData.PmcSig(e);
            if (left.TryGetValue(sig, out var n) && n > 0) { left[sig] = n - 1; return false; }
            otherKept++;
            return true;
        }
        var bosses = loc.BossLocationSpawn.Where(e => !MapCatalog.IsPmc(e) || KeepPmc(e)).ToList();
        for (var i = 0; i < plan.Groups.Count; i++)
        {
            var g = plan.Groups[i];
            var side = g.Usec ? "pmcUSEC" : "pmcBEAR";
            bosses.Add(new BossLocationSpawn
            {
                BossName = side, BossEscortType = side, BossChance = 100,
                BossDifficulty = g.Difficulty, BossEscortDifficulty = g.Difficulty,
                BossEscortAmount = (g.Size - 1).ToString(), BossZone = PmcZoneFor(info.PmcZones, ms, i),
                IsBossPlayer = false, IsRandomTimeSpawn = false, Time = Math.Max(1, g.TimeSec),
                // TriggerName이 비어 있으면 게임은 TriggerId를 보지 않는다(타이머 웨이브): Gravity 분대 표식으로 쓴다(4차 재검토 2번)
                TriggerId = Gravity.Shared.WaveMark.For(info.Id, plan.Seed, i), TriggerName = "", Delay = 0, ForceSpawn = false,
                IgnoreMaxBots = g.IsStart ? s.IgnoreCapStart : s.IgnoreCapWaves,
                Supports = null!, SpawnMode = Modes, SptId = "gravity"
            });
        }

        // 스캐브 웨이브: 클라가 있을 때만 갈아 끼운다(시작 스캐브가 0개여도 바닐라 assault는 지운다).
        var manageScavs = hasClient;
        var waves = (loc.Waves ?? []).Where(w => !manageScavs
            || (w.WildSpawnType != WildSpawnType.assault && (s.Scav.StartMarksman || w.WildSpawnType != WildSpawnType.marksman))).ToList();
        var startZones = scavZones.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var startIndex = 0;
        foreach (var w in plan.ScavWaves)
        {
            if (manageScavs && w.IsStart)
            {
                // 일반 웨이브는 쉼표 목록을 해석하지 않습니다. 한 명씩 허용 구역에 분산하고 인원을 표식에 보존합니다.
                // 지연 0은 게임의 BeforeGameStarted 경로에서 프로필·모델을 준비하게 합니다.
                var time = w.TimeMinSec == 0 ? -1 : w.TimeMinSec;
                for (var n = 0; n < w.SlotsMax; n++)
                {
                    var zone = startZones.Length == 0 ? "" : startZones[startIndex++ % startZones.Length];
                    waves.Add(new Wave
                    {
                        BotPreset = "normal", BotSide = "Savage", WildSpawnType = WildSpawnType.assault, IsPlayers = false,
                        KeepZoneOnSpawn = false, SpawnPoints = Gravity.Shared.ScavWaveMark.Encode(1, 1, zone), SlotsMin = 1, SlotsMax = 1,
                        TimeMin = time, TimeMax = time, SpawnMode = [.. Modes], SptId = "gravity"
                    });
                }
            }
            else waves.Add(new Wave
            {
                BotPreset = "normal", BotSide = "Savage", WildSpawnType = WildSpawnType.assault, IsPlayers = false,
                KeepZoneOnSpawn = false, SpawnPoints = scavZones, SlotsMin = w.SlotsMin, SlotsMax = w.SlotsMax,
                TimeMin = w.TimeMinSec, TimeMax = w.TimeMaxSec, SpawnMode = [.. Modes], SptId = "gravity"
            });
        }
        if (manageScavs) for (var i = 0; i < waves.Count; i++) waves[i].Number = i;

        // BotMax는 웨이브 밖 자동 스폰의 목표치라 건드리지 않는다(4.1 클라 전체 상한은 클라 설정 — 2-2에서 클라 플러그인으로).
        loc.BossLocationSpawn = bosses;
        if (manageScavs)
        {
            // 네이티브 CheckMinCounts는 종류별 최소 인원이 모자라면 다른 종류의 웨이브까지 늘립니다.
            // 저격을 끈 세관(원본 marksman 최소 2)에서 시작 assault 1명이 2명이 되는 것을 방지합니다.
            // 레이드 복제본만 바꾸고, 클라가 없거나 유지한 저격·다른 역할의 범위는 보존합니다.
            if (loc.MinMaxBots != null) loc.MinMaxBots = loc.MinMaxBots.Select(b => b.WildSpawnType == "assault"
                || !s.Scav.StartMarksman && b.WildSpawnType == "marksman" ? b with { Min = 0 } : b).ToList();
            loc.Waves = waves;
        }
        return otherKept;
    }

    /// <summary>보스 켜기·확률·맵 끄기. 설정에 없는 역할(팩션·이벤트·모드 보스)은 건드리지 않는다.</summary>
    private static readonly string[] Diffs = ["easy", "normal", "hard", "impossible"];

    /// <summary>
    /// 보스·특수 봇 항목을 웹 설정대로 고친다(0.3). 새 항목을 먼저 다 만든 뒤 한 번에 바꿔 넣는다(중간 예외 시 원본 유지).
    /// 보스: 확률(0.1), 호위 데려오기·호위 수·등장 시간·난이도·구역·추가 스폰. 특수 봇: 켜기·맵별 확률·무리 크기.
    /// 구역은 이 맵 보스 항목에 실제로 있는 이름만 남기고(웹 목업 id 등 모르는 이름은 버림), 다 버려지면 바닐라 구역.
    /// </summary>
    /// <param name="templates">보스 역할 → 다른 맵 DB 항목(MapCatalog.BossTemplates). 웹에서 원래 없던 맵을 켠 보스는 이걸 복제해 넣는다(0.4).</param>
    /// <summary>1.0.5: 이 맵에서 보스 등장 확률. 꺼짐·맵 꺼짐 = 0, 맵별 값 > 보스 공통(즈랴치 제외) > 보스 기본.</summary>
    internal static double BossChanceFor(GravityConfig cfg, string id, BossSettings b, string mapId)
    {
        if (!b.On || (b.Maps.TryGetValue(mapId, out var on) && !on)) return 0;
        if (b.MapChance != null && b.MapChance.TryGetValue(mapId, out var mc)) return mc;
        return cfg.BossCommonOn && id != "zryachiy" ? cfg.BossCommonChance : b.Chance;
    }

    public static void ApplyBosses(LocationBase loc, GravityConfig cfg, string mapId, int seed = 1, int raidSec = 0,
        IReadOnlyDictionary<string, BossLocationSpawn>? templates = null)
    {
        var rnd = new Random(seed ^ 0x5B055);
        var valid = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in loc.BossLocationSpawn)
            foreach (var z in (e.BossZone ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                valid.Add(z);
        var specialByRole = (cfg.Specials ?? new Dictionary<string, SpecialSettings>())
            .Where(kv => ConfigModel.SpecialRoles.ContainsKey(kv.Key))
            .ToDictionary(kv => ConfigModel.SpecialRoles[kv.Key], kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        var extraRoles = new HashSet<string>((cfg.Specials ?? new Dictionary<string, SpecialSettings>()).Keys
            .Where(SpecialSettings.ExtraModel).Select(k => ConfigModel.SpecialRoles[k]), StringComparer.OrdinalIgnoreCase);
        var result = new List<BossLocationSpawn>(loc.BossLocationSpawn.Count);
        var extra = new List<BossLocationSpawn>();
        foreach (var e in loc.BossLocationSpawn)
        {
            if (e.BossName is { } role && BossRoleToId.TryGetValue(role, out var id) && cfg.Bosses.TryGetValue(id, out var b))
            {
                var chance = BossChanceFor(cfg, id, b, mapId);
                var n = Detail(e with { BossChance = chance }, b);
                result.Add(n);
                if (b.Extra && chance > 0) extra.Add(n with { SptId = "gravity" });
                continue;
            }
            if (e.BossName is { } sr && specialByRole.TryGetValue(sr, out var sp))
            {
                if (extraRoles.Contains(sr))
                {
                    // 레이더·로그: 원래 항목(이벤트 포함)은 확률·인원·트리거 그대로. 끄거나 "원래 스폰 유지"를 끄면 그 항목만 0%.
                    // 시각 창은 트리거 없는(판 시작·시각) 항목에만. 레버·탈출구 같은 이벤트 항목은 그대로.
                    var o = !sp.On || !sp.KeepOn(mapId) ? e with { BossChance = 0 } : e;
                    if (sp.On && sp.TTo > 0 && string.IsNullOrEmpty(o.TriggerName)) o = o with { Time = SpecialTime(sp.TFrom, sp.TTo, rnd, raidSec, o.Time ?? -1) };
                    result.Add(o);
                    continue;
                }
                var n = e;
                if (!sp.On) n = n with { BossChance = 0 };
                else
                {
                    if (sp.Chance.TryGetValue(mapId, out var c)) n = n with { BossChance = c };
                    if (sp.Max > 0) n = n with { BossEscortAmount = Range(Math.Max(0, sp.Min - 1), Math.Max(0, sp.Max - 1)) };
                    if (sp.TTo > 0 && string.IsNullOrEmpty(n.TriggerName)) n = n with { Time = SpecialTime(sp.TFrom, sp.TTo, rnd, raidSec, n.Time ?? -1) };
                }
                result.Add(n);
                continue;
            }
            result.Add(e);
        }
        foreach (var (sr, sp) in specialByRole)
            if (extraRoles.Contains(sr) && sp.On && sp.VanillaMax > 0) CapVanillaGroups(result, sr, sp.VanillaMax, rnd);
        // 0.4: 웹에서 이 맵을 켰는데 바닐라 항목이 없는 보스는 다른 맵 항목을 본보기로 새로 넣는다(즈랴치는 등대 섬 전용이라 제외).
        if (templates != null)
            foreach (var (role, id) in BossRoleToId)
            {
                if (id == "zryachiy" || !cfg.Bosses.TryGetValue(id, out var b) || !b.On) continue;
                if (!b.Maps.TryGetValue(mapId, out var on) || !on) continue;
                if (result.Any(x => string.Equals(x.BossName, role, StringComparison.OrdinalIgnoreCase))) continue;
                if (!templates.TryGetValue(role, out var tpl)) continue;
                var chance = BossChanceFor(cfg, id, b, mapId);
                if (chance <= 0) continue;
                var zones = valid.Where(z => !z.StartsWith("ZoneSniper", StringComparison.OrdinalIgnoreCase)).ToArray();
                var n = Detail(tpl with { BossChance = chance, BossZone = string.Join(",", zones.Length > 0 ? zones : valid), TriggerId = "", TriggerName = "", SptId = "gravity" }, b);
                result.Add(n);
                if (b.Extra) result.Add(n with { });
            }
        result.AddRange(extra);
        // 1.0.6 #39: 원래 무리가 없는 맵에 켠 특수 봇은 다른 맵 항목을 본보기로 넣는다(구역 = 이 맵 보스 구역, 저격 구역 제외).
        var newZones = valid.Where(z => !z.StartsWith("ZoneSniper", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (newZones.Length == 0) newZones = valid.ToArray();
        if (templates != null)
            foreach (var (sr, sp) in specialByRole)
            {
                if (extraRoles.Contains(sr) || !sp.On || !sp.Chance.TryGetValue(mapId, out var c) || c <= 0) continue;
                if (result.Any(x => string.Equals(x.BossName, sr, StringComparison.OrdinalIgnoreCase))) continue;
                if (!templates.TryGetValue(sr, out var tpl)) continue;
                var n = tpl with
                {
                    BossChance = c, BossZone = string.Join(",", newZones), TriggerId = "", TriggerName = "", Delay = 0,
                    Time = SpecialTime(sp.TFrom, sp.TTo, rnd, raidSec, tpl.Time ?? -1), IgnoreMaxBots = false, ForceSpawn = false, SptId = "gravity"
                };
                if (sp.Max > 0) n = n with { BossEscortAmount = Range(Math.Max(0, sp.Min - 1), Math.Max(0, sp.Max - 1)) };
                result.Add(n);
            }
        AddExtraSpecials(loc.BossLocationSpawn, result, cfg, mapId, rnd, raidSec, templates, newZones);
        loc.BossLocationSpawn = result;

        // 0.4: 저격 스캐브(바닐라 marksman 웨이브). 끄면 지우고, 맵 확률이 있으면 웨이브마다 그 확률로 남기며, 인원 칸이 있으면 바꾼다.
        if (cfg.Specials != null && cfg.Specials.TryGetValue("snipers", out var sn) && loc.Waves != null)
        {
            var waves = new List<Wave>(loc.Waves.Count);
            foreach (var w in loc.Waves)
            {
                if (w.WildSpawnType != WildSpawnType.marksman) { waves.Add(w); continue; }
                if (!sn.On) continue;
                if (sn.Chance.TryGetValue(mapId, out var sc) && rnd.NextDouble() * 100 >= sc) continue;
                var nw = sn.Max > 0 ? w with { SlotsMin = Math.Max(0, sn.Min), SlotsMax = sn.Max } : w;
                if (sn.TTo > 0)
                {
                    int lo = (int)Math.Round(sn.TFrom * 60), hi = (int)Math.Round(sn.TTo * 60);
                    if (raidSec > 0) { lo = (int)Math.Min(lo, raidSec * 0.9); hi = (int)Math.Min(hi, raidSec * 0.9); }
                    nw = nw with { TimeMin = lo, TimeMax = Math.Max(lo, hi) };
                }
                waves.Add(nw);
            }
            loc.Waves = waves;
        }

        BossLocationSpawn Detail(BossLocationSpawn e, BossSettings b)
        {
            var n = e;
            if (!b.Followers) n = n with { BossEscortAmount = "0" };
            else if (b.EscMax > 0) n = n with { BossEscortAmount = Range(b.EscMin, b.EscMax) };
            if (b.TTo > 0)
            {
                var t = (b.TFrom + rnd.NextDouble() * (b.TTo - b.TFrom)) * 60;
                if (raidSec > 0) t = Math.Min(t, raidSec * 0.9);
                n = n with { Time = Math.Max(1, Math.Round(t)) };
            }
            if (b.Diff is { Length: > 0 })
            {
                var d = b.Diff == "random" ? Diffs[rnd.Next(Diffs.Length)] : b.Diff;
                n = n with { BossDifficulty = d, BossEscortDifficulty = d };
            }
            if (b.Zones != null && b.Zones.TryGetValue(mapId, out var zs))
            {
                var keep = zs.Where(valid.Contains).Distinct().ToArray();
                if (keep.Length > 0) n = n with { BossZone = string.Join(",", keep) };
            }
            return n;
        }
    }

    /// <summary>바닐라 호위 수 형식: 가능한 수를 쉼표로(클라가 하나를 고름). 예: 2~4 → "2,3,4".</summary>
    /// <summary>
    /// 1.0.6 트리거 스폰 조절(레버·스위치·탈출구·게임 이벤트). 보스·특수 봇·팩션 처리가 끝난 뒤 마지막에 부른다.
    /// 규칙(맵 → "트리거id|원래 역할")이 있는 트리거 항목의 확률·호위 인원·(플레이어 조작 트리거의 레이더만) 역할을 바꾼다.
    /// 트리거·구역·Delay·Time은 그대로 둔다. 이미 0%인 항목(원래 스폰 유지 끔·보스 끔)은 0%로 둔다.
    /// 파르티잔처럼 보스 설정이 확률을 정하는 역할(BossRoleToId)과 Gravity가 바꿔 넣은 항목(BlackDiv 게이트 등)은 건드리지 않는다.
    /// </summary>
    /// <returns>바꾼 항목 수(로그용).</returns>
    public static int ApplyTriggers(LocationBase loc, GravityConfig cfg, string mapId, bool blackDivInstalled)
    {
        if (cfg.Triggers == null || !cfg.Triggers.TryGetValue(mapId, out var rules) || loc.BossLocationSpawn == null) return 0;
        var changed = 0;
        var list = loc.BossLocationSpawn.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (string.IsNullOrEmpty(e.TriggerName) || string.IsNullOrEmpty(e.TriggerId) || e.BossName is not { Length: > 0 } role) continue;
            if (e.SptId == "gravity" || BossRoleToId.ContainsKey(role)) continue;
            if (!rules.TryGetValue($"{e.TriggerId}|{role}", out var rule)) continue;
            var n = e;
            if ((e.BossChance ?? 0) > 0 && rule.Chance is { } c) n = n with { BossChance = c };
            if (rule.Max > 0) n = n with { BossEscortAmount = Range(Math.Max(0, rule.Min - 1), Math.Max(0, rule.Max - 1)) };
            if (rule.Role.Length > 0 && !string.Equals(rule.Role, role, StringComparison.OrdinalIgnoreCase)
                && CanSwapRole(e, role, rule.Role, blackDivInstalled))
                n = n with { BossName = rule.Role, BossEscortType = rule.Role };
            if (n == e) continue;
            list[i] = n;
            changed++;
        }
        if (changed > 0) loc.BossLocationSpawn = list;
        return changed;
    }

    /// <summary>역할 바꾸기는 플레이어 조작 트리거(interactObject)의 레이더만, BlackDiv는 설치됐을 때만.</summary>
    internal static bool CanSwapRole(BossLocationSpawn e, string from, string to, bool blackDivInstalled) =>
        string.Equals(e.TriggerName, "interactObject", StringComparison.OrdinalIgnoreCase)
        && string.Equals(from, "pmcBot", StringComparison.OrdinalIgnoreCase)
        && (to is "exUsec" or "pmcBot" || (to == "blackDivAssault" && blackDivInstalled));

    /// <summary>특수 봇 시각 창(분) → 게임 Time(초). 끝이 0이면 원래대로(-1 = 판 시작).</summary>
    internal static double SpecialTime(double from, double to, Random rnd, double raidSec, double original = -1)
    {
        if (to <= 0) return original;
        var t = (from + rnd.NextDouble() * Math.Max(0, to - from)) * 60;
        if (raidSec > 0) t = Math.Min(t, raidSec * 0.9);
        return Math.Max(1, Math.Round(t));
    }

    /// <summary>
    /// 레이더·로그 "추가 스폰"(ABPS식, 사용자 결정 11-10): 원래 항목과 별개로 한 무리를 이 맵 확률로 더 넣는다.
    /// 본보기·구역은 이 맵의 원래 항목(이벤트 항목 포함)에서 가져온다. 원래 항목이 없는 맵(1.0.6 #39)은 다른 맵의
    /// 트리거 없는 항목(templates)을 본보기로, 구역은 이 맵 보스 구역(newZones)으로 넣는다.
    /// 추가분은 맵 상한을 지킨다(IgnoreMaxBots=false). 트리거는 비운다(타이머 웨이브).
    /// </summary>
    internal static void AddExtraSpecials(IReadOnlyList<BossLocationSpawn> original, List<BossLocationSpawn> result, GravityConfig cfg, string mapId,
        Random? random = null, double raidSec = 0, IReadOnlyDictionary<string, BossLocationSpawn>? templates = null, string[]? newZones = null)
    {
        var rnd = random ?? new Random(1);
        if (cfg.Specials == null) return;
        foreach (var (id, sp) in cfg.Specials)
        {
            if (!SpecialSettings.ExtraModel(id) || !sp.On || sp.Extra == null || sp.ExtraMax <= 0) continue;
            if (!sp.Extra.TryGetValue(mapId, out var chance) || chance <= 0) continue;
            var role = ConfigModel.SpecialRoles[id];
            var own = original.Where(e => string.Equals(e.BossName, role, StringComparison.OrdinalIgnoreCase) && e.SptId != "gravity").ToList();
            BossLocationSpawn tpl;
            string[] zones;
            if (own.Count > 0)
            {
                tpl = own.FirstOrDefault(e => string.IsNullOrEmpty(e.TriggerName)) ?? own[0];
                zones = own.SelectMany(e => (e.BossZone ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    .Distinct(StringComparer.Ordinal).ToArray();
            }
            else if (templates != null && templates.TryGetValue(role, out var t)) { tpl = t; zones = newZones ?? []; }
            else continue;
            result.Add(tpl with
            {
                BossChance = chance, BossZone = string.Join(",", zones),
                BossEscortAmount = Range(Math.Max(0, sp.ExtraMin - 1), Math.Max(0, Math.Max(sp.ExtraMin, sp.ExtraMax) - 1)),
                TriggerId = Gravity.Shared.WaveMark.ExtraSpecial(mapId, role), TriggerName = "", Time = SpecialTime(sp.XFrom, sp.XTo, rnd, raidSec), Delay = 0, IgnoreMaxBots = false, ForceSpawn = false, SptId = "gravity"
            });
        }
    }

    /// <summary>
    /// 레이더·로그 원래 항목(트리거 없는 것)의 한 판 총인원을 max로 자른다(1.0.1). 서버에서 확률을 미리 굴려 나올 무리는 100%,
    /// 안 나올 무리는 0%로 정하고, 인원(우두머리 1 + 호위)이 넘치면 마지막 무리 호위를 줄이거나 뺀다. 순서는 섞어서 앞 항목만 유리하지 않게.
    /// 레버·탈출구 같은 이벤트 항목은 플레이어가 일으키는 것이라 세지 않고 그대로 둔다.
    /// </summary>
    internal static void CapVanillaGroups(List<BossLocationSpawn> list, string role, int max, Random rnd)
    {
        var idx = Enumerable.Range(0, list.Count).Where(i => string.Equals(list[i].BossName, role, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(list[i].TriggerName) && (list[i].BossChance ?? 0) > 0 && list[i].SptId != "gravity").OrderBy(_ => rnd.Next()).ToList();
        int total = 0;
        foreach (var i in idx)
        {
            var e = list[i];
            if (rnd.NextDouble() * 100 >= (e.BossChance ?? 0) || total >= max) { list[i] = e with { BossChance = 0 }; continue; }
            var opts = (e.BossEscortAmount ?? "0").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var n) ? Math.Max(0, n) : 0).DefaultIfEmpty(0).ToArray();
            int escorts = Math.Min(opts[rnd.Next(opts.Length)], max - total - 1);
            total += 1 + escorts;
            list[i] = e with { BossChance = 100, BossEscortAmount = escorts.ToString() };
        }
    }

    internal static string Range(int min, int max) =>
        string.Join(",", Enumerable.Range(min, Math.Max(0, max - min) + 1));

    /// <summary>
    /// PMC 분대 하나의 BossZone. 쉼표 목록은 쓰지 않는다: 클라는 목록에서 구역 하나를 무작위로 골라 레이드 끝까지
    /// 그 구역만 재시도한다(kb 2.62).
    /// - 구역을 다 켰으면(다 껐으면 다 켠 것으로 봄) "" = 클라가 자기 PMC 구역(HasPmcBotSpawns) 전부를 섞어 차례로 시도
    ///   (11-09 세관 확인 판 18/18). 클라에 PMC 구역이 없는 맵(공장: BotZone 지점이 전부 일반 Bot)은 대기열에 쌓이지 않고 바로 버려진다
    ///   — 이름을 직접 넣으면 같은 구역을 3초마다 영원히 재시도하므로 그보다 낫다(11-09 공장 시험). 그런 맵의 PMC는 0.2 클라 플러그인 몫.
    /// - 일부만 켰으면 켠 구역 이름 하나씩을 분대 순서대로 돌려 준다(all은 BotPmc 지점이 있는 구역 목록, MapCatalog).
    /// </summary>
    internal static string PmcZoneFor(string[] all, MapSettings ms, int groupIndex)
    {
        var on = all.Where(z => !ms.Zones.TryGetValue(z, out var v) || v).ToArray();
        if (on.Length == 0 || on.Length == all.Length) return "";
        return on[groupIndex % on.Length];
    }

    /// <summary>맵 구역 중 웹에서 끈 것을 뺀다. 다 꺼지면 전부 쓴다.</summary>
    internal static string Zones(string[] all, MapSettings ms)
    {
        var on = all.Where(z => !ms.Zones.TryGetValue(z, out var v) || v).ToArray();
        return string.Join(",", on.Length > 0 ? on : all);
    }
}
