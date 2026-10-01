#pragma once
#include <Windows.h>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <atomic>

class OverlayWindow {
public:
    void start();
    void stop();
    ~OverlayWindow();

    HWND hwnd() const { return hwnd_; }

private:
    void window_thread_proc();
    static LRESULT CALLBACK wnd_proc(HWND hwnd, UINT msg,
                                      WPARAM wp, LPARAM lp);

    std::thread wnd_thread_;
    HWND hwnd_{};
    std::mutex mutex_;
    std::condition_variable cv_;
    std::atomic<bool> ready_{false};
};
