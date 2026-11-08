using System.Text.Json;
using Gravity.Shared;
using Xunit;

namespace Gravity.Tests.Shared;

public class DtoJsonTests
{
    private static readonly JsonSerializerOptions Opt = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ClientRaidSettings_RoundTrip_SystemTextJson_CamelCase()
    {
        var s = new ClientRaidSettings(
            "bigmap", true, 123, 2400, 300, 600, 20, 120.5, 80, 40, 15,
            true, 60, 50, "mixed", 0.3,
            new Dictionary<string, double> { ["ZoneA"] = 2.0 },
            false,
            new GroupCapDto(true, "all", "split", new[] { new GroupCapRuleDto(5, "gte", 1) }),
            new ScavDto(true, 1, 2, 3, 4, 5, 6, 7, 8, 0.5),
            new InfDto(1, 2, 0.5, 0.1),
            new[] { new RefillGroup(true, 3, "normal") },
            new NearDto(true, 1, 2, 30, 90, 10, 1, 3, 0.5, 0.4, 5, true, true, false, 0.2, 2, 4,
                true, "exUsec", 2, "pmcBot", 1, 0.5));

        var json = JsonSerializer.Serialize(s, Opt);
        Assert.Contains("\"mapId\":\"bigmap\"", json);
        Assert.Contains("\"minDist\":30", json);

        var back = JsonSerializer.Deserialize<ClientRaidSettings>(json, Opt)!;
        Assert.Equal("bigmap", back.MapId);
        Assert.Equal(20, back.Cap);
        Assert.Equal(30, back.Near.MinDist);
        Assert.Equal(3, back.RefillGroups[0].Size);
        Assert.Equal(2.0, back.ZoneWeights["ZoneA"]);
        Assert.Equal("gte", back.GroupCap.Rules[0].Cmp);
        Assert.Equal(0.1, back.Inf!.Jitter);
    }

    [Fact]
    public void ClientRaidSettings_NullInf_RoundTrips()
    {
        var s = new ClientRaidSettings("m", false, 1, 1, 1, 1, 1, 1, 1, 1, 1, false, 0, 0, "random", 0,
            new Dictionary<string, double>(), false, new GroupCapDto(false, "all", "split", Array.Empty<GroupCapRuleDto>()),
            new ScavDto(false, 0, 0, 0, 0, 0, 0, 0, 0, 0), null, Array.Empty<RefillGroup>(),
            new NearDto(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, false, 0, 0, 0, false, "", 0, "", 0, 0));
        var back = JsonSerializer.Deserialize<ClientRaidSettings>(JsonSerializer.Serialize(s, Opt), Opt)!;
        Assert.Null(back.Inf);
    }

    [Fact]
    public void HelloAndReport_RoundTrip()
    {
        var h = JsonSerializer.Deserialize<HelloResponse>(JsonSerializer.Serialize(new HelloResponse("0.2.0", true, false), Opt), Opt)!;
        Assert.True(h.Compatible);
        var hr = JsonSerializer.Deserialize<HelloRequest>(JsonSerializer.Serialize(new HelloRequest("0.2.0", true), Opt), Opt)!;
        Assert.True(hr.AbpsClient);
        var r = new RaidReport("m", 7, new Dictionary<string, int> { ["assault"] = 3 },
            new Dictionary<string, int> { ["cap"] = 1 }, 2, new[] { new[] { 1, 2, 3, 5 } });
        var back = JsonSerializer.Deserialize<RaidReport>(JsonSerializer.Serialize(r, Opt), Opt)!;
        Assert.Equal(3, back.Spawned["assault"]);
        Assert.Equal(5, back.AlivePerMinute[0][3]);
    }
}
