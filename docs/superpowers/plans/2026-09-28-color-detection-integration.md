# Color Detection Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tích hợp module Color Detection (DXGI GPU Screen Capture + Unsafe Color Matcher + BFS Cluster Finder + Direct2D Overlay) vào auto-kite bot với 2 chế độ (Manual/Auto) theo Factory/Strategy pattern, sử dụng async/await task với PeriodicTimer và hỗ trợ đóng gói Single-File executable.

**Architecture:** Tạo project class library riêng `DetectColor` chứa toàn bộ logic capture GPU, detection unsafe pointer, clustering và overlay Direct2D. Trong `auto-kite`, đa hình hóa 2 chế độ (Manual qua phím C, Auto qua phím Space) bằng `IOrbWalkStrategy` + `OrbWalkStrategyFactory`. Thay thế toàn bộ timer/thread cũ bằng `async Task` kết hợp `PeriodicTimer` và `CancellationTokenSource`. Cấu hình `auto-kite.csproj` hỗ trợ xuất bản single-file self-contained `.exe`.

**Tech Stack:** .NET 10 (`net10.0-windows`), C# 13, Vortice.Direct2D1 / Direct3D11 / DXGI / Mathematics 3.8.1, LowLevelInput.Net, Newtonsoft.Json 13.0.3, Windows Win32 API.

**Spec:** `docs/superpowers/specs/2026-09-28-color-detection-integration-design.md`

## Global Constraints

- Target Framework: `net10.0-windows` trên nền kiến trúc x64 (`win-x64`).
- AllowUnsafeBlocks: Bắt buộc `true` cho `DetectColor` và `auto-kite`.
- Zero Allocation trong hot path: Tái sử dụng byte buffer và staging texture qua các frame.
- Không dùng `Thread.Sleep` trong detection loop hay orb-walk loop — dùng `PeriodicTimer`.
- Overlay Window (Win32 message loop) phải duy trì dedicated OS thread với STA apartment state do thread-affinity của Win32.
- Đường dẫn file cấu hình luôn resolve qua `AppContext.BaseDirectory` để tương thích Single-File executable.

---

### Task 1: Create DetectColor Library Project & Data Models

**Files:**
- Create: `DetectColor/DetectColor.csproj`
- Create: `DetectColor/Models/CaptureResult.cs`
- Create: `DetectColor/Models/ColorMatch.cs`
- Create: `DetectColor/Models/ColorCluster.cs`
- Create: `DetectColor/Models/DetectedBox.cs`
- Modify: `auto-kite.sln`

**Interfaces:**
- Produces:
  - `DetectColor.Models.CaptureResult`: `byte[] Buffer`, `int Width`, `int Height`, `int ScreenX`, `int ScreenY`, `long CaptureTimeUs`, `int Stride`, `int PixelCount`, `bool IsEmpty`
  - `DetectColor.Models.ColorMatch`: `Point LocalPosition`, `Point ScreenPosition`, `Color ActualColor`, `double Distance`
  - `DetectColor.Models.ColorCluster`: `Rectangle BoundingRect`, `Point Center`, `int PixelCount`
  - `DetectColor.Models.DetectedBox`: `Rectangle ScreenRect`, `Color BorderColor`, `string? Label`, `DateTime DetectedAt`

- [ ] **Step 1: Create `DetectColor/DetectColor.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <PlatformTarget>x64</PlatformTarget>
    <RootNamespace>DetectColor</RootNamespace>
    <AssemblyName>DetectColor</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Vortice.Direct2D1" Version="3.8.1" />
    <PackageReference Include="Vortice.Direct3D11" Version="3.8.1" />
    <PackageReference Include="Vortice.DXGI" Version="3.8.1" />
    <PackageReference Include="Vortice.Mathematics" Version="2.0.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create Data Models in `DetectColor/Models/`**

Tạo 4 file models: `CaptureResult.cs`, `ColorMatch.cs`, `ColorCluster.cs`, `DetectedBox.cs`.

`DetectColor/Models/CaptureResult.cs`:
```csharp
namespace DetectColor.Models;

