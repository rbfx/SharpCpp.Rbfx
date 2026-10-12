# CMake toolchain file for ios-arm64. Needs macOS and Xcode.
# The SDK path comes from the environment variable IOS_SDK_PATH or from xcrun.
set(CMAKE_SYSTEM_NAME iOS)
set(CMAKE_OSX_ARCHITECTURES arm64)

if(DEFINED ENV{IOS_SDK_PATH} AND NOT "$ENV{IOS_SDK_PATH}" STREQUAL "")
  set(_sharpcpp_ios_sdk "$ENV{IOS_SDK_PATH}")
else()
  execute_process(
    COMMAND xcrun --sdk iphoneos --show-sdk-path
    OUTPUT_VARIABLE _sharpcpp_ios_sdk
    OUTPUT_STRIP_TRAILING_WHITESPACE
    RESULT_VARIABLE _sharpcpp_xcrun_result)
  if(NOT _sharpcpp_xcrun_result EQUAL 0 OR "${_sharpcpp_ios_sdk}" STREQUAL "")
    message(FATAL_ERROR "Set IOS_SDK_PATH or install Xcode (xcrun --sdk iphoneos --show-sdk-path failed).")
  endif()
endif()

set(CMAKE_OSX_SYSROOT "${_sharpcpp_ios_sdk}" CACHE PATH "")
set(CMAKE_OSX_DEPLOYMENT_TARGET 15.0 CACHE STRING "")
set(RBFX_SDK_PARSE_USE_PLATFORM_COMPILER_INCLUDES ON CACHE BOOL "")
