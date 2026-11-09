namespace Gravity.Core;

/// <summary>모드 폴더 안의 파일 경로 모음.</summary>
public sealed class GravityPaths(string modDir)
{
    public string ModDir { get; } = modDir;
    public string ConfigDir => Path.Combine(ModDir, "config");
    public string ConfigFile => Path.Combine(ConfigDir, "gravity.json");
    public string BackupDir => Path.Combine(ConfigDir, "backup");
    public string WwwRoot => Path.Combine(ModDir, "wwwroot");
    /// <summary>내 프리셋(0.6).</summary>
    public string PresetsFile => Path.Combine(ConfigDir, "presets.json");
    /// <summary>레이드 보고·봇 장비 기록(0.6 진단).</summary>
    public string ReportsDir => Path.Combine(ConfigDir, "reports");
}
