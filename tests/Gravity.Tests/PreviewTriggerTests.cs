using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Factions;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Eft.Common;
using Xunit;

namespace Gravity.Tests;

/// <summary>다른 모드의 조건부 이벤트는 실제 발생이 확정된 항목처럼 미리보기에서 세지 않습니다.</summary>
public class PreviewTriggerTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public PreviewTriggerTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;
    private static GravityRuntime Runtime(string dir, string map, double chance = 25)
    {
        var store = new GravityConfigStore(Path.Combine(dir, "state.json"), Path.Combine(dir, "backup"));
        var raw = JsonNode.Parse("""{"spawn":{"startMode":"count","startPmcMin":0,"startPmcMax":0},"maps":{"bigmap":{"style":"start"},"woods":{"style":"start"}},"factions":{"blackdiv":{"managed":true,"maps":{"bigmap":true,"woods":false},"bd":{"chance":25}}}}""")!;
        raw["factions"]!["blackdiv"]!["bd"]!["chance"] = chance;
        Assert.True(store.Save(raw).Ok);
        var catalog = new MapCatalog();
        catalog.AddForTest(new MapInfo(map, 12, 20, 2400, ["ZoneA"], ["ZoneS"]));
        var rt = new GravityRuntime(store, catalog, ModDetector.Detect([], ["BlackDivServer"]), _ => { }, _ => { })
        {
            Factions = new FactionData { BlackDivMaps = ["bigmap"] },
            CloneBase = id => new LocationBase
            {
                Id = id, EscapeTimeLimit = 40, Waves = [], BossLocationSpawn =
                [
                    new BossLocationSpawn { BossName = "blackDivAssault", BossChance = 25, TriggerId = "hunt", TriggerName = "", Time = 600 },
                    new BossLocationSpawn { BossName = "ISBBossCommander", BossChance = 100, TriggerId = "isb_secret_massacre_hunt", TriggerName = "botEvent", Time = -1 },
                    new BossLocationSpawn { BossName = "blackDivAssault", BossChance = 100, TriggerId = "isb_secret_bd_counterattack", TriggerName = "botEvent", Time = -1 }
                ]
            }
        };
        return rt;
    }

    [Fact]
    public void EventCandidatesDoNotMakeTwentyFivePercentHuntersAppearEveryRaid()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gravity-preview-" + Guid.NewGuid().ToString("N"));
        try
        {
            var rt = Runtime(dir, "bigmap");
            var rows = Enumerable.Range(0, 10).SelectMany(i => rt.Simulate("bigmap", 50, 100 + i)!.Rows).ToArray();
            var appeared = rows.Count(r => r.Specials.Contains("blackdiv"));
            _output.WriteLine($"Customs: ordinary Black Division {appeared}/{rows.Length}; conditional events retained as candidates");
            Assert.InRange(appeared, 95, 155);
            Assert.DoesNotContain(rows, r => r.Specials.Contains("ISBBossCommander"));
            Assert.All(rows, r => { Assert.Contains("ISBBossCommander", r.Conditional!); Assert.Contains("blackdiv", r.Conditional!); });
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void DisabledWoodsHuntersDoNotReappearThroughAnEventEntry()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gravity-preview-" + Guid.NewGuid().ToString("N"));
        try
        {
            var rows = Runtime(dir, "woods").Simulate("woods", 10, 7)!.Rows;
            _output.WriteLine($"Woods: ordinary Black Division {rows.Count(r => r.Specials.Contains("blackdiv"))}/{rows.Count}");
            Assert.All(rows, row => Assert.DoesNotContain("blackdiv", row.Specials));
            Assert.All(rows, row => Assert.DoesNotContain("ISBBossCommander", row.Specials));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    private static GravityRuntime.SimRow RollEntries(params BossLocationSpawn[] entries)
    {
        var cfg = ConfigModel.Default();
        var map = new MapInfo("woods", 12, 20, 2400, ["ZoneA"], ["ZoneS"]);
        var plan = new SpawnPlan("woods", 2400, 0, 7, "start", [], [], 24, 0);
        var build = new GravityRuntime.RaidBuild(plan, map, null, null, 0, null, null);
        return GravityRuntime.Roll(new LocationBase { Id = "woods", BossLocationSpawn = entries.ToList() }, build, cfg, "woods", false, "original", new Random(7));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("NONE")]
    [InlineData(" none ")]
    public void HuntIdWithoutEventTriggerRemainsAnOrdinaryTimedSpawn(string? trigger)
    {
        var row = RollEntries(new BossLocationSpawn { BossName = "unmanagedTimedBot", BossChance = 100, TriggerId = "hunt", TriggerName = trigger });
        Assert.Contains("unmanagedTimedBot", row.Specials);
        Assert.Empty(row.Conditional!);
    }

    [Theory]
    [InlineData("botEvent")]
    [InlineData("byQuest")]
    [InlineData("interactObject")]
    public void NamedEventsAreCandidatesWithoutAssumingTheyFire(string trigger)
    {
        var row = RollEntries(new BossLocationSpawn { BossName = "unmanagedEventBot", BossChance = 100, TriggerId = "event", TriggerName = trigger });
        Assert.Empty(row.Specials);
        Assert.Contains("unmanagedEventBot", row.Conditional!);
    }

    [Fact]
    public void ZeroChanceEventsAreNotEvenCandidates()
    {
        var row = RollEntries(new BossLocationSpawn { BossName = "unmanagedEventBot", BossChance = 0, TriggerName = "botEvent" });
        Assert.Empty(row.Specials); Assert.Empty(row.Conditional!);
    }

    [Fact]
    public void WedgeAndDivisionHuntersHaveDistinctResultLabels()
    {
        var row = RollEntries(new BossLocationSpawn { BossName = "blackDivAssault", BossChance = 100 },
            new BossLocationSpawn { BossName = "bossWedge", BossChance = 100 });
        Assert.Contains("blackdiv", row.Specials); Assert.Contains("wedge", row.Specials);
    }
}
