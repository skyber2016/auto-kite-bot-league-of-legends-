#include "input/input_simulator.h"
#include <thread>
#include <random>
#include <chrono>
#include <cmath>

static thread_local std::mt19937 t_rng{std::random_device{}()};

static int screen_width() { return GetSystemMetrics(SM_CXSCREEN); }
static int screen_height() { return GetSystemMetrics(SM_CYSCREEN); }

static INPUT make_key_input(uint16_t scancode, DWORD flags, ULONG_PTR extra) {
    INPUT inp{};
    inp.type = INPUT_KEYBOARD;
    inp.ki.wScan = scancode;
    inp.ki.dwFlags = KEYEVENTF_SCANCODE | flags;
    inp.ki.dwExtraInfo = extra;
    return inp;
}

static INPUT make_mouse_input(DWORD flags, ULONG_PTR extra,
                               int dx = 0, int dy = 0) {
    INPUT inp{};
    inp.type = INPUT_MOUSE;
    inp.mi.dwFlags = flags;
    inp.mi.dx = dx;
    inp.mi.dy = dy;
    inp.mi.dwExtraInfo = extra;
    return inp;
}

void InputSimulator::send_attack_click(uint16_t scancode, ULONG_PTR extra) {
    INPUT inputs[2] = {
        make_key_input(scancode, 0, extra),
        make_key_input(scancode, KEYEVENTF_KEYUP, extra),
    };
    SendInput(2, inputs, sizeof(INPUT));
}

void InputSimulator::send_move_click(ULONG_PTR extra) {
    INPUT inputs[2] = {
        make_mouse_input(MOUSEEVENTF_RIGHTDOWN, extra),
        make_mouse_input(MOUSEEVENTF_RIGHTUP, extra),
    };
    SendInput(2, inputs, sizeof(INPUT));
}

void InputSimulator::send_move_and_key_down(int x, int y, uint16_t scancode, ULONG_PTR extra) {
    int sw = screen_width(), sh = screen_height();
    if (sw <= 0) sw = 1;
    if (sh <= 0) sh = 1;
    int abs_x = static_cast<int>(static_cast<double>(x) / sw * 65535.0);
    int abs_y = static_cast<int>(static_cast<double>(y) / sh * 65535.0);
    INPUT inputs[2] = {
        make_mouse_input(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE, extra,
                         abs_x, abs_y),
        make_key_input(scancode, 0, extra),
    };
    SendInput(2, inputs, sizeof(INPUT));
}

void InputSimulator::set_cursor_position(int x, int y) {
    SetCursorPos(x, y);
}

void InputSimulator::move_cursor_smooth(int from_x, int from_y,
                                        int to_x, int to_y,
                                        int steps, int total_ms) {
    if (steps <= 0) steps = 1;
    int step_ms = total_ms / steps;
    std::uniform_int_distribution<int> jitter(-2, 2);

    for (int i = 1; i <= steps; ++i) {
        double t = static_cast<double>(i) / steps;
        int x = from_x + static_cast<int>((to_x - from_x) * t) + jitter(t_rng);
        int y = from_y + static_cast<int>((to_y - from_y) * t) + jitter(t_rng);
        SetCursorPos(x, y);
        if (step_ms > 0)
            std::this_thread::sleep_for(std::chrono::milliseconds(step_ms));
    }
    SetCursorPos(to_x, to_y);
}

ULONG_PTR InputSimulator::get_extra_info(const Settings& settings) {
    if (settings.extra_info_mode == "zero")
        return 0;
    return static_cast<ULONG_PTR>(GetMessageExtraInfo());
}
