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

### Key Events

```
KeyDown(C)     → activeMode = Manual, start DetectionLoop + OrbWalkTimer
KeyUp(C)       → activeMode = None,   stop all + clear overlay

KeyDown(Space) → activeMode = Auto,   start DetectionLoop + OrbWalkTimer
KeyUp(Space)   → activeMode = None,   stop all + clear overlay
```

Nếu đang giữ 1 phím rồi nhấn phím kia → bỏ qua (chỉ 1 mode active tại 1 thời điểm).

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

  "EnableOverlay": false
}
```

| Field | Description | Default |
|---|---|---|
| `ManualKey` | VirtualKeyCode cho Manual mode | 46 (C) |
| `AutoKey` | VirtualKeyCode cho Auto mode | 32 (Space) |
| `TargetColorR/G/B` | Màu HP bar cần detect | 255, 0, 0 (đỏ) |
| `ColorTolerance` | Ngưỡng dung sai Euclidean RGB | 40 |
| `CaptureSize` | Vùng capture quanh chuột (px) | 400 |
| `MinClusterPixels` | Lọc nhiễu cluster nhỏ | 10 |
| `DetectionFpsCap` | Giới hạn FPS detection | 60 |
| `EnableOverlay` | Bật/tắt Direct2D overlay debug | false |

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

## 8. OrbWalkTimer_Elapsed (Modified Logic)

```csharp
// Pseudocode
void OrbWalkTimer_Elapsed()
{
    if (!HasProcess || IsExiting || !IsForeground) return;

    double time = PrecisionTimer.Elapsed.TotalSeconds;
    bool hasTarget = HasDetectedTarget;
    int tx = Interlocked.Read(ref _targetX);  // atomic read
    int ty = Interlocked.Read(ref _targetY);

    if (true || nextInput < time)  // input gate (currently disabled)
    {
        if (nextAttack < time)
        {
            nextInput = time + MinInputDelay;

            if (activeMode == Mode.Manual)
            {
                if (hasTarget)
                {
                    InputSimulator.SetCursorPosition(tx, ty);
                    InputSimulator.SendAttackClick(DIK_A);
                }
                // else: skip attack, fall through to move check below
            }
            else if (activeMode == Mode.Auto)
            {
                if (hasTarget)
                {
                    InputSimulator.SetCursorPosition(tx, ty);
                }
                InputSimulator.SendAttackClick(DIK_A);  // always attack
            }

            if (hasTarget || activeMode == Mode.Auto)
            {
                double attackTime = PrecisionTimer.Elapsed.TotalSeconds;
                nextMove = attackTime + GetBufferedWindupDuration();
                nextAttack = attackTime + GetSecondsPerAttack();
            }
        }
        else if (nextMove < time)
        {
            nextInput = time + MinInputDelay;
            InputSimulator.SendMoveClick();  // right-click move (both modes)
        }
    }
}
```

---

## 9. Files Changed Summary

| File | Change |
|---|---|
| `auto-kite.csproj` | `net10.0-windows`, AllowUnsafe, ProjectReference DetectColor |
| `Program.cs` | 2 key handlers, detection loop, modified orb-walk logic, lazy init |
| `InputSimulator.cs` | Add `SetCursorPosition()` |
| `Settings.cs` | Add all new config fields |
| `DetectColor/` (new) | 10 new files: models, capture, detection, overlay, input |
