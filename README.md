# Auto Kite Bot — League of Legends

Native C++20 orbwalk bot for League of Legends. Uses DXGI screen capture for target detection and Win32 SendInput for humanized input simulation.

## Features

- **Color-based target detection** — DXGI Desktop Duplication screen capture + pixel color matching
- **Orbwalk engine** — timestamp-based attack/move cycle matching game mechanics
- **Auto & Manual mode** — hold key to activate, release to stop
- **Detection toggle (M key)** — ON: detect + attack target, OFF: attack at cursor
- **Humanized input** — jittered delays, click hold duration, pattern scrambling
- **Kite direction** — auto mode moves cursor away from target
- **Champion data** — fetches attack speed from Riot Client API, windup from CommunityDragon
- **Overlay** — optional Direct2D overlay showing detection boxes
- **Audio feedback** — beep sounds on toggle

## Build

### Requirements

- **Visual Studio 2022+** with C++ Desktop workload
- **vcpkg** (package manager)
- **Windows SDK** (for `rc.exe`)

### Steps

```powershell
# 1. Clone & enter
cd cpp

# 2. Configure (vcpkg auto-installs dependencies)
cmake --preset default

# 3. Build
cmake --build build --config Release

# 4. Run tests
cd build && ctest --build-config Release --output-on-failure
```

Output: `cpp/build/Release/auto_kite.exe` (~330 KB)

### Dependencies (auto-installed via vcpkg)

| Package | Usage |
|---------|-------|
| nlohmann-json | Settings JSON serialization |
| libcurl | Riot Client API + CommunityDragon HTTP |
| directxtk | DirectX helpers |
| gtest | Unit tests |

## Usage

```powershell
cd cpp/build/Release
./auto_kite.exe
```

### Keybinds

| Key | Action |
|-----|--------|
| **Space** (VK 32) | Hold = Auto mode (detect + orbwalk) |
| **C** (VK 67) | Hold = Manual mode (attack target at cursor) |
| **M** | Toggle detection ON/OFF (beep feedback) |
| **Ctrl+C** | Exit |

### Modes

| Mode | Detection ON (M) | Detection OFF (M) |
|------|-------------------|---------------------|
| **Auto** (Space) | Detect target → move cursor → attack | Attack at current cursor |
| **Manual** (C) | Save cursor → move to target → attack → restore cursor | Same |

## Settings (`settings.json`)

Auto-created on first run. Edit and restart to apply.

### Keybinds

| Setting | Default | Description |
|---------|---------|-------------|
| `manual_key` | `67` (C) | Virtual key code for manual mode |
| `auto_key` | `32` (Space) | Virtual key code for auto mode |
| `attack_move_scancode` | `0x23` | Scancode for attack-move key (default: End key) |

### Target Detection

| Setting | Default | Description |
|---------|---------|-------------|
| `target_color_r` | `52` | Target indicator color — Red component (0-255) |
| `target_color_g` | `3` | Target indicator color — Green component |
| `target_color_b` | `0` | Target indicator color — Blue component |
| `color_tolerance` | `0` | Color matching tolerance (0 = exact match) |
| `capture_size` | `1000` | Screen capture area size in pixels (centered on cursor) |
| `min_cluster_pixels` | `10` | Minimum pixel count to consider a valid target |
| `detection_fps_cap` | `60` | Max detection scans per second |
| `target_offset_x` | `70` | Offset from detected cluster center to actual target X |
| `target_offset_y` | `120` | Offset from detected cluster center to actual target Y |
| `target_sticky_radius` | `50` | Pixels — ignore small target position changes (anti-jitter) |

### Orbwalk Timing

These control the attack → move cycle. Understanding the timeline:

```
|←────────────── Interval (1/AS) ───────────────→|
|                                                  |
|◄── Windup ──►|◄── Move window ──►|◄── Idle ──►|
  (wait for      (right-click to      (wait for
   animation)     kite/reposition)     cooldown)
|               |                    |             |
0ms          windup_ms          move_deadline   next_attack
```

| Setting | Default | Description |
|---------|---------|-------------|
| `max_windup_ms` | `100` | **Cap** windup wait time. After attack, wait this long before moving. Set per champion — too low cancels attack, too high wastes move time. `0` = use calculated value from game data |
| `max_move_ms` | `300` | **Cap** move phase duration. Stop right-clicking after this many ms. `0` = move until next attack (no limit) |
| `windup_buffer_ms` | `15` | Safety buffer added to calculated windup (network lag protection) |
| `min_windup_buffer_ms` | `5` | Minimum buffer floor |
| `windup_jitter_ms` | `5` | Random jitter on windup buffer (anti-detection) |
| `min_input_delay_ms` | `75` | Minimum delay between each move-click in move phase |
| `input_jitter_ms` | `15` | Random jitter on input delay |
| `orbwalk_tick_rate_ms` | `1` | Orbwalk loop tick interval |
| `attack_speed_poll_ms` | `500` | How often to poll Riot API for attack speed |

#### Windup explained

Windup is calculated dynamically from champion data + current attack speed:

```
base_windup = 0.3 / attack_speed  (simplified formula)
buffered_windup = base_windup + buffer + jitter
final_windup = min(buffered_windup, max_windup_ms)
```

| Attack Speed | Base Windup | With buffer |
|-------------|-------------|-------------|
| 0.625 | 480ms | 495ms |
| 1.0 | 300ms | 315ms |
| 1.5 | 200ms | 215ms |
| 2.0 | 150ms | 165ms |
| 2.5 | 120ms | 135ms |

