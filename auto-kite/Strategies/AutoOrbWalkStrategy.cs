namespace OddAutoWalker.Strategies;

/// <summary>
/// Auto Mode (Space Key): Continuously attacks. Focuses target if detected, otherwise attacks nearest.
/// </summary>
public sealed class AutoOrbWalkStrategy : IOrbWalkStrategy
{
    public bool TryAttack(bool hasTarget, int targetX, int targetY, ushort attackScancode)
    {
        if (hasTarget)
        {
            InputSimulator.SetCursorPosition(targetX, targetY);
        }

        InputSimulator.SendAttackClick(attackScancode);
        return true;
    }
}
