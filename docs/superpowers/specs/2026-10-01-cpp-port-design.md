# C++ Native Port — Design Specification

**Date:** 2026-10-01
**Status:** Draft
**Scope:** Full rewrite of auto-kite bot from C# .NET 10 to native C++20

## 1. Overview

Port the entire auto-kite bot (SidaAutoCarry) from C# .NET 10 to native C++20.
Goal: eliminate .NET runtime dependency entirely — pure native Windows x64 executable.

### Technology Stack

| Concern | C# (current) | C++ (target) |
|---|---|---|
| Language | C# 13 / .NET 10 | C++20 (MSVC) |
| Build | MSBuild .csproj/.sln | CMake + vcpkg |
| JSON | Newtonsoft.Json | nlohmann/json (vcpkg) |
| HTTP | System.Net.Http.HttpClient | libcurl (vcpkg) |
| DirectX | Vortice.Direct2D1/3D11/DXGI | Raw COM APIs + DirectXTK |
| Input hooks | LowLevelInput.Net (managed) | Raw Win32 SetWindowsHookExW |
| Input simulation | Win32 SendInput (P/Invoke) | Win32 SendInput (direct) |
| Testing | xUnit | Google Test (vcpkg) |
| Threading | async/await + PeriodicTimer | std::jthread + std::chrono |
| COM pointers | Vortice managed wrappers | Microsoft::WRL::ComPtr<> |
| Memory | GC + unsafe blocks | RAII + raw pointers |

## 2. Project Structure

```
cpp/
├── CMakeLists.txt                  # Root CMake (C++20, vcpkg toolchain)
├── vcpkg.json                      # Manifest dependencies
├── src/
│   ├── main.cpp                    # Entry point, lifecycle
│   ├── core/
│   │   ├── settings.h / settings.cpp
│   │   ├── timing_jitter.h / timing_jitter.cpp
│   │   └── direct_input_keys.h
│   ├── input/
│   │   ├── input_simulator.h / input_simulator.cpp
│   │   ├── mouse_helper.h / mouse_helper.cpp
│   │   └── input_hooks.h / input_hooks.cpp
│   ├── capture/
│   │   └── dxgi_capturer.h / dxgi_capturer.cpp
│   ├── detection/
│   │   ├── color_matcher.h / color_matcher.cpp
│   │   └── cluster_finder.h / cluster_finder.cpp
│   ├── overlay/
│   │   ├── overlay_window.h / overlay_window.cpp
│   │   └── overlay_renderer.h / overlay_renderer.cpp
│   ├── strategy/
│   │   ├── orb_walk_strategy.h     # Pure virtual interface
│   │   ├── auto_strategy.h / auto_strategy.cpp
│   │   ├── manual_strategy.h / manual_strategy.cpp
│   │   └── strategy_factory.h / strategy_factory.cpp
│   ├── net/
│   │   └── riot_api_client.h / riot_api_client.cpp
│   └── models/
│       ├── capture_result.h
│       ├── color_cluster.h
│       ├── color_match.h
│       └── detected_box.h
├── tests/
│   ├── CMakeLists.txt
│   ├── test_settings.cpp
│   ├── test_timing_jitter.cpp
│   ├── test_color_matcher.cpp
│   ├── test_cluster_finder.cpp
│   └── test_strategy.cpp
└── cmake/
    └── (helper modules if needed)
```

### vcpkg.json

```json
{
  "name": "auto-kite-cpp",
  "version-string": "1.0.0",
  "dependencies": [
    "nlohmann-json",
    "curl",
    "gtest",
    "directxtk"
  ]
}
```

### CMakeLists.txt (root)

- `CMAKE_CXX_STANDARD 20`
- `target_link_libraries`: d3d11, dxgi, d2d1, dwmapi, user32, winmm + vcpkg packages
- Two targets: `auto_kite` (executable) and `auto_kite_tests` (gtest)
- Windows-only: `WIN32_EXECUTABLE` off (console app)
- `/W4` warnings, release optimizations `/O2`

## 3. Threading & Concurrency

### Thread Architecture

| Thread | Type | Purpose | Pacing |
|---|---|---|---|
| Main | OS main thread | CLI, lifecycle, Ctrl+C handler | Blocks on signal |
| Hook | std::thread | Win32 message pump for WH_KEYBOARD_LL/WH_MOUSE_LL | GetMessage loop |
| Detection | std::jthread | DXGI capture → color match → cluster | sleep_for(~16ms) |
| OrbWalk | std::jthread | Strategy execution → SendInput | sleep_for(1ms) |
| API Polling | std::jthread | libcurl GET → parse JSON → store attack speed | sleep_for(500ms) |
| Overlay Window | std::thread | Win32 HWND message pump | GetMessage loop |
| Overlay Render | std::jthread | D2D1 draw boxes | sleep_for(~16ms) |

