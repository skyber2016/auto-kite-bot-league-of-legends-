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

        // Move cursor to target (smooth or instant)
        if (settings.EnableSmoothCursor)
            await InputSimulator.MoveCursorSmoothAsync(savedPos.X, savedPos.Y, targetX, targetY, settings.CursorSteps, settings.CursorMoveMs);
        else
            InputSimulator.SetCursorPosition(targetX, targetY);

        // Key press with humanized hold duration
        InputSimulator.Keyboard.KeyDown(attackScancode);
        await Task.Delay(TimingJitter.Apply(settings.KeyHoldBaseMs, settings.KeyHoldJitterMs));
        InputSimulator.Keyboard.KeyUp(attackScancode);

        // Restore cursor
        if (settings.EnableCursorRestore)
            InputSimulator.SetCursorPosition(savedPos.X, savedPos.Y);

        return true;
    }
}
