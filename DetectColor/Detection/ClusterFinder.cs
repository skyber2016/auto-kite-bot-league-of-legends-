namespace DetectColor.Detection;

using System.Collections.Generic;
using System.Drawing;
using DetectColor.Models;

public sealed class ClusterFinder
{
    private static readonly (int dx, int dy)[] Neighbors =
    [
        (-1, -1), (0, -1), (1, -1),
        (-1,  0),          (1,  0),
        (-1,  1), (0,  1), (1,  1)
    ];

    public List<ColorCluster> FindClusters(
        List<ColorMatch> matches,
        int imageWidth,
        int imageHeight,
        int captureScreenX,
        int captureScreenY,
        int minPixels = 10)
    {
        if (matches.Count == 0 || imageWidth <= 0 || imageHeight <= 0) return [];

        var grid = new bool[imageWidth, imageHeight];
        foreach (var match in matches)
        {
            var p = match.LocalPosition;
            if (p.X >= 0 && p.X < imageWidth && p.Y >= 0 && p.Y < imageHeight)
            {
                grid[p.X, p.Y] = true;
            }
        }

        var visited = new bool[imageWidth, imageHeight];
        var clusters = new List<ColorCluster>();
        var queue = new Queue<Point>();

        for (int y = 0; y < imageHeight; y++)
        {
            for (int x = 0; x < imageWidth; x++)
            {
                if (!grid[x, y] || visited[x, y])
                    continue;

                queue.Clear();
                queue.Enqueue(new Point(x, y));
                visited[x, y] = true;

                int minX = x, maxX = x, minY = y, maxY = y;
                long sumX = 0, sumY = 0;
                int count = 0;

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    count++;
                    sumX += current.X;
                    sumY += current.Y;

                    if (current.X < minX) minX = current.X;
                    if (current.X > maxX) maxX = current.X;
                    if (current.Y < minY) minY = current.Y;
                    if (current.Y > maxY) maxY = current.Y;

                    foreach (var (dx, dy) in Neighbors)
                    {
                        int nx = current.X + dx;
                        int ny = current.Y + dy;

                        if (nx >= 0 && nx < imageWidth && ny >= 0 && ny < imageHeight
                            && grid[nx, ny] && !visited[nx, ny])
                        {
                            visited[nx, ny] = true;
                            queue.Enqueue(new Point(nx, ny));
                        }
                    }
                }

                if (count >= minPixels)
                {
                    clusters.Add(new ColorCluster
                    {
                        BoundingRect = new Rectangle(
                            captureScreenX + minX,
                            captureScreenY + minY,
                            maxX - minX + 1,
                            maxY - minY + 1),
                        Center = new Point(
                            captureScreenX + (int)(sumX / count),
                            captureScreenY + (int)(sumY / count)),
                        PixelCount = count
                    });
                }
            }
        }

        clusters.Sort((a, b) => b.PixelCount.CompareTo(a.PixelCount));
        return clusters;
    }
}
