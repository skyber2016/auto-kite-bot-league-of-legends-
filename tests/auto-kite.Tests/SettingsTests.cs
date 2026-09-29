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
        Assert.Equal(10, settings.ColorTolerance);

        Assert.Equal(1000, settings.CaptureSize);
        Assert.Equal(10, settings.MinClusterPixels);
        Assert.Equal(60, settings.DetectionFpsCap);

        Assert.True(settings.EnableOverlay);

        Assert.Equal(66, settings.WindupBufferMs);
        Assert.Equal(150, settings.MinInputDelayMs);
        Assert.Equal(1, settings.OrbWalkTickRateMs);
        Assert.Equal(500, settings.AttackSpeedPollMs);
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
}