public readonly struct CaptureResult
{
    public byte[] Buffer { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int ScreenX { get; init; }
    public int ScreenY { get; init; }
    public long CaptureTimeUs { get; init; }

    public int Stride => Width * 4;
    public int PixelCount => Width * Height;
    public bool IsEmpty => Buffer is null or { Length: 0 };
}
```

`DetectColor/Models/ColorMatch.cs`:
```csharp
namespace DetectColor.Models;

using System.Drawing;

public readonly struct ColorMatch
{
    public Point LocalPosition { get; init; }
    public Point ScreenPosition { get; init; }
    public Color ActualColor { get; init; }
    public double Distance { get; init; }
}
```

`DetectColor/Models/ColorCluster.cs`:
```csharp
namespace DetectColor.Models;

using System.Drawing;

public readonly struct ColorCluster
{
    public Rectangle BoundingRect { get; init; }
    public Point Center { get; init; }
    public int PixelCount { get; init; }
}
```

`DetectColor/Models/DetectedBox.cs`:
```csharp
namespace DetectColor.Models;

using System.Drawing;

public readonly struct DetectedBox
{
    public Rectangle ScreenRect { get; init; }
    public Color BorderColor { get; init; }
    public string? Label { get; init; }
    public DateTime DetectedAt { get; init; }
}
```

- [ ] **Step 3: Add `DetectColor.csproj` to solution and build**

Run:
```bash
dotnet sln auto-kite.sln add DetectColor/DetectColor.csproj
dotnet build DetectColor/DetectColor.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add DetectColor/ auto-kite.sln
git commit -m "feat(detect-color): initialize project and data models"
```

---

### Task 2: Implement ColorMatcher & ClusterFinder with Unit Tests

**Files:**
- Create: `DetectColor/Detection/ColorMatcher.cs`
- Create: `DetectColor/Detection/ClusterFinder.cs`
- Create: `tests/DetectColor.Tests/DetectColor.Tests.csproj`
- Create: `tests/DetectColor.Tests/ColorMatcherTests.cs`
- Create: `tests/DetectColor.Tests/ClusterFinderTests.cs`

**Interfaces:**
- Consumes: `DetectColor.Models.CaptureResult`, `ColorMatch`, `ColorCluster`
- Produces:
  - `ColorMatcher.FindPixels(CaptureResult capture, Color targetColor, int tolerance)` -> `List<ColorMatch>`
  - `ClusterFinder.FindClusters(List<ColorMatch> matches, int imageWidth, int imageHeight, int captureScreenX, int captureScreenY, int minPixels)` -> `List<ColorCluster>`

- [ ] **Step 1: Write `DetectColor/Detection/ColorMatcher.cs`**

```csharp
namespace DetectColor.Detection;

using System;
using System.Collections.Generic;
using System.Drawing;
using DetectColor.Models;

public sealed class ColorMatcher
{
    public List<ColorMatch> FindPixels(CaptureResult capture, Color targetColor, int tolerance)
    {
        if (capture.IsEmpty) return [];

        var results = new List<ColorMatch>();
        int toleranceSq = tolerance * tolerance;
        int tR = targetColor.R;
        int tG = targetColor.G;
        int tB = targetColor.B;
        int width = capture.Width;
        int height = capture.Height;
        int screenX = capture.ScreenX;
        int screenY = capture.ScreenY;

        unsafe
        {
            fixed (byte* pBuffer = capture.Buffer)
            {
                byte* ptr = pBuffer;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int b = ptr[0];
                        int g = ptr[1];
                        int r = ptr[2];

                        int dr = r - tR;
                        int dg = g - tG;
                        int db = b - tB;
                        int distSq = dr * dr + dg * dg + db * db;

                        if (distSq <= toleranceSq)
                        {
                            results.Add(new ColorMatch
                            {
                                LocalPosition = new Point(x, y),
                                ScreenPosition = new Point(screenX + x, screenY + y),
                                ActualColor = Color.FromArgb(r, g, b),
                                Distance = Math.Sqrt(distSq)
                            });
                        }

                        ptr += 4;
                    }
                }
            }
        }

        return results;
    }
}
```

- [ ] **Step 2: Write `DetectColor/Detection/ClusterFinder.cs`**

```csharp
namespace DetectColor.Detection;

