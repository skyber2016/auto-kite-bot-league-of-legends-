#pragma once
#include <Windows.h>
#include <functional>
#include <thread>
#include <atomic>

class InputHooks {
public:
    using KeyboardCallback = std::function<void(int vk_code, bool key_down)>;

    void start(KeyboardCallback on_keyboard);
    void stop();
    ~InputHooks();

private:
    void hook_thread_proc();

    static LRESULT CALLBACK keyboard_proc(int code, WPARAM wp, LPARAM lp);

    KeyboardCallback on_keyboard_;
    std::thread hook_thread_;
    DWORD hook_thread_id_{};
    HHOOK kb_hook_{};
    std::atomic<bool> running_{false};

    static InputHooks* s_instance_;
};
