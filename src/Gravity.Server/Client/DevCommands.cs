using System.Collections.Concurrent;
using Gravity.Core;
using Gravity.Shared;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace Gravity.Client;

/// <summary>
/// 1.1.0 개발자 화면 '지금 넣어 보기': 웹 버튼 → 대기열 → 레이드 중 클라가 몇 초마다 묻는 경로(/gravity-client/poll)로 가져가 바로 넣는다.
/// 명령은 종류만 쌓고, 클라가 물을 때 그 맵 기준으로 역할·인원을 정한다. 1분 넘게 안 가져간 명령은 버린다.
/// </summary>
public static class DevCommands
{
    public static readonly string[] Kinds = ["wave", "scav", "boss", "faction", "near"];
    private static readonly ConcurrentQueue<(string Kind, DateTime At)> Pending = new();
    private static int _id;

    /// <summary>클라가 마지막으로 물은 맵과 시각(웹 안내용).</summary>
    public static string? PolledMap { get; private set; }
    public static DateTime? PolledAt { get; private set; }

    public static bool InRaid => PolledAt is { } t && (DateTime.Now - t).TotalSeconds < 15;

    public static bool Enqueue(string kind)
    {
        if (Array.IndexOf(Kinds, kind) < 0 || Pending.Count >= 20) return false;
        Pending.Enqueue((kind, DateTime.Now));
        return true;
    }

    public static void Clear() { while (Pending.TryDequeue(out _)) { } }

    /// <summary>클라가 물을 때: 쌓인 명령을 이 맵 기준으로 만든다. 만들 수 없는 명령(보스 없는 맵 등)은 Note로 남긴다.</summary>
    public static List<DevCommand> Drain(string mapId, LocationBase? loc, GravityConfig cfg, bool ruaf, Action<string>? note = null)
    {
        PolledMap = mapId;
        PolledAt = DateTime.Now;
        var list = new List<DevCommand>();
        var rnd = new Random();
        while (Pending.TryDequeue(out var p))
        {
            if ((DateTime.Now - p.At).TotalSeconds > 60) continue;
            var c = Build(p.Kind, mapId, loc, cfg, ruaf, rnd, Interlocked.Increment(ref _id));
            if (c != null) list.Add(c);
            else note?.Invoke(L.T($"지금 넣어 보기 '{p.Kind}': 이 맵({mapId})에서 넣을 수 없음", $"Spawn now '{p.Kind}': not possible on this map ({mapId})"));
        }
        return list;
    }

    internal static DevCommand? Build(string kind, string mapId, LocationBase? loc, GravityConfig cfg, bool ruaf, Random rnd, int id)
    {
        switch (kind)
        {
            case "wave":
            {
                var s = cfg.SpawnFor(mapId);
                var side = rnd.NextDouble() * 100 < s.UsecPct ? "pmcUSEC" : "pmcBEAR";
                var size = PickIndex(s.Squad, rnd) + 1;
                return new DevCommand(kind, side, side, size - 1, "normal", id);
            }
            case "scav":
            case "near":
                return new DevCommand(kind, "assault", "assault", 0, "normal", id);
            case "boss":
            {
                var b = loc?.BossLocationSpawn?.Where(e => e.BossName is { } n && n.StartsWith("boss", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.BossChance ?? 0).FirstOrDefault();
                if (b == null) return null;
                return new DevCommand(kind, b.BossName!, string.IsNullOrEmpty(b.BossEscortType) ? b.BossName! : b.BossEscortType!, FirstNumber(b.BossEscortAmount), "normal", id);
            }
            case "faction":
                if (!ruaf) return null;
                return new DevCommand(kind, Gravity.Factions.FactionRaid.RuafLeader, Gravity.Factions.FactionRaid.RuafRifleman, 3, "normal", id);
        }
        return null;
    }

    /// <summary>"2,3" 같은 인원 표기의 첫 수(없으면 0).</summary>
    internal static int FirstNumber(string? s)
    {
        var first = (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return int.TryParse(first, out var n) ? Math.Clamp(n, 0, 8) : 0;
    }

    private static int PickIndex(double[] w, Random rnd)
    {
        double sum = w.Sum();
        if (sum <= 0) return 0;
        double x = rnd.NextDouble() * sum;
        for (int i = 0; i < w.Length; i++) if ((x -= w[i]) < 0) return i;
        return w.Length - 1;
    }
}
