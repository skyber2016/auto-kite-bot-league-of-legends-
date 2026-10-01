#pragma once
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#include <cstdint>
#include "core/settings.h"

namespace InputSimulator {
    void send_attack_click(uint16_t scancode, ULONG_PTR extra = 0);
    void send_move_click(ULONG_PTR extra = 0);
    void send_mouse_down_right(ULONG_PTR extra = 0);
    void send_mouse_up_right(ULONG_PTR extra = 0);
    void send_move_and_key_down(int x, int y, uint16_t scancode, ULONG_PTR extra = 0);
    void set_cursor_position(int x, int y);
    void move_cursor_smooth(int from_x, int from_y,
                            int to_x, int to_y,
                            int steps, int total_ms);
    ULONG_PTR get_extra_info(const Settings& settings);
}
