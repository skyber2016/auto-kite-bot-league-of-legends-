using System;
using System.Threading.Tasks;

namespace OddAutoWalker.Strategies;

/// <summary>
/// Auto Mode (Space Key): Always attacks at current cursor position.
/// No target detection needed — just press attack key wherever cursor is.
/// </summary>
public sealed class AutoOrbWalkStrategy : IOrbWalkStrategy
{
    public async Task<bool> TryAttackAsync(bool hasTarget, int targetX, int targetY, ushort attackScancode, Settings settings)
    {
        // Auto mode: always attack at current cursor position, no cursor movement
        InputSimulator.Keyboard.KeyDown(attackScancode);
        await Task.Delay(TimingJitter.Apply(settings.KeyHoldBaseMs, settings.KeyHoldJitterMs));
        InputSimulator.Keyboard.KeyUp(attackScancode);

        return true;
    }
}
