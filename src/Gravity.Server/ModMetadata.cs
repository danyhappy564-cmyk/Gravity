using SPTarkov.Server.Core.Models.Spt.Mod;

namespace Gravity;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.rf.gravity";
    public string Name { get; init; } = "Gravity";
    public string Author { get; init; } = "R_F";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new(GravityInfo.Version);
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/danyhappy564-cmyk/Gravity";
    public string License { get; init; } = "MIT";
}

public static class GravityInfo
{
    public const string Version = "0.6.0";
    public const string Tag = "[Gravity]";
}
