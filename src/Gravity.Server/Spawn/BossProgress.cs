using System.Text.Json;
using Gravity.Core;

namespace Gravity.Spawn;

/// <summary>
/// 1.1.0 보스 점증·점감(웹 bossProg). On: 안 나오면 다음 판 확률 +Step. Regressive: 나오면 다음 판 −Step(아니면 원래 확률로 돌아감).
/// 확률은 Min~Max 안(원래 확률이 범위 밖이면 그 확률까지 넓힘). 1.1.3부터 기본 꺼짐, 설정 판 113 전 저장본도 꺼짐(화면 기본값 '켜짐'은 서버가 안 읽던 값).
/// </summary>
public sealed record BossProgSettings(bool On = false, double Step = 10, double Min = 20, double Max = 100, bool Regressive = false);

/// <summary>1.1.0 보스 공통 난이도 분포(웹 bossDiff, 보스별 난이도 '공통 분포'인 보스에 씀). 합이 0이면 normal.</summary>
public sealed record BossDiffDist(double Easy = 0, double Normal = 60, double Hard = 30, double Impossible = 10)
{
    public string Pick(Random rnd)
    {
        double sum = Easy + Normal + Hard + Impossible;
        if (sum <= 0) return "normal";
        double x = rnd.NextDouble() * sum;
        if ((x -= Easy) < 0) return "easy";
        if ((x -= Normal) < 0) return "normal";
        if ((x -= Hard) < 0) return "hard";
        return "impossible";
    }
}

/// <summary>
/// 보스 점증·점감 상태: 보스|맵 → 원래 확률에 더할 값. 실제 레이드 시작 때만 굴리고 바꾼다(미리 보기·시뮬레이션은 읽기만).
/// 파일: config/boss-progress.json. 못 읽으면 빈 상태.
/// </summary>
public sealed class BossProgress
{
    private readonly Dictionary<string, double> _offset = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    public string? File { get; init; }
    public string? LastError { get; private set; }

    public static string Key(string bossId, string mapId) => bossId + "|" + mapId;

    public double OffsetOf(string key) { lock (_lock) return _offset.GetValueOrDefault(key); }

    /// <summary>
    /// 이번 판 확률: 원래 확률이 0이면 0(끈 보스·끈 맵), 아니면 원래+누적을 Min~Max 안으로.
    /// 1.1.3: 범위는 원래 확률을 늘 포함한다 — 누적 0이면 정한 확률 그대로(1.1.2까지는 5% 보스가 Min 20 때문에 첫 판부터 20%).
    /// </summary>
    public double Effective(BossProgSettings p, string key, double baseChance) =>
        baseChance <= 0 || !p.On ? baseChance : Math.Clamp(baseChance + OffsetOf(key), Math.Min(Math.Min(p.Min, p.Max), baseChance), Math.Max(Math.Max(p.Min, p.Max), baseChance));

    /// <summary>판 결과 기록: 안 나옴 → +Step, 나옴 → 점감이면 −Step(이미 음수면 더 내림), 아니면 0으로.</summary>
    public void Record(BossProgSettings p, string key, bool spawned)
    {
        lock (_lock)
        {
            var cur = _offset.GetValueOrDefault(key);
            var next = spawned ? (p.Regressive ? Math.Min(cur, 0) - p.Step : 0) : Math.Max(cur, 0) + p.Step;
            next = Math.Clamp(next, -100, 100);
            if (next == 0) _offset.Remove(key); else _offset[key] = next;
        }
    }

    public void Reset() { lock (_lock) _offset.Clear(); }

    public IReadOnlyDictionary<string, double> Snapshot() { lock (_lock) return new Dictionary<string, double>(_offset); }

    public static BossProgress Load(string? file)
    {
        var p = new BossProgress { File = file };
        try
        {
            if (file != null && System.IO.File.Exists(file) && JsonSerializer.Deserialize<Dictionary<string, double>>(System.IO.File.ReadAllText(file)) is { } d)
                foreach (var (k, v) in d) if (!double.IsNaN(v) && k.Length < 80) p._offset[k] = Math.Clamp(v, -100, 100);
        }
        catch { /* 빈 상태로 */ }
        return p;
    }

    public void Save()
    {
        if (File == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(Snapshot()));
        }
        catch (Exception ex) { LastError = ex.Message; }
    }
}
