#pragma once
#include <Windows.h>

struct ColorMatch {
    POINT local_pos;
    POINT screen_pos;
    int distance_sq;
};
