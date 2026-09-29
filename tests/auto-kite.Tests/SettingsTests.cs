#nullable enable

namespace OddAutoWalker.Tests;

using System;
using System.IO;
using LowLevelInput.Hooks;
using Xunit;

public class SettingsTests
{
    [Fact]
    public void DefaultSettings_HaveCorrectValues()
    {
        var settings = new Settings();

        // Note: Task 7 brief mentions "ManualKey == 46", derived from design spec table row "46 (C)".
        // In LowLevelInput VirtualKeyCode enum, C is 67 (0x43), while 46 (0x2E) is Delete.
        // As defined in Settings.cs: ManualKey = (int)VirtualKeyCode.C == 67.
        Assert.Equal((int)VirtualKeyCode.C, settings.ManualKey);
        Assert.Equal(67, settings.ManualKey);
        Assert.Equal((int)VirtualKeyCode.Space, settings.AutoKey);
        Assert.Equal(32, settings.AutoKey);

        Assert.Equal(52, settings.TargetColorR);
        Assert.Equal(3, settings.TargetColorG);
        Assert.Equal(0, settings.TargetColorB);
        Assert.Equal(5, settings.ColorTolerance);

        Assert.Equal(1000, settings.CaptureSize);
        Assert.Equal(10, settings.MinClusterPixels);
        Assert.Equal(60, settings.DetectionFpsCap);

        Assert.True(settings.EnableOverlay);

        Assert.Equal(66, settings.WindupBufferMs);
        Assert.Equal(75, settings.MinInputDelayMs);
        Assert.Equal(1, settings.OrbWalkTickRateMs);
        Assert.Equal(500, settings.AttackSpeedPollMs);
    }

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

    [Fact]
    public void CreateNew_CreatesFileAndDirectory_AndLoadReadsBack()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "auto-kite-test-" + Guid.NewGuid().ToString("N"));
        string settingsFile = Path.Combine(tempDir, "settings", "settings.json");

        try
        {
            var settings = new Settings
            {
                ManualKey = 46,
                AutoKey = 32,
                TargetColorR = 220,
                TargetColorG = 10,
                TargetColorB = 20,
                ColorTolerance = 35,
                CaptureSize = 350,
                MinClusterPixels = 15,
                DetectionFpsCap = 50,
                EnableOverlay = true,
                WindupBufferMs = 70,
                MinInputDelayMs = 25,
                OrbWalkTickRateMs = 20,
                AttackSpeedPollMs = 400
            };

            settings.CreateNew(settingsFile);

            Assert.True(File.Exists(settingsFile));

            var loaded = new Settings();
            loaded.Load(settingsFile);

            Assert.Equal(46, loaded.ManualKey);
            Assert.Equal(32, loaded.AutoKey);
            Assert.Equal(220, loaded.TargetColorR);
            Assert.Equal(10, loaded.TargetColorG);
            Assert.Equal(20, loaded.TargetColorB);
            Assert.Equal(35, loaded.ColorTolerance);
            Assert.Equal(350, loaded.CaptureSize);
            Assert.Equal(15, loaded.MinClusterPixels);
            Assert.Equal(50, loaded.DetectionFpsCap);
            Assert.True(loaded.EnableOverlay);
            Assert.Equal(70, loaded.WindupBufferMs);
            Assert.Equal(25, loaded.MinInputDelayMs);
            Assert.Equal(20, loaded.OrbWalkTickRateMs);
            Assert.Equal(400, loaded.AttackSpeedPollMs);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void CreateNew_WithDefaultSettings_LoadReadsDefaults()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "auto-kite-test-" + Guid.NewGuid().ToString("N"));
        string settingsFile = Path.Combine(tempDir, "settings.json");

        try
        {
            var settings = new Settings();
            settings.CreateNew(settingsFile);

            var loaded = new Settings
            {
                CaptureSize = 999,
                DetectionFpsCap = 999,
                OrbWalkTickRateMs = 999
            };
            loaded.Load(settingsFile);

            Assert.Equal((int)VirtualKeyCode.C, loaded.ManualKey);
            Assert.Equal(32, loaded.AutoKey);
            Assert.Equal(1000, loaded.CaptureSize);
            Assert.Equal(60, loaded.DetectionFpsCap);
            Assert.Equal(1, loaded.OrbWalkTickRateMs);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

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
}
