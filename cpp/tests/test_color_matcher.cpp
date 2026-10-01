#include <gtest/gtest.h>
#include "detection/color_matcher.h"
#include "models/capture_result.h"

TEST(ColorMatcher, ExactMatchOn2x2Buffer) {
    // 2x2 BGRA buffer: pixel 0 = (B=0, G=3, R=52, A=255) = target
    // pixel 1 = (B=255, G=255, R=255, A=255) = white
    // pixel 2 = (B=0, G=3, R=52, A=255) = target
    // pixel 3 = (B=0, G=0, R=0, A=255) = black
    CaptureResult capture;
    capture.width = 2;
    capture.height = 2;
    capture.screen_x = 100;
    capture.screen_y = 200;
    capture.stride = 2 * 4;
    capture.buffer = {
        0, 3, 52, 255,    255, 255, 255, 255,
        0, 3, 52, 255,    0, 0, 0, 255,
    };

    auto matches = ColorMatcher::find_pixels(capture, 52, 3, 0, 0);
    ASSERT_EQ(matches.size(), 2u);
    EXPECT_EQ(matches[0].local_pos.x, 0);
    EXPECT_EQ(matches[0].local_pos.y, 0);
    EXPECT_EQ(matches[0].screen_pos.x, 100);
    EXPECT_EQ(matches[0].screen_pos.y, 200);
    EXPECT_EQ(matches[1].local_pos.x, 0);
    EXPECT_EQ(matches[1].local_pos.y, 1);
}

TEST(ColorMatcher, ToleranceFiltering) {
    CaptureResult capture;
    capture.width = 1;
    capture.height = 1;
    capture.screen_x = 0;
    capture.screen_y = 0;
    capture.stride = 4;
    capture.buffer = {0, 5, 55, 255};  // B=0, G=5, R=55

    // Exact match fails (distance = sqrt(9+4) ≈ 3.6, dist_sq = 13)
    auto exact = ColorMatcher::find_pixels(capture, 52, 3, 0, 0);
    EXPECT_TRUE(exact.empty());

    // With tolerance 4 (16 >= 13), should match
    auto tolerant = ColorMatcher::find_pixels(capture, 52, 3, 0, 4);
    EXPECT_EQ(tolerant.size(), 1u);
}

TEST(ColorMatcher, EmptyCaptureReturnsEmpty) {
    CaptureResult capture;
    auto matches = ColorMatcher::find_pixels(capture, 52, 3, 0, 0);
    EXPECT_TRUE(matches.empty());
}
