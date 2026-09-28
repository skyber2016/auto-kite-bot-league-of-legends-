namespace OddAutoWalker.Tests;

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
    public void ManualStrategy_WithoutTarget_ReturnsFalse()
    {
        var strategy = new ManualOrbWalkStrategy();
        bool result = strategy.TryAttack(hasTarget: false, targetX: 0, targetY: 0, attackScancode: 0x1E);
        Assert.False(result);
    }
}
