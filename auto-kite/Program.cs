#nullable enable
using LowLevelInput.Hooks;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Collections.Generic;
using DetectColor.Capture;
using DetectColor.Detection;
using DetectColor.Models;
using DetectColor.Overlay;
using DetectColor.Input;
using OddAutoWalker.Strategies;

namespace OddAutoWalker
{
    public class Program
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uMilliseconds);

        private const string ActivePlayerEndpoint = @"https://127.0.0.1:2999/liveclientdata/activeplayer";
        private const string PlayerListEndpoint = @"https://127.0.0.1:2999/liveclientdata/playerlist";
        private const string ChampionStatsEndpoint = @"https://raw.communitydragon.org/latest/game/data/characters/";
        private static readonly string SettingsFile = Path.Combine(AppContext.BaseDirectory, "settings", "settings.json");

        // Thread-safe flags — volatile ensures cross-thread visibility
        private static volatile bool HasProcess = false;
        private static volatile bool IsExiting = false;

        private static readonly Settings CurrentSettings = new Settings();

        // HttpClient replaces deprecated WebClient — connection pooling, async I/O, no thread blocking
        private static readonly HttpClient Client = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, errors) => true
        });

        private static readonly InputManager InputManager = new InputManager();
        private static Process LeagueProcess = null;

        private static string ActivePlayerName = string.Empty;
        private static string ChampionName = string.Empty;
        private static string RawChampionName = string.Empty;

        private static double ClientAttackSpeed = 0.625;
        private static volatile bool HasAttackSpeedData = false;
        private static double ChampionAttackCastTime = 0.625;
        private static double ChampionAttackTotalTime = 0.625;
        private static double ChampionAttackSpeedRatio = 0.625;
        private static double ChampionAttackDelayPercent = 0.3;
        private static double ChampionAttackDelayScaling = 1.0;

        // High-resolution timer (~1μs precision vs ~15.6ms for DateTime.Now on Windows)
        private static readonly Stopwatch PrecisionTimer = Stopwatch.StartNew();

        private static volatile bool HasDetectedTarget = false;
        private static int _targetX;
        private static int _targetY;

        private static IOrbWalkStrategy? _currentStrategy;
        private static OrbWalkMode _currentMode = OrbWalkMode.None;
        private static CancellationTokenSource? _activeLoopCts;

        private static DxgiCapturer? _capturer;
        private static ColorMatcher? _colorMatcher;
        private static ClusterFinder? _clusterFinder;
        private static OverlayWindow? _overlayWindow;
        private static OverlayRenderer? _overlayRenderer;


        // These are all in seconds (Stopwatch-based elapsed time)
        public static double GetSecondsPerAttack() => 1 / ClientAttackSpeed;
        public static double GetWindupDuration() => (((GetSecondsPerAttack() * ChampionAttackDelayPercent) - ChampionAttackCastTime) * ChampionAttackDelayScaling) + ChampionAttackCastTime;
        public static double GetBufferedWindupDuration() => GetWindupDuration() + (CurrentSettings.WindupBufferMs / 1000.0);

        public static async Task Main(string[] args)
        {
#if !DEBUG
            if (!File.Exists(SettingsFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                CurrentSettings.CreateNew(SettingsFile);
            }
            else
            {
                CurrentSettings.Load(SettingsFile);
            }
#endif
            Console.Clear();
            Console.CursorVisible = false;

            // Raise Windows timer resolution to 1ms (default is 15.6ms)
            timeBeginPeriod(1);

            InputManager.Initialize();
            InputManager.OnKeyboardEvent += InputManager_OnKeyboardEvent;
            InputManager.OnMouseEvent += InputManager_OnMouseEvent;

            Console.WriteLine($"[Manual Mode] Hold '{(VirtualKeyCode)CurrentSettings.ManualKey}': Attack only when detected target");
            Console.WriteLine($"[Auto Mode]   Hold '{(VirtualKeyCode)CurrentSettings.AutoKey}': Always attack (focus detected target)");
            Console.WriteLine();
            Console.WriteLine("--- Settings ---");
            Console.WriteLine($"  Tick Rate:      {CurrentSettings.OrbWalkTickRateMs}ms");
            Console.WriteLine($"  Move Delay:     {CurrentSettings.MinInputDelayMs}ms");
            Console.WriteLine($"  Windup Buffer:  {CurrentSettings.WindupBufferMs}ms");
            Console.WriteLine($"  AS Poll:        {CurrentSettings.AttackSpeedPollMs}ms");
            Console.WriteLine($"  Target Color:   RGB({CurrentSettings.TargetColorR}, {CurrentSettings.TargetColorG}, {CurrentSettings.TargetColorB}) ±{CurrentSettings.ColorTolerance}");
            Console.WriteLine($"  Capture Size:   {CurrentSettings.CaptureSize}px");
            Console.WriteLine($"  Target Offset:  X={CurrentSettings.TargetOffsetX}, Y={CurrentSettings.TargetOffsetY}");
            Console.WriteLine($"  Overlay:        {(CurrentSettings.EnableOverlay ? "ON" : "OFF")}");
            Console.WriteLine("----------------");
            Console.WriteLine();

            await CheckLeagueProcessAsync(CancellationToken.None);
            Console.WriteLine("[OK] League process found — starting attack speed polling...");

            // Start polling AFTER process found — avoids wasted iterations
            _ = Task.Run(() => AttackSpeedPollingLoopAsync());

            Console.ReadLine();
            IsExiting = true;
            StopMode();
            timeEndPeriod(1);
        }

        private static void InputManager_OnMouseEvent(VirtualKeyCode key, KeyState state, int x, int y)
        {
        }

        private static void InputManager_OnKeyboardEvent(VirtualKeyCode key, KeyState state)
        {
            if (state == KeyState.Down)
            {
                if (_currentMode == OrbWalkMode.None)
                {
                    if (key == (VirtualKeyCode)CurrentSettings.ManualKey)
                    {
                        StartMode(OrbWalkMode.Manual);
                    }
                    else if (key == (VirtualKeyCode)CurrentSettings.AutoKey)
                    {
                        StartMode(OrbWalkMode.Auto);
                    }
                }
            }
            else if (state == KeyState.Up)
            {
                if (key == (VirtualKeyCode)CurrentSettings.ManualKey && _currentMode == OrbWalkMode.Manual)
                {
                    StopMode();
                }
                else if (key == (VirtualKeyCode)CurrentSettings.AutoKey && _currentMode == OrbWalkMode.Auto)
                {
                    StopMode();
                }
            }
        }

        private static void StartMode(OrbWalkMode mode)
        {
            _currentMode = mode;
            _currentStrategy = OrbWalkStrategyFactory.Create(mode);

            _capturer ??= new DxgiCapturer();
            _colorMatcher ??= new ColorMatcher();
            _clusterFinder ??= new ClusterFinder();
            if (CurrentSettings.EnableOverlay && _overlayWindow == null)
            {
                _overlayWindow = new OverlayWindow();
                _overlayRenderer = new OverlayRenderer();
                _overlayWindow.Show();
                _overlayRenderer.Start(_overlayWindow.Hwnd);
            }

            _activeLoopCts = new CancellationTokenSource();
            _ = Task.Run(() => DetectionLoopAsync(_activeLoopCts.Token));
            _ = Task.Run(() => OrbWalkLoopAsync(_activeLoopCts.Token));
        }

        private static void StopMode()
        {
            _currentMode = OrbWalkMode.None;
            _currentStrategy = null;
            HasDetectedTarget = false;
            _activeLoopCts?.Cancel();
            _activeLoopCts?.Dispose();
            _activeLoopCts = null;
            _overlayRenderer?.Clear();
        }

        private static async Task CheckLeagueProcessAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && (LeagueProcess is null || !HasProcess))
            {
                LeagueProcess = Process.GetProcessesByName("League of Legends").FirstOrDefault();
                if (LeagueProcess is null || LeagueProcess.HasExited)
                {
                    await Task.Delay(2000, ct);
                    continue;
                }
                HasProcess = true;
                LeagueProcess.EnableRaisingEvents = true;
                LeagueProcess.Exited += LeagueProcess_Exited;
            }
        }

        private static void LeagueProcess_Exited(object sender, EventArgs e)
        {
            HasProcess = false;
            LeagueProcess = null;
            Console.WriteLine("League Process Exited");
            _ = CheckLeagueProcessAsync(CancellationToken.None);
        }

        private static async Task DetectionLoopAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, CurrentSettings.DetectionFpsCap)));
            Color targetColor = Color.FromArgb(CurrentSettings.TargetColorR, CurrentSettings.TargetColorG, CurrentSettings.TargetColorB);
            try
            {
                while (await timer.WaitForNextTickAsync(ct))
                {
                    if (_capturer == null || _colorMatcher == null || _clusterFinder == null) continue;

                    var cursor = MouseHelper.GetCursorPosition();
                    var capture = _capturer.Capture(cursor.X, cursor.Y, CurrentSettings.CaptureSize);
                    if (capture.IsEmpty)
                    {
                        HasDetectedTarget = false;
                        continue;
                    }

                    if (CurrentSettings.EnableOverlay && _overlayRenderer != null)
                    {
                        _overlayRenderer.SetScanRegion(new Rectangle(capture.ScreenX, capture.ScreenY, capture.Width, capture.Height));
                    }

                    var matches = _colorMatcher.FindPixels(capture, targetColor, CurrentSettings.ColorTolerance);
                    var clusters = _clusterFinder.FindClusters(matches, capture.Width, capture.Height, capture.ScreenX, capture.ScreenY, CurrentSettings.MinClusterPixels);

                    if (clusters.Count > 0)
                    {
                        // Select cluster closest to cursor
                        ColorCluster best = clusters[0];
                        double bestDistSq = double.MaxValue;
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

                        // Apply offset: shift from detected color (e.g. health bar) to champion body
                        int targetX = best.Center.X + CurrentSettings.TargetOffsetX;
                        int targetY = best.Center.Y + CurrentSettings.TargetOffsetY;

                        Interlocked.Exchange(ref _targetX, targetX);
                        Interlocked.Exchange(ref _targetY, targetY);
                        HasDetectedTarget = true;
                    }
                    else
                    {
                        HasDetectedTarget = false;
                    }

                    if (CurrentSettings.EnableOverlay && _overlayRenderer != null)
                    {
                        var boxes = new List<DetectedBox>(clusters.Count + 1);
                        foreach (var cl in clusters)
                        {
                            boxes.Add(new DetectedBox
                            {
                                ScreenRect = cl.BoundingRect,
                                BorderColor = Color.Lime,
                                Label = $"{cl.PixelCount}px",
                                DetectedAt = DateTime.UtcNow
                            });
                        }

                        // Draw crosshair at actual target position (after offset)
                        if (HasDetectedTarget)
                        {
                            int tx = Interlocked.CompareExchange(ref _targetX, 0, 0);
                            int ty = Interlocked.CompareExchange(ref _targetY, 0, 0);
                            int markerSize = 20;
                            boxes.Add(new DetectedBox
                            {
                                ScreenRect = new Rectangle(tx - markerSize / 2, ty - markerSize / 2, markerSize, markerSize),
                                BorderColor = Color.Red,
                                Label = "TARGET",
                                DetectedAt = DateTime.UtcNow
                            });
                        }

                        _overlayRenderer.SetBoxes(boxes);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[DetectionLoop] Error: {ex.Message}");
            }
        }

        private static async Task OrbWalkLoopAsync(CancellationToken ct)
        {
            // Timing state is local — fresh start every activation, no cross-thread race
            double nextInput = 0, nextMove = 0, nextAttack = 0;

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(1, CurrentSettings.OrbWalkTickRateMs)));
            try
            {
                while (await timer.WaitForNextTickAsync(ct))
                {
                    ExecuteOrbWalkTick(ref nextInput, ref nextMove, ref nextAttack);
                }
            }
            catch (OperationCanceledException) { }
        }

        private static void ExecuteOrbWalkTick(ref double nextInput, ref double nextMove, ref double nextAttack)
        {
            if (!HasProcess || !HasAttackSpeedData || IsExiting || LeagueProcess == null || GetForegroundWindow() != LeagueProcess.MainWindowHandle)
            {
                return;
            }

            var strategy = _currentStrategy;
            if (strategy == null) return;

            double time = PrecisionTimer.Elapsed.TotalSeconds;
            bool hasTarget = HasDetectedTarget;
            int tx = Interlocked.CompareExchange(ref _targetX, 0, 0);
            int ty = Interlocked.CompareExchange(ref _targetY, 0, 0);

            // Attack phase: fire when cooldown is ready
            if (nextAttack <= time)
            {
                if (strategy.TryAttack(hasTarget, tx, ty, (ushort)DirectInputKeys.DIK_H))
                {
                    double attackTime = PrecisionTimer.Elapsed.TotalSeconds;
                    nextMove = attackTime + GetBufferedWindupDuration();
                    nextAttack = attackTime + GetSecondsPerAttack();
                    return; // Attack fired — wait for windup before moving
                }
                // TryAttack skipped (no target in manual mode) — fall through to move
            }

            // Move phase: kite between attacks, throttled by MinInputDelayMs
            if (nextMove <= time && nextInput <= time)
            {
                nextInput = time + (CurrentSettings.MinInputDelayMs / 1000.0);
                InputSimulator.SendMoveClick();
            }
        }

        private static async Task AttackSpeedPollingLoopAsync()
        {
            while (!IsExiting)
            {
                if (!HasProcess || IsExiting)
                {
                    await Task.Delay(CurrentSettings.AttackSpeedPollMs);
                    continue;
                }

                try
                {
                    string response = await Client.GetStringAsync(ActivePlayerEndpoint);
                    JToken activePlayerToken = JToken.Parse(response);

                    // Champion init — non-blocking: if this fails, we still read attack speed below
                    if (string.IsNullOrEmpty(ChampionName))
                    {
                        try
                        {
                            ActivePlayerName = activePlayerToken?["riotIdGameName"]?.ToString()
                                            ?? activePlayerToken?["summonerName"]?.ToString()
                                            ?? string.Empty;

                            string playerListResponse = await Client.GetStringAsync(PlayerListEndpoint);
                            JToken playerListToken = JToken.Parse(playerListResponse);
                            foreach (JToken token in playerListToken)
                            {
                                string tokenName = token["riotIdGameName"]?.ToString()
                                                ?? token["summonerName"]?.ToString()
                                                ?? string.Empty;
                                if (tokenName.Equals(ActivePlayerName))
                                {
                                    ChampionName = token["championName"]?.ToString() ?? string.Empty;
                                    string[] rawNameArray = (token["rawChampionName"]?.ToString() ?? string.Empty).Split('_', StringSplitOptions.RemoveEmptyEntries);
                                    RawChampionName = rawNameArray[^1];
                                }
                            }

                            if (!string.IsNullOrEmpty(RawChampionName) && await GetChampionBaseValuesAsync(RawChampionName))
                            {
                                Console.WriteLine($"[OK] Champion: {ChampionName}");
                                Console.WriteLine($"  AS Ratio:       {ChampionAttackSpeedRatio:F4}");
                                Console.WriteLine($"  Delay%:         {ChampionAttackDelayPercent:F4}");
                                Console.WriteLine($"  Delay Scaling:  {ChampionAttackDelayScaling:F4}");
                                Console.WriteLine($"  Cast Time:      {ChampionAttackCastTime:F4}s");
                                Console.WriteLine($"  Total Time:     {ChampionAttackTotalTime:F4}s");
                            }
                            else
                            {
                                ChampionName = string.Empty; // Retry next poll
                            }
                        }
                        catch (Exception ex)
                        {
                            ChampionName = string.Empty;
                            Console.WriteLine($"[Init] Retrying... {ex.Message}");
                        }
                    }

                    // ALWAYS read attack speed — even if champion init failed
                    double newAS = activePlayerToken["championStats"]?["attackSpeed"]?.Value<double>() ?? ClientAttackSpeed;
                    if (newAS != ClientAttackSpeed)
                    {
                        ClientAttackSpeed = newAS;
                        double secPerAtk = GetSecondsPerAttack();
                        double windup = GetWindupDuration();
                        double bufferedWindup = GetBufferedWindupDuration();
                        double moveWindow = secPerAtk - bufferedWindup;
                        Console.WriteLine($"[AS] {ClientAttackSpeed:F3} | Interval: {secPerAtk * 1000:F0}ms | Windup: {windup * 1000:F0}ms + {CurrentSettings.WindupBufferMs}ms = {bufferedWindup * 1000:F0}ms | Move Window: {moveWindow * 1000:F0}ms");
                    }
                    else if (!HasAttackSpeedData)
                    {
                        ClientAttackSpeed = newAS;
                    }

                    if (!HasAttackSpeedData)
                    {
                        HasAttackSpeedData = true;
                        double secPerAtk = GetSecondsPerAttack();
                        double windup = GetWindupDuration();
                        double bufferedWindup = GetBufferedWindupDuration();
                        double moveWindow = secPerAtk - bufferedWindup;
                        Console.WriteLine($"[OK] Attack speed ready: {ClientAttackSpeed:F3} AS");
                        Console.WriteLine($"  Interval:       {secPerAtk * 1000:F0}ms");
                        Console.WriteLine($"  Windup:         {windup * 1000:F0}ms + {CurrentSettings.WindupBufferMs}ms buffer = {bufferedWindup * 1000:F0}ms");
                        Console.WriteLine($"  Move Window:    {moveWindow * 1000:F0}ms");
                    }
                }
                catch (HttpRequestException)
                {
                    // API not available yet (game loading) — silent retry
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AttackSpeedPoll] Error: {ex.Message}");
                }

                await Task.Delay(CurrentSettings.AttackSpeedPollMs);
            }
        }

        private static async Task<bool> GetChampionBaseValuesAsync(string championName)
        {
            try
            {
                string lowerChampionName = championName.ToLower();
                string response = await Client.GetStringAsync($"{ChampionStatsEndpoint}{lowerChampionName}/{lowerChampionName}.bin.json");
                JToken championBinToken = JToken.Parse(response);

                JToken championRootStats = championBinToken[$"Characters/{championName}/CharacterRecords/Root"];
                if (championRootStats == null)
                {
                    Console.WriteLine($"[Init] No root stats for '{championName}' in CommunityDragon");
                    return false;
                }

                ChampionAttackSpeedRatio = championRootStats["attackSpeedRatio"]?.Value<double>() ?? 0.625;

                JToken basicAttack = championRootStats["basicAttack"];
                if (basicAttack == null)
                {
                    Console.WriteLine($"[Init] No basicAttack data for '{championName}'");
                    return false;
                }

                JToken delayOffsetToken = basicAttack["mAttackDelayCastOffsetPercent"];
                JToken delayScalingToken = basicAttack["mAttackDelayCastOffsetPercentAttackSpeedRatio"];

                if (delayScalingToken?.Value<double?>() != null)
                {
                    ChampionAttackDelayScaling = delayScalingToken.Value<double>();
                }

                if (delayOffsetToken?.Value<double?>() == null)
                {
                    JToken attackTotalTimeToken = basicAttack["mAttackTotalTime"];
                    JToken attackCastTimeToken = basicAttack["mAttackCastTime"];

                    if (attackTotalTimeToken?.Value<double?>() == null && attackCastTimeToken?.Value<double?>() == null)
                    {
                        string attackName = basicAttack["mAttackName"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(attackName))
                        {
                            string attackSpell = $"Characters/{attackName.Split(new[] { "BasicAttack" }, StringSplitOptions.RemoveEmptyEntries)[0]}/Spells/{attackName}";
                            double? offset = championBinToken[attackSpell]?["mSpell"]?["delayCastOffsetPercent"]?.Value<double?>();
                            if (offset != null)
                                ChampionAttackDelayPercent += offset.Value;
                        }
                    }
                    else if (attackTotalTimeToken != null && attackCastTimeToken != null)
                    {
                        ChampionAttackTotalTime = attackTotalTimeToken.Value<double>();
                        ChampionAttackCastTime = attackCastTimeToken.Value<double>();
                        ChampionAttackDelayPercent = ChampionAttackCastTime / ChampionAttackTotalTime;
                    }
                }
                else
                {
                    ChampionAttackDelayPercent += delayOffsetToken.Value<double>();
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Init] Failed to load '{championName}': {ex.Message}");
                return false;
            }
        }
    }
}
