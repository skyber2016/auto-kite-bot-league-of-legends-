# Gameplay Enhancement & Anti-Detection Features — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 10 features across gameplay precision (cursor restore, configurable attack key, adaptive windup, kite direction, sticky target) and anti-detection (timing jitter, key hold duration, smooth cursor, pattern scrambling, dwExtraInfo config).

**Architecture:** New `TimingJitter` utility class provides thread-safe randomization used across all timing paths. `IOrbWalkStrategy.TryAttack` becomes async `Task<bool>` to support key hold delays and smooth cursor. All new settings are added to `Settings.cs` with `Load()` deserialization.

**Tech Stack:** .NET 10, C#, xUnit, Win32 SendInput, DXGI

**Spec:** `docs/superpowers/specs/2026-09-29-gameplay-antidetect-features-design.md`

## Global Constraints

- Target framework: `net10.0-windows`
- All input via Win32 `SendInput` with DirectInput scancodes
- No new NuGet dependencies
- Settings serialized via Newtonsoft.Json to `settings.json`
- `Settings.Load()` uses manual property assignment — every new property must be added to both the class body AND the `Load()` method
- Existing tests must continue to pass (update assertions for changed defaults if needed)
- Commit after each task

---

### Task 1: TimingJitter Utility

**Files:**
- Create: `auto-kite/TimingJitter.cs`
- Test: `tests/auto-kite.Tests/TimingJitterTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces:
  - `TimingJitter.Apply(int baseMs, int jitterMs) → int` (symmetric jitter)
  - `TimingJitter.ApplyPositive(int baseMs, int jitterMs) → int` (additive-only jitter)
  - `TimingJitter.NextDouble() → double` (0.0..1.0)

- [ ] **Step 1: Write failing tests**

```csharp
// tests/auto-kite.Tests/TimingJitterTests.cs
namespace OddAutoWalker.Tests;

using Xunit;

public class TimingJitterTests
{
    [Fact]
    public void Apply_ZeroJitter_ReturnsBase()
    {
        int result = TimingJitter.Apply(100, 0);
        Assert.Equal(100, result);
    }

    [Fact]
    public void Apply_WithJitter_ReturnsValueInRange()
    {
        for (int i = 0; i < 200; i++)
        {
            int result = TimingJitter.Apply(100, 20);
            Assert.InRange(result, 80, 120);
        }
    }

    [Fact]
    public void ApplyPositive_WithJitter_ReturnsValueInPositiveRange()
    {
        for (int i = 0; i < 200; i++)
        {
            int result = TimingJitter.ApplyPositive(50, 10);
            Assert.InRange(result, 50, 60);
        }
    }

    [Fact]
    public void ApplyPositive_ZeroJitter_ReturnsBase()
    {
        int result = TimingJitter.ApplyPositive(50, 0);
        Assert.Equal(50, result);
    }

    [Fact]
    public void NextDouble_ReturnsValueBetweenZeroAndOne()
    {
        for (int i = 0; i < 100; i++)
        {
            double result = TimingJitter.NextDouble();
            Assert.InRange(result, 0.0, 1.0);
        }
    }

