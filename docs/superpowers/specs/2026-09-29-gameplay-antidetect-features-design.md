# Gameplay Enhancement & Anti-Detection Features — Design Spec

**Date:** 2026-09-29
**Status:** Approved
**Scope:** 10 new features across 2 categories (Gameplay, Anti-Detection)

---

## Overview

This spec defines 10 new features to improve orb-walk precision, kite effectiveness, and input humanization for the auto-kite bot. Features are ordered by implementation priority.

## Current State

- Orb-walk loop: deterministic 1ms tick, fixed `WindupBufferMs=66ms`, fixed `MinInputDelayMs=75ms`
- Attack key hardcoded to `DIK_H` scancode
- Cursor teleports to target instantly via `SetCursorPosition()`, never restored
- All input events sent as batched syscalls (0ms key hold time)
- Single color detection, nearest-cluster-to-cursor selection per frame

---

## Feature 1: Cursor Restore (Snap-back)

**Category:** Gameplay
**Impact:** ★★★★★ | **Effort:** Low

### Problem
`AutoOrbWalkStrategy` and `ManualOrbWalkStrategy` call `SetCursorPosition(targetX, targetY)` then `SendAttackClick()` but never restore cursor to its original position. The player loses cursor control during kiting.

### Design
- Before moving cursor to target, save current position via `MouseHelper.GetCursorPosition()` (already exists in `DetectColor.Input`)
- After `SendAttackClick()`, call `SetCursorPosition(savedX, savedY)` to restore
- Add reference to `DetectColor` project from `auto-kite` (already exists)

### Settings
- `EnableCursorRestore` : `bool` (default `true`)

### Files Changed
- `AutoOrbWalkStrategy.cs` — save/restore cursor around attack
- `ManualOrbWalkStrategy.cs` — save/restore cursor around attack
- `Settings.cs` — add `EnableCursorRestore`

---

## Feature 2: Randomized Input Timing (Jitter)

**Category:** Anti-Detection
**Impact:** ★★★★★ | **Effort:** Low

### Problem
All timing is deterministic (variance = 0). Statistical analysis can trivially detect bot-like patterns.

### Design
New static class `TimingJitter`:
```csharp
public static class TimingJitter
{
    private static readonly ThreadLocal<Random> _rng = new(() => new Random());

    /// Returns baseMs + uniform random in [-jitterMs, +jitterMs]
    public static int Apply(int baseMs, int jitterMs)
    {
        if (jitterMs <= 0) return baseMs;
        return baseMs + _rng.Value!.Next(-jitterMs, jitterMs + 1);
    }

    /// Returns baseMs + uniform random in [0, +jitterMs] (additive only)
    public static int ApplyPositive(int baseMs, int jitterMs)
    {
        if (jitterMs <= 0) return baseMs;
        return baseMs + _rng.Value!.Next(0, jitterMs + 1);
    }

    public static double NextDouble() => _rng.Value!.NextDouble();
}
```

### Application Points
1. **Move delay** in `ExecuteOrbWalkTick`: `TimingJitter.Apply(MinInputDelayMs, InputJitterMs)`
2. **Windup buffer** in `GetBufferedWindupDuration`: `TimingJitter.ApplyPositive(WindupBufferMs, WindupJitterMs)` — positive-only to never cancel animation early

### Settings
- `InputJitterMs` : `int` (default `15`)
- `WindupJitterMs` : `int` (default `10`)

### Files Changed
- New: `TimingJitter.cs`
- `Program.cs` — use jitter in timing calculations
- `Settings.cs` — add jitter settings

---

## Feature 3: Configurable Attack Move Scancode

**Category:** Gameplay
**Impact:** ★★★★☆ | **Effort:** Very Low

### Problem
`Program.cs:358` hardcodes `(ushort)DirectInputKeys.DIK_H`. Players who bind "Player Attack Move Click" to A/X cannot use the bot.

### Design
- Add `AttackMoveScancode` to `Settings` (default `0x23` = `DIK_H`)
- Replace hardcoded reference in `Program.cs`

