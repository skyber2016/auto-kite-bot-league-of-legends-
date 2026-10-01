#include <gtest/gtest.h>
#include "core/settings.h"
#include <filesystem>

namespace fs = std::filesystem;

class SettingsTest : public ::testing::Test {
protected:
    fs::path test_dir_;

    void SetUp() override {
        test_dir_ = fs::temp_directory_path() / "auto_kite_test_settings";
        fs::create_directories(test_dir_);
    }
    void TearDown() override {
        fs::remove_all(test_dir_);
    }
};

TEST_F(SettingsTest, DefaultValues) {
    Settings s;
    EXPECT_EQ(s.manual_key, 67);
    EXPECT_EQ(s.auto_key, 32);
    EXPECT_EQ(s.target_color_r, 52);
    EXPECT_EQ(s.target_color_g, 3);
    EXPECT_EQ(s.target_color_b, 0);
    EXPECT_EQ(s.color_tolerance, 0);
    EXPECT_EQ(s.capture_size, 1000);
    EXPECT_EQ(s.min_cluster_pixels, 10);
    EXPECT_EQ(s.detection_fps_cap, 60);
    EXPECT_EQ(s.target_offset_x, 70);
    EXPECT_EQ(s.target_offset_y, 120);
    EXPECT_FALSE(s.enable_overlay);
    EXPECT_EQ(s.windup_buffer_ms, 66);
    EXPECT_EQ(s.min_input_delay_ms, 75);
    EXPECT_EQ(s.orbwalk_tick_rate_ms, 1);
    EXPECT_EQ(s.attack_speed_poll_ms, 500);
    EXPECT_TRUE(s.enable_cursor_restore);
    EXPECT_EQ(s.input_jitter_ms, 15);
    EXPECT_EQ(s.windup_jitter_ms, 10);
    EXPECT_EQ(s.attack_move_scancode, 0x23);
    EXPECT_EQ(s.key_hold_base_ms, 40);
    EXPECT_EQ(s.key_hold_jitter_ms, 30);
    EXPECT_EQ(s.click_hold_base_ms, 30);
    EXPECT_EQ(s.click_hold_jitter_ms, 20);
    EXPECT_EQ(s.min_windup_buffer_ms, 15);
    EXPECT_TRUE(s.enable_smooth_cursor);
    EXPECT_EQ(s.cursor_steps, 3);
    EXPECT_EQ(s.cursor_move_ms, 8);
    EXPECT_FALSE(s.auto_kite_direction);
    EXPECT_EQ(s.kite_distance, 200);
    EXPECT_EQ(s.target_sticky_radius, 50);
    EXPECT_DOUBLE_EQ(s.skip_move_chance, 0.07);
    EXPECT_DOUBLE_EQ(s.extra_move_chance, 0.05);
    EXPECT_EQ(s.extra_info_mode, "native");
}

TEST_F(SettingsTest, CreateNewAndLoad) {
    auto path = test_dir_ / "settings.json";
    Settings::create_new(path);
    EXPECT_TRUE(fs::exists(path));

    Settings loaded = Settings::load(path);
    EXPECT_EQ(loaded.manual_key, 67);
    EXPECT_EQ(loaded.auto_key, 32);
    EXPECT_EQ(loaded.target_color_r, 52);
    EXPECT_EQ(loaded.target_color_g, 3);
    EXPECT_EQ(loaded.target_color_b, 0);
    EXPECT_EQ(loaded.color_tolerance, 0);
    EXPECT_EQ(loaded.capture_size, 1000);
    EXPECT_EQ(loaded.min_cluster_pixels, 10);
    EXPECT_EQ(loaded.detection_fps_cap, 60);
    EXPECT_EQ(loaded.target_offset_x, 70);
    EXPECT_EQ(loaded.target_offset_y, 120);
    EXPECT_FALSE(loaded.enable_overlay);
    EXPECT_EQ(loaded.windup_buffer_ms, 66);
    EXPECT_EQ(loaded.min_input_delay_ms, 75);
    EXPECT_EQ(loaded.orbwalk_tick_rate_ms, 1);
    EXPECT_EQ(loaded.attack_speed_poll_ms, 500);
    EXPECT_TRUE(loaded.enable_cursor_restore);
    EXPECT_EQ(loaded.input_jitter_ms, 15);
    EXPECT_EQ(loaded.windup_jitter_ms, 10);
    EXPECT_EQ(loaded.attack_move_scancode, 0x23);
    EXPECT_EQ(loaded.key_hold_base_ms, 40);
    EXPECT_EQ(loaded.key_hold_jitter_ms, 30);
    EXPECT_EQ(loaded.click_hold_base_ms, 30);
    EXPECT_EQ(loaded.click_hold_jitter_ms, 20);
    EXPECT_EQ(loaded.min_windup_buffer_ms, 15);
    EXPECT_TRUE(loaded.enable_smooth_cursor);
    EXPECT_EQ(loaded.cursor_steps, 3);
    EXPECT_EQ(loaded.cursor_move_ms, 8);
    EXPECT_FALSE(loaded.auto_kite_direction);
    EXPECT_EQ(loaded.kite_distance, 200);
    EXPECT_EQ(loaded.target_sticky_radius, 50);
    EXPECT_DOUBLE_EQ(loaded.skip_move_chance, 0.07);
    EXPECT_DOUBLE_EQ(loaded.extra_move_chance, 0.05);
    EXPECT_EQ(loaded.extra_info_mode, "native");
}

TEST_F(SettingsTest, SaveAndReload) {
    auto path = test_dir_ / "settings.json";
    Settings s;
    s.capture_size = 500;
    s.manual_key = 65;
    s.save(path);

    Settings loaded = Settings::load(path);
    EXPECT_EQ(loaded.capture_size, 500);
    EXPECT_EQ(loaded.manual_key, 65);
    // Other fields retain defaults
    EXPECT_EQ(loaded.auto_key, 32);
    EXPECT_EQ(loaded.detection_fps_cap, 60);
}

TEST_F(SettingsTest, LoadNonExistentReturnsDefaults) {
    auto path = test_dir_ / "does_not_exist.json";
    Settings loaded = Settings::load(path);
    EXPECT_EQ(loaded.manual_key, 67);
    EXPECT_EQ(loaded.auto_key, 32);
    EXPECT_EQ(loaded.capture_size, 1000);
}
