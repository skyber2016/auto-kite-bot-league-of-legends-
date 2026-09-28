namespace DetectColor.Models;

using System.Drawing;

public readonly struct DetectedBox
{
    public Rectangle ScreenRect { get; init; }
    public Color BorderColor { get; init; }
    public string? Label { get; init; }
    public DateTime DetectedAt { get; init; }
}