### Synchronization

```cpp
// Lock-free hot path
std::atomic<int32_t> target_x_{0}, target_y_{0};
std::atomic<bool> has_target_{false};
std::atomic<double> attack_speed_{0.0};
std::atomic<bool> has_attack_speed_data_{false};
std::atomic<bool> has_process_{false};

// Mutex for cold path (overlay data)
std::mutex overlay_mutex_;
std::vector<DetectedBox> overlay_boxes_;

// Lifecycle
std::jthread detection_thread_, orbwalk_thread_, api_thread_, render_thread_;
std::thread hook_thread_, overlay_wnd_thread_;
```

### Loop Pattern

```cpp
void detection_loop(std::stop_token stop) {
    auto interval = std::chrono::microseconds(1'000'000 / settings_.detection_fps_cap);
    while (!stop.stop_requested()) {
        auto start = std::chrono::steady_clock::now();
        // ... work ...
        auto elapsed = std::chrono::steady_clock::now() - start;
        if (elapsed < interval)
            std::this_thread::sleep_for(interval - elapsed);
    }
}
```

### C# → C++ Mapping

| C# | C++ |
|---|---|
| `CancellationTokenSource` | `std::stop_source` / `stop_token` (built into jthread) |
| `PeriodicTimer(TimeSpan)` | Manual sleep_for(interval - elapsed) |
| `Interlocked.Exchange` | `std::atomic<>.store(val, memory_order_release)` |
| `volatile bool` | `std::atomic<bool>` |
| `ThreadLocal<Random>` | `thread_local std::mt19937` |
| `Task.Run(...)` | `std::jthread(lambda)` |
| `ManualResetEventSlim` | `std::condition_variable` + `std::mutex` |

## 4. DXGI Capture

Raw COM APIs replacing Vortice wrappers. Logic is 1:1 identical to C# `DxgiCapturer`.

```cpp
class DxgiCapturer {
    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11DeviceContext> context_;

    struct MonitorInfo {
        RECT bounds;
        ComPtr<IDXGIOutput1> output;
        ComPtr<IDXGIOutputDuplication> duplication;
        ComPtr<ID3D11Texture2D> staging;
    };
    std::vector<MonitorInfo> monitors_;
    std::vector<uint8_t> buffer_;  // reusable, zero-alloc hot path

public:
    CaptureResult capture(int center_x, int center_y, int size);
    // Handles DXGI_ERROR_ACCESS_LOST by recreating duplication
};
```

Flow:
1. CreateDXGIFactory1 → enumerate adapters/outputs
2. D3D11CreateDevice with BGRA support
3. DuplicateOutput → AcquireNextFrame → CopyResource → ReleaseFrame
4. Map staging texture → memcpy cropped rows → unmap

## 5. Color Detection

### ColorMatcher

Native pointer scan — no unsafe keyword needed, no bounds checking overhead.

```cpp
std::vector<ColorMatch> find_pixels(
    const CaptureResult& capture,
    uint8_t r, uint8_t g, uint8_t b,
    int tolerance);
```

Euclidean distance in RGB space: `dr² + dg² + db² ≤ tolerance²`
Compare against squared tolerance to avoid sqrt.

### ClusterFinder

8-directional BFS, identical algorithm to C#.

```cpp
std::vector<ColorCluster> find_clusters(
    const std::vector<ColorMatch>& matches,
    int width, int height,
    int min_pixels);
```

Uses `std::vector<bool>` grid + `std::queue<POINT>` for BFS traversal.
Outputs sorted by pixel_count descending.

## 6. Input System

### InputHooks (replacing LowLevelInput.Net)

```cpp
class InputHooks {
    static LRESULT CALLBACK keyboard_proc(int code, WPARAM wp, LPARAM lp);
    static LRESULT CALLBACK mouse_proc(int code, WPARAM wp, LPARAM lp);

    std::thread hook_thread_;  // dedicated Win32 message pump
    HHOOK kb_hook_{}, mouse_hook_{};

    // Callbacks
    std::function<void(int vk, bool down)> on_keyboard_;
    std::function<void(int msg, POINT pt)> on_mouse_;

public:
    void start();
    void stop();  // PostThreadMessage(WM_QUIT) → join
};
```

