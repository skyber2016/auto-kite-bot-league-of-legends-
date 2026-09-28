# Color Detection Integration — Design Spec

**Date:** 2026-09-28
**Project:** auto-kite (League of Legends orb-walk bot)
**Status:** Approved

---

## 1. Goal

Tích hợp thuật toán Color Detection (DXGI Desktop Duplication + unsafe pixel scan + BFS clustering) vào auto-kite bot để:
- **Detect enemy champion HP bars** → target champion có HP thấp
- **Detect minion HP bars** → last-hit minion khi HP đủ thấp

Hai mode hoạt động qua 2 phím tắt, overlay debug tùy chọn.

---

## 2. Two-Mode Behavior

### Mode Manual (C key)

| Trạng thái | Hành vi |
|---|---|
| Target detected | Move chuột đến target center → SendAttackClick |
| Không detect | Chỉ SendMoveClick (right-click move) — **không** attack |
| Trong windup delay | SendMoveClick (right-click move) |

### Mode Auto (Space key)

| Trạng thái | Hành vi |
|---|---|
| Target detected | Move chuột đến target center → SendAttackClick |
| Không detect | SendAttackClick (A-click nearest) — **vẫn** attack |
| Trong windup delay | SendMoveClick (right-click move) |

### Key Events & Mode Resolution via Factory

```
KeyDown(C)     → activeMode = Manual → _strategy = OrbWalkStrategyFactory.Create(OrbWalkMode.Manual)
KeyUp(C)       → activeMode = None   → _strategy = null, stop all + clear overlay

KeyDown(Space) → activeMode = Auto   → _strategy = OrbWalkStrategyFactory.Create(OrbWalkMode.Auto)
KeyUp(Space)   → activeMode = None   → _strategy = null, stop all + clear overlay
```

Nếu đang giữ 1 phím rồi nhấn phím kia → bỏ qua (chỉ 1 mode active tại 1 thời điểm).
Việc phân xử logic tấn công của từng mode được đa hình hóa hoàn toàn thông qua `IOrbWalkStrategy` và `OrbWalkStrategyFactory`, loại bỏ toàn bộ `if-else` trong vòng lặp tick.


---

## 3. Project Structure

```
auto-kite.sln
│
├── auto-kite/                          (existing, modified)
│   ├── Program.cs                      ← 2 modes, detection integration
│   ├── InputSimulator.cs               ← thêm SetCursorPosition
│   ├── Settings.cs                     ← thêm detection/overlay config
│   └── DirectInputKeys.cs             (unchanged)
│
├── DetectColor/                        (new class library)
│   ├── DetectColor.csproj              ← net10.0-windows, Vortice packages
│   ├── Models/
│   │   ├── CaptureResult.cs
│   │   ├── ColorMatch.cs
│   │   ├── ColorCluster.cs
│   │   └── DetectedBox.cs
│   ├── Capture/
│   │   └── DxgiCapturer.cs            ← DXGI Desktop Duplication (GPU)
│   ├── Detection/
│   │   ├── ColorMatcher.cs            ← unsafe pointer pixel scan (CPU)
│   │   └── ClusterFinder.cs           ← BFS 8-way clustering (CPU)
│   ├── Overlay/
│   │   ├── OverlayWindow.cs           ← Win32 transparent click-through window
│   │   └── OverlayRenderer.cs         ← Direct2D 60 FPS render loop
│   └── Input/
│       └── MouseHelper.cs             ← GetCursorPos P/Invoke
│
└── LowLevelInput.Net/                 (existing, unchanged)
```

### Dependencies

**DetectColor.csproj:**
```xml
<TargetFramework>net10.0-windows</TargetFramework>
<AllowUnsafeBlocks>true</AllowUnsafeBlocks>

<PackageReference Include="Vortice.Direct2D1" Version="3.8.1" />
<PackageReference Include="Vortice.Direct3D11" Version="3.8.1" />
<PackageReference Include="Vortice.DXGI" Version="3.8.1" />
<PackageReference Include="Vortice.Mathematics" Version="2.0.0" />
```

