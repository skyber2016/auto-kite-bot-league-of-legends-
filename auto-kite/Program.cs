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

        private const string ActivePlayerEndpoint = @"https://127.0.0.1:2999/liveclientdata/activeplayer";
        private const string PlayerListEndpoint = @"https://127.0.0.1:2999/liveclientdata/playerlist";
        private const string ChampionStatsEndpoint = @"https://raw.communitydragon.org/latest/game/data/characters/";
        private static readonly string SettingsFile = Path.Combine(AppContext.BaseDirectory, "settings", "settings.json");

        // Thread-safe flags — volatile ensures cross-thread visibility
        private static volatile bool HasProcess = false;
        private static volatile bool IsExiting = false;
        private static volatile bool IsIntializingValues = false;
        private static volatile bool IsUpdatingAttackValues = false;

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

        // When these values are in the past (relative to PrecisionTimer), the action they gate can be taken
        // Stored as elapsed seconds from Stopwatch for high-resolution comparison
        private static double nextInput = 0;
        private static double nextMove = 0;
        private static double nextAttack = 0;

        // These are all in seconds (Stopwatch-based elapsed time)
        public static double GetSecondsPerAttack() => 1 / ClientAttackSpeed;
        public static double GetWindupDuration() => (((GetSecondsPerAttack() * ChampionAttackDelayPercent) - ChampionAttackCastTime) * ChampionAttackDelayScaling) + ChampionAttackCastTime;
        public static double GetBufferedWindupDuration() => GetWindupDuration() + (CurrentSettings.WindupBufferMs / 1000.0);

        public static async Task Main(string[] args)
        {
            if (!File.Exists(SettingsFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                CurrentSettings.CreateNew(SettingsFile);
            }
            else
            {
                CurrentSettings.Load(SettingsFile);
            }

            Console.Clear();
            Console.CursorVisible = false;

            InputManager.Initialize();
            InputManager.OnKeyboardEvent += InputManager_OnKeyboardEvent;
            InputManager.OnMouseEvent += InputManager_OnMouseEvent;

            // Async polling loop replaces 33ms timer
            _ = Task.Run(() => AttackSpeedPollingLoopAsync());

            Console.WriteLine($"[Manual Mode] Hold '{(VirtualKeyCode)CurrentSettings.ManualKey}': Attack only when detected target");
            Console.WriteLine($"[Auto Mode]   Hold '{(VirtualKeyCode)CurrentSettings.AutoKey}': Always attack (focus detected target)");

            await CheckLeagueProcessAsync(CancellationToken.None);

            Console.ReadLine();
            IsExiting = true;
            StopMode();
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

                        Interlocked.Exchange(ref _targetX, best.Center.X);
                        Interlocked.Exchange(ref _targetY, best.Center.Y);
                        HasDetectedTarget = true;
                    }
                    else
                    {
                        HasDetectedTarget = false;
                    }

                    if (CurrentSettings.EnableOverlay && _overlayRenderer != null)
                    {
                        var boxes = new List<DetectedBox>(clusters.Count);
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
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(1, CurrentSettings.OrbWalkTickRateMs)));
            try
            {
                while (await timer.WaitForNextTickAsync(ct))
                {
                    ExecuteOrbWalkTick();
                }
            }
            catch (OperationCanceledException) { }
        }

        private static void ExecuteOrbWalkTick()
        {
            if (!HasProcess || IsExiting || LeagueProcess == null || GetForegroundWindow() != LeagueProcess.MainWindowHandle)
            {
                return;
            }

            var strategy = _currentStrategy;
            if (strategy == null) return;

            double time = PrecisionTimer.Elapsed.TotalSeconds;
            bool hasTarget = HasDetectedTarget;
            int tx = Interlocked.CompareExchange(ref _targetX, 0, 0);
            int ty = Interlocked.CompareExchange(ref _targetY, 0, 0);

            double minInputDelaySec = CurrentSettings.MinInputDelayMs / 1000.0;

            if (nextAttack < time)
            {
                nextInput = time + minInputDelaySec;

                if (strategy.TryAttack(hasTarget, tx, ty, (ushort)DirectInputKeys.DIK_A))
                {
                    double attackTime = PrecisionTimer.Elapsed.TotalSeconds;
                    nextMove = attackTime + GetBufferedWindupDuration();
                    nextAttack = attackTime + GetSecondsPerAttack();
                }
            }
            else if (nextMove < time)
            {
                nextInput = time + minInputDelaySec;
                InputSimulator.SendMoveClick();
            }
        }

        private static async Task AttackSpeedPollingLoopAsync()
        {
            while (!IsExiting)
            {
                await Task.Delay(CurrentSettings.AttackSpeedPollMs);

                if (!HasProcess || IsExiting || IsIntializingValues || IsUpdatingAttackValues)
                    continue;

                IsUpdatingAttackValues = true;

                JToken activePlayerToken = null;
                try
                {
                    string response = await Client.GetStringAsync(ActivePlayerEndpoint);
                    activePlayerToken = JToken.Parse(response);
                }
                catch
                {
                    IsUpdatingAttackValues = false;
                    continue;
                }

                if (string.IsNullOrEmpty(ChampionName))
                {
                    ActivePlayerName = activePlayerToken?["summonerName"].ToString();
                    IsIntializingValues = true;

                    try
                    {
                        string playerListResponse = await Client.GetStringAsync(PlayerListEndpoint);
                        JToken playerListToken = JToken.Parse(playerListResponse);
                        foreach (JToken token in playerListToken)
                        {
                            if (token["summonerName"].ToString().Equals(ActivePlayerName))
                            {
                                ChampionName = token["championName"].ToString();
                                string[] rawNameArray = token["rawChampionName"].ToString().Split('_', StringSplitOptions.RemoveEmptyEntries);
                                RawChampionName = rawNameArray[^1];
                            }
                        }
                    }
                    catch
                    {
                        IsIntializingValues = false;
                        IsUpdatingAttackValues = false;
                        continue;
                    }

                    if (!await GetChampionBaseValuesAsync(RawChampionName))
                    {
                        IsIntializingValues = false;
                        IsUpdatingAttackValues = false;
                        continue;
                    }

                    IsIntializingValues = false;
                }

                ClientAttackSpeed = activePlayerToken["championStats"]["attackSpeed"].Value<double>();
                IsUpdatingAttackValues = false;
            }
        }

        private static async Task<bool> GetChampionBaseValuesAsync(string championName)
        {
            string lowerChampionName = championName.ToLower();
            JToken championBinToken = null;
            try
            {
                string response = await Client.GetStringAsync($"{ChampionStatsEndpoint}{lowerChampionName}/{lowerChampionName}.bin.json");
                championBinToken = JToken.Parse(response);
            }
            catch
            {
                return false;
            }
            JToken championRootStats = championBinToken[$"Characters/{championName}/CharacterRecords/Root"];
            ChampionAttackSpeedRatio = championRootStats["attackSpeedRatio"].Value<double>(); ;

            JToken championBasicAttackInfoToken = championRootStats["basicAttack"];
            JToken championAttackDelayOffsetToken = championBasicAttackInfoToken["mAttackDelayCastOffsetPercent"];
            JToken championAttackDelayOffsetSpeedRatioToken = championBasicAttackInfoToken["mAttackDelayCastOffsetPercentAttackSpeedRatio"];

            if (championAttackDelayOffsetSpeedRatioToken?.Value<double?>() != null)
            {
                ChampionAttackDelayScaling = championAttackDelayOffsetSpeedRatioToken.Value<double>();
            }

            if (championAttackDelayOffsetToken?.Value<double?>() == null)
            {
                JToken attackTotalTimeToken = championBasicAttackInfoToken["mAttackTotalTime"];
                JToken attackCastTimeToken = championBasicAttackInfoToken["mAttackCastTime"];

                if (attackTotalTimeToken?.Value<double?>() == null && attackCastTimeToken?.Value<double?>() == null)
                {
                    string attackName = championBasicAttackInfoToken["mAttackName"].ToString();
                    string attackSpell = $"Characters/{attackName.Split(new[] { "BasicAttack" }, StringSplitOptions.RemoveEmptyEntries)[0]}/Spells/{attackName}";
                    ChampionAttackDelayPercent += championBinToken[attackSpell]["mSpell"]["delayCastOffsetPercent"].Value<double>();
                }
                else
                {
                    ChampionAttackTotalTime = attackTotalTimeToken.Value<double>();
                    ChampionAttackCastTime = attackCastTimeToken.Value<double>(); ;

                    ChampionAttackDelayPercent = ChampionAttackCastTime / ChampionAttackTotalTime;
                }
            }
            else
            {
                ChampionAttackDelayPercent += championAttackDelayOffsetToken.Value<double>(); ;
            }

            return true;
        }
    }
}
