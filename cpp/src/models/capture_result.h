#pragma once
#include <cstdint>
#include <vector>

struct CaptureResult {
    std::vector<uint8_t> buffer;  // BGRA pixel data
    int width{};
    int height{};
    int screen_x{};
    int screen_y{};
    int stride{};

    int pixel_count() const { return width * height; }
    bool is_empty() const { return buffer.empty(); }
};
