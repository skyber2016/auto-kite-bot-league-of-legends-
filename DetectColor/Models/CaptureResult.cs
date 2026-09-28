namespace DetectColor.Models;

public readonly struct CaptureResult
{
    public byte[] Buffer { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int ScreenX { get; init; }
    public int ScreenY { get; init; }
    public long CaptureTimeUs { get; init; }

    public int Stride => Width * 4;
    public int PixelCount => Width * Height;
    public bool IsEmpty => Buffer is null or { Length: 0 };
}
