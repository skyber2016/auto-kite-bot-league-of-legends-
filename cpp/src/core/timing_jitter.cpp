#include "core/timing_jitter.h"
#include <random>

static thread_local std::mt19937 t_rng{std::random_device{}()};

int TimingJitter::apply(int base_ms, int jitter_ms) {
    if (jitter_ms <= 0) return base_ms;
    std::uniform_int_distribution<int> dist(-jitter_ms, jitter_ms);
    return base_ms + dist(t_rng);
}

int TimingJitter::apply_positive(int base_ms, int jitter_ms) {
    if (jitter_ms <= 0) return base_ms;
    std::uniform_int_distribution<int> dist(0, jitter_ms);
    return base_ms + dist(t_rng);
}

double TimingJitter::next_double() {
    std::uniform_real_distribution<double> dist(0.0, 1.0);
    return dist(t_rng);
}
