using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gravity.Core;

public sealed record LoadResult(bool Ok, string? Error);
public sealed record SaveResult(bool Ok, string? Error, List<string> Clamped);

/// <summary>설정 파일 하나(웹 state 전체)를 읽고 저장한다. 저장은 임시 파일 → 교체, 백업 5개.</summary>
public sealed class GravityConfigStore(string configFile, string backupDir)
{
    private const int MaxBackups = 5;
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public JsonNode? RawState { get; private set; }
    public GravityConfig Current { get; private set; } = ConfigModel.Default();
    public string? LoadWarning { get; private set; }

    public LoadResult Load()
    {
        lock (_lock)
        {
            if (!File.Exists(configFile)) { RawState = null; Current = ConfigModel.Default(); return new LoadResult(true, null); }
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(configFile, Encoding.UTF8));
                RawState = root?["state"] ?? throw new JsonException("state 칸이 없습니다");
                Current = ConfigModel.FromState(RawState, []);
                return new LoadResult(true, null);
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(backupDir);
                var broken = Path.Combine(backupDir, $"broken-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
                File.Move(configFile, broken);
                RawState = null;
                Current = ConfigModel.Default();
                LoadWarning = $"설정 파일이 깨져 기본값으로 시작했습니다. 원래 파일: {broken} ({ex.Message})";
                return new LoadResult(false, LoadWarning);
            }
        }
    }

    public SaveResult Save(JsonNode state)
    {
        if (state is not JsonObject) return new SaveResult(false, "state는 JSON 객체여야 합니다", []);
        var clamped = new List<string>();
        var cfg = ConfigModel.FromState(state, clamped);
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configFile)!);
            Directory.CreateDirectory(backupDir);
            if (File.Exists(configFile))
            {
                File.Copy(configFile, Path.Combine(backupDir, $"gravity-{DateTime.Now:yyyyMMdd-HHmmss-fffffff}.json"), true);
                foreach (var old in Directory.GetFiles(backupDir, "gravity-*.json").OrderByDescending(f => f, StringComparer.Ordinal).Skip(MaxBackups))
                    File.Delete(old);
            }
            var doc = new JsonObject
            {
                ["format"] = "gravity-config",
                ["version"] = 1,
                ["saved"] = DateTime.Now.ToString("s"),
                ["state"] = state.DeepClone()
            };
            var tmp = configFile + ".tmp";
            File.WriteAllText(tmp, doc.ToJsonString(Pretty), new UTF8Encoding(false));
            File.Move(tmp, configFile, true);
            RawState = state.DeepClone();
            Current = cfg;
            LoadWarning = null;
        }
        return new SaveResult(true, null, clamped);
    }
}