**auto-kite.csproj changes:**
- `net10.0` → `net10.0-windows` (required to reference DetectColor)
- Add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`
- Add `<ProjectReference Include="..\DetectColor\DetectColor.csproj" />`
- Add Single-File Publish properties (`PublishSingleFile`, `SelfContained`, `IncludeNativeLibrariesForSelfExtract`, `EnableCompressionInSingleFile`, `PublishTrimmed=false`)

---

## 4. Detection Pipeline (60 FPS capped)

Chạy trên thread riêng, mỗi frame:

```
1. GetCursorPos()                          → (cx, cy)
2. DxgiCapturer.Capture(cx, cy, size)      → CaptureResult (GPU texture → staging → CPU buffer)
3. ColorMatcher.FindPixels(result, color, tolerance) → List<ColorMatch> (unsafe ptr scan)
4. ClusterFinder.FindClusters(matches, minPixels)    → List<ColorCluster> (BFS flood-fill)
5. Select closest cluster to cursor                  → best target Point?
6. Atomic write to shared state                      → (targetX, targetY, hasTarget)
7. If overlay enabled: push boxes to OverlayRenderer
8. Frame pacing: ensure ≥16.67ms per frame           → cap 60 FPS
```

### Target Selection

Từ danh sách clusters (sorted by pixel count descending), chọn **cluster có Center gần chuột nhất** (Euclidean distance). Lý do: người chơi đưa chuột gần target họ muốn đánh.

### GPU Utilization

- DXGI Desktop Duplication: frame buffer copy trực tiếp từ VRAM (GPU-accelerated)
- Staging texture + byte buffer được reuse (zero-allocation hot path)
- CPU chỉ xử lý color matching + clustering trên mapped buffer

---

## 5. Threading Model & Data Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                        MAIN THREAD                              │
│  LowLevelInput hooks → KeyDown/KeyUp → start/stop threads      │
└───────────┬─────────────────────────────────┬───────────────────┘
            │                                 │
            ▼                                 ▼
┌───────────────────────┐    volatile     ┌───────────────────────┐
│   DETECTION THREAD    │ ──────────────► │   ORBWALK TIMER       │
│   (Task.Run, 60 FPS)  │  SharedTarget   │   (33ms tick)         │
│                       │  int x, y       │                       │
│   DXGI Capture (GPU)  │  bool hasTarget │   Read target         │
│   ColorMatch  (CPU)   │                 │   SetCursorPos        │
│   Cluster BFS (CPU)   │                 │   Attack / Move       │
└───────────┬───────────┘                 └───────────────────────┘
            │ if overlay
            ▼
┌───────────────────────┐
│   OVERLAY THREAD      │
│   (STA, Direct2D)     │
│   60 FPS render       │
└───────────────────────┘
```

### Shared State

```csharp
private static volatile bool HasDetectedTarget = false;
private static int _targetX;  // Interlocked
private static int _targetY;  // Interlocked
```

1 writer (detection), 1 reader (orb-walk). Worst case: 1 frame stale (~16ms) — acceptable.

### Resource Lifecycle

```
App Start → CheckLeagueProcess()
  → First KeyDown(C/Space):
      Lazy init DxgiCapturer (keep alive until game exit)
      Lazy init Overlay (if EnableOverlay, keep alive until game exit)
  → KeyDown: start detection loop + orb-walk timer
  → KeyUp:   stop detection loop + orb-walk timer + clear overlay boxes
  → Game exit: Dispose DxgiCapturer + Overlay + all resources
```

Lazy init vì DXGI init cost ~50-100ms — chỉ tạo 1 lần, reuse.

---

## 6. Settings Configuration

Tất cả tham số tunable từ file `settings.json` — không cần rebuild.

### Full settings.json

```json
{
  "ManualKey": 46,
  "AutoKey": 32,

  "TargetColorR": 255,
  "TargetColorG": 0,
  "TargetColorB": 0,
  "ColorTolerance": 40,

  "CaptureSize": 400,
  "MinClusterPixels": 10,
  "DetectionFpsCap": 60,

  "EnableOverlay": false,

  "WindupBufferMs": 66,
  "MinInputDelayMs": 33,
  "OrbWalkTickRateMs": 33,
  "AttackSpeedPollMs": 500
}
```

### Parameter Reference

#### Key Bindings

| Field | Type | Default | Description |
|---|---|---|---|
| `ManualKey` | int | 46 (C) | VirtualKeyCode — Manual mode: chỉ attack khi detected |
| `AutoKey` | int | 32 (Space) | VirtualKeyCode — Auto mode: attack liên tục, focus nếu detected |

#### Color Detection

