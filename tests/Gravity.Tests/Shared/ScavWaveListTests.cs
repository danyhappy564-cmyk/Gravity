using System;
using System.Linq;
using Gravity.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Gravity.Tests.Shared;

/// <summary>1.0.6 스캐브 웨이브 목록 판 일정(ScavWaveList): 흔들림·60초 재시도·스캐브 레이드·하드 스톱.</summary>
public class ScavWaveListTests
{
    private static ScavWaveDto W(double sec, int min, int max) => new(sec, min, max);

    [Fact]
    public void Due_OnTime_WithJitterRange()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var l = new ScavWaveList([W(300, 3, 3)], 20, 0, 10000, new Random(seed));
            Assert.Equal(1, l.Count);
            Assert.InRange(l.Times[0], 280, 320);
            Assert.Equal(0, l.Due(l.Times[0] - 0.5, 10));
            Assert.Equal(3, l.Due(l.Times[0], 10));
            Assert.Equal(0, l.Due(l.Times[0] + 2, 10));
        }
    }

    [Fact]
    public void Due_RoomLimited_CarriesThenDropsAfter60s()
    {
        var l = new ScavWaveList([W(100, 5, 5)], 0, 0, 10000, new Random(1));
        Assert.Equal(2, l.Due(100, 2));
        Assert.Equal(3, l.Waiting);
        Assert.Equal(0, l.Due(102, 0));
        Assert.Equal(1, l.Due(150, 1));
        Assert.Equal(0, l.Due(161, 10));
        Assert.Equal(2, l.Dropped);
        Assert.Equal(0, l.Waiting);
    }

    [Fact]
    public void Waves_NotMergedAfterDrop()
    {
        var l = new ScavWaveList([W(100, 4, 4), W(200, 3, 3)], 0, 0, 10000, new Random(1));
        Assert.Equal(0, l.Due(100, 0));
        Assert.Equal(3, l.Due(200, 20)); // 앞 웨이브 4는 버리고 이 웨이브 3만
        Assert.Equal(4, l.Dropped);
    }

    [Fact]
    public void ScavRaid_PastWavesSkipped()
    {
        var l = new ScavWaveList([W(300, 2, 2), W(900, 4, 4)], 0, 600, 10000, new Random(1));
        Assert.Equal(1, l.Count);
        Assert.Equal(0, l.Due(0, 20));
        Assert.Equal(0, l.Due(299, 20));
        Assert.Equal(4, l.Due(300, 20));
        Assert.Equal(0, l.Dropped);
    }

    [Fact]
    public void HardStop_And_Zero_Skipped()
    {
        var l = new ScavWaveList([W(100, 0, 0), W(200, 2, 2), W(2000, 5, 5)], 0, 0, 1500, new Random(1));
        Assert.Equal(1, l.Count);
        Assert.Equal(2, l.Due(200, 20));
        Assert.Equal(0, l.Due(5000, 20));
        Assert.Equal(0, l.Dropped);
    }

    [Fact]
    public void GiveBack_NotPlaced_RetriedInSameWave()
    {
        var l = new ScavWaveList([W(100, 3, 3), W(300, 2, 2)], 0, 0, 10000, new Random(1));
        Assert.Equal(3, l.Due(100, 10));
        l.GiveBack(2); // 지점이 없어 2마리 못 넣음
        Assert.Equal(2, l.Waiting);
        Assert.Equal(1, l.Spawned);
        Assert.Equal(2, l.Due(130, 10));
        Assert.Equal(2, l.Due(300, 10));
        Assert.Equal(0, l.Dropped);
        l.GiveBack(5); // 낸 수보다 많이 돌려도 그 웨이브 몫까지만
        Assert.Equal(2, l.Waiting);
    }

    [Fact]
    public void Empty_NoError()
    {
        var l = new ScavWaveList([], 20, 0, 1500, new Random(1));
        Assert.Equal(0, l.Count);
        Assert.Equal(0, l.Due(100, 20));
        Assert.Contains("0", l.Describe());
    }

    [Fact]
    public void SameSeed_SameSchedule()
    {
        ScavWaveDto[] list = [W(60, 1, 5), W(400, 2, 8), W(900, 0, 6)];
        var a = new ScavWaveList(list, 30, 0, 10000, new Random(77));
        var b = new ScavWaveList(list, 30, 0, 10000, new Random(77));
        Assert.Equal(a.Times, b.Times);
        Assert.Equal(a.Counts, b.Counts);
        Assert.All(a.Counts, n => Assert.InRange(n, 1, 8));
    }

    private static readonly JsonSerializerSettings Wire = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false, OverrideSpecifiedNames = true }
        }
    };

    [Fact]
    public void Old_ScavDto_Json_ListNull()
    {
        var old = """{"managed":true,"activeMin":1,"activeMax":2,"quietMin":3,"quietMax":4,"refillSec":5,"pending":6,"softCap":7,"perPlayer":8,"pscavChance":0.5,"style":"waves"}""";
        var back = JsonConvert.DeserializeObject<ScavDto>(old, Wire)!;
        Assert.Null(back.List);
        Assert.Equal(0, back.ListJitterSec);

        var withList = new ScavDto(true, 1, 2, 3, 4, 5, 6, 7, 8, 0.5, Style: "start", List: [W(120, 2, 4)], ListJitterSec: 20);
        var round = JsonConvert.DeserializeObject<ScavDto>(JsonConvert.SerializeObject(withList, Wire), Wire)!;
        Assert.Equal(120, round.List![0].TimeSec);
        Assert.Equal(4, round.List[0].Max);
        Assert.Equal(20, round.ListJitterSec);
    }
}