Dedicated thread runs `SetWindowsHookExW` + `GetMessage` loop.
Dispatches events via `std::function` callbacks to avoid stalling the hook chain.

### InputSimulator (1:1 port)

```cpp
namespace InputSimulator {
    void send_attack_click(uint16_t scancode);           // KeyDown + KeyUp batched
    void send_move_click();                               // RightDown + RightUp batched
    void send_move_and_key_down(int x, int y, uint16_t sc); // Atomic mouse+key
    void set_cursor_position(int x, int y);
    void move_cursor_smooth(int fx, int fy, int tx, int ty, int steps, int ms);
    ULONG_PTR get_extra_info(const Settings& s);
}
```

All via Win32 `SendInput` with `INPUT` structs. Identical to C# implementation.

### MouseHelper

```cpp
namespace MouseHelper {
    POINT get_cursor_position();  // GetCursorPos wrapper
}
```

## 7. Overlay System

### OverlayWindow

Dedicated thread creating transparent, click-through, always-on-top Win32 popup.

```cpp
class OverlayWindow {
    std::thread wnd_thread_;
    HWND hwnd_{};
    std::condition_variable ready_cv_;  // replaces ManualResetEventSlim

    static LRESULT CALLBACK wnd_proc(HWND, UINT, WPARAM, LPARAM);
    void window_thread_proc();

public:
    void start();   // spawns thread, waits for HWND ready
    void stop();    // DestroyWindow via PostMessage → join
    HWND hwnd() const { return hwnd_; }
};
```

Window styles: `WS_POPUP | WS_VISIBLE`, extended: `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`.
DWM: `DwmExtendFrameIntoClientArea` with MARGINS = -1.
DPI: `SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)`.

### OverlayRenderer

Direct2D rendering via `ID2D1Factory` + `ID2D1HwndRenderTarget`.
DirectXTK helpers for D2D setup convenience.

```cpp
class OverlayRenderer {
    ComPtr<ID2D1Factory> d2d_factory_;
    ComPtr<ID2D1HwndRenderTarget> render_target_;
    std::unordered_map<COLORREF, ComPtr<ID2D1SolidColorBrush>> brush_cache_;

    std::mutex sync_;
    std::vector<DetectedBox> boxes_;
    RECT scan_region_{};

public:
    void start(HWND hwnd);
    void set_boxes(std::vector<DetectedBox> boxes);
    void set_scan_region(RECT r);
    void render_loop(std::stop_token stop);  // ~60 FPS
};
```

## 8. Strategy Pattern

```cpp
// Pure virtual interface
class IOrbWalkStrategy {
public:
    virtual ~IOrbWalkStrategy() = default;
    virtual bool try_attack(bool has_target, int tx, int ty,
                           uint16_t scancode, const Settings& s) = 0;
};

class AutoOrbWalkStrategy : public IOrbWalkStrategy { ... };
class ManualOrbWalkStrategy : public IOrbWalkStrategy { ... };

enum class OrbWalkMode { None, Manual, Auto };

class OrbWalkStrategyFactory {
    static inline AutoOrbWalkStrategy auto_instance_;
    static inline ManualOrbWalkStrategy manual_instance_;
public:
    static IOrbWalkStrategy* get(OrbWalkMode mode);
};
```

Auto mode: always attacks at cursor. Manual mode: snap to target, attack, restore cursor.
Logic is 1:1 identical to C#.

Note: C++ strategies use synchronous `bool try_attack(...)` with inline `Sleep()` calls
for key hold delays, rather than async. The orbwalk loop already runs on its own jthread,
so blocking is fine — there's no thread pool to starve.

## 9. HTTP / Riot API Client

```cpp
class RiotApiClient {
    CURL* curl_{};  // reusable handle
    std::string response_buffer_;

public:
    RiotApiClient();   // curl_easy_init, set SSL bypass
    ~RiotApiClient();  // curl_easy_cleanup

    // GET https://127.0.0.1:2999/liveclientdata/activeplayer
    std::optional<nlohmann::json> get_active_player();

    // GET CommunityDragon champion data
    std::optional<nlohmann::json> get_champion_data(const std::string& name);
};
```

SSL bypass: `curl_easy_setopt(curl_, CURLOPT_SSL_VERIFYPEER, 0L)` for Riot's self-signed cert.
Reusable CURL handle for connection pooling.

## 10. Settings

