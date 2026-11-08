namespace Gravity.Core;

/// <summary>모드 폴더 안의 파일 경로 모음.</summary>
public sealed class GravityPaths(string modDir)
{
    public string ModDir { get; } = modDir;
    public string ConfigDir => Path.Combine(ModDir, "config");
    public string ConfigFile => Path.Combine(ConfigDir, "gravity.json");
    public string BackupDir => Path.Combine(ConfigDir, "backup");
    public string WwwRoot => Path.Combine(ModDir, "wwwroot");
}
