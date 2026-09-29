using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using INPUT = OddAutoWalker.InputSimulator.Input;
using MOUSEINPUT = OddAutoWalker.InputSimulator.MouseInput;

namespace OddAutoWalker
{
    public static class InputSimulator
    {

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern UIntPtr GetMessageExtraInfo();

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private static class Win32
        {
            [DllImport("user32.dll")]
            public static extern int GetSystemMetrics(int nIndex);
        }

        private const int INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        // Cached — avoids reflection-based Marshal.SizeOf on every call
        private static readonly int InputSize = Marshal.SizeOf(typeof(Input));

        [Flags]
        private enum InputType
        {
            Mouse = 0,
            Keyboard = 1,
            Hardware = 2
        }

        [Flags]
        private enum KeyEventF
        {
            KeyDown = 0x0000,
            ExtendedKey = 0x0001,
            KeyUp = 0x0002,
            Unicode = 0x0004,
            Scancode = 0x0008,
        }

        [Flags]
        private enum MouseDataF
        {
            None = 0,
            XButton1 = 0x0001,
            XButton2 = 0x0002
        }

        [Flags]
        private enum MouseEventF
        {
            None = 0,
            Absolute = 0x8000,
            HWheel = 0x1000,
            Move = 0x0001,
            MoveNoCoalesce = 0x2000,
            LeftDown = 0x0002,
            LeftUp = 0x0004,
            RightDown = 0x0008,
            RightUp = 0x0010,
            MiddleDown = 0x0020,
            MiddleUp = 0x0040,
            VirtualDesk = 0x4000,
            Wheel = 0x0800,
            XDown = 0x0080,
            XUp = 0x0100
        }

        internal struct Input
        {
            public int type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] public MouseInput mi;
            [FieldOffset(0)] public KeyboardInput ki;
            [FieldOffset(0)] public readonly HardwareInput hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseInput
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardInput
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HardwareInput
        {
            public readonly uint uMsg;
            public readonly ushort wParamL;
            public readonly ushort wParamH;
        }

        /// <summary>
        /// Sends attack command as a single key press (KeyDown → KeyUp).
        /// The key should be bound to "Player Attack Move Click" in game settings.
        /// 2 inputs in 1 syscall.
        /// </summary>
        public static void SendAttackClick(ushort attackScancode)
        {
            var extraInfo = GetMessageExtraInfo();
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

        /// <summary>
        /// Sends right-click (move command) as a single batched SendInput call:
        /// MouseDown(Right) → MouseUp(Right)
        /// 2 inputs in 1 syscall instead of 2 separate kernel transitions.
        /// </summary>
        public static void SendMoveClick()
        {
            var extraInfo = GetMessageExtraInfo();
            Input[] inputs = new Input[2]
            {
                new Input
                {
                    type = (int)InputType.Mouse,
                    u = new InputUnion
                    {
                        mi = new MouseInput
                        {
                            mouseData = 0,
                            dwFlags = (uint)MouseEventF.RightDown,
                            dwExtraInfo = extraInfo
                        }
                    }
                },
                new Input
                {
                    type = (int)InputType.Mouse,
                    u = new InputUnion
                    {
                        mi = new MouseInput
                        {
                            mouseData = 0,
                            dwFlags = (uint)MouseEventF.RightUp,
                            dwExtraInfo = extraInfo
                        }
                    }
                }
            };

            SendInput(2, inputs, InputSize);
        }

        /// <summary>
        /// Moves the cursor to absolute screen coordinates using SendInput.
        /// Normalized to 0..65535 as expected by MOUSEEVENTF_ABSOLUTE.
        /// </summary>
        public static void SetCursorPosition(int x, int y)
        {
            int screenWidth = Win32.GetSystemMetrics(0);  // SM_CXSCREEN
            int screenHeight = Win32.GetSystemMetrics(1); // SM_CYSCREEN

            int normX = (int)Math.Round(x * 65535.0 / (screenWidth - 1));
            int normY = (int)Math.Round(y * 65535.0 / (screenHeight - 1));

            INPUT input = new INPUT
            {
                type = INPUT_MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = normX,
                        dy = normY,
                        dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
                        mouseData = 0,
                        dwExtraInfo = UIntPtr.Zero,
                        time = 0
                    }
                }
            };

            SendInput(1, new[] { input }, InputSize);
        }

