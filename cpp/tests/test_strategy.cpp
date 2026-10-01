#include <gtest/gtest.h>
#include "strategy/strategy_factory.h"
#include "strategy/auto_strategy.h"
#include "strategy/manual_strategy.h"

TEST(StrategyFactory, ManualReturnsManualStrategy) {
    auto* s = OrbWalkStrategyFactory::get(OrbWalkMode::Manual);
    ASSERT_NE(s, nullptr);
    EXPECT_NE(dynamic_cast<ManualOrbWalkStrategy*>(s), nullptr);
}

TEST(StrategyFactory, AutoReturnsAutoStrategy) {
    auto* s = OrbWalkStrategyFactory::get(OrbWalkMode::Auto);
    ASSERT_NE(s, nullptr);
    EXPECT_NE(dynamic_cast<AutoOrbWalkStrategy*>(s), nullptr);
}

TEST(StrategyFactory, NoneReturnsNull) {
    auto* s = OrbWalkStrategyFactory::get(OrbWalkMode::None);
    EXPECT_EQ(s, nullptr);
}

TEST(StrategyFactory, InvalidModeReturnsNull) {
    auto* s = OrbWalkStrategyFactory::get(static_cast<OrbWalkMode>(999));
    EXPECT_EQ(s, nullptr);
}

TEST(ManualStrategy, ReturnsFalseWithNoTarget) {
    ManualOrbWalkStrategy strategy;
    Settings settings;
    bool result = strategy.try_attack(false, 0, 0, 0x23, settings);
    EXPECT_FALSE(result);
}
