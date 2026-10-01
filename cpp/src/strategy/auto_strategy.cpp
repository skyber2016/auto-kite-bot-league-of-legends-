#include "strategy/auto_strategy.h"
#include "input/input_simulator.h"
#include "core/timing_jitter.h"
#include <thread>
#include <chrono>

bool AutoOrbWalkStrategy::try_attack(bool /*has_target*/, int /*target_x*/,
                                     int /*target_y*/,
                                     uint16_t attack_scancode,
                                     const Settings& settings) {
    InputSimulator::send_attack_click(attack_scancode);
    int hold_ms = TimingJitter::apply(settings.key_hold_base_ms,
                                       settings.key_hold_jitter_ms);
    if (hold_ms > 0)
        std::this_thread::sleep_for(std::chrono::milliseconds(hold_ms));
    return true;
}
