using System;
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

            // Move cursor to target
            InputSimulator.SetCursorPosition(targetX, targetY);

            // Small delay to ensure game sees the new cursor position
            await Task.Delay(5);
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
