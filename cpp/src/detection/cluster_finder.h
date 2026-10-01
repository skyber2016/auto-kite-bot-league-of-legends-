#pragma once
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include "models/color_match.h"
#include "models/color_cluster.h"
#include <vector>

namespace ClusterFinder {
    std::vector<ColorCluster> find_clusters(
        const std::vector<ColorMatch>& matches,
        int image_width, int image_height,
        int min_pixels);
}
