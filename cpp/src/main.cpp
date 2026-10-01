#define NOMINMAX
#include <Windows.h>
#include <iostream>
#include <iomanip>
#include <thread>
#include <atomic>
#include <chrono>
#include <cmath>
#include <csignal>
#include <algorithm>
#include <cctype>

#pragma comment(lib, "winmm.lib")

#include "core/settings.h"
#include "core/timing_jitter.h"
#include "core/direct_input_keys.h"
#include "input/input_hooks.h"
#include "input/input_simulator.h"
#include "input/mouse_helper.h"
#include "capture/dxgi_capturer.h"
#include "detection/color_matcher.h"
#include "detection/cluster_finder.h"
#include "overlay/overlay_window.h"
#include "overlay/overlay_renderer.h"
#include "strategy/strategy_factory.h"
#include "net/riot_api_client.h"

// ── Global State ──
static Settings g_settings;
static std::atomic<bool> g_running{true};
static std::atomic<bool> g_has_process{false};
static std::atomic<bool> g_has_attack_speed{false};
static std::atomic<bool> g_has_target{false};
static std::atomic<int> g_target_x{0};
static std::atomic<int> g_target_y{0};
static std::atomic<double> g_attack_speed{0.0};

// Champion base values from CommunityDragon
static std::atomic<double> g_attack_speed_ratio{0.625};
static std::atomic<double> g_attack_delay_offset{0.3};
static std::atomic<double> g_attack_delay_scaling{1.0};
static std::atomic<double> g_attack_cast_time{0.0};

static std::atomic<OrbWalkMode> g_active_mode{OrbWalkMode::None};
static std::atomic<bool> g_detection_enabled{true};
static std::jthread g_detection_thread;
static std::jthread g_orbwalk_thread;
static std::jthread g_api_thread;

static DxgiCapturer g_capturer;
static InputHooks g_hooks;
static RiotApiClient g_api_client;
static std::unique_ptr<OverlayWindow> g_overlay_wnd;
static std::unique_ptr<OverlayRenderer> g_overlay_renderer;

// ── Windup Math (ported from Program.cs) ──
static double get_seconds_per_attack() {
    double as = g_attack_speed.load();
    return (as > 0.0) ? 1.0 / as : 1.0;
}

static double get_windup_duration() {
    double spa = get_seconds_per_attack();
    double base = (spa * g_attack_delay_offset.load() - g_attack_cast_time.load())
                  * g_attack_delay_scaling.load() + g_attack_cast_time.load();
    return std::max(0.0, base);
}

static double get_buffered_windup() {
    double windup = get_windup_duration();
    double as = g_attack_speed.load();
    double scale = g_attack_speed_ratio.load() / std::max(0.3, as);
    int adaptive_ms = std::max(g_settings.min_windup_buffer_ms,
        static_cast<int>(g_settings.windup_buffer_ms * scale));
    int buffered_ms = TimingJitter::apply_positive(adaptive_ms,
                                                    g_settings.windup_jitter_ms);
    return windup + buffered_ms / 1000.0;
}

// ── Process Check ──
static bool is_league_foreground() {
    HWND fg = GetForegroundWindow();
    if (!fg) return false;
    wchar_t title[256];
    GetWindowTextW(fg, title, 256);
    return wcsstr(title, L"League of Legends") != nullptr;
}