    [Fact]
    public void Apply_NegativeJitter_ReturnsBase()
    {
        int result = TimingJitter.Apply(100, -5);
        Assert.Equal(100, result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/auto-kite.Tests --filter "FullyQualifiedName~TimingJitterTests" --no-build 2>&1; dotnet test tests/auto-kite.Tests --filter "FullyQualifiedName~TimingJitterTests" -v n`
Expected: Build error — `TimingJitter` does not exist

- [ ] **Step 3: Implement TimingJitter**

```csharp
// auto-kite/TimingJitter.cs
using System;
using System.Threading;

namespace OddAutoWalker;

public static class TimingJitter
{
    private static readonly ThreadLocal<Random> Rng = new(() => new Random());

    /// <summary>
    /// Returns baseMs + uniform random in [-jitterMs, +jitterMs].
    /// </summary>
    public static int Apply(int baseMs, int jitterMs)
    {
        if (jitterMs <= 0) return baseMs;
        return baseMs + Rng.Value!.Next(-jitterMs, jitterMs + 1);
    }

    /// <summary>
    /// Returns baseMs + uniform random in [0, +jitterMs]. Never reduces below base.
    /// </summary>
    public static int ApplyPositive(int baseMs, int jitterMs)
    {
        if (jitterMs <= 0) return baseMs;
        return baseMs + Rng.Value!.Next(0, jitterMs + 1);
    }

    /// <summary>
    /// Returns a random double in [0.0, 1.0).
    /// </summary>
    public static double NextDouble() => Rng.Value!.NextDouble();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/auto-kite.Tests --filter "FullyQualifiedName~TimingJitterTests" -v n`
Expected: All 6 tests PASS

- [ ] **Step 5: Commit**

```bash
git add auto-kite/TimingJitter.cs tests/auto-kite.Tests/TimingJitterTests.cs
git commit -m "feat(auto-kite): add TimingJitter utility for randomized input timing"
```

---

### Task 2: Settings — Add All New Properties

**Files:**
- Modify: `auto-kite/Settings.cs`
- Modify: `tests/auto-kite.Tests/SettingsTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces: 18 new properties on `Settings` class (see list below)

- [ ] **Step 1: Write failing test for new settings defaults**

Add to `tests/auto-kite.Tests/SettingsTests.cs`:

```csharp
[Fact]
public void DefaultSettings_HaveCorrectNewFeatureValues()
{
    var settings = new Settings();

    // Feature 1: Cursor Restore
    Assert.True(settings.EnableCursorRestore);

    // Feature 2: Timing Jitter
    Assert.Equal(15, settings.InputJitterMs);
    Assert.Equal(10, settings.WindupJitterMs);

    // Feature 3: Attack Move Scancode
    Assert.Equal(0x23, settings.AttackMoveScancode);

    // Feature 4: Key Hold Duration
    Assert.Equal(40, settings.KeyHoldBaseMs);
    Assert.Equal(30, settings.KeyHoldJitterMs);
    Assert.Equal(30, settings.ClickHoldBaseMs);
    Assert.Equal(20, settings.ClickHoldJitterMs);

    // Feature 5: Adaptive Windup
    Assert.Equal(15, settings.MinWindupBufferMs);

    // Feature 6: Smooth Cursor
    Assert.True(settings.EnableSmoothCursor);
    Assert.Equal(3, settings.CursorSteps);
    Assert.Equal(8, settings.CursorMoveMs);

    // Feature 7: Kite Direction
    Assert.False(settings.AutoKiteDirection);
    Assert.Equal(200, settings.KiteDistance);

    // Feature 8: Sticky Target
    Assert.Equal(50, settings.TargetStickyRadius);

    // Feature 9: Pattern Scrambling
    Assert.Equal(0.07, settings.SkipMoveChance, 2);
    Assert.Equal(0.05, settings.ExtraMoveChance, 2);

    // Feature 10: ExtraInfo
    Assert.Equal("native", settings.ExtraInfoMode);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/auto-kite.Tests --filter "FullyQualifiedName~DefaultSettings_HaveCorrectNewFeatureValues" -v n`
Expected: Build error — properties do not exist

- [ ] **Step 3: Add new properties to Settings.cs**

Add after the existing `AttackSpeedPollMs` property in `auto-kite/Settings.cs`:

```csharp
// --- New Feature Settings ---

// Feature 1: Cursor Restore
public bool EnableCursorRestore { get; set; } = true;

// Feature 2: Timing Jitter
public int InputJitterMs { get; set; } = 15;
public int WindupJitterMs { get; set; } = 10;

// Feature 3: Configurable Attack Key (DirectInput scancode, 0x23 = DIK_H)
public int AttackMoveScancode { get; set; } = 0x23;

// Feature 4: Key Hold Duration (ms)
public int KeyHoldBaseMs { get; set; } = 40;
public int KeyHoldJitterMs { get; set; } = 30;
public int ClickHoldBaseMs { get; set; } = 30;
public int ClickHoldJitterMs { get; set; } = 20;

// Feature 5: Adaptive Windup Buffer (ms)
public int MinWindupBufferMs { get; set; } = 15;

// Feature 6: Humanized Cursor Movement
public bool EnableSmoothCursor { get; set; } = true;
public int CursorSteps { get; set; } = 3;
public int CursorMoveMs { get; set; } = 8;

// Feature 7: Kite Direction (Auto mode only)
public bool AutoKiteDirection { get; set; } = false;
public int KiteDistance { get; set; } = 200;

// Feature 8: Sticky Target
public int TargetStickyRadius { get; set; } = 50;

// Feature 9: Input Pattern Scrambling
public double SkipMoveChance { get; set; } = 0.07;
public double ExtraMoveChance { get; set; } = 0.05;

// Feature 10: dwExtraInfo mode ("native" or "zero")
public string ExtraInfoMode { get; set; } = "native";
```

- [ ] **Step 4: Update Settings.Load() to read new properties**

In the `Load()` method of `Settings.cs`, add after `AttackSpeedPollMs = loaded.AttackSpeedPollMs;`:

```csharp
TargetOffsetX = loaded.TargetOffsetX;
TargetOffsetY = loaded.TargetOffsetY;
EnableCursorRestore = loaded.EnableCursorRestore;
InputJitterMs = loaded.InputJitterMs;
WindupJitterMs = loaded.WindupJitterMs;
AttackMoveScancode = loaded.AttackMoveScancode;
KeyHoldBaseMs = loaded.KeyHoldBaseMs;
KeyHoldJitterMs = loaded.KeyHoldJitterMs;
ClickHoldBaseMs = loaded.ClickHoldBaseMs;
ClickHoldJitterMs = loaded.ClickHoldJitterMs;
MinWindupBufferMs = loaded.MinWindupBufferMs;
EnableSmoothCursor = loaded.EnableSmoothCursor;
CursorSteps = loaded.CursorSteps;
CursorMoveMs = loaded.CursorMoveMs;
AutoKiteDirection = loaded.AutoKiteDirection;
KiteDistance = loaded.KiteDistance;
TargetStickyRadius = loaded.TargetStickyRadius;
SkipMoveChance = loaded.SkipMoveChance;
ExtraMoveChance = loaded.ExtraMoveChance;
ExtraInfoMode = loaded.ExtraInfoMode;
```

- [ ] **Step 5: Write and run serialization round-trip test**

Add to `SettingsTests.cs`:

```csharp
[Fact]
public void CreateNew_WithNewSettings_LoadReadsBack()
{
    string tempDir = Path.Combine(Path.GetTempPath(), "auto-kite-test-" + Guid.NewGuid().ToString("N"));
    string settingsFile = Path.Combine(tempDir, "settings.json");

    try
    {
        var settings = new Settings
        {
            EnableCursorRestore = false,
            InputJitterMs = 25,
            WindupJitterMs = 5,
            AttackMoveScancode = 0x1E, // DIK_A
            KeyHoldBaseMs = 50,
            KeyHoldJitterMs = 20,
            ClickHoldBaseMs = 40,
            ClickHoldJitterMs = 15,
            MinWindupBufferMs = 20,
            EnableSmoothCursor = false,
            CursorSteps = 5,
            CursorMoveMs = 12,
            AutoKiteDirection = true,
            KiteDistance = 300,
            TargetStickyRadius = 80,
            SkipMoveChance = 0.10,
            ExtraMoveChance = 0.08,
            ExtraInfoMode = "zero"
        };

        settings.CreateNew(settingsFile);
        var loaded = new Settings();
        loaded.Load(settingsFile);

        Assert.False(loaded.EnableCursorRestore);
        Assert.Equal(25, loaded.InputJitterMs);
        Assert.Equal(5, loaded.WindupJitterMs);
        Assert.Equal(0x1E, loaded.AttackMoveScancode);
        Assert.Equal(50, loaded.KeyHoldBaseMs);
        Assert.Equal(20, loaded.KeyHoldJitterMs);
        Assert.Equal(40, loaded.ClickHoldBaseMs);
        Assert.Equal(15, loaded.ClickHoldJitterMs);
        Assert.Equal(20, loaded.MinWindupBufferMs);
        Assert.False(loaded.EnableSmoothCursor);
        Assert.Equal(5, loaded.CursorSteps);
        Assert.Equal(12, loaded.CursorMoveMs);
        Assert.True(loaded.AutoKiteDirection);
        Assert.Equal(300, loaded.KiteDistance);
        Assert.Equal(80, loaded.TargetStickyRadius);
        Assert.Equal(0.10, loaded.SkipMoveChance, 2);
        Assert.Equal(0.08, loaded.ExtraMoveChance, 2);
        Assert.Equal("zero", loaded.ExtraInfoMode);
    }
    finally
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }
}
```

- [ ] **Step 6: Run all settings tests**

Run: `dotnet test tests/auto-kite.Tests --filter "FullyQualifiedName~SettingsTests" -v n`
Expected: All tests PASS (including existing ones — update `DefaultSettings_HaveCorrectValues` if needed for any changed defaults)

- [ ] **Step 7: Commit**

```bash
git add auto-kite/Settings.cs tests/auto-kite.Tests/SettingsTests.cs
git commit -m "feat(auto-kite): add 18 new settings for gameplay and anti-detection features"
```

---

### Task 3: Async Strategy Interface + Cursor Restore + Smooth Cursor + Key Hold

**Files:**
- Modify: `auto-kite/Strategies/IOrbWalkStrategy.cs`
- Modify: `auto-kite/Strategies/ManualOrbWalkStrategy.cs`
- Modify: `auto-kite/Strategies/AutoOrbWalkStrategy.cs`
- Modify: `auto-kite/InputSimulator.cs`
- Modify: `tests/auto-kite.Tests/OrbWalkStrategyTests.cs`

**Interfaces:**
- Consumes: `TimingJitter` (Task 1), `Settings` new properties (Task 2), existing `InputSimulator`, `MouseHelper`
- Produces:
  - `IOrbWalkStrategy.TryAttackAsync(bool hasTarget, int targetX, int targetY, ushort attackScancode, Settings settings) → Task<bool>`
  - `InputSimulator.MoveCursorSmoothAsync(int fromX, int fromY, int toX, int toY, int steps, int totalMs) → Task`
  - `InputSimulator.GetExtraInfo(Settings settings) → UIntPtr`

- [ ] **Step 1: Add MoveCursorSmoothAsync and GetExtraInfo to InputSimulator**

Add to `auto-kite/InputSimulator.cs`, after the existing `SetCursorPosition` method:

```csharp
/// <summary>
/// Moves cursor from (fromX,fromY) to (toX,toY) in multiple steps with slight noise.
/// </summary>
public static async Task MoveCursorSmoothAsync(int fromX, int fromY, int toX, int toY, int steps, int totalMs)
{
    if (steps <= 1)
    {
        SetCursorPosition(toX, toY);
        return;
    }

    int delayPerStep = Math.Max(1, totalMs / steps);
    for (int i = 1; i <= steps; i++)
    {
        double t = (double)i / steps;
        int x = (int)(fromX + (toX - fromX) * t + TimingJitter.Apply(0, 2));
        int y = (int)(fromY + (toY - fromY) * t + TimingJitter.Apply(0, 2));
        SetCursorPosition(x, y);
        if (i < steps)
            await Task.Delay(delayPerStep);
    }
}

/// <summary>
/// Returns the appropriate dwExtraInfo value based on settings.
/// </summary>
public static UIntPtr GetExtraInfo(Settings settings)
{
    return settings.ExtraInfoMode == "zero" ? UIntPtr.Zero : GetMessageExtraInfo();
}
```

Add required using at top of `InputSimulator.cs`:

```csharp
using System.Threading.Tasks;
```

- [ ] **Step 2: Replace GetMessageExtraInfo() calls in InputSimulator**

Replace all 6 occurrences of `GetMessageExtraInfo()` in the existing methods (`SendAttackClick`, `SendMoveClick`, `Keyboard.KeyDown`, `Keyboard.KeyUp`, `Mouse.MouseDown`, `Mouse.MouseUp`) with a parameter-based approach. Since `InputSimulator` is static and these methods are called from strategies that now have access to Settings, add a `Settings?` parameter with default `null`:

For `SendAttackClick` and `SendMoveClick`, add an overload that accepts `UIntPtr extraInfo`:

```csharp
public static void SendAttackClick(ushort attackScancode, UIntPtr extraInfo)
{
    Input[] inputs = new Input[2]
    {
        new Input
        {
            type = (int)InputType.Keyboard,
            u = new InputUnion
            {
                ki = new KeyboardInput
                {
                    wVk = 0,
                    wScan = attackScancode,
                    dwFlags = (uint)(KeyEventF.KeyDown | KeyEventF.Scancode),
                    dwExtraInfo = extraInfo
                }
            }
        },
        new Input
        {
            type = (int)InputType.Keyboard,
            u = new InputUnion
            {
                ki = new KeyboardInput
                {
                    wVk = 0,
                    wScan = attackScancode,
                    dwFlags = (uint)(KeyEventF.KeyUp | KeyEventF.Scancode),
                    dwExtraInfo = extraInfo
                }
            }
        }
    };
    SendInput(2, inputs, InputSize);
}
```

Keep existing `SendAttackClick(ushort)` as backward-compatible overload calling the new one with `GetMessageExtraInfo()`. Same pattern for `SendMoveClick`.

- [ ] **Step 3: Change IOrbWalkStrategy to async**

Replace `auto-kite/Strategies/IOrbWalkStrategy.cs`:

```csharp
using System.Threading.Tasks;

namespace OddAutoWalker.Strategies;

public interface IOrbWalkStrategy
{
    /// <summary>
    /// Executes attack logic depending on the active mode.
    /// Returns true if an attack was triggered, or false if skipped.
    /// </summary>
    Task<bool> TryAttackAsync(bool hasTarget, int targetX, int targetY, ushort attackScancode, Settings settings);
}
```

- [ ] **Step 4: Implement ManualOrbWalkStrategy (async + cursor restore + smooth + key hold)**

Replace `auto-kite/Strategies/ManualOrbWalkStrategy.cs`:

```csharp
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

        var extraInfo = InputSimulator.GetExtraInfo(settings);

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
```

- [ ] **Step 5: Implement AutoOrbWalkStrategy (async + cursor restore + smooth + key hold)**

Replace `auto-kite/Strategies/AutoOrbWalkStrategy.cs`:

```csharp
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
```

- [ ] **Step 6: Update existing strategy tests for async**

Replace `tests/auto-kite.Tests/OrbWalkStrategyTests.cs`:

```csharp
namespace OddAutoWalker.Tests;

using OddAutoWalker.Strategies;
using Xunit;

public class OrbWalkStrategyTests
{
    [Fact]
    public void Factory_CreateManual_ReturnsManualStrategy()
    {
        var strategy = OrbWalkStrategyFactory.Create(OrbWalkMode.Manual);
        Assert.NotNull(strategy);
        Assert.IsType<ManualOrbWalkStrategy>(strategy);
    }

    [Fact]
    public void Factory_CreateAuto_ReturnsAutoStrategy()
    {
        var strategy = OrbWalkStrategyFactory.Create(OrbWalkMode.Auto);
        Assert.NotNull(strategy);
        Assert.IsType<AutoOrbWalkStrategy>(strategy);
    }

    [Fact]
    public void Factory_CreateNone_ReturnsNull()
    {
        var strategy = OrbWalkStrategyFactory.Create(OrbWalkMode.None);
        Assert.Null(strategy);
    }

    [Fact]
    public async Task ManualStrategy_WithoutTarget_ReturnsFalse()
    {
        var strategy = new ManualOrbWalkStrategy();
        var settings = new Settings();
        bool result = await strategy.TryAttackAsync(hasTarget: false, targetX: 0, targetY: 0, attackScancode: 0x1E, settings: settings);
        Assert.False(result);
    }
}
```

- [ ] **Step 7: Run all strategy tests**

Run: `dotnet test tests/auto-kite.Tests --filter "FullyQualifiedName~OrbWalkStrategyTests" -v n`
Expected: All 4 tests PASS

- [ ] **Step 8: Commit**

```bash
git add auto-kite/Strategies/ auto-kite/InputSimulator.cs tests/auto-kite.Tests/OrbWalkStrategyTests.cs
git commit -m "feat(auto-kite): async strategies with cursor restore, smooth cursor, and humanized key hold"
```

---

### Task 4: Program.cs — Integrate All Features into Main Loop

**Files:**
- Modify: `auto-kite/Program.cs`

**Interfaces:**
- Consumes: `TimingJitter` (Task 1), `Settings` new properties (Task 2), async `IOrbWalkStrategy.TryAttackAsync` (Task 3), `InputSimulator.GetExtraInfo` (Task 3)
- Produces: Fully integrated main loop with all 10 features active

- [ ] **Step 1: Update ExecuteOrbWalkTick to async with all features**

Change `ExecuteOrbWalkTick` signature and body in `Program.cs`:

```csharp
private static async Task ExecuteOrbWalkTickAsync(double nextInput, double nextMove, double nextAttack,
    Action<double> setNextInput, Action<double> setNextMove, Action<double> setNextAttack)
```

Actually, since ref parameters cannot be used with async, refactor `OrbWalkLoopAsync` to hold timing state inline:

Replace `OrbWalkLoopAsync` (lines 324-338) and `ExecuteOrbWalkTick` (lines 340-374) with:

```csharp
private static async Task OrbWalkLoopAsync(CancellationToken ct)
{
    double nextInput = 0, nextMove = 0, nextAttack = 0;

    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(1, CurrentSettings.OrbWalkTickRateMs)));
    try
    {
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (!HasProcess || !HasAttackSpeedData || IsExiting || LeagueProcess == null || GetForegroundWindow() != LeagueProcess.MainWindowHandle)
                continue;

            var strategy = _currentStrategy;
            if (strategy == null) continue;

            double time = PrecisionTimer.Elapsed.TotalSeconds;
            bool hasTarget = HasDetectedTarget;
            int tx = Interlocked.CompareExchange(ref _targetX, 0, 0);
            int ty = Interlocked.CompareExchange(ref _targetY, 0, 0);

            // Attack phase: fire when cooldown is ready
            if (nextAttack <= time)
            {
                if (await strategy.TryAttackAsync(hasTarget, tx, ty, (ushort)CurrentSettings.AttackMoveScancode, CurrentSettings))
                {
                    double attackTime = PrecisionTimer.Elapsed.TotalSeconds;
                    nextMove = attackTime + GetBufferedWindupDuration();
                    nextAttack = attackTime + GetSecondsPerAttack();
                    continue; // Attack fired — wait for windup before moving
                }
                // TryAttack skipped (no target in manual mode) — fall through to move
            }

            // Move phase: kite between attacks, throttled by MinInputDelayMs + jitter
            if (nextMove <= time && nextInput <= time)
            {
                // Feature 9: Pattern Scrambling
                double roll = TimingJitter.NextDouble();
                if (roll < CurrentSettings.SkipMoveChance)
                {
                    // Skip this move — natural human pause
                    nextInput = time + (TimingJitter.Apply(CurrentSettings.MinInputDelayMs, CurrentSettings.InputJitterMs) / 1000.0);
                    continue;
                }

                // Feature 7: Kite Direction (Auto mode only)
                if (CurrentSettings.AutoKiteDirection && _currentMode == OrbWalkMode.Auto && hasTarget)
                {
                    var cursor = DetectColor.Input.MouseHelper.GetCursorPosition();
                    double dx = cursor.X - tx;
                    double dy = cursor.Y - ty;
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len > 1)
                    {
                        int moveX = cursor.X + (int)(dx / len * CurrentSettings.KiteDistance);
                        int moveY = cursor.Y + (int)(dy / len * CurrentSettings.KiteDistance);
                        InputSimulator.SetCursorPosition(moveX, moveY);
                    }
                }

                // Feature 4: Humanized move-click with hold duration
                InputSimulator.Mouse.MouseDown(InputSimulator.Mouse.Buttons.Right);
                await Task.Delay(TimingJitter.Apply(CurrentSettings.ClickHoldBaseMs, CurrentSettings.ClickHoldJitterMs));
                InputSimulator.Mouse.MouseUp(InputSimulator.Mouse.Buttons.Right);

                // Feature 9: Extra move chance
                if (roll > 1.0 - CurrentSettings.ExtraMoveChance)
                {
                    await Task.Delay(TimingJitter.Apply(15, 10));
                    InputSimulator.Mouse.MouseDown(InputSimulator.Mouse.Buttons.Right);
                    await Task.Delay(TimingJitter.Apply(CurrentSettings.ClickHoldBaseMs, CurrentSettings.ClickHoldJitterMs));
                    InputSimulator.Mouse.MouseUp(InputSimulator.Mouse.Buttons.Right);
                }

                // Feature 2: Jittered input delay
                nextInput = time + (TimingJitter.Apply(CurrentSettings.MinInputDelayMs, CurrentSettings.InputJitterMs) / 1000.0);
            }
        }
    }
    catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Update GetBufferedWindupDuration for adaptive windup (Feature 5)**

Replace the existing `GetBufferedWindupDuration` method (line 87) with:

```csharp
public static double GetBufferedWindupDuration()
{
    // Feature 5: Adaptive Windup Buffer — scales inversely with attack speed
    double scaleFactor = ChampionAttackSpeedRatio / Math.Max(0.3, ClientAttackSpeed);
    double adaptiveBufferMs = Math.Max(
        CurrentSettings.MinWindupBufferMs,
        CurrentSettings.WindupBufferMs * scaleFactor
    );

    // Feature 2: Positive-only jitter (never reduces below adaptive buffer)
    double jitteredBufferMs = TimingJitter.ApplyPositive((int)adaptiveBufferMs, CurrentSettings.WindupJitterMs);

    return GetWindupDuration() + (jitteredBufferMs / 1000.0);
}
```

- [ ] **Step 3: Update DetectionLoopAsync for Sticky Target (Feature 8)**

Add static fields near the other detection fields (around line 69):

```csharp
private static Point _lastTargetCenter;
private static bool _hasLastTarget = false;
```

In `DetectionLoopAsync`, replace the cluster selection logic (lines 254-278) with:

```csharp
if (clusters.Count > 0)
{
    ColorCluster best = clusters[0];
    double bestDistSq = double.MaxValue;

    // Feature 8: Sticky Target — prefer previous target if still visible
    bool foundSticky = false;
    if (_hasLastTarget)
    {
        foreach (var c in clusters)
        {
            int sdx = c.Center.X - _lastTargetCenter.X;
            int sdy = c.Center.Y - _lastTargetCenter.Y;
            double stickyDistSq = sdx * sdx + sdy * sdy;
            int stickyRadius = CurrentSettings.TargetStickyRadius;
            if (stickyDistSq <= stickyRadius * stickyRadius)
            {
                best = c;
                foundSticky = true;
                break;
            }
        }
    }

    // If no sticky match, fall back to nearest-to-cursor
    if (!foundSticky)
    {
        foreach (var c in clusters)
        {
            int dx = c.Center.X - cursor.X;
            int dy = c.Center.Y - cursor.Y;
            double distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = c;
            }
        }
    }

    _lastTargetCenter = best.Center;
    _hasLastTarget = true;

    int targetX = best.Center.X + CurrentSettings.TargetOffsetX;
    int targetY = best.Center.Y + CurrentSettings.TargetOffsetY;

    Interlocked.Exchange(ref _targetX, targetX);
    Interlocked.Exchange(ref _targetY, targetY);
    HasDetectedTarget = true;
}
else
{
    HasDetectedTarget = false;
    _hasLastTarget = false;
}
```

- [ ] **Step 4: Update console output to show new settings**

After the existing settings console output (around line 123), add:

```csharp
Console.WriteLine($"  Cursor Restore: {(CurrentSettings.EnableCursorRestore ? "ON" : "OFF")}");
Console.WriteLine($"  Smooth Cursor:  {(CurrentSettings.EnableSmoothCursor ? "ON" : "OFF")} ({CurrentSettings.CursorSteps} steps, {CurrentSettings.CursorMoveMs}ms)");
Console.WriteLine($"  Input Jitter:   ±{CurrentSettings.InputJitterMs}ms");
Console.WriteLine($"  Windup Jitter:  +0..{CurrentSettings.WindupJitterMs}ms");
Console.WriteLine($"  Key Hold:       {CurrentSettings.KeyHoldBaseMs}±{CurrentSettings.KeyHoldJitterMs}ms");
Console.WriteLine($"  Click Hold:     {CurrentSettings.ClickHoldBaseMs}±{CurrentSettings.ClickHoldJitterMs}ms");
Console.WriteLine($"  Attack Key:     0x{CurrentSettings.AttackMoveScancode:X2}");
Console.WriteLine($"  Min Windup:     {CurrentSettings.MinWindupBufferMs}ms");
Console.WriteLine($"  Kite Direction: {(CurrentSettings.AutoKiteDirection ? "ON" : "OFF")} ({CurrentSettings.KiteDistance}px)");
Console.WriteLine($"  Sticky Target:  {CurrentSettings.TargetStickyRadius}px");
Console.WriteLine($"  Skip Move:      {CurrentSettings.SkipMoveChance:P0}");
Console.WriteLine($"  Extra Move:     {CurrentSettings.ExtraMoveChance:P0}");
Console.WriteLine($"  ExtraInfo:      {CurrentSettings.ExtraInfoMode}");
```

- [ ] **Step 5: Build and run existing tests**

Run: `dotnet build auto-kite.sln && dotnet test tests/auto-kite.Tests -v n`
Expected: Build succeeds, all tests pass

- [ ] **Step 6: Commit**

```bash
git add auto-kite/Program.cs
git commit -m "feat(auto-kite): integrate all 10 features into main loop

- Async orb-walk loop with configurable attack scancode
- Adaptive windup buffer scaling with attack speed
- Randomized input timing jitter (move delay + windup buffer)
- Humanized move-click with key hold duration
- Kite direction control for Auto mode
- Sticky target with hysteresis radius
- Input pattern scrambling (skip/extra move)
- Configurable dwExtraInfo mode"
```

---

### Task 5: Final Verification & Documentation

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: all previous tasks
- Produces: updated documentation

- [ ] **Step 1: Run full test suite**

Run: `dotnet test auto-kite.sln -v n`
Expected: All tests pass across both `auto-kite.Tests` and `DetectColor.Tests`

- [ ] **Step 2: Build release**

Run: `dotnet build auto-kite.sln -c Release`
Expected: Clean build with 0 errors, 0 warnings

- [ ] **Step 3: Update README.md**

Add a "Features" section to `README.md` documenting:
- Dual-mode orb-walking (Manual/Auto)
- Color detection with DXGI capture
- Cursor restore (snap-back)
- Humanized input timing (jitter, key hold duration, pattern scrambling)
- Smooth cursor movement
- Adaptive windup buffer
- Auto kite direction
- Sticky target lock
- Configurable attack key and settings
- All `settings.json` options with defaults

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: update README with all new features and settings reference"
```
