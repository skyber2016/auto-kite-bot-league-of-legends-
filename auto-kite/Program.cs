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
using System.Timers;
using Timer = System.Timers.Timer;

namespace OddAutoWalker
{
    public class Program
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private const string ActivePlayerEndpoint = @"https://127.0.0.1:2999/liveclientdata/activeplayer";
        private const string PlayerListEndpoint = @"https://127.0.0.1:2999/liveclientdata/playerlist";
        private const string ChampionStatsEndpoint = @"https://raw.communitydragon.org/latest/game/data/characters/";
        private const string SettingsFile = @"settings\settings.json";

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

        private static readonly Timer OrbWalkTimer = new Timer(100d / 3d);

        private static volatile bool OrbWalkerTimerActive = false;

        private static string ActivePlayerName = string.Empty;
        private static string ChampionName = string.Empty;
        private static string RawChampionName = string.Empty;

        private static double ClientAttackSpeed = 0.625;
        private static double ChampionAttackCastTime = 0.625;
        private static double ChampionAttackTotalTime = 0.625;
        private static double ChampionAttackSpeedRatio = 0.625;
        private static double ChampionAttackDelayPercent = 0.3;
        private static double ChampionAttackDelayScaling = 1.0;

        /// <summary>
        /// This is a buffer to prevent you from accidentally canceling your auto-attack too soon, as a result of fps, ping, or otherwise.
        /// </summary>
        private static readonly double WindupBuffer = 1d / 15d;

        // If we're trying to input faster than this, don't
        private static readonly double MinInputDelay = 1d / 30d;

        // This is honestly just semi-random because we need an interval to run the timer at
        private static readonly double OrderTickRate = 1d / 30d;

        // Attack speed only changes on level-up or item purchase — 500ms is more than enough
        private const int AttackSpeedPollIntervalMs = 500;

        // High-resolution timer (~1μs precision vs ~15.6ms for DateTime.Now on Windows)
        private static readonly Stopwatch PrecisionTimer = Stopwatch.StartNew();

#if DEBUG
        private static int TimerCallbackCounter = 0;
#endif

        // These are all in seconds (Stopwatch-based elapsed time)
        public static double GetSecondsPerAttack() => 1 / ClientAttackSpeed;
        public static double GetWindupDuration() => (((GetSecondsPerAttack() * ChampionAttackDelayPercent) - ChampionAttackCastTime) * ChampionAttackDelayScaling) + ChampionAttackCastTime;
        public static double GetBufferedWindupDuration() => GetWindupDuration() + WindupBuffer;

