namespace DetectColor.Models;

using System.Drawing;

public readonly struct ColorCluster
{
    public Rectangle BoundingRect { get; init; }
    public Point Center { get; init; }
    public int PixelCount { get; init; }
}
