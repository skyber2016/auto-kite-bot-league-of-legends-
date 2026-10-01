#include <gtest/gtest.h>
#include "core/timing_jitter.h"

TEST(TimingJitter, ApplyWithinRange) {
    for (int i = 0; i < 200; ++i) {
        int result = TimingJitter::apply(100, 15);
        EXPECT_GE(result, 85);
        EXPECT_LE(result, 115);
    }
}

TEST(TimingJitter, ApplyPositiveWithinRange) {
    for (int i = 0; i < 200; ++i) {
        int result = TimingJitter::apply_positive(100, 10);
        EXPECT_GE(result, 100);
        EXPECT_LE(result, 110);
    }
}

TEST(TimingJitter, ApplyZeroJitter) {
    int result = TimingJitter::apply(50, 0);
    EXPECT_EQ(result, 50);
}

TEST(TimingJitter, ApplyPositiveZeroJitter) {
    int result = TimingJitter::apply_positive(50, 0);
    EXPECT_EQ(result, 50);
}

TEST(TimingJitter, NextDoubleInRange) {
    for (int i = 0; i < 200; ++i) {
        double val = TimingJitter::next_double();
        EXPECT_GE(val, 0.0);
        EXPECT_LT(val, 1.0);
    }
}
