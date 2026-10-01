#pragma once

class TimingJitter {
public:
    static int apply(int base_ms, int jitter_ms);
    static int apply_positive(int base_ms, int jitter_ms);
    static double next_double();
};
