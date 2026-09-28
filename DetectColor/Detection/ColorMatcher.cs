namespace DetectColor.Detection;

using System;
using System.Collections.Generic;
using System.Drawing;
using DetectColor.Models;

public sealed class ColorMatcher
{
    public List<ColorMatch> FindPixels(CaptureResult capture, Color targetColor, int tolerance)
    {
        if (capture.IsEmpty) return [];

        var results = new List<ColorMatch>();
        int toleranceSq = tolerance * tolerance;
        int tR = targetColor.R;
        int tG = targetColor.G;
        int tB = targetColor.B;
        int width = capture.Width;
        int height = capture.Height;
        int screenX = capture.ScreenX;
        int screenY = capture.ScreenY;

        unsafe
        {
            fixed (byte* pBuffer = capture.Buffer)
            {
                byte* ptr = pBuffer;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // Dữ liệu BGRA 4 bytes/pixel
                        int b = ptr[0];
                        int g = ptr[1];
                        int r = ptr[2];

                        int dr = r - tR;
                        int dg = g - tG;
                        int db = b - tB;
                        int distSq = dr * dr + dg * dg + db * db;

                        if (distSq <= toleranceSq)
                        {
                            results.Add(new ColorMatch
                            {
                                LocalPosition = new Point(x, y),
                                ScreenPosition = new Point(screenX + x, screenY + y),
                                ActualColor = Color.FromArgb(r, g, b),
                                Distance = Math.Sqrt(distSq)
                            });
                        }

                        ptr += 4;
                    }
                }
            }
        }

        return results;
    }
}
