using System.Threading.Tasks;
using DetectColor.Input;

namespace OddAutoWalker.Strategies;

/// <summary>
/// Auto Mode (Space Key): Continuously attacks. Focuses target if detected, otherwise attacks nearest.
/// </summary>
public sealed class AutoOrbWalkStrategy : IOrbWalkStrategy
{
    public async Task<bool> TryAttackAsync(bool hasTarget, int targetX, int targetY, ushort attackScancode, Settings settings)
    {
        System.Drawing.Point? savedPos = null;

        if (hasTarget)
        {
            savedPos = MouseHelper.GetCursorPosition();

            if (settings.EnableSmoothCursor)
                await InputSimulator.MoveCursorSmoothAsync(savedPos.Value.X, savedPos.Value.Y, targetX, targetY, settings.CursorSteps, settings.CursorMoveMs);
            else
                InputSimulator.SetCursorPosition(targetX, targetY);
        }

        // Key press with humanized hold duration
        InputSimulator.Keyboard.KeyDown(attackScancode);
        await Task.Delay(TimingJitter.Apply(settings.KeyHoldBaseMs, settings.KeyHoldJitterMs));
        InputSimulator.Keyboard.KeyUp(attackScancode);

        // Restore cursor if we moved it
        if (settings.EnableCursorRestore && savedPos.HasValue)
            InputSimulator.SetCursorPosition(savedPos.Value.X, savedPos.Value.Y);

        return true;
    }
}
