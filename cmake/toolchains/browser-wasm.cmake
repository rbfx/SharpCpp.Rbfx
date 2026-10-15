# CMake toolchain file for browser-wasm. Needs the environment variable EMSDK (activated Emscripten SDK).
if(NOT DEFINED ENV{EMSDK} OR "$ENV{EMSDK}" STREQUAL "")
  message(FATAL_ERROR "Set EMSDK to the activated Emscripten SDK directory.")
endif()

set(CMAKE_C_FLAGS "-fwasm-exceptions" CACHE STRING "")
set(CMAKE_CXX_FLAGS "-fwasm-exceptions" CACHE STRING "")
set(RBFX_SDK_PARSE_EXTRA_OPTIONS "-fwasm-exceptions" CACHE STRING "")

# Emscripten has no clang-scan-deps. Ninja must not scan C++ sources for modules.
set(CMAKE_CXX_SCAN_FOR_MODULES OFF CACHE BOOL "")

include("$ENV{EMSDK}/upstream/emscripten/cmake/Modules/Platform/Emscripten.cmake")
