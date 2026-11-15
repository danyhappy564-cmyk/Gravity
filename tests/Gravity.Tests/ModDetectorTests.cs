using Gravity.Core;
using Gravity.Spawn;
using Xunit;

namespace Gravity.Tests;

public class ModDetectorTests
{
    [Fact]
    public void Abps_LocksSpawn()
    {
        var d = ModDetector.Detect(["acidphantasm-botplacementsystem"], []);
        Assert.True(d.Abps);
        Assert.True(d.SpawnLocked);
        Assert.False(d.GearLocked);
    }

    [Fact]
    public void IsbSpecialForces_DetectedWithoutLock()
    {
        var byAssembly = ModDetector.Detect(["ISBSpecialForces"], []);
        var byFolder = ModDetector.Detect([], ["ISBSpecialForces"]);
        var aishiOnly = ModDetector.Detect(["ISBAishi"], ["ISB Aishi"]);
        Assert.True(byAssembly.Isb);
        Assert.True(byFolder.Isb);
        Assert.False(byAssembly.SpawnLocked);
        Assert.False(aishiOnly.Isb);
    }

    [Fact]
    public void NoMods_NothingLocked()
    {
        var d = ModDetector.Detect(["Gravity"], ["Gravity"]);
        Assert.False(d.SpawnLocked);
        Assert.False(d.Svm);
    }

    [Fact]
    public void SvmFolderBracketsDetected()
    {
        var d = ModDetector.Detect([], ["ORBIT", "[SVM] Server Value Modifier", "RUAFComeHomeServer", "Solarint-SAIN-ServerMod"]);
        Assert.True(d.Svm);
        Assert.Equal("[SVM] Server Value Modifier", d.SvmFolder);
        Assert.True(d.Orbit && d.Ruaf && d.Sain);
    }

    [Fact]
    public void EscortAverageAndZones()
    {
        Assert.Equal(1.4, MapCatalog.AvgEscort("1,1,1,1,1,1,2,2,2,2"), 3);
        Assert.Equal(0, MapCatalog.AvgEscort(null));
        Assert.Equal(new[] { "A", "B", "C" }, MapCatalog.Split(["B,A", null, " C , A"]));
    }
}
