using System.Threading.Tasks;

namespace OddAutoWalker.Strategies;

public interface IOrbWalkStrategy
{
    /// <summary>
    /// Executes attack logic depending on the active mode.
    /// Returns true if an attack was triggered, or false if skipped.
    /// </summary>
    Task<bool> TryAttackAsync(bool hasTarget, int targetX, int targetY, ushort attackScancode, Settings settings);

    /// <summary>
    /// Executes attack logic synchronously for backwards compatibility with callers.
    /// </summary>
    bool TryAttack(bool hasTarget, int targetX, int targetY, ushort attackScancode)
        => TryAttackAsync(hasTarget, targetX, targetY, attackScancode, new Settings()).GetAwaiter().GetResult();
}
