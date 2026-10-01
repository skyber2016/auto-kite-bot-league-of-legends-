find_package(GTest CONFIG REQUIRED)

add_executable(auto_kite_tests
    test_placeholder.cpp
    test_timing_jitter.cpp
    ${CMAKE_SOURCE_DIR}/src/core/timing_jitter.cpp
)
target_link_libraries(auto_kite_tests PRIVATE GTest::gtest GTest::gtest_main)
target_include_directories(auto_kite_tests PRIVATE ${CMAKE_SOURCE_DIR}/src)
target_compile_options(auto_kite_tests PRIVATE /W4 /EHsc)

include(GoogleTest)
gtest_discover_tests(auto_kite_tests)
