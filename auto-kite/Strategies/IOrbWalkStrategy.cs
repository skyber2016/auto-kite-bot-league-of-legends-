namespace OddAutoWalker.Strategies;

public interface IOrbWalkStrategy
{
    /// <summary>
    /// Executes attack logic depending on the active mode.
    /// Returns true if an attack was triggered, or false if skipped.
    /// </summary>
    bool TryAttack(bool hasTarget, int targetX, int targetY, ushort attackScancode);
}
