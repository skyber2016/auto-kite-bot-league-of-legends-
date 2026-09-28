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
