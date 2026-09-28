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
        public int TargetColorR { get; set; } = 255;
        public int TargetColorG { get; set; } = 0;
        public int TargetColorB { get; set; } = 0;
        public int ColorTolerance { get; set; } = 40;

        // Detection Area & Filtering
        public int CaptureSize { get; set; } = 400;
        public int MinClusterPixels { get; set; } = 10;
        public int DetectionFpsCap { get; set; } = 60;

        // Visual Overlay Debug
        public bool EnableOverlay { get; set; } = false;

        // Orb-Walk Timing parameters (ms)
        public int WindupBufferMs { get; set; } = 66;
        public int MinInputDelayMs { get; set; } = 33;
        public int OrbWalkTickRateMs { get; set; } = 33;
        public int AttackSpeedPollMs { get; set; } = 500;

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
            }
        }
    }
}