using System.Collections.Generic;
using System.Drawing;
using DetectColor.Models;

public sealed class ClusterFinder
{
    private static readonly (int dx, int dy)[] Neighbors =
    [
        (-1, -1), (0, -1), (1, -1),
        (-1,  0),          (1,  0),
        (-1,  1), (0,  1), (1,  1)
    ];

    public List<ColorCluster> FindClusters(
        List<ColorMatch> matches,
        int imageWidth,
        int imageHeight,
        int captureScreenX,
        int captureScreenY,
        int minPixels = 10)
    {
        if (matches.Count == 0 || imageWidth <= 0 || imageHeight <= 0) return [];

        var grid = new bool[imageWidth, imageHeight];
        foreach (var match in matches)
        {
            var p = match.LocalPosition;
            if (p.X >= 0 && p.X < imageWidth && p.Y >= 0 && p.Y < imageHeight)
            {
                grid[p.X, p.Y] = true;
            }
        }

        var visited = new bool[imageWidth, imageHeight];
        var clusters = new List<ColorCluster>();
        var queue = new Queue<Point>();

        for (int y = 0; y < imageHeight; y++)
        {
            for (int x = 0; x < imageWidth; x++)
            {
                if (!grid[x, y] || visited[x, y])
                    continue;

                queue.Clear();
                queue.Enqueue(new Point(x, y));
                visited[x, y] = true;

                int minX = x, maxX = x, minY = y, maxY = y;
                long sumX = 0, sumY = 0;
                int count = 0;

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    count++;
                    sumX += current.X;
                    sumY += current.Y;

                    if (current.X < minX) minX = current.X;
                    if (current.X > maxX) maxX = current.X;
                    if (current.Y < minY) minY = current.Y;
                    if (current.Y > maxY) maxY = current.Y;

                    foreach (var (dx, dy) in Neighbors)
                    {
                        int nx = current.X + dx;
                        int ny = current.Y + dy;

                        if (nx >= 0 && nx < imageWidth && ny >= 0 && ny < imageHeight
                            && grid[nx, ny] && !visited[nx, ny])
                        {
                            visited[nx, ny] = true;
                            queue.Enqueue(new Point(nx, ny));
                        }
                    }
                }

                if (count >= minPixels)
                {
                    clusters.Add(new ColorCluster
                    {
                        BoundingRect = new Rectangle(
                            captureScreenX + minX,
                            captureScreenY + minY,
                            maxX - minX + 1,
                            maxY - minY + 1),
                        Center = new Point(
                            captureScreenX + (int)(sumX / count),
                            captureScreenY + (int)(sumY / count)),
                        PixelCount = count
                    });
                }
            }
        }

        clusters.Sort((a, b) => b.PixelCount.CompareTo(a.PixelCount));
        return clusters;
    }
}
```

- [ ] **Step 3: Create Test Project & Unit Tests**

Tạo `tests/DetectColor.Tests/DetectColor.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\DetectColor\DetectColor.csproj" />
  </ItemGroup>

</Project>
```

Tạo `tests/DetectColor.Tests/ColorMatcherTests.cs`:
```csharp
namespace DetectColor.Tests;

using System.Drawing;
using DetectColor.Detection;
using DetectColor.Models;
using Xunit;

public class ColorMatcherTests
{
    [Fact]
    public void FindPixels_ExactColor_MatchesSuccessfully()
    {
        var matcher = new ColorMatcher();
        // 2x2 image, BGRA: (0,0) is Red (B=0, G=0, R=255, A=255)
        byte[] buffer = new byte[2 * 2 * 4];
        buffer[2] = 255; buffer[3] = 255; // (0,0) Red

        var capture = new CaptureResult
        {
            Buffer = buffer,
            Width = 2,
            Height = 2,
            ScreenX = 100,
            ScreenY = 200
        };

        var matches = matcher.FindPixels(capture, Color.FromArgb(255, 0, 0), tolerance: 10);
        Assert.Single(matches);
        Assert.Equal(new Point(0, 0), matches[0].LocalPosition);
        Assert.Equal(new Point(100, 200), matches[0].ScreenPosition);
    }
}
```

Tạo `tests/DetectColor.Tests/ClusterFinderTests.cs`:
```csharp
namespace DetectColor.Tests;

