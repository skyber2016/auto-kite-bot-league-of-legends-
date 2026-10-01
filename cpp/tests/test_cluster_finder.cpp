#include <gtest/gtest.h>
#include "detection/cluster_finder.h"
#include "models/color_match.h"
#include "models/color_cluster.h"
#include <vector>

TEST(ClusterFinder, SingleCluster5x5Block) {
    std::vector<ColorMatch> matches;
    for (int y = 0; y < 5; ++y)
        for (int x = 0; x < 5; ++x)
            matches.push_back({{x, y}, {x + 100, y + 100}, 0});

    auto clusters = ClusterFinder::find_clusters(matches, 10, 10, 1);
    ASSERT_EQ(clusters.size(), 1u);
    EXPECT_EQ(clusters[0].pixel_count, 25);
    EXPECT_EQ(clusters[0].center.x, 2);
    EXPECT_EQ(clusters[0].center.y, 2);
    EXPECT_EQ(clusters[0].bounding_rect.left, 0);
    EXPECT_EQ(clusters[0].bounding_rect.top, 0);
    EXPECT_EQ(clusters[0].bounding_rect.right, 5);
    EXPECT_EQ(clusters[0].bounding_rect.bottom, 5);
}

TEST(ClusterFinder, FiltersSmallClusters) {
    std::vector<ColorMatch> matches;
    matches.push_back({{0, 0}, {0, 0}, 0});
    matches.push_back({{1, 0}, {1, 0}, 0});

    auto clusters = ClusterFinder::find_clusters(matches, 10, 10, 5);
    EXPECT_TRUE(clusters.empty());
}

TEST(ClusterFinder, TwoSeparateClusters) {
    std::vector<ColorMatch> matches;
    // Cluster A at (0,0)-(2,2)
    for (int y = 0; y < 3; ++y)
        for (int x = 0; x < 3; ++x)
            matches.push_back({{x, y}, {x, y}, 0});
    // Cluster B at (8,8)-(9,9)
    for (int y = 8; y < 10; ++y)
        for (int x = 8; x < 10; ++x)
            matches.push_back({{x, y}, {x, y}, 0});

    auto clusters = ClusterFinder::find_clusters(matches, 10, 10, 1);
    ASSERT_EQ(clusters.size(), 2u);
    // Sorted descending by pixel_count
    EXPECT_EQ(clusters[0].pixel_count, 9);
    EXPECT_EQ(clusters[1].pixel_count, 4);
}

TEST(ClusterFinder, EmptyMatchesReturnsEmpty) {
    std::vector<ColorMatch> matches;
    auto clusters = ClusterFinder::find_clusters(matches, 10, 10, 1);
    EXPECT_TRUE(clusters.empty());
}

TEST(ClusterFinder, ZeroOrNegativeDimensionsReturnEmpty) {
    std::vector<ColorMatch> matches = {{{0, 0}, {0, 0}, 0}};
    EXPECT_TRUE(ClusterFinder::find_clusters(matches, 0, 10, 1).empty());
    EXPECT_TRUE(ClusterFinder::find_clusters(matches, 10, 0, 1).empty());
    EXPECT_TRUE(ClusterFinder::find_clusters(matches, -5, -5, 1).empty());
}
