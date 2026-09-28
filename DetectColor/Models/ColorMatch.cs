namespace DetectColor.Models;

using System.Drawing;

public readonly struct ColorMatch
{
    public Point LocalPosition { get; init; }
    public Point ScreenPosition { get; init; }
    public Color ActualColor { get; init; }
    public double Distance { get; init; }
}
