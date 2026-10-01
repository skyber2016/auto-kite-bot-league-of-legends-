#include "detection/color_matcher.h"

std::vector<ColorMatch> ColorMatcher::find_pixels(
    const CaptureResult& capture,
    uint8_t target_r, uint8_t target_g, uint8_t target_b,
    int tolerance)
{
    if (capture.is_empty() || capture.width <= 0 || capture.height <= 0) {
        return {};
    }

    const int tol_sq = tolerance * tolerance;
    const int stride = capture.stride > 0 ? capture.stride : capture.width * 4;
    const uint8_t* row_ptr = capture.buffer.data();
    std::vector<ColorMatch> matches;

    for (int y = 0; y < capture.height; ++y) {
        const uint8_t* ptr = row_ptr;
        for (int x = 0; x < capture.width; ++x, ptr += 4) {
            int dr = static_cast<int>(ptr[2]) - target_r;
            int dg = static_cast<int>(ptr[1]) - target_g;
            int db = static_cast<int>(ptr[0]) - target_b;
            int dist_sq = dr * dr + dg * dg + db * db;

            if (dist_sq <= tol_sq) {
                matches.push_back({
                    {static_cast<LONG>(x), static_cast<LONG>(y)},
                    {static_cast<LONG>(capture.screen_x + x),
                     static_cast<LONG>(capture.screen_y + y)},
                    dist_sq
                });
            }
        }
        row_ptr += stride;
    }
    return matches;
}
