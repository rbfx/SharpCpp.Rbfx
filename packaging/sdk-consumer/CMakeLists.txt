# Smoke test of the installed SharpCpp.Rbfx CMake SDK.
# The project uses only the files of the SDK and of the rbfx SDK.
# Set CMAKE_PREFIX_PATH to both install roots.
cmake_minimum_required(VERSION 3.28)
project(SdkConsumer LANGUAGES CXX)

# The rbfx SDK of Android and of the desktop has shared libraries. The rbfx SDK of iOS and of
# Emscripten has static libraries.
if(NOT DEFINED BUILD_SHARED_LIBS)
  if(CMAKE_SYSTEM_NAME STREQUAL "iOS" OR CMAKE_SYSTEM_NAME STREQUAL "Emscripten" OR EMSCRIPTEN)
    set(BUILD_SHARED_LIBS OFF)
  else()
    set(BUILD_SHARED_LIBS ON)
  endif()
endif()

# The Android toolchain limits find_package to the target sysroot. The two SDKs are in CMAKE_PREFIX_PATH.
set(CMAKE_FIND_ROOT_PATH_MODE_PACKAGE BOTH)
find_package(SharpCppRbfx CONFIG REQUIRED)

add_library(Greeter.Impl STATIC Greeter.cpp)
target_compile_features(Greeter.Impl PUBLIC cxx_std_20)
target_include_directories(Greeter.Impl PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(Greeter.Impl PUBLIC Urho3D SharpCpp::RbfxHost)
set_target_properties(Greeter.Impl PROPERTIES POSITION_INDEPENDENT_CODE ON)

rbfx_csharp_add_wrapper(Greeter
  HEADERS Greeter.hpp
  INCLUDE_DIRECTORIES .
  IMPLEMENTATION Greeter.Impl
  NAMESPACE Consumer)

# A cross build checks the libraries only. The application needs a target platform entry point.
if(NOT CMAKE_CROSSCOMPILING)
  add_executable(GreeterApp main.cpp)
  target_link_libraries(GreeterApp PRIVATE Greeter.Impl)
endif()
