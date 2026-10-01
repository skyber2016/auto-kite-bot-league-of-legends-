#pragma once
#include "strategy/orb_walk_strategy.h"

class AutoOrbWalkStrategy : public IOrbWalkStrategy {
public:
    bool try_attack(bool has_target, int target_x, int target_y,
                   uint16_t attack_scancode,
                   const Settings& settings) override;
};
