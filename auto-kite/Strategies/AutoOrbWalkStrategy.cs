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

        try
        {
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
        }
        finally
        {
            // Always restore cursor, even if an exception occurred
            if (settings.EnableCursorRestore && savedPos.HasValue)
            {
                InputSimulator.SetCursorPosition(savedPos.Value.X, savedPos.Value.Y);
                // Verify restore succeeded — retry once if cursor didn't move back
                var check = MouseHelper.GetCursorPosition();
                if (Math.Abs(check.X - savedPos.Value.X) > 5 || Math.Abs(check.Y - savedPos.Value.Y) > 5)
                    InputSimulator.SetCursorPosition(savedPos.Value.X, savedPos.Value.Y);
            }
        }

        return true;
    }
}
