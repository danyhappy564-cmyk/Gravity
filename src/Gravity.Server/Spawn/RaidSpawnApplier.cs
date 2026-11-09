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
                TriggerId = "", TriggerName = "", Delay = 0, ForceSpawn = false,
                IgnoreMaxBots = g.IsStart ? s.IgnoreCapStart : s.IgnoreCapWaves,
                Supports = null!, SpawnMode = Modes, SptId = "gravity"
            });
        }

        // 스캐브 웨이브: 클라가 있을 때만 갈아 끼운다(시작 스캐브가 0개여도 바닐라 assault는 지운다).
        var manageScavs = hasClient;
        var waves = (loc.Waves ?? []).Where(w => !manageScavs
            || (w.WildSpawnType != WildSpawnType.assault && (s.Scav.StartMarksman || w.WildSpawnType != WildSpawnType.marksman))).ToList();
        foreach (var w in plan.ScavWaves)
            waves.Add(new Wave
            {
                BotPreset = "normal", BotSide = "Savage", WildSpawnType = WildSpawnType.assault, IsPlayers = false,
                KeepZoneOnSpawn = false, SpawnPoints = scavZones, SlotsMin = w.SlotsMin, SlotsMax = w.SlotsMax,
                TimeMin = w.TimeMinSec, TimeMax = w.TimeMaxSec, SpawnMode = [.. Modes], SptId = "gravity"
            });
        if (manageScavs) for (var i = 0; i < waves.Count; i++) waves[i].Number = i;

        // BotMax는 웨이브 밖 자동 스폰의 목표치라 건드리지 않는다(4.1 클라 전체 상한은 클라 설정 — 2-2에서 클라 플러그인으로).
        loc.BossLocationSpawn = bosses;
        if (manageScavs) loc.Waves = waves;
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

        var result = new List<BossLocationSpawn>(loc.BossLocationSpawn.Count);
        var extra = new List<BossLocationSpawn>();
        foreach (var e in loc.BossLocationSpawn)
        {
            if (e.BossName is { } role && BossRoleToId.TryGetValue(role, out var id) && cfg.Bosses.TryGetValue(id, out var b))
            {
                var mapOn = !b.Maps.TryGetValue(mapId, out var on) || on;
                var chance = !b.On || !mapOn ? 0 : cfg.BossCommonOn && id != "zryachiy" ? cfg.BossCommonChance : b.Chance;
                var n = Detail(e with { BossChance = chance }, b);
                result.Add(n);
                if (b.Extra && chance > 0) extra.Add(n with { SptId = "gravity" });
                continue;
            }
            if (e.BossName is { } sr && specialByRole.TryGetValue(sr, out var sp))
            {
                var n = e;
                if (!sp.On) n = n with { BossChance = 0 };
                else
                {
                    if (sp.Chance.TryGetValue(mapId, out var c)) n = n with { BossChance = c };
                    if (sp.Max > 0) n = n with { BossEscortAmount = Range(Math.Max(0, sp.Min - 1), Math.Max(0, sp.Max - 1)) };
                }
                result.Add(n);
                continue;
            }
            result.Add(e);
        }
        // 0.4: 웹에서 이 맵을 켰는데 바닐라 항목이 없는 보스는 다른 맵 항목을 본보기로 새로 넣는다(즈랴치는 등대 섬 전용이라 제외).
        if (templates != null)
            foreach (var (role, id) in BossRoleToId)
            {
                if (id == "zryachiy" || !cfg.Bosses.TryGetValue(id, out var b) || !b.On) continue;
                if (!b.Maps.TryGetValue(mapId, out var on) || !on) continue;
                if (result.Any(x => string.Equals(x.BossName, role, StringComparison.OrdinalIgnoreCase))) continue;
                if (!templates.TryGetValue(role, out var tpl)) continue;
                var chance = cfg.BossCommonOn ? cfg.BossCommonChance : b.Chance;
                if (chance <= 0) continue;
                var zones = valid.Where(z => !z.StartsWith("ZoneSniper", StringComparison.OrdinalIgnoreCase)).ToArray();
                var n = Detail(tpl with { BossChance = chance, BossZone = string.Join(",", zones.Length > 0 ? zones : valid), TriggerId = "", TriggerName = "", SptId = "gravity" }, b);
                result.Add(n);
                if (b.Extra) result.Add(n with { });
            }
        result.AddRange(extra);
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
                waves.Add(sn.Max > 0 ? w with { SlotsMin = Math.Max(0, sn.Min), SlotsMax = sn.Max } : w);
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