```cpp
struct Settings {
    // Keybinds
    int manual_key = 67;        // VK_C
    int auto_key = 32;          // VK_SPACE

    // Target vision
    uint8_t target_color_r = 52, target_color_g = 3, target_color_b = 0;
    int color_tolerance = 0;
    int capture_size = 1000;
    int min_cluster_pixels = 10;
    int detection_fps_cap = 60;
    int target_offset_x = 70;
    int target_offset_y = 120;

    // Overlay
    bool enable_overlay = false;

    // Orb-walk timings
    int windup_buffer_ms = 66;
    int min_input_delay_ms = 75;
    int orbwalk_tick_rate_ms = 1;
    int attack_speed_poll_ms = 500;

    // Anti-detection
    bool enable_cursor_restore = true;
    int input_jitter_ms = 15;
    int windup_jitter_ms = 10;
    uint16_t attack_move_scancode = 0x23;
    int key_hold_base_ms = 40;
    int key_hold_jitter_ms = 30;
    int click_hold_base_ms = 30;
    int click_hold_jitter_ms = 20;
    int min_windup_buffer_ms = 15;
    bool enable_smooth_cursor = true;
    int cursor_steps = 3;
    int cursor_move_ms = 8;
    bool auto_kite_direction = false;
    int kite_distance = 200;
    int target_sticky_radius = 50;
    double skip_move_chance = 0.07;
    double extra_move_chance = 0.05;
    std::string extra_info_mode = "native";

    // Serialize
    static Settings load(const std::filesystem::path& path);
    static void create_new(const std::filesystem::path& path);
    void save(const std::filesystem::path& path) const;
};
```

Uses `nlohmann::json` with `NLOHMANN_DEFINE_TYPE_NON_INTRUSIVE` macro for auto serialization.

## 11. TimingJitter

```cpp
class TimingJitter {
    static thread_local std::mt19937 rng_;  // per-thread, no lock contention
public:
    static int apply(int base_ms, int jitter_ms);          // base + uniform(-j, +j)
    static int apply_positive(int base_ms, int jitter_ms); // base + uniform(0, +j)
    static double next_double();                            // [0.0, 1.0)
};
```

## 12. Models

All POD structs, no methods beyond simple helpers:

```cpp
struct CaptureResult {
    std::vector<uint8_t> buffer;  // BGRA
    int width{}, height{};
    int screen_x{}, screen_y{};
    int stride{};
    bool is_empty() const { return buffer.empty(); }
};

struct ColorMatch {
    POINT local_pos;
    POINT screen_pos;
    int distance_sq;
};

struct ColorCluster {
    RECT bounding_rect;
    POINT center;
    int pixel_count;
};

struct DetectedBox {
    RECT screen_rect;
    COLORREF border_color;
    std::string label;
};
```

## 13. Main Entry Point

```cpp
int main(int argc, char* argv[]) {
    // 1. Load or create settings.json
    // 2. timeBeginPeriod(1)
    // 3. Init DXGI capturer
    // 4. Init input hooks (keyboard callback → start/stop mode)
    // 5. Init overlay (if enabled)
    // 6. Start API polling thread
    // 7. Wait for Ctrl+C (SetConsoleCtrlHandler)
    // 8. request_stop() all jthreads → join
    // 9. timeEndPeriod(1)
    // 10. Cleanup
}
```

Console output: `std::cout` / `std::format` replacing `Console.WriteLine`.
Colors: Win32 `SetConsoleTextAttribute` or ANSI escape codes.

## 14. Testing

Google Test, mirroring existing xUnit tests:

| Test File | Covers | Mirrors |
|---|---|---|
| test_settings.cpp | All 28 default values, JSON round-trip | SettingsTests.cs |
| test_timing_jitter.cpp | apply, apply_positive, next_double, bounds | TimingJitterTests.cs |
| test_color_matcher.cpp | Exact match, tolerance, synthetic BGRA buffer | ColorMatcherTests.cs |
| test_cluster_finder.cpp | BFS on 5x5 block, centroid, count | ClusterFinderTests.cs |
| test_strategy.cpp | Factory returns, Manual returns false when no target | OrbWalkStrategyTests.cs |

## 15. Build & Publish

```bash
# Configure (vcpkg auto-integrates via toolchain file)
cmake -B build -S cpp --preset=default

# Build Release
cmake --build build --config Release

# Output: build/Release/auto_kite.exe (single native binary)
```

CMake preset or toolchain file points to vcpkg:
`-DCMAKE_TOOLCHAIN_FILE=[vcpkg root]/scripts/buildsystems/vcpkg.cmake`

Output is a single native .exe + DLLs (libcurl, etc.) in the same directory.
No .NET runtime needed.