using System.Collections.Generic;
using System.Drawing;
using DetectColor.Detection;
using DetectColor.Models;
using Xunit;

public class ClusterFinderTests
{
    [Fact]
    public void FindClusters_ConnectedPixels_FormsSingleCluster()
    {
        var finder = new ClusterFinder();
        var matches = new List<ColorMatch>();
        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                matches.Add(new ColorMatch
                {
                    LocalPosition = new Point(x, y),
                    ScreenPosition = new Point(x, y)
                });
            }
        }

        var clusters = finder.FindClusters(matches, 10, 10, 0, 0, minPixels: 10);
        Assert.Single(clusters);
        Assert.Equal(25, clusters[0].PixelCount);
        Assert.Equal(new Point(2, 2), clusters[0].Center);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:
```bash
dotnet test tests/DetectColor.Tests/DetectColor.Tests.csproj
```
Expected: All tests pass (2 passed).

- [ ] **Step 5: Commit**

```bash
git add DetectColor/Detection/ tests/
git commit -m "feat(detect-color): add ColorMatcher, ClusterFinder and unit tests"
```

---

### Task 3: Implement DxgiCapturer & MouseHelper

**Files:**
- Create: `DetectColor/Input/MouseHelper.cs`
- Create: `DetectColor/Capture/DxgiCapturer.cs`

**Interfaces:**
- Produces:
  - `MouseHelper.GetCursorPosition()` -> `Point`
  - `DxgiCapturer`: `CaptureResult Capture(int centerX, int centerY, int size)`
  - `IDisposable` implementation on `DxgiCapturer`

- [ ] **Step 1: Write `DetectColor/Input/MouseHelper.cs`**

```csharp
namespace DetectColor.Input;

using System.Drawing;
using System.Runtime.InteropServices;

public static partial class MouseHelper
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    public static Point GetCursorPosition()
    {
        GetCursorPos(out var point);
        return new Point(point.X, point.Y);
    }
}
```

- [ ] **Step 2: Write `DetectColor/Capture/DxgiCapturer.cs`**

Implement `DxgiCapturer` with multi-monitor support, zero-allocation staging texture reuse, and mapped CPU crop.

