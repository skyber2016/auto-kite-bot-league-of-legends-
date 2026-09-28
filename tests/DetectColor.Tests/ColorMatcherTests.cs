namespace DetectColor.Tests;

using System.Drawing;
using DetectColor.Detection;
using DetectColor.Models;
using Xunit;

public class ColorMatcherTests
{
    [Fact]
    public void FindPixels_ExactColor_MatchesSuccessfully()
    {
        var matcher = new ColorMatcher();
        // 2x2 image, BGRA: (0,0) is Red (B=0, G=0, R=255, A=255)
        byte[] buffer = new byte[2 * 2 * 4];
        buffer[2] = 255; buffer[3] = 255; // (0,0) Red

        var capture = new CaptureResult
        {
            Buffer = buffer,
            Width = 2,
            Height = 2,
            ScreenX = 100,
            ScreenY = 200
        };

        var matches = matcher.FindPixels(capture, Color.FromArgb(255, 0, 0), tolerance: 10);
        Assert.Single(matches);
        Assert.Equal(new Point(0, 0), matches[0].LocalPosition);
        Assert.Equal(new Point(100, 200), matches[0].ScreenPosition);
    }
}
