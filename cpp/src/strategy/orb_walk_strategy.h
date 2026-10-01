#pragma once
#include <cstdint>
#include "core/settings.h"

enum class OrbWalkMode { None, Manual, Auto };

class IOrbWalkStrategy {
public:
    virtual ~IOrbWalkStrategy() = default;
    virtual bool try_attack(bool has_target, int target_x, int target_y,
                           uint16_t attack_scancode,
                           const Settings& settings) = 0;
};
