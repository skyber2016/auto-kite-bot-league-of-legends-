using System;
using System.Threading;

namespace OddAutoWalker;

public static class TimingJitter
{
    private static readonly ThreadLocal<Random> Rng = new(() => new Random());

    /// <summary>
    /// Returns baseMs + uniform random in [-jitterMs, +jitterMs].
    /// </summary>
    public static int Apply(int baseMs, int jitterMs)
    {
        if (jitterMs <= 0) return baseMs;
        return baseMs + Rng.Value!.Next(-jitterMs, jitterMs + 1);
    }

    /// <summary>
    /// Returns baseMs + uniform random in [0, +jitterMs]. Never reduces below base.
    /// </summary>
    public static int ApplyPositive(int baseMs, int jitterMs)
    {
        if (jitterMs <= 0) return baseMs;
        return baseMs + Rng.Value!.Next(0, jitterMs + 1);
    }

    /// <summary>
    /// Returns a random double in [0.0, 1.0).
    /// </summary>
    public static double NextDouble() => Rng.Value!.NextDouble();
}
