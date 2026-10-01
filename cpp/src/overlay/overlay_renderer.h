#pragma once
#include "models/detected_box.h"
#include <d2d1.h>
#include <wrl/client.h>
#include <vector>
#include <mutex>
#include <thread>
#include <unordered_map>

using Microsoft::WRL::ComPtr;

class OverlayRenderer {
public:
    void start(HWND hwnd);
    void stop();
    ~OverlayRenderer();

    void set_boxes(std::vector<DetectedBox> boxes);
    void set_scan_region(RECT region);
    void clear();

private:
    void render_loop(std::stop_token stop);
    bool init_d2d(HWND hwnd);
    ID2D1SolidColorBrush* get_brush(COLORREF color);

    ComPtr<ID2D1Factory> factory_;
    ComPtr<ID2D1HwndRenderTarget> target_;
    std::unordered_map<COLORREF, ComPtr<ID2D1SolidColorBrush>> brush_cache_;

    std::mutex sync_;
    std::vector<DetectedBox> boxes_;
    RECT scan_region_{};

    std::jthread render_thread_;
};
