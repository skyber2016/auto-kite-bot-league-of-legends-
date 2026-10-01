#include "detection/cluster_finder.h"
#include <queue>
#include <algorithm>

namespace {
constexpr POINT kNeighbors[] = {
    {-1, -1}, {0, -1}, {1, -1},
    {-1,  0},          {1,  0},
    {-1,  1}, {0,  1}, {1,  1},
};
}

std::vector<ColorCluster> ClusterFinder::find_clusters(
    const std::vector<ColorMatch>& matches,
    int image_width, int image_height,
    int min_pixels)
{
    if (image_width <= 0 || image_height <= 0 || matches.empty()) {
        return {};
    }

    const size_t total_pixels = static_cast<size_t>(image_width) * static_cast<size_t>(image_height);
    std::vector<uint8_t> grid(total_pixels, 0);
    for (const auto& m : matches) {
        int x = m.local_pos.x;
        int y = m.local_pos.y;
        if (x >= 0 && x < image_width && y >= 0 && y < image_height) {
            grid[static_cast<size_t>(y) * image_width + x] = 1;
        }
    }

    std::vector<uint8_t> visited(total_pixels, 0);
    std::vector<ColorCluster> clusters;

    for (int y = 0; y < image_height; ++y) {
        for (int x = 0; x < image_width; ++x) {
            size_t idx = static_cast<size_t>(y) * image_width + x;
            if (!grid[idx] || visited[idx]) continue;

            // BFS
            std::queue<POINT> queue;
            queue.push({static_cast<LONG>(x), static_cast<LONG>(y)});
            visited[idx] = 1;

            int min_x = x, max_x = x, min_y = y, max_y = y;
            long long sum_x = 0, sum_y = 0;
            int count = 0;

            while (!queue.empty()) {
                POINT pt = queue.front();
                queue.pop();
                sum_x += pt.x;
                sum_y += pt.y;
                count++;
                if (pt.x < min_x) min_x = pt.x;
                if (pt.x > max_x) max_x = pt.x;
                if (pt.y < min_y) min_y = pt.y;
                if (pt.y > max_y) max_y = pt.y;

                for (const auto& offset : kNeighbors) {
                    int nx = pt.x + offset.x;
                    int ny = pt.y + offset.y;
                    if (nx >= 0 && nx < image_width && ny >= 0 && ny < image_height) {
                        size_t nidx = static_cast<size_t>(ny) * image_width + nx;
                        if (grid[nidx] && !visited[nidx]) {
                            visited[nidx] = 1;
                            queue.push({static_cast<LONG>(nx), static_cast<LONG>(ny)});
                        }
                    }
                }
            }

            if (count >= min_pixels) {
                clusters.push_back({
                    {static_cast<LONG>(min_x), static_cast<LONG>(min_y),
                     static_cast<LONG>(max_x + 1),
                     static_cast<LONG>(max_y + 1)},
                    {static_cast<LONG>(sum_x / count),
                     static_cast<LONG>(sum_y / count)},
                    count
                });
            }
        }
    }

    std::sort(clusters.begin(), clusters.end(),
              [](const ColorCluster& a, const ColorCluster& b) {
                  return a.pixel_count > b.pixel_count;
              });

    return clusters;
}
