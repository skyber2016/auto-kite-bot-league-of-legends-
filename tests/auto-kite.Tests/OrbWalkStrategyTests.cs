namespace OddAutoWalker.Tests;

using System;
using System.Threading.Tasks;
using OddAutoWalker.Strategies;
using Xunit;

public class OrbWalkStrategyTests
{
    [Fact]
    public void Factory_CreateManual_ReturnsManualStrategy()
    {
        var strategy = OrbWalkStrategyFactory.Create(OrbWalkMode.Manual);
        Assert.NotNull(strategy);
        Assert.IsType<ManualOrbWalkStrategy>(strategy);
    }

    [Fact]
    public void Factory_CreateAuto_ReturnsAutoStrategy()
    {
        var strategy = OrbWalkStrategyFactory.Create(OrbWalkMode.Auto);
        Assert.NotNull(strategy);
        Assert.IsType<AutoOrbWalkStrategy>(strategy);
    }

    [Fact]
    public void Factory_CreateNone_ReturnsNull()
    {
        var strategy = OrbWalkStrategyFactory.Create(OrbWalkMode.None);
        Assert.Null(strategy);
    }

    [Fact]
    public async Task ManualStrategy_WithoutTarget_ReturnsFalse()
    {
        var strategy = new ManualOrbWalkStrategy();
        var settings = new Settings();
        bool result = await strategy.TryAttackAsync(hasTarget: false, targetX: 0, targetY: 0, attackScancode: 0x1E, settings: settings);
        Assert.False(result);
    }

    [Fact]
    public void GetExtraInfo_ZeroMode_ReturnsZero()
    {
        var settings = new Settings { ExtraInfoMode = "zero" };
        var extraInfo = InputSimulator.GetExtraInfo(settings);
        Assert.Equal(UIntPtr.Zero, extraInfo);
    }

    [Fact]
    public void GetExtraInfo_NativeMode_DoesNotThrow()
    {
        var settings = new Settings { ExtraInfoMode = "native" };
        var extraInfo = InputSimulator.GetExtraInfo(settings);
        // Native mode calls Win32 GetMessageExtraInfo() without throwing
    }
}
