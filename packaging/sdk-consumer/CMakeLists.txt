# Smoke test of the installed SharpCpp.Rbfx CMake SDK.
# The project uses only the files of the SDK and of the rbfx SDK.
# Set CMAKE_PREFIX_PATH to both install roots.
cmake_minimum_required(VERSION 3.28)
project(SdkConsumer LANGUAGES CXX)

# The rbfx SDK needs a shared library type.
if(NOT DEFINED BUILD_SHARED_LIBS)
  set(BUILD_SHARED_LIBS ON)
endif()

find_package(SharpCppRbfx CONFIG REQUIRED)

add_library(Greeter.Impl STATIC Greeter.cpp)
target_compile_features(Greeter.Impl PUBLIC cxx_std_20)
target_include_directories(Greeter.Impl PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(Greeter.Impl PUBLIC Urho3D SharpCpp::RbfxHost)
set_target_properties(Greeter.Impl PROPERTIES POSITION_INDEPENDENT_CODE ON)

rbfx_csharp_add_usercode(Greeter
  HEADERS Greeter.hpp
  INCLUDE_DIRECTORIES .
  LINK_LIBRARIES Greeter.Impl
  NAMESPACE Consumer)

add_executable(GreeterApp main.cpp)
target_link_libraries(GreeterApp PRIVATE Greeter.Impl)