// ── Detection Loop ──
static void detection_loop(std::stop_token stop) {
    auto interval = std::chrono::microseconds(
        1'000'000 / g_settings.detection_fps_cap);

    while (!stop.stop_requested()) {
        auto start = std::chrono::steady_clock::now();

        if (g_active_mode.load() == OrbWalkMode::None || !g_detection_enabled.load()) {
            g_has_target = false;
            if (g_overlay_renderer) g_overlay_renderer->clear();
            auto elapsed = std::chrono::steady_clock::now() - start;
            if (elapsed < interval)
                std::this_thread::sleep_for(interval - elapsed);
            continue;
        }

        POINT cursor = MouseHelper::get_cursor_position();
        auto capture = g_capturer.capture(
            cursor.x, cursor.y, g_settings.capture_size);

        if (capture.is_empty()) {
            g_has_target = false;
            auto elapsed = std::chrono::steady_clock::now() - start;
            if (elapsed < interval)
                std::this_thread::sleep_for(interval - elapsed);
            continue;
        }

        auto matches = ColorMatcher::find_pixels(
            capture,
            g_settings.target_color_r,
            g_settings.target_color_g,
            g_settings.target_color_b,
            g_settings.color_tolerance);

        auto clusters = ClusterFinder::find_clusters(
            matches, capture.width, capture.height,
            g_settings.min_cluster_pixels);

        if (!clusters.empty()) {
            int tx = clusters[0].center.x + capture.screen_x
                     + g_settings.target_offset_x;
            int ty = clusters[0].center.y + capture.screen_y
                     + g_settings.target_offset_y;

            // Sticky target
            if (g_has_target.load()) {
                int old_x = g_target_x.load(), old_y = g_target_y.load();
                int dx = tx - old_x, dy = ty - old_y;
                if (dx * dx + dy * dy <
                    g_settings.target_sticky_radius *
                    g_settings.target_sticky_radius) {
                    tx = old_x;
                    ty = old_y;
                }
            }

            g_target_x = tx;
            g_target_y = ty;
            g_has_target = true;

            // Update overlay
            if (g_overlay_renderer) {
                std::vector<DetectedBox> boxes;
                for (const auto& c : clusters) {
                    boxes.push_back({
                        {c.bounding_rect.left + capture.screen_x,
                         c.bounding_rect.top + capture.screen_y,
                         c.bounding_rect.right + capture.screen_x,
                         c.bounding_rect.bottom + capture.screen_y},
                        RGB(0, 255, 0), ""
                    });
                }
                g_overlay_renderer->set_boxes(std::move(boxes));
                RECT scan = {
                    capture.screen_x, capture.screen_y,
                    capture.screen_x + capture.width,
                    capture.screen_y + capture.height
                };
                g_overlay_renderer->set_scan_region(scan);
            }
        } else {
            g_has_target = false;
            if (g_overlay_renderer) g_overlay_renderer->clear();
        }

        auto elapsed = std::chrono::steady_clock::now() - start;
        if (elapsed < interval)
            std::this_thread::sleep_for(interval - elapsed);
    }
}

