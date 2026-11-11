using Gravity.Shared;
using Xunit;

namespace Gravity.Tests.Shared;

public class ScavWaveMarkTests
{
    [Theory]
    [InlineData("ZoneCenter")]
    [InlineData("BotZone")]
    [InlineData("")]
    public void StartWaveCountAndZoneSurviveWireFormat(string zone)
    {
        Assert.True(ScavWaveMark.TryDecode(ScavWaveMark.Encode(1,1,zone),out var min,out var max,out var decoded));
        Assert.Equal(1,min);Assert.Equal(1,max);Assert.Equal(zone,decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ZoneCenter")]
    [InlineData("ZoneCenter,ZoneOLI")]
    [InlineData("~gravity.scav/-1/1/ZoneCenter")]
    [InlineData("~gravity.scav/2/1/ZoneCenter")]
    [InlineData("~gravity.scav/1/4097/ZoneCenter")]
    [InlineData("~gravity.scav/1/1")]
    public void NativeOrMalformedWavesAreNotTreatedAsManagedStarts(string? text) =>
        Assert.False(ScavWaveMark.TryDecode(text,out _,out _,out _));
}
