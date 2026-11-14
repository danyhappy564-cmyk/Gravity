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
        Assert.Null(st.RuleFor("scav", 1));
        var b = Bot();
        Assert.False(BotStats.Apply(b, StatRule.Original));
        Assert.Equal(725, Max(b));
        Assert.Equal(2500, b.Skills!.Common.First().Progress);
    }

    [Fact]
    public void Pmc440_SetsEveryPart()
    {
        var b = Bot();
        Assert.True(BotStats.Apply(b, new StatRule("pmc", 1, "orig", 1, 100)));
        Assert.Equal(440, Max(b));
        Assert.Equal(35, b.Health!.BodyParts!["Head"].Health!.Maximum);
        Assert.Equal(85, b.Health.BodyParts["Chest"].Health!.Current);
        Assert.Equal(65, b.Health.BodyParts["RightLeg"].Health!.Maximum);
    }

    [Fact]
    public void Mult_ScalesMaxAndCurrent()
    {
        var b = Bot();
        BotStats.Apply(b, new StatRule("mult", 0.5, "orig", 1, 100));
        Assert.Equal(75, b.Health!.BodyParts!["Chest"].Health!.Maximum);
        Assert.Equal(70, b.Health.BodyParts["Chest"].Health!.Current);
        Assert.True(b.Health.BodyParts.Values.All(p => p.Health!.Current >= 1 && p.Health.Current <= p.Health.Maximum));
    }

    [Fact]
    public void SkillLevel_And_Pct()
    {
        var b = Bot();
        BotStats.Apply(b, new StatRule("orig", 1, "level", 1, 100));
        Assert.All(b.Skills!.Common, s => Assert.Equal(100, s.Progress));
        var c = Bot();
        BotStats.Apply(c, new StatRule("orig", 1, "pct", 1, 200));
        Assert.Equal(5000, c.Skills!.Common.First().Progress);
        var d = Bot();
        BotStats.Apply(d, new StatRule("orig", 1, "pct", 1, 300));
        Assert.Equal(5100, d.Skills!.Common.First().Progress); // 최대에서 자름
    }

    [Fact]
    public void Mastering_Untouched()
    {
        var b = Bot();
        BotStats.Apply(b, new StatRule("pmc", 1, "level", 1, 100));
        Assert.Equal(400, b.Skills!.Mastering!.First().Progress);
    }

    [Fact]
    public void MissingParts_NoError()
    {
        var b = new BotBase { Health = null, Skills = null };
        Assert.True(BotStats.Apply(b, new StatRule("pmc", 1, "level", 1, 100)));
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
    public void Config_Read_PerTier_AndGroups()
    {
        var st = Cfg("""{"stats":{"tiers":{"1":{"health":"mult","healthMult":9,"skills":"level","skillLevel":99},"7":{"health":"weird","skills":"pct","skillPct":-5}},"groups":{"boss":false}}}""").Stats;
        Assert.True(st.Any);
        Assert.Equal(new StatRule("mult", 3, "level", 51, 100), st.Tiers[0]);
        Assert.Equal(new StatRule("orig", 1, "pct", 1, 0), st.Tiers[6]);
        Assert.Equal(StatRule.Original, st.Tiers[3]);
        Assert.Null(st.RuleFor("boss", 0));          // 보스 묶음 끔
        Assert.NotNull(st.RuleFor("scav", 0));
        Assert.Null(st.RuleFor("scav", 3));          // T4는 원래대로
        Assert.NotNull(st.RuleFor("follower", 6));   // 묶음 칸이 없으면 켬
    }

    [Fact]
    public void TierOf_DressedFixedLevel()
    {
        var g = ConfigModel.Default().Gear!;
        Assert.Equal(4, BotStats.TierOf(g, "scav", 4, 10));                // 입힌 티어 우선
        Assert.Equal(1, BotStats.TierOf(g, "scav", null, 10));             // 스캐브 고정 T2
        Assert.Equal(5, BotStats.TierOf(g, "boss_killa", null, 10));       // 보스 고정 T6
        Assert.Equal(GearTier.TierOfLevel(g.Tiers, 35), BotStats.TierOf(g, "usec", null, 35)); // PMC는 레벨
    }
}