// ── OrbWalk Loop ──
static void orbwalk_loop(std::stop_token stop) {
    auto next_attack = std::chrono::steady_clock::now();
    auto next_move = next_attack;
    auto next_input = next_attack;

    auto interval = std::chrono::milliseconds(std::max(1, g_settings.orbwalk_tick_rate_ms));

    while (!stop.stop_requested()) {
        std::this_thread::sleep_for(interval);

        if (!is_league_foreground()) continue;

        auto mode = g_active_mode.load();
        if (mode == OrbWalkMode::None) continue;
        if (!g_has_process.load() || !g_has_attack_speed.load()) continue;

        auto* strategy = OrbWalkStrategyFactory::get(mode);
        if (!strategy) continue;

        auto now = std::chrono::steady_clock::now();
        bool has_target = g_has_target.load();
        int tx = g_target_x.load();
        int ty = g_target_y.load();
        uint16_t scancode = static_cast<uint16_t>(g_settings.attack_move_scancode);

        // Attack phase
        if (now >= next_attack) {
            if (strategy->try_attack(has_target, tx, ty, scancode, g_settings)) {
                auto attack_time = std::chrono::steady_clock::now();
                double buffered_windup = get_buffered_windup();
                double spa = get_seconds_per_attack();
                next_move = attack_time + std::chrono::microseconds(static_cast<int64_t>(buffered_windup * 1e6));
                next_attack = attack_time + std::chrono::microseconds(static_cast<int64_t>(spa * 1e6));
                continue;
            }
        }

        // Move phase — throttled by next_move and next_input
        if (now >= next_move && now >= next_input) {
            // Feature 9: Pattern Scrambling — skip move chance
            double roll = TimingJitter::next_double();
            if (roll < g_settings.skip_move_chance) {
                int delay = TimingJitter::apply(g_settings.min_input_delay_ms, g_settings.input_jitter_ms);
                next_input = now + std::chrono::milliseconds(delay);
                continue;
            }

            // Feature 7: Kite Direction (Auto mode only)
            if (g_settings.auto_kite_direction && mode == OrbWalkMode::Auto && has_target) {
                POINT cursor = MouseHelper::get_cursor_position();
                double dx = static_cast<double>(cursor.x) - tx;
                double dy = static_cast<double>(cursor.y) - ty;
                double len = std::sqrt(dx * dx + dy * dy);
                if (len > 1.0) {
                    int move_x = cursor.x + static_cast<int>(dx / len * g_settings.kite_distance);
                    int move_y = cursor.y + static_cast<int>(dy / len * g_settings.kite_distance);
                    InputSimulator::set_cursor_position(move_x, move_y);
                }
            }

            // Feature 4: Humanized move-click — MouseDown, delay, MouseUp
            InputSimulator::send_mouse_down_right();
            int hold_ms = TimingJitter::apply(g_settings.click_hold_base_ms, g_settings.click_hold_jitter_ms);
            std::this_thread::sleep_for(std::chrono::milliseconds(hold_ms));
            InputSimulator::send_mouse_up_right();

            // Feature 9: Extra move chance
            if (roll > 1.0 - g_settings.extra_move_chance) {
                std::this_thread::sleep_for(std::chrono::milliseconds(TimingJitter::apply(15, 10)));
                InputSimulator::send_mouse_down_right();
                std::this_thread::sleep_for(std::chrono::milliseconds(
                    TimingJitter::apply(g_settings.click_hold_base_ms, g_settings.click_hold_jitter_ms)));
                InputSimulator::send_mouse_up_right();
            }

            // Feature 2: Jittered input delay
            int input_delay = TimingJitter::apply(g_settings.min_input_delay_ms, g_settings.input_jitter_ms);
            next_input = now + std::chrono::milliseconds(input_delay);
        }
    }
}