        /// <summary>
        /// Atomically moves cursor to (x, y) AND sends KeyDown in a single SendInput call.
        /// This guarantees the cursor is at the target position when the key press is processed,
        /// eliminating the race condition between separate SetCursorPosition + KeyDown calls.
        /// </summary>
        public static void SendMoveAndKeyDown(int x, int y, ushort scancode)
        {
            int screenWidth = Win32.GetSystemMetrics(0);
            int screenHeight = Win32.GetSystemMetrics(1);
            int normX = (int)Math.Round(x * 65535.0 / (screenWidth - 1));
            int normY = (int)Math.Round(y * 65535.0 / (screenHeight - 1));

            var extraInfo = GetMessageExtraInfo();
            Input[] inputs = new Input[2]
            {
                new Input
                {
                    type = INPUT_MOUSE,
                    u = new InputUnion
                    {
                        mi = new MouseInput
                        {
                            dx = normX,
                            dy = normY,
                            dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
                            mouseData = 0,
                            dwExtraInfo = extraInfo,
                            time = 0
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
                            wScan = scancode,
                            dwFlags = (uint)(KeyEventF.KeyDown | KeyEventF.Scancode),
                            dwExtraInfo = extraInfo
                        }
                    }
                }
            };

            SendInput(2, inputs, InputSize);
        }

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

        public static class Keyboard
        {
            public static void KeyDown(ushort keycode)
            {
                Input[] inputs =
                {
                            new Input
                            {
                                type = (int) InputType.Keyboard,
                                u = new InputUnion
                                {
                                    ki = new KeyboardInput
                                    {
                                        wVk = 0,
                                        wScan = keycode,
                                        dwFlags = (uint) (KeyEventF.KeyDown | KeyEventF.Scancode),
                                        dwExtraInfo = GetMessageExtraInfo()
                                    }
                                }
                            }
                        };

                SendInput((uint)inputs.Length, inputs, InputSize);
            }

            public static void KeyUp(ushort keycode)
            {
                Input[] inputs =
                {
                            new Input
                            {
                                type = (int) InputType.Keyboard,
                                u = new InputUnion
                                {
                                    ki = new KeyboardInput
                                    {
                                        wVk = 0,
                                        wScan = keycode,
                                        dwFlags = (uint) (KeyEventF.KeyUp | KeyEventF.Scancode),
                                        dwExtraInfo = GetMessageExtraInfo()
                                    }
                                }
                            }
                        };

                SendInput((uint)inputs.Length, inputs, InputSize);
            }

            public static void KeyPress(ushort keycode)
            {
                KeyDown(keycode);
                KeyUp(keycode);
            }
        }

        public static class Mouse
        {
            public enum Buttons
            {
                Left,
                Right,
                Middle,
                X1,
                X2
            }

            public static void MouseDown(Buttons button)
            {

                MouseEventF flags;
                MouseDataF data;

                (flags, data) = button switch
                {
                    Buttons.Left => (MouseEventF.LeftDown, MouseDataF.None),
                    Buttons.Right => (MouseEventF.RightDown, MouseDataF.None),
                    Buttons.Middle => (MouseEventF.MiddleDown, MouseDataF.None),
                    Buttons.X1 => (MouseEventF.XDown, MouseDataF.XButton1),
                    Buttons.X2 => (MouseEventF.XDown, MouseDataF.XButton2),
                    _ => throw new Exception("Unknown button type"),
                };

                Input[] inputs =
                {
                            new Input
                            {
                                type = (int) InputType.Mouse,
                                u = new InputUnion
                                {
                                    mi = new MouseInput
                                    {
                                        mouseData = (uint)data,
                                        dwFlags = (uint)flags,
                                        dwExtraInfo = GetMessageExtraInfo()
                                    }
                                }
                            }
                        };

                SendInput((uint)inputs.Length, inputs, InputSize);
            }

            public static void MouseUp(Buttons button)
            {

                MouseEventF flags;
                MouseDataF data;

                (flags, data) = button switch
                {
                    Buttons.Left => (MouseEventF.LeftUp, MouseDataF.None),
                    Buttons.Right => (MouseEventF.RightUp, MouseDataF.None),
                    Buttons.Middle => (MouseEventF.MiddleUp, MouseDataF.None),
                    Buttons.X1 => (MouseEventF.XUp, MouseDataF.XButton1),
                    Buttons.X2 => (MouseEventF.XUp, MouseDataF.XButton2),
                    _ => throw new Exception("Unknown button type"),
                };

                Input[] inputs =
                {
                            new Input
                            {
                                type = (int) InputType.Mouse,
                                u = new InputUnion
                                {
                                    mi = new MouseInput
                                    {
                                        mouseData = (uint)data,
                                        dwFlags = (uint)flags,
                                        dwExtraInfo = GetMessageExtraInfo()
                                    }
                                }
                            }
                        };

                SendInput((uint)inputs.Length, inputs, InputSize);
            }

            public static void MouseClick(Buttons button)
            {
                MouseDown(button);
                MouseUp(button);
            }

            public static void MouseDoubleClick(Buttons button)
            {
                MouseClick(button);
                MouseClick(button);
            }
        }
    }
}
