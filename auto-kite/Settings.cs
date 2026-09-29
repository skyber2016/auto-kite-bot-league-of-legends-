#nullable enable

namespace OddAutoWalker
{
    using System;
    using System.IO;
    using LowLevelInput.Hooks;
    using Newtonsoft.Json;

    public class Settings
    {
        // Key bindings
        public int ManualKey { get; set; } = (int)VirtualKeyCode.C;
        public int AutoKey { get; set; } = (int)VirtualKeyCode.Space;

        // Backward compatibility for Program.cs (to be replaced in Task 8)
        [JsonIgnore]
        [Obsolete("Replaced by ManualKey and AutoKey")]
        public int ActivationKey { get => ManualKey; set => ManualKey = value; }

        // Target Color Detection (RGB)
        public int TargetColorR { get; set; } = 52;
        public int TargetColorG { get; set; } = 3;
        public int TargetColorB { get; set; } = 0;
        public int ColorTolerance { get; set; } = 5;

        // Detection Area & Filtering
        public int CaptureSize { get; set; } = 1000;
        public int MinClusterPixels { get; set; } = 10;
        public int DetectionFpsCap { get; set; } = 60;

        // Target Offset — shift from detected position (e.g. health bar) to champion body
        public int TargetOffsetX { get; set; } = 70;
        public int TargetOffsetY { get; set; } = 120;

        // Visual Overlay Debug
        public bool EnableOverlay { get; set; } = true;

        // Orb-Walk Timing parameters (ms)
        public int WindupBufferMs { get; set; } = 66;
        public int MinInputDelayMs { get; set; } = 75;
        public int OrbWalkTickRateMs { get; set; } = 1;
        public int AttackSpeedPollMs { get; set; } = 500;

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

        public void CreateNew(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var sw = new StreamWriter(File.Create(path));
            sw.WriteLine("/* All Corresponding Key Bind Key Codes");
            foreach (int i in Enum.GetValues(typeof(VirtualKeyCode)))
            {
                sw.WriteLine($"* \t{i} - {(VirtualKeyCode)i}");
            }
            sw.WriteLine("*/");
            sw.WriteLine(JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public void Load(string path)
        {
            string json = File.ReadAllText(path);
            var loaded = JsonConvert.DeserializeObject<Settings>(json);
            if (loaded != null)
            {
                ManualKey = loaded.ManualKey;
                AutoKey = loaded.AutoKey;
                TargetColorR = loaded.TargetColorR;
                TargetColorG = loaded.TargetColorG;
                TargetColorB = loaded.TargetColorB;
                ColorTolerance = loaded.ColorTolerance;
                CaptureSize = loaded.CaptureSize;
                MinClusterPixels = loaded.MinClusterPixels;
                DetectionFpsCap = loaded.DetectionFpsCap;
                EnableOverlay = loaded.EnableOverlay;
                WindupBufferMs = loaded.WindupBufferMs;
                MinInputDelayMs = loaded.MinInputDelayMs;
                OrbWalkTickRateMs = loaded.OrbWalkTickRateMs;
                AttackSpeedPollMs = loaded.AttackSpeedPollMs;
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
            }
        }
    }
}
