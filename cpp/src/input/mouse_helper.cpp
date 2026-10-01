#include "input/mouse_helper.h"

POINT MouseHelper::get_cursor_position() {
    POINT pt{};
    GetCursorPos(&pt);
    return pt;
}