        public static void Main(string[] args)
        {
            if (!File.Exists(SettingsFile))
            {
                Directory.CreateDirectory("settings");
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

            OrbWalkTimer.Elapsed += OrbWalkTimer_Elapsed;
#if DEBUG
            Timer callbackTimer = new Timer(16.66);
            callbackTimer.Elapsed += Timer_CallbackLog;
#endif

            // Async polling loop replaces 33ms timer — polls every 500ms via HttpClient async I/O
            _ = Task.Run(AttackSpeedPollingLoopAsync);

            Console.WriteLine($"Press and hold '{(VirtualKeyCode)CurrentSettings.ActivationKey}' to activate the Orb Walker");

            CheckLeagueProcess();

            Console.ReadLine();
        }

#if DEBUG
        private static void Timer_CallbackLog(object sender, ElapsedEventArgs e)
        {
            if (TimerCallbackCounter > 1 || TimerCallbackCounter < 0)
            {
                Console.Clear();
                Console.WriteLine("Timer Error Detected");
                throw new Exception("Timers must not run simultaneously");
            }
        }
#endif

        private static void InputManager_OnMouseEvent(VirtualKeyCode key, KeyState state, int x, int y)
        {
        }

        private static void InputManager_OnKeyboardEvent(VirtualKeyCode key, KeyState state)
        {
            if (key == (VirtualKeyCode)CurrentSettings.ActivationKey)
            {
                switch (state)
                {
                    case KeyState.Down when !OrbWalkerTimerActive:
                        OrbWalkerTimerActive = true;
                        OrbWalkTimer.Start();
                        break;

                    case KeyState.Up when OrbWalkerTimerActive:
                        OrbWalkerTimerActive = false;
                        OrbWalkTimer.Stop();
                        break;
                }
            }
        }

        // When these values are in the past (relative to PrecisionTimer), the action they gate can be taken
        // Stored as elapsed seconds from Stopwatch for high-resolution comparison
        private static double nextInput = 0;
        private static double nextMove = 0;
        private static double nextAttack = 0;

#if DEBUG
        private static readonly Stopwatch owStopWatch = new Stopwatch();
#endif

        private static void OrbWalkTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
#if DEBUG
            owStopWatch.Start();
            TimerCallbackCounter++;
#endif
            if (!HasProcess || IsExiting || GetForegroundWindow() != LeagueProcess.MainWindowHandle)
            {
#if DEBUG
                TimerCallbackCounter--;
#endif

                return;
            }

            // High-resolution timestamp (~1μs precision vs ~15.6ms for DateTime.Now)
            double time = PrecisionTimer.Elapsed.TotalSeconds;

            // Make sure we can send input without being dropped
            // This is used for gating movement orders when waiting for an attack to be prepared
            // This is not needed if this function is not ran frequently enough for it to matter
            // If it isn't, you might end up with this timer and this function's timer being out of sync
            //   resulting in a (worst-case) OrderTickRate + MinInputDelay delay
            // It is currently disabled due to this, enable it if you want/need to
            if (true || nextInput < time)
            {
                // If we can attack, do so
                if (nextAttack < time)
                {
                    // Store current time + input delay so we're aware when we can move next
                    nextInput = time + MinInputDelay;

                    // Send attack input as single batched SendInput call (4 inputs → 1 syscall)
                    InputSimulator.SendAttackClick((ushort)DirectInputKeys.DIK_A);

                    // High-resolution timestamp after input for precise next-attack scheduling
                    double attackTime = PrecisionTimer.Elapsed.TotalSeconds;

                    // Store timings for when to next attack / move
                    nextMove = attackTime + GetBufferedWindupDuration();
                    nextAttack = attackTime + GetSecondsPerAttack();
                }
                // If we can't attack but we can move, do so
                else if (nextMove < time)
                {
                    // Store current time + input delay so we're aware when we can attack / move next
                    nextInput = time + MinInputDelay;

                    // Send move input as single batched SendInput call (2 inputs → 1 syscall)
                    InputSimulator.SendMoveClick();
                }
            }
#if DEBUG
            TimerCallbackCounter--;
            owStopWatch.Reset();
#endif
        }

        private static void CheckLeagueProcess()
        {
            while (LeagueProcess is null || !HasProcess)
            {
                LeagueProcess = Process.GetProcessesByName("League of Legends").FirstOrDefault();
                if (LeagueProcess is null || LeagueProcess.HasExited)
                {
                    // Avoid busy-wait spin loop: process enumeration is a heavy WMI query,
                    // no need to hammer it — game launch takes seconds, not milliseconds
                    Thread.Sleep(2000);
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
            //Console.Clear();
            Console.WriteLine("League Process Exited");
            CheckLeagueProcess();
        }

        /// <summary>
        /// Async polling loop for attack speed updates.
        /// Replaces the 33ms System.Timers.Timer with a 500ms async loop:
        /// - Attack speed only changes on level-up or item purchase
        /// - Reduces HTTP calls from ~30/s to ~2/s
        /// - Uses HttpClient async I/O instead of WebClient blocking calls
        /// </summary>
        private static async Task AttackSpeedPollingLoopAsync()
        {
            while (!IsExiting)
            {
                await Task.Delay(AttackSpeedPollIntervalMs);

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

#if DEBUG
                    Console.Title = $"({ActivePlayerName}) {ChampionName}";
#endif

                    IsIntializingValues = false;
                }

#if DEBUG
                Console.SetCursorPosition(0, 0);
                Console.WriteLine($"{owStopWatch.ElapsedMilliseconds}\n" +
                    $"Attack Speed Ratio: {ChampionAttackSpeedRatio}\n" +
                    $"Windup Percent: {ChampionAttackDelayPercent}\n" +
                    $"Current AS: {ClientAttackSpeed:0.00####}\n" +
                    $"Seconds Per Attack: {GetSecondsPerAttack():0.00####}\n" +
                    $"Windup Duration: {GetWindupDuration():0.00####}s + {WindupBuffer}s delay\n" +
                    $"Attack Down Time: {(GetSecondsPerAttack() - GetWindupDuration()):0.00####}s");
#endif

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