#### Move window explained

Each move-click in the move window takes approximately:
- Click hold: `click_hold_base_ms` ± `click_hold_jitter_ms` (~30-50ms)
- Then wait: `min_input_delay_ms` ± `input_jitter_ms` (~75-90ms)
- **Total per click: ~105-140ms**

| `max_move_ms` | Clicks per cycle | Use case |
|---------------|-----------------|----------|
| `100` | ~1 | Minimal movement |
| `200` | ~1-2 | Light kiting |
| `300` | ~2-3 | Standard kiting |
| `500` | ~4-5 | Heavy kiting |
| `0` | Unlimited | Move until next attack |

### Anti-Detection (Humanization)

| Setting | Default | Description |
|---------|---------|-------------|
| `enable_cursor_restore` | `true` | Restore cursor position after manual mode attack |
| `enable_smooth_cursor` | `true` | Move cursor in steps instead of instant teleport |
| `cursor_steps` | `3` | Number of intermediate cursor positions |
| `cursor_move_ms` | `8` | Delay between cursor steps |
| `key_hold_base_ms` | `40` | How long to hold attack key down |
| `key_hold_jitter_ms` | `30` | Random jitter on key hold |
| `click_hold_base_ms` | `30` | How long to hold right mouse button |
| `click_hold_jitter_ms` | `20` | Random jitter on click hold |
| `skip_move_chance` | `0.07` | 7% chance to skip a move-click (pattern scrambling) |
| `extra_move_chance` | `0.05` | 5% chance to do an extra move-click (pattern scrambling) |

### Kite Direction (Auto Mode)

| Setting | Default | Description |
|---------|---------|-------------|
| `auto_kite_direction` | `false` | Enable kiting away from target in auto mode |
| `kite_distance` | `200` | Pixels to move cursor away from target |

### Other

| Setting | Default | Description |
|---------|---------|-------------|
| `enable_overlay` | `false` | Show Direct2D overlay with detection boxes |
| `extra_info_mode` | `"native"` | Extra info display mode |

## Example `settings.json`

```json
{
    "manual_key": 67,
    "auto_key": 32,
    "attack_move_scancode": 35,
    "target_color_r": 52,
    "target_color_g": 3,
    "target_color_b": 0,
    "color_tolerance": 0,
    "capture_size": 1000,
    "min_cluster_pixels": 10,
    "detection_fps_cap": 60,
    "target_offset_x": 70,
    "target_offset_y": 120,
    "enable_overlay": false,
    "max_windup_ms": 100,
    "max_move_ms": 300,
    "windup_buffer_ms": 15,
    "min_windup_buffer_ms": 5,
    "windup_jitter_ms": 5,
    "min_input_delay_ms": 75,
    "input_jitter_ms": 15,
    "orbwalk_tick_rate_ms": 1,
    "attack_speed_poll_ms": 500,
    "enable_cursor_restore": true,
    "enable_smooth_cursor": true,
    "cursor_steps": 3,
    "cursor_move_ms": 8,
    "key_hold_base_ms": 40,
    "key_hold_jitter_ms": 30,
    "click_hold_base_ms": 30,
    "click_hold_jitter_ms": 20,
    "skip_move_chance": 0.07,
    "extra_move_chance": 0.05,
    "auto_kite_direction": false,
    "kite_distance": 200,
    "target_sticky_radius": 50,
    "extra_info_mode": "native"
}
```

## Architecture

```
cpp/
├── CMakeLists.txt
├── CMakePresets.json
├── vcpkg.json
├── app.rc                      # App icon resource
├── res/app.ico                 # App icon
├── src/
│   ├── main.cpp                # Orchestrator — all async loops
│   ├── core/
│   │   ├── settings.h/.cpp     # JSON settings (nlohmann/json)
│   │   ├── timing_jitter.h/.cpp # Thread-safe random jitter
│   │   └── direct_input_keys.h  # DirectInput key codes
│   ├── input/
│   │   ├── input_simulator.h/.cpp  # SendInput wrapper
│   │   ├── input_hooks.h/.cpp      # WH_KEYBOARD_LL global hooks
│   │   └── mouse_helper.h/.cpp     # GetCursorPos wrapper
│   ├── capture/
│   │   └── dxgi_capturer.h/.cpp    # DXGI Desktop Duplication
│   ├── detection/
│   │   ├── color_matcher.h/.cpp    # BGRA pixel color scan
│   │   └── cluster_finder.h/.cpp   # 8-way BFS clustering
│   ├── strategy/
│   │   ├── orbwalk_strategy.h      # IOrbWalkStrategy interface
│   │   ├── auto_strategy.h/.cpp    # Auto mode (detect + attack)
│   │   ├── manual_strategy.h/.cpp  # Manual mode (cursor save/restore)
│   │   └── strategy_factory.h/.cpp # Factory
│   ├── net/
│   │   └── riot_api_client.h/.cpp  # Riot Client API + CommunityDragon
│   ├── overlay/
│   │   ├── overlay_window.h/.cpp   # WS_EX_LAYERED transparent window
│   │   └── overlay_renderer.h/.cpp # Direct2D render loop
│   └── models/
│       ├── capture_result.h
│       ├── color_match.h
│       ├── color_cluster.h
│       └── detected_box.h
└── tests/                      # Google Test (22 tests)
```

## License

See [LICENSE](LICENSE).
