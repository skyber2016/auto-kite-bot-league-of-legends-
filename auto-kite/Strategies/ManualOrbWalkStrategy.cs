using System;
using System.Threading.Tasks;
using DetectColor.Input;

namespace OddAutoWalker.Strategies;

/// <summary>
/// Manual Mode (C Key): Only attacks when a target is detected.
/// </summary>
public sealed class ManualOrbWalkStrategy : IOrbWalkStrategy
{
    public async Task<bool> TryAttackAsync(bool hasTarget, int targetX, int targetY, ushort attackScancode, Settings settings)
    {
        if (!hasTarget) return false;

        // Save cursor position for restore
        var savedPos = MouseHelper.GetCursorPosition();

        try
        {
            // Move cursor to target
            InputSimulator.SetCursorPosition(targetX, targetY);

            // Small delay to ensure game sees the new cursor position
            await Task.Delay(5);

            // Key press with humanized hold duration
            InputSimulator.Keyboard.KeyDown(attackScancode);
            await Task.Delay(TimingJitter.Apply(settings.KeyHoldBaseMs, settings.KeyHoldJitterMs));
            InputSimulator.Keyboard.KeyUp(attackScancode);
        }
        finally
        {
            // Always restore cursor, even if an exception occurred
            if (settings.EnableCursorRestore)
            {
                InputSimulator.SetCursorPosition(savedPos.X, savedPos.Y);
                // Verify restore succeeded — retry once if cursor didn't move back
                var check = MouseHelper.GetCursorPosition();
                if (Math.Abs(check.X - savedPos.X) > 5 || Math.Abs(check.Y - savedPos.Y) > 5)
                    InputSimulator.SetCursorPosition(savedPos.X, savedPos.Y);
            }
        }

        return true;
    }
}