### Settings
- `AttackMoveScancode` : `int` (default `0x23`)

### Files Changed
- `Settings.cs` — add property
- `Program.cs` — replace hardcoded scancode

---

## Feature 4: Variable Key Hold Duration

**Category:** Anti-Detection
**Impact:** ★★★★☆ | **Effort:** Low

### Problem
`SendAttackClick()` sends KeyDown+KeyUp in a single `SendInput` call — 0ms hold time. Human key press duration is 50-120ms. This is an obvious bot fingerprint.

### Design
- Change `IOrbWalkStrategy.TryAttack()` signature: `bool` → `Task<bool>` (async)
- In strategies, replace `SendAttackClick(scancode)` with:
  ```
  Keyboard.KeyDown(scancode)
  await Task.Delay(TimingJitter.Apply(KeyHoldBaseMs, KeyHoldJitterMs))
  Keyboard.KeyUp(scancode)
  ```
- Similarly for move-click in `ExecuteOrbWalkTick`:
  ```
  Mouse.MouseDown(Right)
  await Task.Delay(TimingJitter.Apply(ClickHoldBaseMs, ClickHoldJitterMs))
  Mouse.MouseUp(Right)
  ```
- `ExecuteOrbWalkTick` becomes `async Task`
- `OrbWalkLoopAsync` awaits it

### Settings
- `KeyHoldBaseMs` : `int` (default `40`)
- `KeyHoldJitterMs` : `int` (default `30`) → range 10..70ms
- `ClickHoldBaseMs` : `int` (default `30`)
- `ClickHoldJitterMs` : `int` (default `20`) → range 10..50ms

### Files Changed
- `IOrbWalkStrategy.cs` — `Task<bool> TryAttack(...)` (async)
- `AutoOrbWalkStrategy.cs` — async implementation
- `ManualOrbWalkStrategy.cs` — async implementation
- `Program.cs` — async `ExecuteOrbWalkTick`, async move-click
- `Settings.cs` — add hold settings

---

## Feature 5: Adaptive Windup Buffer

**Category:** Gameplay
**Impact:** ★★★★★ | **Effort:** Medium

### Problem
`WindupBufferMs = 66ms` is static. At 2.0+ AS (late game), total attack interval is ~500ms, windup ~150ms, so 66ms buffer consumes nearly half the move window — kiting becomes sluggish.

### Design
Scale buffer inversely with attack speed:
```csharp
public static double GetBufferedWindupDuration()
{
    double scaleFactor = ChampionAttackSpeedRatio / Math.Max(0.3, ClientAttackSpeed);
    double adaptiveBufferMs = Math.Max(
        CurrentSettings.MinWindupBufferMs,
        CurrentSettings.WindupBufferMs * scaleFactor
    );
    // Apply positive jitter (Feature 2)
    double jitteredBufferMs = TimingJitter.ApplyPositive((int)adaptiveBufferMs, CurrentSettings.WindupJitterMs);
    return GetWindupDuration() + (jitteredBufferMs / 1000.0);
}
```

At base AS (0.625): `scaleFactor ≈ 1.0` → buffer ≈ 66ms (no change)
At 2.0 AS: `scaleFactor ≈ 0.31` → buffer ≈ max(15, 20.5) = 20.5ms
At 2.5 AS: `scaleFactor ≈ 0.25` → buffer ≈ max(15, 16.5) = 16.5ms

### Settings
- `MinWindupBufferMs` : `int` (default `15`)

### Files Changed
- `Program.cs` — update `GetBufferedWindupDuration()`
- `Settings.cs` — add `MinWindupBufferMs`

---

## Feature 6: Humanized Cursor Movement

**Category:** Anti-Detection
**Impact:** ★★★★☆ | **Effort:** Medium

### Problem
`SetCursorPosition()` teleports the cursor instantly from point A to point B in one frame — impossible for a human.

