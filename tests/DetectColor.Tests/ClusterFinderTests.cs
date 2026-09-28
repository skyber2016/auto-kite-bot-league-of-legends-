namespace DetectColor.Tests;

using System.Collections.Generic;
using System.Drawing;
using DetectColor.Detection;
using DetectColor.Models;
using Xunit;

public class ClusterFinderTests
{
    [Fact]
    public void FindClusters_ConnectedPixels_FormsSingleCluster()
    {
        var finder = new ClusterFinder();
        var matches = new List<ColorMatch>();
        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                matches.Add(new ColorMatch
                {
                    LocalPosition = new Point(x, y),
                    ScreenPosition = new Point(x, y)
                });
            }
        }

        var clusters = finder.FindClusters(matches, 10, 10, 0, 0, minPixels: 10);
        Assert.Single(clusters);
        Assert.Equal(25, clusters[0].PixelCount);
        Assert.Equal(new Point(2, 2), clusters[0].Center);
    }
}
