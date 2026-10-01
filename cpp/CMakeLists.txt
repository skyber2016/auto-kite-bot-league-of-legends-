cmake_minimum_required(VERSION 3.25)
project(auto_kite_cpp LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Executable
add_executable(auto_kite src/main.cpp)
target_include_directories(auto_kite PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src)
target_compile_options(auto_kite PRIVATE /W4 /EHsc)
target_link_libraries(auto_kite PRIVATE
    d3d11 dxgi d2d1 dwrite dwmapi
    user32 winmm
)

# Tests
enable_testing()
add_subdirectory(tests)
