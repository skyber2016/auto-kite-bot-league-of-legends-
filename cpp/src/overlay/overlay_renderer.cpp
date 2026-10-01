#include "overlay/overlay_renderer.h"
#include <d2d1helper.h>
#include <chrono>

#pragma comment(lib, "d2d1.lib")

void OverlayRenderer::start(HWND hwnd) {
    hwnd_ = hwnd;
    if (!init_d2d(hwnd)) return;
    render_thread_ = std::jthread([this](std::stop_token st) {
        render_loop(st);
    });
}

void OverlayRenderer::stop() {
    render_thread_.request_stop();
    if (render_thread_.joinable())
        render_thread_.join();
}

OverlayRenderer::~OverlayRenderer() {
    stop();
}

bool OverlayRenderer::init_d2d(HWND hwnd) {
    hwnd_ = hwnd;
    if (!factory_) {
        HRESULT hr = D2D1CreateFactory(
            D2D1_FACTORY_TYPE_SINGLE_THREADED, factory_.GetAddressOf());
        if (FAILED(hr)) return false;
    }

    RECT rc;
    GetClientRect(hwnd, &rc);

    D2D1_RENDER_TARGET_PROPERTIES rtp = D2D1::RenderTargetProperties(
        D2D1_RENDER_TARGET_TYPE_DEFAULT,
        D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,
                          D2D1_ALPHA_MODE_PREMULTIPLIED));

    D2D1_HWND_RENDER_TARGET_PROPERTIES hrtp =
        D2D1::HwndRenderTargetProperties(
            hwnd, D2D1::SizeU(rc.right - rc.left, rc.bottom - rc.top));

    target_.Reset();
    HRESULT hr = factory_->CreateHwndRenderTarget(rtp, hrtp,
                                           target_.GetAddressOf());
    return SUCCEEDED(hr);
}

ID2D1SolidColorBrush* OverlayRenderer::get_brush(COLORREF color) {
    if (!target_) return nullptr;
    auto it = brush_cache_.find(color);
    if (it != brush_cache_.end())
        return it->second.Get();

    ComPtr<ID2D1SolidColorBrush> brush;
    D2D1_COLOR_F c = {
        GetRValue(color) / 255.0f,
        GetGValue(color) / 255.0f,
        GetBValue(color) / 255.0f,
        1.0f
    };
    target_->CreateSolidColorBrush(c, brush.GetAddressOf());
    auto* ptr = brush.Get();
    brush_cache_[color] = std::move(brush);
    return ptr;
}

void OverlayRenderer::set_boxes(std::vector<DetectedBox> boxes) {
    std::lock_guard lock(sync_);
    boxes_ = std::move(boxes);
}

void OverlayRenderer::set_scan_region(RECT region) {
    std::lock_guard lock(sync_);
    scan_region_ = region;
}

void OverlayRenderer::clear() {
    std::lock_guard lock(sync_);
    boxes_.clear();
    scan_region_ = {};
}

void OverlayRenderer::render_loop(std::stop_token stop) {
    using namespace std::chrono;
    auto interval = microseconds(16'667);  // ~60 FPS

    while (!stop.stop_requested()) {
        auto frame_start = steady_clock::now();

        if (needs_reinit_) {
            init_d2d(hwnd_);
            needs_reinit_ = false;
        }

        if (!target_) {
            auto elapsed = steady_clock::now() - frame_start;
            if (elapsed < interval)
                std::this_thread::sleep_for(interval - elapsed);
            continue;
        }

        // Snapshot state
        std::vector<DetectedBox> boxes;
        RECT scan;
        {
            std::lock_guard lock(sync_);
            boxes = boxes_;
            scan = scan_region_;
        }

        target_->BeginDraw();
        target_->Clear(D2D1::ColorF(0, 0, 0, 0));

        // Draw scan region (cyan)
        float x_offset = static_cast<float>(GetSystemMetrics(SM_XVIRTUALSCREEN));
        float y_offset = static_cast<float>(GetSystemMetrics(SM_YVIRTUALSCREEN));

        if (scan.right > scan.left && scan.bottom > scan.top) {
            auto* cyan = get_brush(RGB(0, 255, 255));
            if (cyan) {
                D2D1_RECT_F r = {
                    static_cast<float>(scan.left) - x_offset,
                    static_cast<float>(scan.top) - y_offset,
                    static_cast<float>(scan.right) - x_offset,
                    static_cast<float>(scan.bottom) - y_offset
                };
                target_->DrawRectangle(r, cyan, 1.0f);
            }
        }

        // Draw detected boxes
        for (const auto& box : boxes) {
            auto* brush = get_brush(box.border_color);
            if (brush) {
                D2D1_RECT_F r = {
                    static_cast<float>(box.screen_rect.left) - x_offset,
                    static_cast<float>(box.screen_rect.top) - y_offset,
                    static_cast<float>(box.screen_rect.right) - x_offset,
                    static_cast<float>(box.screen_rect.bottom) - y_offset
                };
                target_->DrawRectangle(r, brush, 2.0f);
            }
        }

        HRESULT hr = target_->EndDraw();
        if (hr == D2DERR_RECREATE_TARGET) {
            brush_cache_.clear();
            needs_reinit_ = true;
        }

        auto elapsed = steady_clock::now() - frame_start;
        if (elapsed < interval)
            std::this_thread::sleep_for(interval - elapsed);
    }
}