```csharp
namespace DetectColor.Capture;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DetectColor.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

public sealed class DxgiCapturer : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly List<MonitorInfo> _monitors = new();
    private byte[]? _buffer;

    public string Name => "DXGI Desktop Duplication";

    private sealed class MonitorInfo : IDisposable
    {
        public readonly int X, Y, Width, Height;
        public readonly IDXGIOutput1 Output1;
        public IDXGIOutputDuplication? Duplication;
        public ID3D11Texture2D? StagingTexture;

        public MonitorInfo(IDXGIOutput1 output1, int x, int y, int w, int h)
        {
            Output1 = output1; X = x; Y = y; Width = w; Height = h;
        }

        public bool Contains(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;

        public void Dispose()
        {
            StagingTexture?.Dispose();
            Duplication?.Dispose();
            Output1.Dispose();
        }
    }

    public DxgiCapturer()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        IDXGIAdapter1? selectedAdapter = null;
        if (factory.EnumAdapters1(0, out var adapter).Success)
        {
            selectedAdapter = adapter;
        }

        if (selectedAdapter is null)
            throw new InvalidOperationException("Không tìm thấy DXGI Adapter.");

        D3D11.D3D11CreateDevice(
            selectedAdapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            Array.Empty<FeatureLevel>(),
            out _device!,
            out _context!).CheckError();

        for (uint i = 0; selectedAdapter.EnumOutputs(i, out var output).Success; i++)
        {
            var desc = output.Description;
            var output1 = output.QueryInterface<IDXGIOutput1>();
            output.Dispose();

            _monitors.Add(new MonitorInfo(
                output1,
                desc.DesktopCoordinates.Left,
                desc.DesktopCoordinates.Top,
                desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left,
                desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top));
        }

        selectedAdapter.Dispose();
    }

    public CaptureResult Capture(int centerX, int centerY, int size)
    {
        var sw = Stopwatch.StartNew();

        MonitorInfo? target = null;
        foreach (var m in _monitors)
        {
            if (m.Contains(centerX, centerY)) { target = m; break; }
        }
        if (target is null) return default;

        try
        {
            target.Duplication ??= target.Output1.DuplicateOutput(_device);
        }
        catch { return default; }

        var hr = target.Duplication!.AcquireNextFrame(100, out _, out var resource);
        if (hr.Failure)
        {
            if (hr == Vortice.DXGI.ResultCode.AccessLost || hr == Vortice.DXGI.ResultCode.AccessDenied)
            {
                target.Duplication?.Dispose();
                target.Duplication = null;
            }
            return default;
        }

        using var texture = resource!.QueryInterface<ID3D11Texture2D>();
        resource.Dispose();

        var texDesc = texture.Description;

        if (target.StagingTexture is null ||
            target.StagingTexture.Description.Width != texDesc.Width ||
            target.StagingTexture.Description.Height != texDesc.Height)
        {
            target.StagingTexture?.Dispose();
            target.StagingTexture = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = texDesc.Width,
                Height = texDesc.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = texDesc.Format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,
                MiscFlags = ResourceOptionFlags.None
            });
        }

        _context.CopyResource(target.StagingTexture!, texture);
        target.Duplication.ReleaseFrame();

        var mapped = _context.Map(target.StagingTexture!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

        int localX = centerX - target.X;
        int localY = centerY - target.Y;
        int half = size / 2;
        int startX = Math.Max(0, localX - half);
        int startY = Math.Max(0, localY - half);
        int endX = Math.Min((int)texDesc.Width, localX + size - half);
        int endY = Math.Min((int)texDesc.Height, localY + size - half);

        int w = endX - startX;
        int h = endY - startY;

        if (w <= 0 || h <= 0)
        {
            _context.Unmap(target.StagingTexture!, 0);
            return default;
        }

        int bufSize = w * h * 4;
        if (_buffer is null || _buffer.Length != bufSize)
            _buffer = new byte[bufSize];

        unsafe
        {
            byte* src = (byte*)mapped.DataPointer;
            int pitch = (int)mapped.RowPitch;

            fixed (byte* dst = _buffer)
            {
                for (int y = 0; y < h; y++)
                {
                    byte* rowSrc = src + (startY + y) * pitch + startX * 4;
                    byte* rowDst = dst + y * w * 4;
                    Unsafe.CopyBlock(rowDst, rowSrc, (uint)(w * 4));
                }
            }
        }

        _context.Unmap(target.StagingTexture!, 0);
        sw.Stop();

        return new CaptureResult
        {
            Buffer = _buffer,
            Width = w,
            Height = h,
            ScreenX = target.X + startX,
            ScreenY = target.Y + startY,
            CaptureTimeUs = sw.ElapsedTicks * 1_000_000 / Stopwatch.Frequency
        };
    }

    public void Dispose()
    {
        foreach (var m in _monitors) m.Dispose();
        _monitors.Clear();
        _context?.Dispose();
        _device?.Dispose();
    }
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build DetectColor/DetectColor.csproj`
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add DetectColor/Input/ DetectColor/Capture/
git commit -m "feat(detect-color): add DxgiCapturer GPU capture and MouseHelper"
```

---

### Task 4: Implement OverlayWindow & OverlayRenderer (with PeriodicTimer)

**Files:**
- Create: `DetectColor/Overlay/OverlayWindow.cs`
- Create: `DetectColor/Overlay/OverlayRenderer.cs`

**Interfaces:**
- Produces:
  - `OverlayWindow`: `Show()`, `Hide()`, `Destroy()`, `Hwnd`, `event OnCreated`, `IDisposable`
  - `OverlayRenderer`: `Start(IntPtr hwnd)`, `Stop()`, `SetBoxes(IReadOnlyList<DetectedBox>)`, `SetScanRegion(Rectangle?)`, `Clear()`, `IDisposable`

- [ ] **Step 1: Write `DetectColor/Overlay/OverlayWindow.cs`**

Triển khai Win32 transparent click-through window trên dedicated STA Thread (theo Section 7 & Section 5 của spec).

- [ ] **Step 2: Write `DetectColor/Overlay/OverlayRenderer.cs`**

Triển khai Direct2D rendering sử dụng `PeriodicTimer` (hoặc `Task.Delay(16)`) thay vì `Thread.Sleep(16)` theo chỉ đạo hiện đại hóa async/await.

- [ ] **Step 3: Verify build**

Run: `dotnet build DetectColor/DetectColor.csproj`
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add DetectColor/Overlay/
git commit -m "feat(detect-color): add transparent OverlayWindow and Direct2D OverlayRenderer"
```

