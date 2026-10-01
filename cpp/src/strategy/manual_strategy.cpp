#include "strategy/manual_strategy.h"
#include "input/input_simulator.h"
#include "input/mouse_helper.h"
#include "core/timing_jitter.h"
#include <thread>
#include <chrono>
#include <cmath>

bool ManualOrbWalkStrategy::try_attack(bool has_target, int target_x,
                                       int target_y,
                                       uint16_t attack_scancode,
                                       const Settings& settings) {
    if (!has_target) return false;

    POINT original = MouseHelper::get_cursor_position();

    // Move cursor to target
    if (settings.enable_smooth_cursor) {
        InputSimulator::move_cursor_smooth(
            original.x, original.y, target_x, target_y,
            settings.cursor_steps, settings.cursor_move_ms);
    } else {
        InputSimulator::set_cursor_position(target_x, target_y);
    }

    std::this_thread::sleep_for(std::chrono::milliseconds(5));

    // Attack
    InputSimulator::send_attack_click(attack_scancode);
    int hold_ms = TimingJitter::apply(settings.key_hold_base_ms,
                                       settings.key_hold_jitter_ms);
    if (hold_ms > 0)
        std::this_thread::sleep_for(std::chrono::milliseconds(hold_ms));

    // Restore cursor
    if (settings.enable_cursor_restore) {
        InputSimulator::set_cursor_position(original.x, original.y);
        std::this_thread::sleep_for(std::chrono::milliseconds(2));
        POINT check = MouseHelper::get_cursor_position();
        int dx = check.x - original.x, dy = check.y - original.y;
        if (dx * dx + dy * dy > 25) {
            InputSimulator::set_cursor_position(original.x, original.y);
        }
    }
    return true;
}
