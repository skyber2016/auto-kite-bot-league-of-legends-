namespace OddAutoWalker.Strategies;

/// <summary>
/// Manual Mode (C Key): Only attacks when a target is detected.
/// </summary>
public sealed class ManualOrbWalkStrategy : IOrbWalkStrategy
{
    public bool TryAttack(bool hasTarget, int targetX, int targetY, ushort attackScancode)
    {
        if (!hasTarget) return false;

        InputSimulator.SetCursorPosition(targetX, targetY);
        InputSimulator.SendAttackClick(attackScancode);
        return true;
    }
}