---

### Task 5: Add SetCursorPosition in InputSimulator & Update auto-kite.csproj

**Files:**
- Modify: `auto-kite/InputSimulator.cs`
- Modify: `auto-kite/auto-kite.csproj`

**Interfaces:**
- Consumes: Win32 `SendInput` MOUSEINPUT
- Produces:
  - `InputSimulator.SetCursorPosition(int x, int y)`

- [ ] **Step 1: Add `SetCursorPosition` to `auto-kite/InputSimulator.cs`**

Sử dụng `SendInput` với flags `MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE`:
```csharp
public static void SetCursorPosition(int x, int y)
{
    int screenWidth = Win32.GetSystemMetrics(0);  // SM_CXSCREEN
    int screenHeight = Win32.GetSystemMetrics(1); // SM_CYSCREEN

    int normX = (int)Math.Round(x * 65535.0 / (screenWidth - 1));
    int normY = (int)Math.Round(y * 65535.0 / (screenHeight - 1));

    INPUT input = new INPUT
    {
        type = INPUT_MOUSE,
        u = new InputUnion
        {
            mi = new MOUSEINPUT
            {
                dx = normX,
                dy = normY,
                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
                mouseData = 0,
                dwExtraInfo = UIntPtr.Zero,
                time = 0
            }
        }
    };

    SendInput(1, [input], InputSize);
}
```

- [ ] **Step 2: Update `auto-kite/auto-kite.csproj`**

Cập nhật target `net10.0-windows`, `AllowUnsafeBlocks=true`, `ProjectReference` tới `DetectColor`, và cấu hình Single-File Publish:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>

    <!-- Single-File Publishing Support -->
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <PublishTrimmed>false</PublishTrimmed>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Release|AnyCPU'">
    <DebugType>none</DebugType>
    <DebugSymbols>false</DebugSymbols>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\LowLevelInput.Net\Project\LowLevelInput\LowLevelInput.csproj" />
    <ProjectReference Include="..\DetectColor\DetectColor.csproj" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Verify build**

Run: `dotnet build auto-kite/auto-kite.csproj`
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add auto-kite/InputSimulator.cs auto-kite/auto-kite.csproj
git commit -m "feat(auto-kite): add SetCursorPosition and configure single-file publish"
```

---

### Task 6: Implement Mode Strategies & Factory

**Files:**
- Create: `auto-kite/Strategies/IOrbWalkStrategy.cs`
- Create: `auto-kite/Strategies/ManualOrbWalkStrategy.cs`
- Create: `auto-kite/Strategies/AutoOrbWalkStrategy.cs`
- Create: `auto-kite/Strategies/OrbWalkStrategyFactory.cs`
- Create: `tests/auto-kite.Tests/auto-kite.Tests.csproj`
- Create: `tests/auto-kite.Tests/OrbWalkStrategyTests.cs`

**Interfaces:**
- Produces:
  - `IOrbWalkStrategy`: `bool TryAttack(bool hasTarget, int targetX, int targetY, ushort attackScancode)`
  - `ManualOrbWalkStrategy`: `TryAttack` returns false if `!hasTarget`, attacks and returns true if `hasTarget`.
  - `AutoOrbWalkStrategy`: `TryAttack` always attacks and returns true, moves cursor if `hasTarget`.
  - `OrbWalkStrategyFactory`: `IOrbWalkStrategy? Create(OrbWalkMode mode)`

- [ ] **Step 1: Write `IOrbWalkStrategy.cs`**

```csharp
namespace OddAutoWalker.Strategies;

