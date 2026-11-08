using System.Text.Json.Nodes;
using Gravity.Core;
using Xunit;

namespace Gravity.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gravity-test-" + Guid.NewGuid().ToString("N"));
    private string File_ => Path.Combine(_dir, "gravity.json");
    private string Backup => Path.Combine(_dir, "backup");

    public ConfigStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private GravityConfigStore Store() => new(File_, Backup);

    [Fact]
    public void Load_MissingFile_UsesDefaults()
    {
        var s = Store();
        var r = s.Load();
        Assert.True(r.Ok);
        Assert.Null(s.RawState);
        Assert.Equal(6, s.Current.Spawn.IntervalMin);
        Assert.True(s.Current.SpawnModule);
    }

    [Fact]
    public void Load_BrokenJson_MovesToBrokenAndDefaults()
    {
        File.WriteAllText(File_, "{ this is not json");
        var s = Store();
        var r = s.Load();
        Assert.False(r.Ok);
        Assert.False(File.Exists(File_));
        Assert.Single(Directory.GetFiles(Backup, "broken-*.json"));
        Assert.Equal(6, s.Current.Spawn.IntervalMin);
    }

    [Fact]
    public void Load_WrongTypes_FallsBackToDefaults()
    {
        var s = Store();
        var save = s.Save(JsonNode.Parse("""{"spawn":{"interval":"abc","waveMin":null}}""")!);
        Assert.True(save.Ok);
        Assert.Equal(6, s.Current.Spawn.IntervalMin);
        Assert.Equal(2, s.Current.Spawn.WaveMin);
    }

    [Fact]
    public void Save_ZeroInterval_ClampedTo0_5()
    {
        var s = Store();
        var r = s.Save(JsonNode.Parse("""{"spawn":{"interval":0}}""")!);
        Assert.True(r.Ok);
        Assert.Equal(0.5, s.Current.Spawn.IntervalMin);
        Assert.Contains("spawn.interval", r.Clamped);
    }

    [Fact]
    public void Save_KeepsUnknownKeys()
    {
        var s = Store();
        s.Save(JsonNode.Parse("""{"gear":{"x":7},"spawn":{"interval":5}}""")!);
        var s2 = Store();
        s2.Load();
        Assert.Equal(7, s2.RawState!["gear"]!["x"]!.GetValue<int>());
        Assert.Equal(5, s2.Current.Spawn.IntervalMin);
    }

    [Fact]
    public void Save_KeepsFiveBackups()
    {
        var s = Store();
        for (var i = 0; i < 8; i++) s.Save(JsonNode.Parse("{\"spawn\":{\"interval\":" + (i + 1) + "}}")!);
        Assert.Equal(5, Directory.GetFiles(Backup, "gravity-*.json").Length);
    }

    [Fact]
    public void Maps_OverrideAndCapClamped()
    {
        var s = Store();
        var r = s.Save(JsonNode.Parse("""{"maps":{"bigmap":{"override":true,"interval":3,"cap":999}}}""")!);
        var m = s.Current.MapFor("bigmap");
        Assert.True(m.Override);
        Assert.Equal(3, m.IntervalMin);
        Assert.Equal(60, m.Cap);
        Assert.Contains("maps.bigmap.cap", r.Clamped);
    }

    [Fact]
    public void Squad_AllZero_BecomesSolo()
    {
        var s = Store();
        s.Save(JsonNode.Parse("""{"spawn":{"squad":{"s1":0,"s2":0,"s3":0,"s4":0,"s5":0}}}""")!);
        Assert.Equal(new[] { 100.0, 0, 0, 0, 0 }, s.Current.Spawn.Squad);
    }
}