### Design
New async method in `InputSimulator`:
```csharp
public static async Task MoveCursorSmoothAsync(int fromX, int fromY, int toX, int toY, int steps, int totalMs)
{
    for (int i = 1; i <= steps; i++)
    {
        double t = (double)i / steps;
        int x = (int)(fromX + (toX - fromX) * t + TimingJitter.Apply(0, 2)); // ±2px noise
        int y = (int)(fromY + (toY - fromY) * t + TimingJitter.Apply(0, 2));
        SetCursorPosition(x, y);
        if (i < steps)
            await Task.Delay(Math.Max(1, totalMs / steps));
    }
}
```

Used in strategies (already async from Feature 4):
```csharp
if (EnableSmoothCursor)
    await InputSimulator.MoveCursorSmoothAsync(savedX, savedY, targetX, targetY, CursorSteps, CursorMoveMs);
else
    InputSimulator.SetCursorPosition(targetX, targetY);
```

### Settings
- `EnableSmoothCursor` : `bool` (default `true`)
- `CursorSteps` : `int` (default `3`)
- `CursorMoveMs` : `int` (default `8`) — total time for interpolation

### Files Changed
- `InputSimulator.cs` — add `MoveCursorSmoothAsync`
- `AutoOrbWalkStrategy.cs` — use smooth cursor
- `ManualOrbWalkStrategy.cs` — use smooth cursor
- `Settings.cs` — add smooth cursor settings

---

## Feature 7: Kite Direction Control

**Category:** Gameplay
**Impact:** ★★★★☆ | **Effort:** Medium

### Problem
With cursor restore (Feature 1), move-click goes toward the player's original cursor position — usually correct for manual kiting. But in Auto mode, the player may not be actively positioning their cursor.

### Design
Optional "auto-kite away" for Auto mode only:
- When `AutoKiteDirection = true` and a target exists:
  1. Calculate vector from target → player cursor (original position before snap)
  2. Normalize and extend by `KiteDistance` pixels
  3. Move cursor to that point before issuing move-click
- When `AutoKiteDirection = false` or no target: move-click at current cursor position (default behavior)

```csharp
// In move phase of ExecuteOrbWalkTick, Auto mode only:
if (AutoKiteDirection && hasTarget)
{
    double dx = cursorX - tx;
    double dy = cursorY - ty;
    double len = Math.Sqrt(dx * dx + dy * dy);
    if (len > 1)
    {
        int moveX = cursorX + (int)(dx / len * KiteDistance);
        int moveY = cursorY + (int)(dy / len * KiteDistance);
        InputSimulator.SetCursorPosition(moveX, moveY);
    }
}
SendMoveClick(); // or async version
// Restore cursor after move if needed
```

### Settings
- `AutoKiteDirection` : `bool` (default `false`)
- `KiteDistance` : `int` (default `200`)

### Files Changed
- `Program.cs` — kite direction logic in move phase
- `Settings.cs` — add kite settings

---

## Feature 8: Target Lock / Sticky Target

**Category:** Gameplay
**Impact:** ★★★☆☆ | **Effort:** Medium

### Problem
Detection loop selects the nearest cluster to cursor every frame. When 2 targets are close together, the selected target flickers between them causing missed attacks.

### Design
- Track `_lastTargetCenter` (Point) and `_targetLockFrames` (int) in `Program.cs`
- In `DetectionLoopAsync`, after finding clusters:
  1. If `_lastTargetCenter` exists and a cluster is within `TargetStickyRadius` of it → prefer that cluster (keep lock)
  2. Only switch to a new cluster if it's closer to cursor AND the old cluster is gone or further than `TargetStickyRadius`
  3. Reset lock when `HasDetectedTarget` goes false

### Settings
- `TargetStickyRadius` : `int` (default `50`) — pixels

### Files Changed
- `Program.cs` — sticky target logic in detection loop
- `Settings.cs` — add `TargetStickyRadius`

---

## Feature 9: Input Pattern Scrambling

**Category:** Anti-Detection
**Impact:** ★★★☆☆ | **Effort:** Low

### Problem
The attack-move pattern is 100% deterministic: Attack → Windup → Move → Move → ... → Attack. Zero variance in structure.

