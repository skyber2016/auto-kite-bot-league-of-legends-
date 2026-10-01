#include "input/input_hooks.h"

InputHooks* InputHooks::s_instance_ = nullptr;

void InputHooks::start(KeyboardCallback on_keyboard) {
    on_keyboard_ = std::move(on_keyboard);
    s_instance_ = this;
    running_ = true;

    hook_thread_ = std::thread(&InputHooks::hook_thread_proc, this);
}

void InputHooks::stop() {
    if (!running_) return;
    running_ = false;
    PostThreadMessageW(hook_thread_id_, WM_QUIT, 0, 0);
    if (hook_thread_.joinable())
        hook_thread_.join();
    s_instance_ = nullptr;
}

InputHooks::~InputHooks() {
    stop();
}

void InputHooks::hook_thread_proc() {
    hook_thread_id_ = GetCurrentThreadId();

    kb_hook_ = SetWindowsHookExW(
        WH_KEYBOARD_LL, keyboard_proc, nullptr, 0);

    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    if (kb_hook_) {
        UnhookWindowsHookEx(kb_hook_);
        kb_hook_ = nullptr;
    }
}

LRESULT CALLBACK InputHooks::keyboard_proc(int code, WPARAM wp, LPARAM lp) {
    if (code >= 0 && s_instance_ && s_instance_->on_keyboard_) {
        auto* info = reinterpret_cast<KBDLLHOOKSTRUCT*>(lp);
        bool key_down = (wp == WM_KEYDOWN || wp == WM_SYSKEYDOWN);
        // Dispatch async to avoid stalling the hook chain
        auto cb = s_instance_->on_keyboard_;
        int vk = static_cast<int>(info->vkCode);
        std::thread([cb, vk, key_down]() { cb(vk, key_down); }).detach();
    }
    return CallNextHookEx(nullptr, code, wp, lp);
}
