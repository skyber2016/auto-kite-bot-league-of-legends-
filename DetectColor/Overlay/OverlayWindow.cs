namespace DetectColor.Overlay;

using System;
using System.Runtime.InteropServices;
using System.Threading;

public partial class OverlayWindow : IDisposable
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private const uint WS_EX_LAYERED = 0x80000;
    private const uint WS_EX_TRANSPARENT = 0x20;
    private const uint WS_EX_TOPMOST = 0x8;
    private const uint WS_EX_TOOLWINDOW = 0x80;
    private const uint WS_EX_NOACTIVATE = 0x08000000;

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;

    private const int SW_SHOWNOACTIVATE = 4;
    private const int SW_HIDE = 0;

    private const uint WM_DESTROY = 0x0002;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_DWMCOMPOSITIONCHANGED = 0x031E;
    private const nint HTTRANSPARENT = -1;
    private const uint LWA_ALPHA = 0x00000002;

    private const uint CS_HREDRAW = 0x0002;
    private const uint CS_VREDRAW = 0x0001;

    public IntPtr Hwnd { get; private set; }
    public int X { get; private set; }
    public int Y { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public event Action<IntPtr>? OnCreated;
    public event Action? OnDestroyed;

    private Thread? _windowThread;
    private readonly ManualResetEventSlim _windowReadyEvent = new(false);
    private bool _isDisposed;
    private WndProcDelegate? _wndProcDelegate;

    public void Show()
    {
        if (_windowThread != null) return;

        _windowThread = new Thread(WindowThreadProc)
        {
            IsBackground = true
        };
        _windowThread.SetApartmentState(ApartmentState.STA);
        _windowThread.Start();

        _windowReadyEvent.Wait();
    }

    public void Hide()
    {
        if (Hwnd != IntPtr.Zero) ShowWindow(Hwnd, SW_HIDE);
    }

    public void Destroy()
    {
        if (Hwnd != IntPtr.Zero) DestroyWindow(Hwnd);
        _windowThread?.Join(1000);
        _windowThread = null;
    }

    private void WindowThreadProc()
    {
        try { SetProcessDpiAwarenessContext((IntPtr)(-4)); } catch { }

        X = GetSystemMetrics(SM_XVIRTUALSCREEN);
        Y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        Width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        Height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        _wndProcDelegate = WndProc;
        string className = "DetectColorOverlayClass";

        var wndClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            style = CS_HREDRAW | CS_VREDRAW,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = IntPtr.Zero,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = IntPtr.Zero,
            lpszClassName = Marshal.StringToHGlobalUni(className),
            hIconSm = IntPtr.Zero
        };

        RegisterClassExW(ref wndClass);

        Hwnd = CreateWindowExW(
            WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            className,
            "DetectColor Overlay",
            WS_POPUP | WS_VISIBLE,
            X, Y, Width, Height,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        SetLayeredWindowAttributes(Hwnd, 0, 255, LWA_ALPHA);
        UpdateWindow(Hwnd);

        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        DwmExtendFrameIntoClientArea(Hwnd, ref margins);

        ShowWindow(Hwnd, SW_SHOWNOACTIVATE);

        OnCreated?.Invoke(Hwnd);
        _windowReadyEvent.Set();

        while (GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }

        OnDestroyed?.Invoke();
        Hwnd = IntPtr.Zero;
        Marshal.FreeHGlobal(wndClass.lpszClassName);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                return (IntPtr)1;

            case WM_PAINT:
                ValidateRect(hWnd, IntPtr.Zero);
                return IntPtr.Zero;

            case WM_NCHITTEST:
                return (IntPtr)HTTRANSPARENT;

            case WM_DWMCOMPOSITIONCHANGED:
                var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
                DwmExtendFrameIntoClientArea(hWnd, ref margins);
                return IntPtr.Zero;

            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;

            default:
                return DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Destroy();
            _windowReadyEvent.Dispose();
            _isDisposed = true;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName, hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int pt_x, pt_y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(IntPtr value);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ValidateRect(IntPtr hWnd, IntPtr lpRect);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    private static partial IntPtr DispatchMessageW(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    private static partial IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);
}
