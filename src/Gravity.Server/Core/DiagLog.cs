using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gravity.Core;

/// <summary>
/// 진단(0.6): 로그 수준 거르기, 최근 알림 30줄(웹 호환 페이지), 레이드 보고 파일(최근 20개), 봇 장비 기록.
/// 로그 수준은 저장된 설정(diag.log)을 그때그때 읽는다: off = 경고만, summary = 알림까지, verbose = 자세한 줄까지.
/// </summary>
public static class DiagLog
{
    public const int RecentMax = 30;
    public const int ReportsKept = 20;
    public const int GearLinesMax = 2000;

    public sealed record Entry(DateTime At, string Level, string Text);

    private static readonly object Gate = new();
    private static readonly LinkedList<Entry> Recent = new();
    private static readonly List<string> GearLines = [];
    // 1.0.5: 같은 줄(필수 칸 복원 등)은 한 판에 한 번 — 반복이 2000줄 한도를 채워 봇 줄이 잘리던 것.
    private static readonly HashSet<string> GearSeen = new(StringComparer.Ordinal);
    private static bool _gearOverflow;

    /// <summary>지금 로그 수준(off·summary·verbose). 로더가 설정 저장소를 읽게 바꾼다.</summary>
    public static Func<string> Level { get; set; } = () => "summary";

    /// <summary>보고·장비 기록 폴더(config/reports). null이면 파일을 쓰지 않는다(시험).</summary>
    public static string? ReportsDir { get; set; }

    public static IReadOnlyList<Entry> RecentEntries()
    {
        lock (Gate) return Recent.Reverse().ToList();
    }

    internal static void Remember(string level, string text)
    {
        lock (Gate)
        {
            if (text.StartsWith(GravityInfo.Tag + " ", StringComparison.Ordinal)) text = text[(GravityInfo.Tag.Length + 1)..];
            Recent.AddLast(new Entry(DateTime.Now, level, text.Length > 400 ? text[..400] + "…" : text));
            while (Recent.Count > RecentMax) Recent.RemoveFirst();
        }
    }

    internal static void ClearForTest()
    {
        lock (Gate) { Recent.Clear(); GearLines.Clear(); GearSeen.Clear(); _gearOverflow = false; }
    }

    /// <summary>경고: 늘 남김.</summary>
    public static Action<string> Warn(Action<string> sink) => m => { Remember("WARN", m); sink(m); };

    /// <summary>알림: off가 아니면 남김(최근 목록에는 늘 넣음).</summary>
    public static Action<string> Info(Action<string> sink) => m => { Remember("INFO", m); if (Level() != "off") sink(m); };

    /// <summary>자세한 줄: verbose일 때만(최근 목록에도 안 넣음).</summary>
    public static Action<string> Verbose(Action<string> sink) => m => { if (Level() == "verbose") sink(m); };

    // ---------- 봇 장비 기록(diag.botLog) ----------

    /// <summary>새 레이드: 지난 판 장비 기록을 파일로 내보내고 비운다.</summary>
    public static void NewGearRaid(string header)
    {
        FlushGear();
        lock (Gate) { GearLines.Clear(); GearSeen.Clear(); _gearOverflow = false; GearLines.Add(header); }
    }

    public static void GearLine(string line)
    {
        bool flush;
        lock (Gate)
        {
            if (!GearSeen.Add(line)) return;
            if (GearLines.Count >= GearLinesMax) { _gearOverflow = true; return; }
            GearLines.Add(line);
            flush = GearLines.Count % 50 == 0;
        }
        // 클라 보고가 없는 판(스폰 꺼짐·클라 없음)에도 기록이 남게 50줄마다 쓴다(Codex 0.6 지적 12).
        if (flush) FlushGear();
    }

    /// <summary>모아 둔 장비 기록을 reports/gear-last.log로(줄이 없으면 그대로 둠).</summary>
    public static void FlushGear()
    {
        string[] lines;
        lock (Gate)
        {
            if (GearLines.Count == 0 || ReportsDir == null) return;
            lines = _gearOverflow ? [.. GearLines, $"... {GearLinesMax}줄에서 자름"] : [.. GearLines];
        }
        try
        {
            Directory.CreateDirectory(ReportsDir);
            File.WriteAllLines(Path.Combine(ReportsDir, "gear-last.log"), lines, new UTF8Encoding(false));
        }
        catch { /* 진단 파일 실패는 무시 */ }
    }

    // ---------- 레이드 보고 파일(diag.report) ----------

    /// <summary>보고 한 판을 reports/<시각>-<맵>.json으로 저장하고 오래된 것은 지운다. 저장한 파일 이름(실패면 null).</summary>
    public static string? SaveReport(string mapId, object body, DateTime? at = null)
    {
        if (ReportsDir == null) return null;
        try
        {
            Directory.CreateDirectory(ReportsDir);
            var safe = new string((mapId ?? "map").Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').ToArray());
            var name = $"{(at ?? DateTime.Now):yyyyMMdd-HHmmss}-{(safe.Length > 0 ? safe : "map")}.json";
            var text = JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            File.WriteAllText(Path.Combine(ReportsDir, name), text, new UTF8Encoding(false));
            foreach (var old in Directory.GetFiles(ReportsDir, "2*.json").OrderByDescending(f => f, StringComparer.Ordinal).Skip(ReportsKept))
                File.Delete(old);
            return name;
        }
        catch { return null; }
    }

    /// <summary>저장된 보고 파일 이름(새것부터).</summary>
    public static string[] ReportFiles() =>
        ReportsDir != null && Directory.Exists(ReportsDir)
            ? Directory.GetFiles(ReportsDir, "2*.json").Select(Path.GetFileName).OfType<string>().OrderByDescending(f => f, StringComparer.Ordinal).ToArray()
            : [];
}
