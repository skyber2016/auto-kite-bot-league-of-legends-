namespace DetectColor.Input;

using System.Drawing;
using System.Runtime.InteropServices;

public static partial class MouseHelper
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    public static Point GetCursorPosition()
    {
        GetCursorPos(out var point);
        return new Point(point.X, point.Y);
    }
}
