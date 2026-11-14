using System.Text.Json.Nodes;
using Gravity.Core;
using Gravity.Gear;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using Xunit;

namespace Gravity.Tests;

/// <summary>1.0.6 봇 체력·스킬 맞추기(Forge #2, 안 B: 봇 묶음별).</summary>
public class BotStatsTests
{
    private static GravityConfig Cfg(string json) => ConfigModel.FromState(JsonNode.Parse(json), []);

    private static BotBase Bot() => new()
    {
        Health = new BotBaseHealth
        {
            BodyParts = new[] { ("Head", 35.0), ("Chest", 150.0), ("Stomach", 120.0), ("LeftArm", 100.0), ("RightArm", 100.0), ("LeftLeg", 110.0), ("RightLeg", 110.0) }
                .ToDictionary(p => p.Item1, p => new BodyPartHealth { Health = new CurrentMinMax { Current = p.Item2 - 10, Maximum = p.Item2 } })
        },
        Skills = new Skills
        {
            Common = new List<CommonSkill> { new() { Id = SkillTypes.Endurance, Progress = 2500 }, new() { Id = SkillTypes.Strength, Progress = 800 } },
            Mastering = new List<MasterySkill> { new() { Id = "M4", Progress = 400 } },
            Points = 0
        }
    };

    private static double Max(BotBase b) => b.Health!.BodyParts!.Values.Sum(p => p.Health!.Maximum ?? 0);

    [Fact]
    public void Default_AllOriginal_NoChange()
    {
        var st = ConfigModel.Default().Stats;
        Assert.False(st.Any);
        var b = Bot();
        Assert.False(BotStats.Apply(b, st.For("scav")));
        Assert.Equal(725, Max(b));
        Assert.Equal(2500, b.Skills!.Common.First().Progress);
    }

    [Fact]
    public void Pmc440_SetsEveryPart()
    {
        var b = Bot();
        Assert.True(BotStats.Apply(b, new StatGroup("pmc", 1, "orig", 1, 100)));
        Assert.Equal(440, Max(b));
        Assert.Equal(35, b.Health!.BodyParts!["Head"].Health!.Maximum);
        Assert.Equal(85, b.Health.BodyParts["Chest"].Health!.Current);
        Assert.Equal(65, b.Health.BodyParts["RightLeg"].Health!.Maximum);
    }

    [Fact]
    public void Mult_ScalesMaxAndCurrent()
    {
        var b = Bot();
        BotStats.Apply(b, new StatGroup("mult", 0.5, "orig", 1, 100));
        Assert.Equal(75, b.Health!.BodyParts!["Chest"].Health!.Maximum);
        Assert.Equal(70, b.Health.BodyParts["Chest"].Health!.Current);
        Assert.True(b.Health.BodyParts.Values.All(p => p.Health!.Current >= 1 && p.Health.Current <= p.Health.Maximum));
    }

    [Fact]
    public void SkillLevel_And_Pct()
    {
        var b = Bot();
        BotStats.Apply(b, new StatGroup("orig", 1, "level", 1, 100));
        Assert.All(b.Skills!.Common, s => Assert.Equal(100, s.Progress));
        var c = Bot();
        BotStats.Apply(c, new StatGroup("orig", 1, "pct", 1, 200));
        Assert.Equal(5000, c.Skills!.Common.First().Progress);
        var d = Bot();
        BotStats.Apply(d, new StatGroup("orig", 1, "pct", 1, 300));
        Assert.Equal(5100, d.Skills!.Common.First().Progress); // 최대에서 자름
    }

    [Fact]
    public void Mastering_Untouched()
    {
        var b = Bot();
        BotStats.Apply(b, new StatGroup("pmc", 1, "level", 1, 100));
        Assert.Equal(400, b.Skills!.Mastering!.First().Progress);
    }

    [Fact]
    public void MissingParts_NoError()
    {
        var b = new BotBase { Health = null, Skills = null };
        Assert.True(BotStats.Apply(b, new StatGroup("pmc", 1, "level", 1, 100)));
    }

    [Theory]
    [InlineData("assault", false, "scav")]
    [InlineData("marksman", false, "scav")]
    [InlineData("bossKilla", false, "boss")]
    [InlineData("followerBully", false, "follower")]
    [InlineData("pmcBot", false, "special")]
    [InlineData("exUsec", false, "special")]
    [InlineData("pmcUSEC", true, "pmc")]
    public void GroupOf_Roles(string role, bool pmc, string group) => Assert.Equal(group, BotStats.GroupOf(role, pmc, null));

    [Fact]
    public void GroupOf_Unknown_Null() => Assert.Null(BotStats.GroupOf("gifter", false, null));

    [Fact]
    public void Config_Read_Clamped()
    {
        var st = Cfg("""{"stats":{"scav":{"health":"mult","healthMult":9,"skills":"level","skillLevel":99},"boss":{"health":"weird","skills":"pct","skillPct":-5}}}""").Stats;
        Assert.True(st.Any);
        Assert.Equal(new StatGroup("mult", 3, "level", 51, 100), st.For("scav"));
        Assert.Equal(new StatGroup("orig", 1, "pct", 1, 0), st.For("boss"));
        Assert.Equal(new StatGroup("orig", 1, "orig", 1, 100), st.For("pmc"));
    }
}
