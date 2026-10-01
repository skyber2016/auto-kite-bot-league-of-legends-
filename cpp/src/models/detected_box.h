#pragma once
#include <Windows.h>
#include <string>

struct DetectedBox {
    RECT screen_rect;
    COLORREF border_color;
    std::string label;
};
