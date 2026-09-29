# Auto-Kite Bot for League of Legends

A high-performance, anti-detect orb-walking and auto-kiting bot for League of Legends built in C# (.NET 10). It features GPU-accelerated DXGI screen capture, color-based target detection with BFS clustering, dynamic attack speed polling via the official Live Client Data API and CommunityDragon, and extensive input humanization to evade detection heuristics.

---

## Table of Contents

- [Overview](#overview)
- [Key Features](#key-features)
- [Architecture](#architecture)
- [Settings Reference](#settings-reference)
- [Prerequisites & In-Game Configuration](#prerequisites--in-game-configuration)
- [Getting Started](#getting-started)
- [Building & Publishing](#building--publishing)
- [Anti-Detection & Humanization Details](#anti-detection--humanization-details)
- [Troubleshooting](#troubleshooting)
- [Disclaimer](#disclaimer)

---

## Overview

Auto-Kite Bot delivers frame-perfect kiting and orb-walking (attack-moving while maintaining maximum movement efficiency) without injecting code or reading protected game memory. Target acquisition is performed via low-latency screen capture and color clustering, while champion attack speed and windup timings are fetched dynamically from the local Riot Live Client Data API and CommunityDragon database.

### Core Highlights

- **Dual-Mode Operation:** Choose between focused target kiting (Manual Mode) or continuous autonomous kiting (Auto Mode).
- **GPU-Accelerated Vision:** DXGI Desktop Duplication captures the center gameplay area with sub-millisecond overhead.
- **Microsecond Precision:** High-resolution timers (`Stopwatch` + Windows 1ms timer period) eliminate timing drift.
- **Deep Humanization:** Multi-layered anti-detection includes cursor smoothing, timing jitter, key hold variation, input scrambling, and cursor restore.
- **Standalone Portable Executable:** Compiles to a self-contained, single-file binary with no runtime dependencies.

---

## Key Features

1. **Dual-Mode Orb-Walking**
   - **Manual Mode (Default: Hold `C`):** Attacks only when an enemy target is detected within the capture area; otherwise moves towards your cursor. Ideal for precise teamfight focus.
   - **Auto Mode (Default: Hold `Space`):** Continuously kites and attacks. Prioritizes detected targets, but will issue attack-moves toward the cursor or kite away automatically if no target is locked.

2. **DXGI GPU-Accelerated Screen Capture**
   - Direct3D 11 / DXGI Desktop Duplication API captures screen buffers directly from VRAM, bypassing slow GDI/BitBlt screen scraping and avoiding frame drops.

3. **Color Detection with BFS Clustering**
   - Scans the capture window for pixels matching the enemy health bar / champion outline color within a configurable RGB tolerance.
   - Applies Breadth-First Search (BFS) connected-component analysis to group matching pixels into clusters, filtering out single-pixel noise and false positives.

4. **Dynamic Attack Speed & Windup Calculation**
   - Automatically polls the official local Riot Live Client Data API (`https://127.0.0.1:2999/liveclientdata/activeplayer`) for real-time attack speed.
   - Queries character metadata from CommunityDragon to accurately obtain base attack delay, cast time, and attack speed scaling ratios for exact windup math.

5. **Cursor Restore (Snap-Back)**
   - Instantly or smoothly snaps the cursor back to your original aiming position after executing an attack-move click, ensuring seamless mouse tracking.

6. **Humanized Smooth Cursor Movement**
   - Moves the cursor in configurable intermediate interpolated steps over a randomized transit time instead of instant coordinate teleportation, mimicking natural human mouse kinematics.

7. **Randomized Input Timing Jitter**
   - Applies symmetric random jitter to move command delays and positive jitter to windup buffers, defeating static interval pattern detectors.

8. **Variable Key Hold Duration**
   - Implements realistic human-like key and button press/release timing with configurable base hold durations and random jitter for both keyboard keys and mouse clicks.

9. **Input Pattern Scrambling**
   - Periodically introduces micro-irregularities such as skipping a move tick or injecting an extra micro-adjustment move click at configurable probabilities.

10. **Adaptive Windup Buffer**
    - Dynamically scales the windup safety margin inversely with attack speed. At high attack speeds (e.g., Jinx, Kog'Maw, Lethal Tempo), the buffer narrows toward a configurable minimum floor to maximize DPS without canceling basic attacks.

11. **Auto Kite Direction**
    - When enabled in Auto Mode, calculates an evasion vector away from the detected enemy and automatically issues move commands at a specified safety distance instead of moving toward the mouse cursor.

12. **Sticky Target Lock (Hysteresis)**
    - Prevents cursor jitter and target flipping when multiple enemies are clustered close together by retaining focus on the previously targeted enemy within a hysteresis radius.

13. **Configurable Attack Key Scancode**
    - Sends low-level DirectInput hardware scancodes (e.g., `0x23` for DIK_H, `0x1E` for DIK_A) for direct compatibility with League of Legends' input pipeline.

14. **Configurable `dwExtraInfo` Mode**
    - Supports `"native"` mode to attach valid hardware input signatures to simulated events or `"zero"` mode for standard raw zeroed input.

15. **Direct2D Hardware-Accelerated Overlay**
    - Optional transparent click-through debug overlay showing the capture boundary, detected target clusters, and calculated attack offsets in real time.

16. **Single-File Self-Contained Deployment**
    - Packaged into a standalone single executable (`SidaAutoCarry.exe`) with all required native libraries bundled; no .NET runtime installation required.

---

## Architecture

```
                       +----------------------------------------+
                       |      League of Legends Game Client     |
                       |  - Borderless / Windowed Mode          |
                       |  - Live Client Data API (Port 2999)    |
                       +-------------------+--------------------+
                                           |
                  +------------------------+------------------------+
                  | (VRAM Desktop Dup)                              | (HTTPS GET stats)
                  v                                                 v
        +-------------------+                             +--------------------+
        |   DxgiCapturer    |                             |  CommunityDragon / |
        |  (Direct3D 11)    |                             |  Live Client API   |
        +---------+---------+                             +----------+---------+
                  | BGRA Frame                                       | Attack Speed,
                  v                                                  | Base Delay, Ratio
        +-------------------+                                        v
        |   ColorMatcher    |                             +--------------------+
        |  (RGB Tolerance)  |                             | PrecisionTimer &   |
        +---------+---------+                             | Windup Calculator  |
                  | Pixel Matches                         +----------+---------+
                  v                                                  |
        +-------------------+                                        |
        |   ClusterFinder   |                                        |
        |   (BFS Engine)    |                                        |
        +---------+---------+                                        |
                  | Target Coords                                    |
                  +-----------------------+  +-----------------------+
                                          |  |
                                          v  v
                             +-----------------------------+
                             |     OrbWalkLoopAsync        |
                             |  - ManualStrategy           |
                             |  - AutoStrategy             |
                             |  - Anti-Detect Humanizer    |
                             +--------------+--------------+
                                            |
                                            v
                             +-----------------------------+
                             |        InputManager         |
                             |  - LowLevelInput Hooks      |
                             |  - SendInput DirectInput    |
                             +-----------------------------+
```

---

## Settings Reference

The configuration file is automatically generated at `settings/settings.json` on first launch.

| Setting | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `ManualKey` | `int` | `67` | Virtual key code for **Manual Mode** (`67` = `C`). Only attacks when a target is detected. |
| `AutoKey` | `int` | `32` | Virtual key code for **Auto Mode** (`32` = `Space`). Continuously attacks and moves. |
| `TargetColorR` | `int` | `52` | Red channel value (0–255) for target pixel identification. |
| `TargetColorG` | `int` | `3` | Green channel value (0–255) for target pixel identification. |
| `TargetColorB` | `int` | `0` | Blue channel value (0–255) for target pixel identification. |
| `ColorTolerance` | `int` | `10` | Maximum absolute channel difference for matching target pixels. |
| `CaptureSize` | `int` | `1000` | Width and height (in pixels) of the screen center region captured via DXGI. |
| `MinClusterPixels` | `int` | `10` | Minimum connected matching pixels required to register a valid target cluster. |
| `DetectionFpsCap` | `int` | `60` | Maximum frame rate for the screen capture and vision detection loop. |
| `TargetOffsetX` | `int` | `70` | Horizontal pixel offset from detected feature (e.g. health bar edge) to champion center. |
| `TargetOffsetY` | `int` | `120` | Vertical pixel offset from detected feature to champion center. |
| `EnableOverlay` | `bool` | `false` | Enables the Direct2D transparent debug overlay window. |
| `WindupBufferMs` | `int` | `66` | Base safety margin (in milliseconds) added to champion attack windup. |
| `MinInputDelayMs` | `int` | `75` | Minimum delay (in milliseconds) between successive movement inputs. |
| `OrbWalkTickRateMs`| `int` | `1` | Polling resolution (in milliseconds) for the main orb-walker loop. |
| `AttackSpeedPollMs`| `int` | `500` | Interval (in milliseconds) to poll current attack speed from Live Client Data API. |
| `EnableCursorRestore` | `bool` | `true` | Restores mouse cursor back to original user position immediately after attacking. |
| `InputJitterMs` | `int` | `15` | Symmetrical timing variance (`±N` ms) applied to movement click delays. |
| `WindupJitterMs` | `int` | `10` | Positive timing jitter (`+0..N` ms) applied to attack windup calculations. |
| `AttackMoveScancode` | `int` | `0x23` | DirectInput scancode for Attack Move (`0x23` = DIK_H, `0x1E` = DIK_A). |
| `KeyHoldBaseMs` | `int` | `40` | Base duration (in milliseconds) keys are held down before release. |
| `KeyHoldJitterMs` | `int` | `30` | Symmetrical variance (`±N` ms) applied to key hold durations. |
| `ClickHoldBaseMs` | `int` | `30` | Base duration (in milliseconds) mouse buttons are held down before release. |
| `ClickHoldJitterMs` | `int` | `20` | Symmetrical variance (`±N` ms) applied to mouse click hold durations. |
| `MinWindupBufferMs` | `int` | `15` | Minimum clamp floor for the adaptive windup buffer at high attack speeds. |
| `EnableSmoothCursor`| `bool` | `true` | Enables multi-step interpolated mouse movement to target and restore positions. |
| `CursorSteps` | `int` | `3` | Number of interpolation sub-steps for smooth cursor movement. |
| `CursorMoveMs` | `int` | `8` | Total duration (in milliseconds) of smooth cursor transit. |
| `AutoKiteDirection` | `bool` | `false` | In Auto Mode, automatically moves champion away from target instead of to cursor. |
| `KiteDistance` | `int` | `200` | Distance (in pixels) to move along the retreat vector in Auto Kite mode. |
| `TargetStickyRadius`| `int` | `50` | Hysteresis distance (in pixels) to maintain lock on current target cluster. |
| `SkipMoveChance` | `double` | `0.07` | Probability (`0.0`–`1.0`) of skipping a move command tick (7% human variance). |
| `ExtraMoveChance` | `double` | `0.05` | Probability (`0.0`–`1.0`) of injecting an additional micro-movement click (5% jitter). |
| `ExtraInfoMode` | `string` | `"native"` | `dwExtraInfo` signature mode for simulated input (`"native"` or `"zero"`). |

---

## Prerequisites & In-Game Configuration

1. **Display Mode:**
   - Set League of Legends video mode to **Borderless** or **Windowed**. DXGI Desktop Duplication requires the desktop compositor to be active.
2. **Key Bindings:**
   - In League of Legends Hotkey settings, bind **Player Attack Move** or **Player Attack Move Click** to the key corresponding to `AttackMoveScancode`. By default, `0x23` is the DirectInput scancode for `H`.
   - If you prefer `A`, set `AttackMoveScancode` to `30` (`0x1E`).
3. **Live Client Data API:**
   - League of Legends enables this API by default on `https://127.0.0.1:2999/liveclientdata/activeplayer`. Ensure no third-party firewall blocks local loopback communication.
4. **Color Calibration:**
   - Default target color `RGB(52, 3, 0)` is tuned for the enemy health bar / level circle.
   - Adjust `TargetOffsetX` and `TargetOffsetY` depending on your resolution and champion scale to ensure the attack cursor lands on the champion's hitbox.

---

## Getting Started

### 1. Launching
1. Run `SidaAutoCarry.exe` (or run via `dotnet run --project auto-kite\auto-kite.csproj`).
2. Start or alt-tab into your League of Legends match (Practice Tool, ARAM, or Summoner's Rift).
3. The console will display detected champion statistics once the match begins:
   ```
   [OK] League process found — starting attack speed polling...
   Connected: Jinx (Base AS: 0.625, Windup Delay: 0.3)
   ```

### 2. Basic Controls
- **Hold `C` (Manual Mode):** Move your mouse cursor as normal. The bot moves to your mouse and attacks when an enemy is within detection range.
- **Hold `Space` (Auto Mode):** The bot will continuously attack move. If an enemy is detected, it will focus the enemy; if `AutoKiteDirection` is true, it will kite away from the enemy automatically.
- **Release Key:** Immediately cancels orb-walking and returns full manual control to your mouse and keyboard.

---

## Building & Publishing

### Requirements
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) (Windows x64)
- Windows 10/11 64-bit

### Build Debug / Run Tests
```powershell
# Run the complete test suite
dotnet test auto-kite.sln -v n

# Build solution in Debug configuration
dotnet build auto-kite.sln
```

### Build Release
```powershell
dotnet build auto-kite.sln -c Release
```

### Publish Single-File Executable
```powershell
dotnet publish auto-kite\auto-kite.csproj -c Release -r win-x64 --self-contained true
```
The compiled, self-contained standalone executable will be located at:
```
auto-kite\bin\Release\net10.0-windows\win-x64\publish\SidaAutoCarry.exe
```

---

## Anti-Detection & Humanization Details

Modern anti-cheat systems analyze input entropy, mouse kinematics, and periodicity to distinguish human play from automated scripts. Auto-Kite incorporates multiple defenses against heuristic profiling:

1. **Non-Invasive Architecture:**
   - Operates completely out-of-process.
   - Zero memory writes, zero DLL injection, zero API hooks into game code.
2. **Kinematic Mouse Smoothing:**
   - Attacks smoothly traverse intermediate points rather than teleporting instantly in 1 millisecond.
3. **Temporal Entropy:**
   - Movement tick delays, windup safety buffers, key down periods, and mouse clicks all undergo bounded random variations.
4. **Behavioral Irregularity:**
   - Configurable probabilities (`SkipMoveChance` and `ExtraMoveChance`) inject natural human imperfection, preventing continuous uniform clicking cadences.
5. **Adaptive Mechanics:**
   - Windup times automatically scale with attack speed changes (items, levels, buffs), keeping actions within believable human reaction windows.

---

## Troubleshooting

- **Bot not attacking:**
  - Verify that your in-game Attack Move binding matches `AttackMoveScancode` (default `0x23` = `H`).
  - Check that the game is running in Borderless or Windowed mode.
  - Set `EnableOverlay: true` in `settings.json` to visually confirm whether enemy clusters are being detected.
- **Bot attacks too early / cancels attacks:**
  - Increase `WindupBufferMs` (e.g., from `66` to `80` or `100`).
  - Ensure high ping or packet loss is compensated by a higher windup buffer.
- **Target not recognized:**
  - Verify your enemy health bar color. If colorblind mode is active, adjust `TargetColorR`, `TargetColorG`, and `TargetColorB` or increase `ColorTolerance`.
- **API not connecting:**
  - Test opening `https://127.0.0.1:2999/liveclientdata/activeplayer` in your browser while in a game. Accept any self-signed SSL warning.

---

## Disclaimer

This software is for educational, research, and concept exploration purposes only. Using third-party automation tools or bots in League of Legends violates Riot Games' Terms of Service and can result in permanent account suspension. Use at your own risk.