public interface IOrbWalkStrategy
{
    bool TryAttack(bool hasTarget, int targetX, int targetY, ushort attackScancode);
}
```

- [ ] **Step 2: Write `ManualOrbWalkStrategy.cs` & `AutoOrbWalkStrategy.cs`**

`ManualOrbWalkStrategy.cs`:
```csharp
namespace OddAutoWalker.Strategies;

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
```

`AutoOrbWalkStrategy.cs`:
```csharp
namespace OddAutoWalker.Strategies;

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
```

- [ ] **Step 3: Write `OrbWalkStrategyFactory.cs`**

```csharp
namespace OddAutoWalker.Strategies;

public enum OrbWalkMode
{
    None,
    Manual,
    Auto
}

public static class OrbWalkStrategyFactory
{
    private static readonly IOrbWalkStrategy Manual = new ManualOrbWalkStrategy();
    private static readonly IOrbWalkStrategy Auto = new AutoOrbWalkStrategy();

    public static IOrbWalkStrategy? Create(OrbWalkMode mode) => mode switch
    {
        OrbWalkMode.Manual => Manual,
        OrbWalkMode.Auto => Auto,
        _ => null
    };
}
```

- [ ] **Step 4: Create unit tests for Factory and Strategy behavior**

Tạo `tests/auto-kite.Tests/OrbWalkStrategyTests.cs` kiểm tra:
- `OrbWalkStrategyFactory.Create(OrbWalkMode.Manual)` trả về `ManualOrbWalkStrategy`.
- `OrbWalkStrategyFactory.Create(OrbWalkMode.Auto)` trả về `AutoOrbWalkStrategy`.
- `OrbWalkStrategyFactory.Create(OrbWalkMode.None)` trả về `null`.
- `ManualStrategy.TryAttack(hasTarget: false)` trả về `false`.

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/auto-kite.Tests/auto-kite.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add auto-kite/Strategies/ tests/auto-kite.Tests/
git commit -m "feat(auto-kite): add Factory and Strategy pattern for Manual/Auto modes"
```

---

### Task 7: Update Settings for Full Configuration Fields & BaseDirectory

**Files:**
- Modify: `auto-kite/Settings.cs`

**Interfaces:**
- Produces:
  - `Settings`: `ManualKey`, `AutoKey`, `TargetColorR`, `TargetColorG`, `TargetColorB`, `ColorTolerance`, `CaptureSize`, `MinClusterPixels`, `DetectionFpsCap`, `EnableOverlay`, `WindupBufferMs`, `MinInputDelayMs`, `OrbWalkTickRateMs`, `AttackSpeedPollMs`
  - `CreateNew(string path)` / `Load(string path)`

- [ ] **Step 1: Update `auto-kite/Settings.cs`**

Thêm các thuộc tính mới với giá trị mặc định được review trong spec (Section 6).
Đảm bảo comments VirtualKeyCode đầy đủ.

- [ ] **Step 2: Verify build**

