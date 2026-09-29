namespace OddAutoWalker.Tests;

using Xunit;

public class TimingJitterTests
{
    [Fact]
    public void Apply_ZeroJitter_ReturnsBase()
    {
        int result = TimingJitter.Apply(100, 0);
        Assert.Equal(100, result);
    }

    [Fact]
    public void Apply_WithJitter_ReturnsValueInRange()
    {
        for (int i = 0; i < 200; i++)
        {
            int result = TimingJitter.Apply(100, 20);
            Assert.InRange(result, 80, 120);
        }
    }

    [Fact]
    public void ApplyPositive_WithJitter_ReturnsValueInPositiveRange()
    {
        for (int i = 0; i < 200; i++)
        {
            int result = TimingJitter.ApplyPositive(50, 10);
            Assert.InRange(result, 50, 60);
        }
    }

    [Fact]
    public void ApplyPositive_ZeroJitter_ReturnsBase()
    {
        int result = TimingJitter.ApplyPositive(50, 0);
        Assert.Equal(50, result);
    }

    [Fact]
    public void NextDouble_ReturnsValueBetweenZeroAndOne()
    {
        for (int i = 0; i < 100; i++)
        {
            double result = TimingJitter.NextDouble();
            Assert.InRange(result, 0.0, 1.0);
        }
    }

    [Fact]
    public void Apply_NegativeJitter_ReturnsBase()
    {
        int result = TimingJitter.Apply(100, -5);
        Assert.Equal(100, result);
    }
}