### Design
In `ExecuteOrbWalkTick` move phase, add probabilistic noise:
```csharp
double roll = TimingJitter.NextDouble();
if (roll < CurrentSettings.SkipMoveChance)
{
    // Skip this move — champion stands still for one tick
    return;
}

// Normal move
await SendMoveClickAsync();

if (roll > 1.0 - CurrentSettings.ExtraMoveChance)
{
    await Task.Delay(TimingJitter.Apply(15, 10)); // small gap
    await SendMoveClickAsync(); // extra click — like spam right-click
}
```

### Settings
- `SkipMoveChance` : `double` (default `0.07`) — 7% chance to skip a move
- `ExtraMoveChance` : `double` (default `0.05`) — 5% chance for extra move click

### Files Changed
- `Program.cs` — scrambling logic in move phase
- `Settings.cs` — add chance settings

---

## Feature 10: Configurable dwExtraInfo

**Category:** Anti-Detection
**Impact:** ★★★☆☆ | **Effort:** Very Low

### Problem
All input events use `GetMessageExtraInfo()` for `dwExtraInfo`. If anti-cheat inspects this field, all bot inputs share the same fingerprint.

### Design
- Add `ExtraInfoMode` setting: `"native"` (default, uses `GetMessageExtraInfo()`), `"zero"` (`IntPtr.Zero`)
- Add `InputSimulator.GetExtraInfo()` helper that reads the setting and returns the appropriate value
- Replace all `GetMessageExtraInfo()` calls in `InputSimulator` with `GetExtraInfo()`

### Settings
- `ExtraInfoMode` : `string` (default `"native"`)

### Files Changed
- `InputSimulator.cs` — add `GetExtraInfo()`, replace calls
- `Settings.cs` — add `ExtraInfoMode`

---

## New Files Summary

| File | Purpose |
|------|---------|
| `auto-kite/TimingJitter.cs` | Thread-safe random jitter utility |

## Settings Summary (all new properties)

| Setting | Type | Default | Feature |
|---------|------|---------|---------|
| `EnableCursorRestore` | bool | true | 1 |
| `InputJitterMs` | int | 15 | 2 |
| `WindupJitterMs` | int | 10 | 2 |
| `AttackMoveScancode` | int | 0x23 | 3 |
| `KeyHoldBaseMs` | int | 40 | 4 |
| `KeyHoldJitterMs` | int | 30 | 4 |
| `ClickHoldBaseMs` | int | 30 | 4 |
| `ClickHoldJitterMs` | int | 20 | 4 |
| `MinWindupBufferMs` | int | 15 | 5 |
| `EnableSmoothCursor` | bool | true | 6 |
| `CursorSteps` | int | 3 | 6 |
| `CursorMoveMs` | int | 8 | 6 |
| `AutoKiteDirection` | bool | false | 7 |
| `KiteDistance` | int | 200 | 7 |
| `TargetStickyRadius` | int | 50 | 8 |
| `SkipMoveChance` | double | 0.07 | 9 |
| `ExtraMoveChance` | double | 0.05 | 9 |
| `ExtraInfoMode` | string | "native" | 10 |

## Architecture Impact

The main architectural change is converting `IOrbWalkStrategy.TryAttack()` from synchronous `bool` to asynchronous `Task<bool>`. This cascades to:
- Both strategy implementations become async
- `ExecuteOrbWalkTick` becomes `async Task`
- `OrbWalkLoopAsync` awaits the tick instead of calling it synchronously

This is necessary for Features 4 (key hold delay) and 6 (smooth cursor). The async overhead is negligible — the orb-walk loop already runs on a dedicated thread via `Task.Run`.

## Testing Strategy

- `TimingJitter` — unit test: output is within expected range, thread safety
- `Settings` — extend existing `SettingsTests.cs` for new properties serialization
- `Strategy` — extend existing `OrbWalkStrategyTests.cs` for async behavior
- Cursor restore, kite direction, sticky target — manual in-game testing (these depend on live screen capture)