// ── API Polling Loop ──
static void api_polling_loop(std::stop_token stop) {
    while (!stop.stop_requested()) {
        if (g_has_process.load()) {
            auto data = g_api_client.get_active_player();
            if (data) {
                try {
                    auto& j = *data;
                    double as = j["championStats"]["attackSpeed"].get<double>();
                    double prev_as = g_attack_speed.load();
                    g_attack_speed = as;

                    if (!g_has_attack_speed.load()) {
                        g_has_attack_speed = true;
                        double spa = get_seconds_per_attack();
                        double windup = get_windup_duration();
                        double buffered = get_buffered_windup();
                        std::cout << "[AS] Ready: " << std::fixed << std::setprecision(3) << as
                                  << " | Interval: " << static_cast<int>(spa * 1000) << "ms"
                                  << " | Windup: " << static_cast<int>(windup * 1000) << "ms"
                                  << " + " << g_settings.windup_buffer_ms << "ms buf = "
                                  << static_cast<int>(buffered * 1000) << "ms"
                                  << " | Move: " << static_cast<int>((spa - buffered) * 1000) << "ms"
                                  << std::endl;
                    } else if (std::abs(as - prev_as) > 0.001) {
                        double spa = get_seconds_per_attack();
                        double windup = get_windup_duration();
                        double buffered = get_buffered_windup();
                        std::cout << "[AS] " << std::fixed << std::setprecision(3) << as
                                  << " | Interval: " << static_cast<int>(spa * 1000) << "ms"
                                  << " | Windup: " << static_cast<int>(windup * 1000) << "ms"
                                  << " + buf = " << static_cast<int>(buffered * 1000) << "ms"
                                  << " | Move: " << static_cast<int>((spa - buffered) * 1000) << "ms"
                                  << std::endl;
                    }

                    // Load champion data once
                    static bool loaded_champ = false;
                    if (!loaded_champ) {
                        std::string name = j.value("championName", std::string(""));
                        if (!name.empty()) {
                            // Lowercase using std::transform
                            std::transform(name.begin(), name.end(), name.begin(), [](unsigned char c) { return std::tolower(c); });
                            auto champ = g_api_client.get_champion_data(name);
                            if (champ) {
                                // Parse champion base attack values from CommunityDragon
                                try {
                                    const auto& champ_json = *champ;

                                    // Find the CharacterRecords/Root key (case-insensitive search)
                                    nlohmann::json root_stats;
                                    bool found_root = false;
                                    for (auto& [key, val] : champ_json.items()) {
                                        std::string lower_key = key;
                                        std::transform(lower_key.begin(), lower_key.end(), lower_key.begin(),
                                                       [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
                                        if (lower_key.find("characterrecords/root") != std::string::npos) {
                                            root_stats = val;
                                            found_root = true;
                                            break;
                                        }
                                    }

                                    if (found_root) {
                                        // attackSpeedRatio
                                        if (root_stats.contains("attackSpeedRatio") && !root_stats["attackSpeedRatio"].is_null()) {
                                            g_attack_speed_ratio.store(root_stats["attackSpeedRatio"].get<double>());
                                        }

                                        // basicAttack
                                        if (root_stats.contains("basicAttack") && root_stats["basicAttack"].is_object()) {
                                            const auto& ba = root_stats["basicAttack"];

                                            // Delay scaling
                                            if (ba.contains("mAttackDelayCastOffsetPercentAttackSpeedRatio") &&
                                                !ba["mAttackDelayCastOffsetPercentAttackSpeedRatio"].is_null()) {
                                                g_attack_delay_scaling.store(
                                                    ba["mAttackDelayCastOffsetPercentAttackSpeedRatio"].get<double>());
                                            }

                                            // Delay offset
                                            if (ba.contains("mAttackDelayCastOffsetPercent") &&
                                                !ba["mAttackDelayCastOffsetPercent"].is_null()) {
                                                double current = g_attack_delay_offset.load();
                                                g_attack_delay_offset.store(
                                                    current + ba["mAttackDelayCastOffsetPercent"].get<double>());
                                            } else if (ba.contains("mAttackTotalTime") && !ba["mAttackTotalTime"].is_null() &&
                                                       ba.contains("mAttackCastTime") && !ba["mAttackCastTime"].is_null()) {
                                                double total = ba["mAttackTotalTime"].get<double>();
                                                double cast = ba["mAttackCastTime"].get<double>();
                                                g_attack_cast_time.store(cast);
                                                if (total > 0.0) {
                                                    g_attack_delay_offset.store(cast / total);
                                                }
                                            } else if (ba.contains("mAttackName") && !ba["mAttackName"].is_null()) {
                                                std::string attack_name = ba["mAttackName"].get<std::string>();
                                                if (!attack_name.empty()) {
                                                    std::string lower_attack = attack_name;
                                                    std::transform(lower_attack.begin(), lower_attack.end(), lower_attack.begin(),
                                                                   [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
                                                    for (auto& [key, val] : champ_json.items()) {
                                                        std::string lower_key = key;
                                                        std::transform(lower_key.begin(), lower_key.end(), lower_key.begin(),
                                                                       [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
                                                        if (lower_key.find("spells/" + lower_attack) != std::string::npos) {
                                                            if (val.contains("mSpell") && val["mSpell"].is_object() &&
                                                                val["mSpell"].contains("delayCastOffsetPercent") &&
                                                                !val["mSpell"]["delayCastOffsetPercent"].is_null()) {
                                                                double offset = val["mSpell"]["delayCastOffsetPercent"].get<double>();
                                                                g_attack_delay_offset.store(g_attack_delay_offset.load() + offset);
                                                            }
                                                            break;
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        std::cout << "[Init] Champion stats loaded from CommunityDragon" << std::endl;
                                        std::cout << "  AS Ratio:      " << g_attack_speed_ratio.load() << std::endl;
                                        std::cout << "  Delay%:        " << g_attack_delay_offset.load() << std::endl;
                                        std::cout << "  Delay Scaling: " << g_attack_delay_scaling.load() << std::endl;
                                        std::cout << "  Cast Time:     " << g_attack_cast_time.load() << "s" << std::endl;
                                    } else {
                                        std::cerr << "[Init] No root stats found in CommunityDragon data" << std::endl;
                                    }
                                } catch (const std::exception& e) {
                                    std::cerr << "[Init] Failed to parse CommunityDragon data: " << e.what() << std::endl;
                                }
                                loaded_champ = true;
                            }
                        }
                    }
                } catch (...) {}
            }
        }

        for (int i = 0; i < g_settings.attack_speed_poll_ms / 100 &&
             !stop.stop_requested(); ++i) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
        }
    }
}

// ── Keyboard Handler ──
static void on_keyboard(int vk, bool down) {
    if (down && !is_league_foreground()) return;

    if (vk == g_settings.manual_key) {
        if (down) {
            auto expected = OrbWalkMode::None;
            g_active_mode.compare_exchange_strong(expected,
                                                   OrbWalkMode::Manual);
        } else {
            auto expected = OrbWalkMode::Manual;
            g_active_mode.compare_exchange_strong(expected,
                                                   OrbWalkMode::None);
        }
    } else if (vk == g_settings.auto_key) {
        if (down) {
            auto expected = OrbWalkMode::None;
            g_active_mode.compare_exchange_strong(expected,
                                                   OrbWalkMode::Auto);
        } else {
            auto expected = OrbWalkMode::Auto;
            g_active_mode.compare_exchange_strong(expected,
                                                   OrbWalkMode::None);
        }
    } else if (vk == 0x4D && down) { // M key — toggle target detection
        bool prev = g_detection_enabled.load();
        g_detection_enabled.store(!prev);
        bool now_on = !prev;
        std::cout << "[Toggle] Detection: " << (now_on ? "ON" : "OFF") << std::endl;
        // Audio feedback — high beep = ON, low beep = OFF
        Beep(now_on ? 800 : 400, 150);
    }
}

// ── Ctrl+C Handler ──
static BOOL WINAPI console_handler(DWORD event) {
    if (event == CTRL_C_EVENT || event == CTRL_CLOSE_EVENT) {
        g_running = false;
        return TRUE;
    }
    return FALSE;
}

// ── Main ──
int main() {
    SetConsoleCtrlHandler(console_handler, TRUE);
    timeBeginPeriod(1);

    // Load settings
    auto settings_path = std::filesystem::current_path() / "settings.json";
    if (std::filesystem::exists(settings_path)) {
        g_settings = Settings::load(settings_path);
        std::cout << "[OK] Settings loaded\n";
    } else {
        Settings::create_new(settings_path);
        g_settings = Settings::load(settings_path);
        std::cout << "[OK] Default settings created\n";
    }

    // Init DXGI
    if (!g_capturer.initialize()) {
        std::cerr << "[FAIL] DXGI init failed\n";
        timeEndPeriod(1);
        return 1;
    }
    std::cout << "[OK] DXGI capturer initialized\n";

    // Init overlay
    if (g_settings.enable_overlay) {
        g_overlay_wnd = std::make_unique<OverlayWindow>();
        g_overlay_wnd->start();
        g_overlay_renderer = std::make_unique<OverlayRenderer>();
        g_overlay_renderer->start(g_overlay_wnd->hwnd());
        std::cout << "[OK] Overlay started\n";
    }

    // Start hooks
    g_hooks.start(on_keyboard);
    std::cout << "[OK] Input hooks active\n";

    // Start threads
    g_has_process = true;  // Simplified — C# polls for process
    g_detection_thread = std::jthread(detection_loop);
    g_orbwalk_thread = std::jthread(orbwalk_loop);
    g_api_thread = std::jthread(api_polling_loop);

    std::cout << "\n=== SidaAutoCarry C++ ===" << std::endl;
    std::cout << "\n[Config] Keys:" << std::endl;
    std::cout << "  Manual key:     VK " << g_settings.manual_key << std::endl;
    std::cout << "  Auto key:       VK " << g_settings.auto_key << std::endl;
    std::cout << "  Attack scancode: 0x" << std::hex << g_settings.attack_move_scancode << std::dec << std::endl;
    std::cout << "  Toggle attack:  M" << std::endl;

    std::cout << "\n[Config] Timing:" << std::endl;
    std::cout << "  Orbwalk tick:   " << g_settings.orbwalk_tick_rate_ms << "ms" << std::endl;
    std::cout << "  Windup buffer:  " << g_settings.windup_buffer_ms << "ms" << std::endl;
    std::cout << "  Min buffer:     " << g_settings.min_windup_buffer_ms << "ms" << std::endl;
    std::cout << "  Windup jitter:  " << g_settings.windup_jitter_ms << "ms" << std::endl;
    std::cout << "  Input delay:    " << g_settings.min_input_delay_ms << "ms +/- " << g_settings.input_jitter_ms << "ms" << std::endl;
    std::cout << "  Click hold:     " << g_settings.click_hold_base_ms << "ms +/- " << g_settings.click_hold_jitter_ms << "ms" << std::endl;
    std::cout << "  Key hold:       " << g_settings.key_hold_base_ms << "ms +/- " << g_settings.key_hold_jitter_ms << "ms" << std::endl;

    std::cout << "\n[Config] Detection:" << std::endl;
    std::cout << "  Target color:   RGB(" << (int)g_settings.target_color_r << ", " << (int)g_settings.target_color_g << ", " << (int)g_settings.target_color_b << ")" << std::endl;
    std::cout << "  Tolerance:      " << g_settings.color_tolerance << std::endl;
    std::cout << "  Min pixels:     " << g_settings.min_cluster_pixels << std::endl;
    std::cout << "  Capture size:   " << g_settings.capture_size << "px" << std::endl;
    std::cout << "  Detect FPS cap: " << g_settings.detection_fps_cap << std::endl;

    std::cout << "\n[Config] Features:" << std::endl;
    std::cout << "  Overlay:        " << (g_settings.enable_overlay ? "ON" : "OFF") << std::endl;
    std::cout << "  Kite direction: " << (g_settings.auto_kite_direction ? "ON" : "OFF") << std::endl;
    std::cout << "  Kite distance:  " << g_settings.kite_distance << "px" << std::endl;
    std::cout << "  Skip move:      " << (g_settings.skip_move_chance * 100) << "%" << std::endl;
    std::cout << "  Extra move:     " << (g_settings.extra_move_chance * 100) << "%" << std::endl;
    std::cout << "  Cursor restore: " << (g_settings.enable_cursor_restore ? "ON" : "OFF") << std::endl;
    std::cout << "  Smooth cursor:  " << (g_settings.enable_smooth_cursor ? "ON" : "OFF") << std::endl;

    std::cout << "\nPress Ctrl+C to exit\n" << std::endl;

    // Wait for exit
    while (g_running.load()) {
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }

    // Cleanup
    std::cout << "\nShutting down...\n";
    g_detection_thread.request_stop();
    g_orbwalk_thread.request_stop();
    g_api_thread.request_stop();
    // jthread destructors auto-join

    g_hooks.stop();

    if (g_overlay_renderer) g_overlay_renderer->stop();
    if (g_overlay_wnd) g_overlay_wnd->stop();

    timeEndPeriod(1);
    std::cout << "[OK] Clean exit\n";
    return 0;
}