| Field | Type | Default | Rationale |
|---|---|---|---|
| `TargetColorR` | int | 255 | Enemy HP bar color trong LoL thiên đỏ. Cần tune theo game settings |
| `TargetColorG` | int | 0 | |
| `TargetColorB` | int | 0 | |
| `ColorTolerance` | int | 40 | Euclidean RGB distance. HP bar có gradient nhẹ → 40 cho phép chênh lệch ~23 mỗi channel. Quá thấp (<20) → miss pixel. Quá cao (>80) → false positive |
| `CaptureSize` | int | 400 | Vùng capture 400×400px quanh chuột. AA range ~550 units ≈ 200-400px tùy zoom. 400 đủ cover. Giảm xuống 200 nếu muốn nhanh hơn |
| `MinClusterPixels` | int | 10 | HP bar LoL thường 50-100+ pixels. 10 lọc noise mà không miss bar nhỏ (minion xa) |
| `DetectionFpsCap` | int | 60 | Cap detection loop ≤ 60 FPS (~16.67ms/frame). Đủ responsive cho orb-walk timing. Giảm xuống 30 nếu CPU yếu |

#### Overlay

| Field | Type | Default | Description |
|---|---|---|---|
| `EnableOverlay` | bool | false | Bật Direct2D transparent overlay để debug. Tốn thêm ~1-2% GPU |

#### Orb-Walk Timing

| Field | Type | Default | Rationale |
|---|---|---|---|
| `WindupBufferMs` | int | 66 | Buffer tránh cancel auto-attack quá sớm (ms). Ở 2.0 AS: windup ~150ms → buffer 66ms = 44% windup. Tăng nếu hay cancel AA (ping cao). Giảm nếu muốn kite nhanh hơn (ping thấp). Range khuyến nghị: 30-100ms |
| `MinInputDelayMs` | int | 33 | Khoảng cách tối thiểu giữa 2 lần gửi input (ms). Tránh input bị drop do gửi quá nhanh. 33ms = ~30 inputs/s. Không nên < 16ms |
| `OrbWalkTickRateMs` | int | 33 | Timer interval cho orb-walk loop (ms). 33ms = ~30 ticks/s. Giảm xuống 16 (60 ticks/s) nếu muốn responsive hơn nhưng tốn CPU. Không nên < 10ms |
| `AttackSpeedPollMs` | int | 500 | Interval polling attack speed từ Riot Client API (ms). AS chỉ đổi khi level up hoặc mua item. 500ms đủ. Giảm xuống 200 nếu muốn reactive hơn |

#### Removed

| Old Field | Status |
|---|---|
| `ActivationKey` | Replaced by `ManualKey` + `AutoKey` |

#### Hardcoded (không cần setting)

| Param | Value | Reason |
|---|---|---|
| `CheckLeagueProcess sleep` | 2000ms | Chỉ chạy khi game chưa mở, không ảnh hưởng gameplay |
| Champion base values | From Riot API | Auto-fetched per game |

---

## 7. InputSimulator Addition

Thêm method `SetCursorPosition` để di chuyển chuột đến target:

```csharp
public static void SetCursorPosition(int x, int y)
{
    // Use SendInput with MOUSEINPUT absolute coordinates
    // Normalized: x * 65535 / screenWidth, y * 65535 / screenHeight
    // Flags: MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE
}
```

Dùng `SendInput` (không dùng `SetCursorPos` Win32) để tương thích với anti-cheat — `SendInput` giống input thật hơn.

---

## 8. Mode Factory & OrbWalk Execution
Thay vì dùng `if-else` lồng nhau kiểm tra mode trong hot path 33ms, hệ thống áp dụng **Factory Method + Strategy Pattern**:

### 8.1. Strategy Interface & Implementations
```csharp
namespace OddAutoWalker.Strategies
{
    public interface IOrbWalkStrategy
    {
        /// <summary>
        /// Thực thi hành vi tấn công tùy theo mode.
        /// Trả về true nếu đã thực hiện đòn đánh (để tính toán windup & next attack timing),
        /// hoặc false nếu bỏ qua đòn đánh (để chờ tick tiếp theo hoặc right-click).
        /// </summary>
        bool TryAttack(bool hasTarget, int targetX, int targetY, ushort attackScancode);
    }

    /// <summary>
    /// Manual Mode (Phím C): Chỉ tấn công khi phát hiện mục tiêu.
    /// </summary>
    public sealed class ManualOrbWalkStrategy : IOrbWalkStrategy
    {
        public bool TryAttack(bool hasTarget, int targetX, int targetY, ushort attackScancode)
        {
            if (!hasTarget) return false;

            InputSimulator.SetCursorPosition(targetX, targetY);
            InputSimulator.SendAttackClick(attackScancode);
            return true;
        }
    }

    /// <summary>
    /// Auto Mode (Phím Space): Tấn công liên tục. Nếu có mục tiêu thì focus vào mục tiêu, ngược lại vẫn A-click.
    /// </summary>
    public sealed class AutoOrbWalkStrategy : IOrbWalkStrategy
    {
        public bool TryAttack(bool hasTarget, int targetX, int targetY, ushort attackScancode)
        {
            if (hasTarget)
            {
                InputSimulator.SetCursorPosition(targetX, targetY);
            }

            InputSimulator.SendAttackClick(attackScancode);
            return true;
        }
    }
}
```

