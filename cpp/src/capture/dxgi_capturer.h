#pragma once
#include "models/capture_result.h"
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
#include <vector>

using Microsoft::WRL::ComPtr;

class DxgiCapturer {
public:
    DxgiCapturer() = default;
    ~DxgiCapturer() = default;

    DxgiCapturer(const DxgiCapturer&) = delete;
    DxgiCapturer& operator=(const DxgiCapturer&) = delete;

    bool initialize();
    CaptureResult capture(int center_x, int center_y, int size);

private:
    struct MonitorInfo {
        RECT bounds;
        ComPtr<IDXGIOutput1> output;
        ComPtr<IDXGIOutputDuplication> duplication;
        ComPtr<ID3D11Texture2D> staging;
    };

    bool create_staging_texture(MonitorInfo& monitor);
    bool recreate_duplication(MonitorInfo& monitor);

    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11DeviceContext> context_;
    std::vector<MonitorInfo> monitors_;
    std::vector<uint8_t> buffer_;
};
