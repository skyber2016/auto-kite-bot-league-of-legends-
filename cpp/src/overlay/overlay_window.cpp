#include "overlay/overlay_window.h"
#include <dwmapi.h>

#pragma comment(lib, "dwmapi.lib")

static const wchar_t* kClassName = L"AutoKiteOverlay";

void OverlayWindow::start() {
    wnd_thread_ = std::thread(&OverlayWindow::window_thread_proc, this);

    std::unique_lock lock(mutex_);
    cv_.wait(lock, [this] { return ready_.load(); });
}

void OverlayWindow::stop() {
    if (hwnd_) {
        PostMessageW(hwnd_, WM_CLOSE, 0, 0);
    }
    if (wnd_thread_.joinable())
        wnd_thread_.join();
    hwnd_ = nullptr;
}

OverlayWindow::~OverlayWindow() {
    stop();
}

void OverlayWindow::window_thread_proc() {
    SetProcessDpiAwarenessContext(
        DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = wnd_proc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = kClassName;
    RegisterClassExW(&wc);

    int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
    int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
    int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
    int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);

    hwnd_ = CreateWindowExW(
        WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST |
        WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
        kClassName, L"", WS_POPUP | WS_VISIBLE,
        x, y, w, h,
        nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);

    SetLayeredWindowAttributes(hwnd_, RGB(0, 0, 0), 0, LWA_COLORKEY);

    MARGINS margins = {-1, -1, -1, -1};
    DwmExtendFrameIntoClientArea(hwnd_, &margins);

    UpdateWindow(hwnd_);
    ShowWindow(hwnd_, SW_SHOW);

    {
        std::lock_guard lock(mutex_);
        ready_ = true;
    }
    cv_.notify_one();

    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
}

LRESULT CALLBACK OverlayWindow::wnd_proc(HWND hwnd, UINT msg,
                                           WPARAM wp, LPARAM lp) {
    switch (msg) {
        case WM_NCHITTEST: return HTTRANSPARENT;
        case WM_ERASEBKGND: return 1;
        case WM_PAINT: ValidateRect(hwnd, nullptr); return 0;
        case WM_DESTROY: PostQuitMessage(0); return 0;
        default: return DefWindowProcW(hwnd, msg, wp, lp);
    }
}