### 8.2. Strategy Factory
```csharp
namespace OddAutoWalker.Strategies
{
    public enum OrbWalkMode
    {
        None,
        Manual,
        Auto
    }

    public static class OrbWalkStrategyFactory
    {
        private static readonly IOrbWalkStrategy ManualStrategy = new ManualOrbWalkStrategy();
        private static readonly IOrbWalkStrategy AutoStrategy = new AutoOrbWalkStrategy();

        public static IOrbWalkStrategy? Create(OrbWalkMode mode) => mode switch
        {
            OrbWalkMode.Manual => ManualStrategy,
            OrbWalkMode.Auto => AutoStrategy,
            _ => null
        };
    }
}
```

### 8.3. OrbWalkTimer_Elapsed (Clean Hot Path — Zero `if-else` for Modes)
```csharp
void OrbWalkTimer_Elapsed()
{
    if (!HasProcess || IsExiting || !IsForeground) return;

    double time = PrecisionTimer.Elapsed.TotalSeconds;
    bool hasTarget = HasDetectedTarget;
    int tx = Interlocked.Read(ref _targetX);
    int ty = Interlocked.Read(ref _targetY);

    var strategy = _currentStrategy;
    if (strategy == null) return;

    if (nextAttack < time)
    {
        nextInput = time + MinInputDelay;

        // Đa hình hóa attack logic qua Strategy được sinh bởi Factory
        if (strategy.TryAttack(hasTarget, tx, ty, DIK_A))
        {
            double attackTime = PrecisionTimer.Elapsed.TotalSeconds;
            nextMove = attackTime + GetBufferedWindupDuration();
            nextAttack = attackTime + GetSecondsPerAttack();
        }
    }
    else if (nextMove < time)
    {
        nextInput = time + MinInputDelay;
        InputSimulator.SendMoveClick();  // right-click di chuyển trong lúc chờ đòn đánh
    }
}
```

---

## 9. Files Changed Summary

| File | Change |
|---|---|
| `auto-kite.csproj` | `net10.0-windows`, AllowUnsafe, ProjectReference DetectColor, Single-File publish config |
| `Program.cs` | 2 key handlers, detection loop, active strategy lifecycle via Factory, AppContext.BaseDirectory for settings |
| `Strategies/IOrbWalkStrategy.cs` | Interface cho mode execution |
| `Strategies/ManualOrbWalkStrategy.cs` | Concrete strategy cho Manual mode |
| `Strategies/AutoOrbWalkStrategy.cs` | Concrete strategy cho Auto mode |
| `Strategies/OrbWalkStrategyFactory.cs` | Factory method khởi tạo singleton strategies |
| `InputSimulator.cs` | Add `SetCursorPosition()` |
| `Settings.cs` | Add all new config fields |
| `DetectColor/` (new) | 10 new files: models, capture, detection, overlay, input |


---

## 10. Single-File Build & Publishing

### Csproj Configuration (`auto-kite.csproj`)

Cấu hình xuất bản single-file trực tiếp trong `auto-kite.csproj` để khi chạy lệnh publish sẽ đóng gói tất cả runtime, dependency DLLs (Vortice, LowLevelInput, Newtonsoft.Json) thành duy nhất 1 file `.exe`:

```xml
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>

    <!-- Single-File Publishing -->
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <!-- Tắt trimming để tránh lỗi reflection serialize/deserialize Newtonsoft.Json -->
    <PublishTrimmed>false</PublishTrimmed>
  </PropertyGroup>
```

### Path Resolution trong Single-File
Khi đóng gói Single-File executable, các đường dẫn tương đối (như `settings\settings.json`) không nên dựa vào `Environment.CurrentDirectory` (vì phụ thuộc vào thư mục gọi lệnh hoặc shortcut).
Thay vào đó, sử dụng:
```csharp
private static readonly string SettingsFile = Path.Combine(AppContext.BaseDirectory, "settings", "settings.json");
```
Điều này đảm bảo file cấu hình `settings.json` luôn được đặt đúng cạnh file `.exe`.

### Publish Command
```bash
dotnet publish auto-kite/auto-kite.csproj -c Release -r win-x64
```
**Output:** File duy nhất `auto-kite.exe` tại `auto-kite\bin\Release\net10.0-windows\win-x64\publish\auto-kite.exe` có thể mang đi chạy độc lập trên bất kỳ máy Windows x64 nào mà không cần cài đặt .NET runtime.

