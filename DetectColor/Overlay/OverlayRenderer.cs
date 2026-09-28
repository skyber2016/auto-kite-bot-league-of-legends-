namespace DetectColor.Overlay;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DetectColor.Models;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;

public partial class OverlayRenderer : IDisposable
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);

    private ID2D1Factory? _factory;
    private ID2D1HwndRenderTarget? _renderTarget;

    private readonly Dictionary<System.Drawing.Color, ID2D1SolidColorBrush> _brushes = new();

    private readonly object _syncLock = new();
    private DetectedBox[] _currentBoxes = [];
    private Rectangle? _scanRegion;

    private CancellationTokenSource? _renderCts;
    private Task? _renderTask;
    private bool _isDisposed;

    private int _overlayX, _overlayY, _width, _height;
    private IntPtr _hwnd;

    public void Start(IntPtr hwnd)
    {
        if (_renderTask != null) return;

        _hwnd = hwnd;
        _overlayX = GetSystemMetrics(SM_XVIRTUALSCREEN);
        _overlayY = GetSystemMetrics(SM_YVIRTUALSCREEN);
        _width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        _height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        _renderCts = new CancellationTokenSource();
        _renderTask = Task.Run(() => RenderLoopAsync(_renderCts.Token));
    }

    public void Stop()
    {
        _renderCts?.Cancel();
        try { _renderTask?.Wait(1000); } catch { }
        _renderTask = null;
        _renderCts?.Dispose();
        _renderCts = null;
    }

    public void SetBoxes(IReadOnlyList<DetectedBox> boxes)
    {
        var arr = new DetectedBox[boxes.Count];
        for (int i = 0; i < boxes.Count; i++) arr[i] = boxes[i];
        lock (_syncLock) { _currentBoxes = arr; }
    }

    public void SetScanRegion(Rectangle? scanRegion)
    {
        lock (_syncLock) { _scanRegion = scanRegion; }
    }

    public void Clear()
    {
        lock (_syncLock)
        {
            _currentBoxes = [];
            _scanRegion = null;
        }
    }

    private void InitializeD2D()
    {
        _factory = D2D1.D2D1CreateFactory<ID2D1Factory>();
        var renderTargetProps = new RenderTargetProperties(
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied));

        var hwndProps = new HwndRenderTargetProperties
        {
            Hwnd = _hwnd,
            PixelSize = new SizeI(_width, _height),
            PresentOptions = PresentOptions.None
        };

        _renderTarget = _factory.CreateHwndRenderTarget(renderTargetProps, hwndProps);
    }

    private void CleanupD2D()
    {
        foreach (var brush in _brushes.Values) brush.Dispose();
        _brushes.Clear();
        _renderTarget?.Dispose();
        _renderTarget = null;
        _factory?.Dispose();
        _factory = null;
    }

    private ID2D1SolidColorBrush GetOrCreateBrush(System.Drawing.Color color)
    {
        if (_renderTarget == null) throw new InvalidOperationException("RenderTarget chưa khởi tạo.");

        if (!_brushes.TryGetValue(color, out var brush))
        {
            brush = _renderTarget.CreateSolidColorBrush(
                new Color4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));
            _brushes[color] = brush;
        }
        return brush;
    }

    private async Task RenderLoopAsync(CancellationToken ct)
    {
        InitializeD2D();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16.67));

        try
        {
            while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
            {
                if (_renderTarget == null) break;

                _renderTarget.BeginDraw();
                _renderTarget.Clear(new Color4(0, 0, 0, 0));

                Rectangle? scan;
                DetectedBox[] boxes;
                lock (_syncLock)
                {
                    scan = _scanRegion;
                    boxes = _currentBoxes;
                }

                // 1. Vẽ khung quét quanh trỏ chuột (Cyan)
                if (scan.HasValue)
                {
                    var scanBrush = GetOrCreateBrush(System.Drawing.Color.FromArgb(180, 0, 255, 255));
                    var localScanX = scan.Value.X - _overlayX;
                    var localScanY = scan.Value.Y - _overlayY;
                    var scanRect = new System.Drawing.RectangleF(localScanX, localScanY, scan.Value.Width, scan.Value.Height);
                    _renderTarget.DrawRectangle(scanRect, scanBrush, 1.5f);
                }

                // 2. Vẽ các hộp bao quanh các mục tiêu tìm được (Lime)
                foreach (var box in boxes)
                {
                    var brush = GetOrCreateBrush(box.BorderColor);
                    var rect = box.ScreenRect;

                    var localX = rect.X - _overlayX;
                    var localY = rect.Y - _overlayY;

                    var d2dRect = new System.Drawing.RectangleF(localX, localY, rect.Width, rect.Height);
                    _renderTarget.DrawRectangle(d2dRect, brush, 3.0f);
                }

                var res = _renderTarget.EndDraw();
                if (res.Failure)
                {
                    CleanupD2D();
                    InitializeD2D();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[OverlayRenderer] Lỗi RenderLoop: {ex.Message}");
        }
        finally
        {
            CleanupD2D();
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Stop();
            CleanupD2D();
            _isDisposed = true;
        }
    }
}
