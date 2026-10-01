#pragma once
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include "models/capture_result.h"
#include "models/color_match.h"
#include <cstdint>
#include <vector>

namespace ColorMatcher {
    std::vector<ColorMatch> find_pixels(
        const CaptureResult& capture,
        uint8_t target_r, uint8_t target_g, uint8_t target_b,
        int tolerance);
}
