#include "capture/dxgi_capturer.h"
#include <dxgi1_2.h>
#include <algorithm>
#include <cstring>
#include <iostream>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")

bool DxgiCapturer::initialize() {
    // Create D3D11 device
    D3D_FEATURE_LEVEL feature_level;
    UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
    HRESULT hr = D3D11CreateDevice(
        nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags,
        nullptr, 0, D3D11_SDK_VERSION,
        device_.GetAddressOf(), &feature_level,
        context_.GetAddressOf());
    if (FAILED(hr)) {
        std::cerr << "Failed to create D3D11 device\n";
        return false;
    }

    // Enumerate outputs
    ComPtr<IDXGIDevice> dxgi_device;
    device_.As(&dxgi_device);
    ComPtr<IDXGIAdapter> adapter;
    dxgi_device->GetAdapter(adapter.GetAddressOf());

    ComPtr<IDXGIOutput> output;
    for (UINT i = 0; adapter->EnumOutputs(i, output.ReleaseAndGetAddressOf()) == S_OK; ++i) {
        DXGI_OUTPUT_DESC desc;
        output->GetDesc(&desc);

        ComPtr<IDXGIOutput1> output1;
        output.As(&output1);
        if (!output1) continue;

        MonitorInfo info;
        info.bounds = desc.DesktopCoordinates;
        info.output = output1;

        hr = output1->DuplicateOutput(device_.Get(),
                                       info.duplication.GetAddressOf());
        if (FAILED(hr)) continue;

        if (!create_staging_texture(info)) continue;
        monitors_.push_back(std::move(info));
    }

    if (monitors_.empty()) {
        std::cerr << "No monitors found for duplication\n";
        return false;
    }
    return true;
}

bool DxgiCapturer::create_staging_texture(MonitorInfo& monitor) {
    int w = monitor.bounds.right - monitor.bounds.left;
    int h = monitor.bounds.bottom - monitor.bounds.top;

    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = static_cast<UINT>(w);
    desc.Height = static_cast<UINT>(h);
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_STAGING;
    desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;

    return SUCCEEDED(device_->CreateTexture2D(
        &desc, nullptr, monitor.staging.ReleaseAndGetAddressOf()));
}

bool DxgiCapturer::recreate_duplication(MonitorInfo& monitor) {
    monitor.duplication.Reset();
    HRESULT hr = monitor.output->DuplicateOutput(
        device_.Get(), monitor.duplication.GetAddressOf());
    return SUCCEEDED(hr);
}

CaptureResult DxgiCapturer::capture(int center_x, int center_y, int size) {
    CaptureResult result{};

    // Find which monitor contains the center point
    MonitorInfo* target = nullptr;
    for (auto& m : monitors_) {
        if (center_x >= m.bounds.left && center_x < m.bounds.right &&
            center_y >= m.bounds.top && center_y < m.bounds.bottom) {
            target = &m;
            break;
        }
    }
    if (!target) {
        if (!monitors_.empty()) target = &monitors_[0];
        else return result;
    }

    if (!target->duplication) {
        if (!recreate_duplication(*target)) return result;
    }

    // Acquire frame
    ComPtr<IDXGIResource> frame_resource;
    DXGI_OUTDUPL_FRAME_INFO frame_info;
    HRESULT hr = target->duplication->AcquireNextFrame(
        100, &frame_info, frame_resource.GetAddressOf());

    if (hr == DXGI_ERROR_ACCESS_LOST) {
        recreate_duplication(*target);
        return result;
    }
    if (FAILED(hr)) return result;

    // Copy to staging
    ComPtr<ID3D11Texture2D> frame_texture;
    frame_resource.As(&frame_texture);
    context_->CopyResource(target->staging.Get(), frame_texture.Get());
    target->duplication->ReleaseFrame();

    // Map staging texture
    D3D11_MAPPED_SUBRESOURCE mapped;
    hr = context_->Map(target->staging.Get(), 0, D3D11_MAP_READ, 0, &mapped);
    if (FAILED(hr)) return result;

    // Calculate crop rect
    int mon_w = target->bounds.right - target->bounds.left;
    int mon_h = target->bounds.bottom - target->bounds.top;
    int local_x = center_x - target->bounds.left;
    int local_y = center_y - target->bounds.top;
    int half = size / 2;

    int src_x = std::max(0, local_x - half);
    int src_y = std::max(0, local_y - half);
    int src_right = std::min(mon_w, local_x + half);
    int src_bottom = std::min(mon_h, local_y + half);
    int crop_w = src_right - src_x;
    int crop_h = src_bottom - src_y;

    if (crop_w <= 0 || crop_h <= 0) {
        context_->Unmap(target->staging.Get(), 0);
        return result;
    }

    // Copy cropped region into buffer
    int dst_stride = crop_w * 4;
    buffer_.resize(static_cast<size_t>(dst_stride) * static_cast<size_t>(crop_h));
    const uint8_t* src = static_cast<const uint8_t*>(mapped.pData);

    for (int row = 0; row < crop_h; ++row) {
        const uint8_t* src_row = src + static_cast<size_t>(src_y + row) * mapped.RowPitch
                                 + static_cast<size_t>(src_x) * 4;
        uint8_t* dst_row = buffer_.data() + static_cast<size_t>(row) * static_cast<size_t>(dst_stride);
        std::memcpy(dst_row, src_row, static_cast<size_t>(dst_stride));
    }

    context_->Unmap(target->staging.Get(), 0);

    result.buffer = buffer_;  // copy (could optimize with move semantics)
    result.width = crop_w;
    result.height = crop_h;
    result.screen_x = target->bounds.left + src_x;
    result.screen_y = target->bounds.top + src_y;
    result.stride = dst_stride;
    return result;
}
