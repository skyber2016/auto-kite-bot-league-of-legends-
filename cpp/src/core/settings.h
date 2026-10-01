#pragma once
#include <cstdint>
#include <string>
#include <filesystem>
#include <nlohmann/json.hpp>

struct Settings {
    // Keybinds
    int manual_key = 67;
    int auto_key = 32;

    // Target vision
    uint8_t target_color_r = 52;
    uint8_t target_color_g = 3;
    uint8_t target_color_b = 0;
    int color_tolerance = 0;
    int capture_size = 1000;
    int min_cluster_pixels = 10;
    int detection_fps_cap = 60;
    int target_offset_x = 70;
    int target_offset_y = 120;

    // Overlay
    bool enable_overlay = false;

    // Orb-walk timings
    int windup_buffer_ms = 15;
    int min_input_delay_ms = 75;
    int orbwalk_tick_rate_ms = 1;
    int attack_speed_poll_ms = 500;

    // Anti-detection
    bool enable_cursor_restore = true;
    int input_jitter_ms = 15;
    int windup_jitter_ms = 5;
    uint16_t attack_move_scancode = 0x23;
    int key_hold_base_ms = 40;
    int key_hold_jitter_ms = 30;
    int click_hold_base_ms = 30;
    int click_hold_jitter_ms = 20;
    int min_windup_buffer_ms = 5;
    bool enable_smooth_cursor = true;
    int cursor_steps = 3;
    int cursor_move_ms = 8;
    bool auto_kite_direction = false;
    int kite_distance = 200;
    int target_sticky_radius = 50;
    double skip_move_chance = 0.07;
    double extra_move_chance = 0.05;
    std::string extra_info_mode = "native";

    static Settings load(const std::filesystem::path& path);
    static void create_new(const std::filesystem::path& path);
    void save(const std::filesystem::path& path) const;

    NLOHMANN_DEFINE_TYPE_INTRUSIVE_WITH_DEFAULT(Settings,
        manual_key, auto_key,
        target_color_r, target_color_g, target_color_b,
        color_tolerance, capture_size, min_cluster_pixels,
        detection_fps_cap, target_offset_x, target_offset_y,
        enable_overlay,
        windup_buffer_ms, min_input_delay_ms, orbwalk_tick_rate_ms,
        attack_speed_poll_ms,
        enable_cursor_restore, input_jitter_ms, windup_jitter_ms,
        attack_move_scancode, key_hold_base_ms, key_hold_jitter_ms,
        click_hold_base_ms, click_hold_jitter_ms, min_windup_buffer_ms,
        enable_smooth_cursor, cursor_steps, cursor_move_ms,
        auto_kite_direction, kite_distance, target_sticky_radius,
        skip_move_chance, extra_move_chance, extra_info_mode
    )
};
