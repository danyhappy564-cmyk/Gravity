using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Gear;
using Gravity.Shared;
using Gravity.Spawn;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using Xunit;

namespace Gravity.Tests;

[Collection("DiagLog")]
public class ReleaseCTests
{
    [Theory]
    [InlineData("separate", true)]
    [InlineData("vanilla", true)]
    [InlineData("respect", false)]
    public void 추가_특수봇은_마지막_상한_처리에도_원본과_구분됩니다(string mode, bool originalIgnore)
    {
        var state = JsonNode.Parse("""{"spawn":{"caps":{"special":{"mode":"separate"}}},"specials":{"raiders":{"extra":{"laboratory":100},"extraMin":3,"extraMax":3}}}""")!;
        state["spawn"]!["caps"]!["special"]!["mode"] = mode;
        var cfg = ConfigModel.FromState(state, []);
        var loc = new LocationBase { Id = "laboratory", BossLocationSpawn = [new BossLocationSpawn {
            BossName = "pmcBot", BossEscortAmount = "2", BossChance = 50, BossZone = "Gate", TriggerName = "interactObject", TriggerId = "lever", IgnoreMaxBots = true }] };
        RaidSpawnApplier.ApplyBosses(loc, cfg, "laboratory");
        CapApplier.Apply(loc, cfg.Spawn.CapsOrDefault, null);
        Assert.Equal(2, loc.BossLocationSpawn.Count);
        Assert.Equal("lever", loc.BossLocationSpawn[0].TriggerId);
        Assert.Equal(originalIgnore, loc.BossLocationSpawn[0].IgnoreMaxBots);
        Assert.False(loc.BossLocationSpawn[1].IgnoreMaxBots);
        Assert.StartsWith("gravity:extra-special:", loc.BossLocationSpawn[1].TriggerId);

    }

    [Fact]
    public void 필수칸_블랙리스트_복원은_파일과_웹_진단에_남습니다()
    {
        const string id = "000000000000000000000072";
        var cfg = ConfigModel.FromState(JsonNode.Parse($$$"""{"blacklist":{"entries":[{"id":"{{{id}}}","bots":["all"]}]}}"""), []).Gear!;
        var items = new GearItems(new Dictionary<string, ItemInfo>(), []);
        var ctx = GearApply.Context(cfg, new GearPick("usec", "pmc", 0, true), items, GearRaid.Pick(items, false, 0, 0, 0, 1), false, new Random(1));
        var dir = Path.Combine(Path.GetTempPath(), "gravity-c-" + Guid.NewGuid());
        var old = DiagLog.ReportsDir;
        try
        {
            DiagLog.ClearForTest(); DiagLog.ReportsDir = dir;
            var set = new HashSet<MongoId> { id };
            GearApply.FilterModSet(set, "soft_armor_front", ctx, true);
            DiagLog.FlushGear();
            Assert.Single(set);
            Assert.Contains(DiagLog.RecentEntries(), e => e.Level == "WARN" && e.Text.Contains(id) && e.Text.Contains("soft_armor_front"));
            Assert.Contains(id, File.ReadAllText(Path.Combine(dir, "gear-last.log")));
        }
        finally { DiagLog.ReportsDir = old; if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
