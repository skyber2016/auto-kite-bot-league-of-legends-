#pragma once
#include "strategy/orb_walk_strategy.h"

class OrbWalkStrategyFactory {
public:
    static IOrbWalkStrategy* get(OrbWalkMode mode);
};
