#pragma once
#include <Windows.h>

struct ColorCluster {
    RECT bounding_rect;
    POINT center;
    int pixel_count;
};
