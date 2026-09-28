namespace OddAutoWalker.Strategies;

public enum OrbWalkMode
{
    None,
    Manual,
    Auto
}

public static class OrbWalkStrategyFactory
{
    private static readonly IOrbWalkStrategy Manual = new ManualOrbWalkStrategy();
    private static readonly IOrbWalkStrategy Auto = new AutoOrbWalkStrategy();

    public static IOrbWalkStrategy? Create(OrbWalkMode mode) => mode switch
    {
        OrbWalkMode.Manual => Manual,
        OrbWalkMode.Auto => Auto,
        _ => null
    };
}
