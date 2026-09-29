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

            // Atomic: move cursor to target AND send KeyDown in a single SendInput syscall.
            InputSimulator.SendMoveAndKeyDown(targetX, targetY, attackScancode);
        }
        else
        {
            // No target — attack-move at current cursor position
            InputSimulator.Keyboard.KeyDown(attackScancode);
        }

        // Humanized key hold duration
        await Task.Delay(TimingJitter.Apply(settings.KeyHoldBaseMs, settings.KeyHoldJitterMs));
        InputSimulator.Keyboard.KeyUp(attackScancode);

        // Restore cursor if we moved it
        if (settings.EnableCursorRestore && savedPos.HasValue)
            InputSimulator.SetCursorPosition(savedPos.Value.X, savedPos.Value.Y);

        return true;
    }
}
