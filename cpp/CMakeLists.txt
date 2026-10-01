cmake_minimum_required(VERSION 3.25)
project(auto_kite_cpp LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(nlohmann_json CONFIG REQUIRED)

# Executable
add_executable(auto_kite
    src/main.cpp
    src/core/timing_jitter.cpp
    src/core/settings.cpp
    src/input/mouse_helper.cpp
    src/input/input_simulator.cpp
    src/detection/color_matcher.cpp
    src/detection/cluster_finder.cpp
)
target_include_directories(auto_kite PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src)
target_compile_options(auto_kite PRIVATE /W4 /EHsc)
target_link_libraries(auto_kite PRIVATE
    d3d11 dxgi d2d1 dwrite dwmapi
    user32 winmm
    nlohmann_json::nlohmann_json
)

# Tests
enable_testing()
add_subdirectory(tests)
