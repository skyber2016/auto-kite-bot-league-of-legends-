#include "strategy/strategy_factory.h"
#include "strategy/auto_strategy.h"
#include "strategy/manual_strategy.h"

static AutoOrbWalkStrategy s_auto_instance;
static ManualOrbWalkStrategy s_manual_instance;

IOrbWalkStrategy* OrbWalkStrategyFactory::get(OrbWalkMode mode) {
    switch (mode) {
        case OrbWalkMode::Auto: return &s_auto_instance;
        case OrbWalkMode::Manual: return &s_manual_instance;
        default: return nullptr;
    }
}