Run: `dotnet build auto-kite/auto-kite.csproj`
Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add auto-kite/Settings.cs
git commit -m "feat(auto-kite): expand Settings with timing, detection, and overlay parameters"
```

---

### Task 8: Integrate Detection & Orb-Walk Async Loops into Program.cs

**Files:**
- Modify: `auto-kite/Program.cs`

**Interfaces:**
- Consumes: `DxgiCapturer`, `ColorMatcher`, `ClusterFinder`, `OverlayWindow`, `OverlayRenderer`, `OrbWalkStrategyFactory`, `IOrbWalkStrategy`
- Produces:
  - `DetectionLoopAsync(CancellationToken ct)`
  - `OrbWalkLoopAsync(CancellationToken ct)`
  - `CheckLeagueProcessAsync(CancellationToken ct)`
  - Updated `InputManager_OnKeyboardEvent` for ManualKey & AutoKey

- [ ] **Step 1: Update `SettingsFile` path resolution**

Thay đổi `SettingsFile` trong `Program.cs`:
```csharp
private static readonly string SettingsFile = Path.Combine(AppContext.BaseDirectory, "settings", "settings.json");
```

- [ ] **Step 2: Replace `CheckLeagueProcess` with `CheckLeagueProcessAsync`**

```csharp
private static async Task CheckLeagueProcessAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested && (LeagueProcess is null || !HasProcess))
    {
        LeagueProcess = Process.GetProcessesByName("League of Legends").FirstOrDefault();
        if (LeagueProcess is null || LeagueProcess.HasExited)
        {
            await Task.Delay(2000, ct);
            continue;
        }
        HasProcess = true;
    }
}
```

- [ ] **Step 3: Implement `DetectionLoopAsync` & `OrbWalkLoopAsync` with `PeriodicTimer`**

Triển khai 2 async task loops theo đúng Section 5.2 và 8.3 của spec:
- `DetectionLoopAsync`: Chạy 60 FPS, gọi `MouseHelper.GetCursorPosition()`, `DxgiCapturer.Capture()`, `ColorMatcher.FindPixels()`, `ClusterFinder.FindClusters()`, tìm cluster gần chuột nhất, cập nhật `_targetX`, `_targetY`, `HasDetectedTarget`. Nếu `EnableOverlay`, cập nhật boxes lên `OverlayRenderer`.
- `OrbWalkLoopAsync`: Chạy với chu kỳ `CurrentSettings.OrbWalkTickRateMs`, gọi `strategy.TryAttack(...)` qua `_currentStrategy`.

- [ ] **Step 4: Update Key Handlers for 2 Modes & CancellationTokenSource**

Khi nhận `KeyDown(CurrentSettings.ManualKey)` hoặc `KeyDown(CurrentSettings.AutoKey)`:
- Nếu chưa có mode nào active:
  - Khởi tạo lazy `DxgiCapturer` và `OverlayWindow` / `OverlayRenderer` (nếu `EnableOverlay`).
  - Gán `_currentStrategy = OrbWalkStrategyFactory.Create(mode)`.
  - Khởi tạo `_activeLoopCts = new CancellationTokenSource()`.
  - Khởi chạy `_ = DetectionLoopAsync(_activeLoopCts.Token)` và `_ = OrbWalkLoopAsync(_activeLoopCts.Token)`.

Khi nhận `KeyUp`:
- Hủy `_activeLoopCts?.Cancel(); _activeLoopCts?.Dispose(); _activeLoopCts = null;`.
- Gán `_currentStrategy = null;`.
- Xóa box trên overlay nếu có.

- [ ] **Step 5: Verify build**

Run: `dotnet build auto-kite/auto-kite.csproj`
Expected: Build succeeded with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add auto-kite/Program.cs
git commit -m "feat(auto-kite): integrate async detection and orb-walk loops with PeriodicTimer"
```

---

### Task 9: Full Solution Build & Single-File Publish Verification

**Files:**
- Output: `auto-kite/bin/Release/net10.0-windows/win-x64/publish/auto-kite.exe`

- [ ] **Step 1: Run all tests in the solution**

Run: `dotnet test auto-kite.sln`
Expected: All tests pass.

- [ ] **Step 2: Build Debug and Release configurations**

Run:
```bash
dotnet build auto-kite.sln -c Debug
dotnet build auto-kite.sln -c Release
```
Expected: All projects compile with 0 errors.

- [ ] **Step 3: Publish Single-File Executable**

Run:
```bash
dotnet publish auto-kite/auto-kite.csproj -c Release -r win-x64
```
Expected: Lệnh hoàn thành thành công và tạo ra file duy nhất `auto-kite.exe` tại thư mục publish.

- [ ] **Step 4: Verify single-file output existence & size**

Run: `Get-Item auto-kite/bin/Release/net10.0-windows/win-x64/publish/auto-kite.exe | Select-Object Name, Length`
Expected: File `auto-kite.exe` tồn tại, dung lượng hợp lý (~50-80MB self-contained).

- [ ] **Step 5: Commit final changes**

```bash
git add .
git commit -m "chore: verify full solution build and single-file publish"
```
