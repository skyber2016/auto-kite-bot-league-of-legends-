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

        // Atomic: move cursor to target AND send KeyDown in a single SendInput syscall.
        // This eliminates the race where the game reads cursor before it reaches the target.
        InputSimulator.SendMoveAndKeyDown(targetX, targetY, attackScancode);

        // Humanized key hold duration
        await Task.Delay(TimingJitter.Apply(settings.KeyHoldBaseMs, settings.KeyHoldJitterMs));
        InputSimulator.Keyboard.KeyUp(attackScancode);

        // Restore cursor to original position
        if (settings.EnableCursorRestore)
            InputSimulator.SetCursorPosition(savedPos.X, savedPos.Y);

        return true;
    }
}
