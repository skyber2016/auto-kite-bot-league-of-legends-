#define NOMINMAX
#include <Windows.h>
#include <iostream>
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
static std::atomic<double> g_seconds_per_attack{0.0};
static std::atomic<double> g_windup_duration{0.0};

// Champion base values from CommunityDragon
static double g_attack_speed_ratio = 0.625;
static double g_attack_delay_offset = 0.0;
static double g_attack_delay_scaling = 1.0;
static double g_attack_cast_time = 0.0;

static std::atomic<OrbWalkMode> g_active_mode{OrbWalkMode::None};
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
    double base = (spa * g_attack_delay_offset - g_attack_cast_time)
                  * g_attack_delay_scaling + g_attack_cast_time;
    return std::max(0.0, base);
}

static double get_buffered_windup() {
    double windup = get_windup_duration();
    double as = g_attack_speed.load();
    double scale = g_attack_speed_ratio / std::max(0.3, as);
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

        if (g_active_mode.load() == OrbWalkMode::None) {
            g_has_target = false;
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
    auto interval = std::chrono::milliseconds(g_settings.orbwalk_tick_rate_ms);
    auto last_attack = std::chrono::steady_clock::now()
                       - std::chrono::seconds(10);

    while (!stop.stop_requested()) {
        auto start = std::chrono::steady_clock::now();
        auto mode = g_active_mode.load();

        if (mode == OrbWalkMode::None || !g_has_attack_speed.load()) {
            std::this_thread::sleep_for(interval);
            continue;
        }

        auto* strategy = OrbWalkStrategyFactory::get(mode);
        if (!strategy) {
            std::this_thread::sleep_for(interval);
            continue;
        }

        double buffered_windup = get_buffered_windup();
        double spa = get_seconds_per_attack();

        auto now = std::chrono::steady_clock::now();
        double since_attack = std::chrono::duration<double>(
            now - last_attack).count();

        if (since_attack >= spa) {
            // Attack phase
            bool has = g_has_target.load();
            int tx = g_target_x.load(), ty = g_target_y.load();

            bool attacked = strategy->try_attack(
                has, tx, ty,
                g_settings.attack_move_scancode, g_settings);

            if (attacked) {
                last_attack = std::chrono::steady_clock::now();
                // Wait for windup
                int windup_ms = static_cast<int>(buffered_windup * 1000);
                if (windup_ms > 0)
                    std::this_thread::sleep_for(
                        std::chrono::milliseconds(windup_ms));
            }
        } else if (since_attack >= buffered_windup) {
            // Move phase
            double roll = TimingJitter::next_double();
            if (roll >= g_settings.skip_move_chance) {
                InputSimulator::send_move_click();
                int delay = TimingJitter::apply(
                    g_settings.min_input_delay_ms,
                    g_settings.input_jitter_ms);
                if (delay > 0)
                    std::this_thread::sleep_for(
                        std::chrono::milliseconds(delay));

                // Extra move chance
                if (TimingJitter::next_double() < g_settings.extra_move_chance) {
                    std::this_thread::sleep_for(std::chrono::milliseconds(
                        TimingJitter::apply(20, 10)));
                    InputSimulator::send_move_click();
                }
            }
        }

        auto elapsed = std::chrono::steady_clock::now() - start;
        if (elapsed < interval)
            std::this_thread::sleep_for(interval - elapsed);
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
                    g_attack_speed = as;
                    g_has_attack_speed = true;

                    // Load champion data once
                    static bool loaded_champ = false;
                    if (!loaded_champ) {
                        std::string name = j["championName"].get<std::string>();
                        // Lowercase using std::transform
                        std::transform(name.begin(), name.end(), name.begin(), [](unsigned char c) { return std::tolower(c); });
                        auto champ = g_api_client.get_champion_data(name);
                        if (champ) {
                            // Parse base attack values from CommunityDragon
                            // (simplified — actual parsing depends on JSON structure)
                            loaded_champ = true;
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
    if (!is_league_foreground()) return;

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

    std::cout << "\n=== SidaAutoCarry C++ ===\n";
    std::cout << "Manual: hold key " << g_settings.manual_key << "\n";
    std::cout << "Auto:   hold key " << g_settings.auto_key << "\n";
    std::cout << "Press Ctrl+C to exit\n\n";

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
