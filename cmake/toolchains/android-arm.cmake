# CMake toolchain file for android-arm. Needs the environment variable ANDROID_NDK_ROOT (or ANDROID_NDK_HOME).
if(DEFINED ENV{ANDROID_NDK_ROOT})
  set(_sharpcpp_ndk "$ENV{ANDROID_NDK_ROOT}")
elseif(DEFINED ENV{ANDROID_NDK_HOME})
  set(_sharpcpp_ndk "$ENV{ANDROID_NDK_HOME}")
else()
  message(FATAL_ERROR "Set ANDROID_NDK_ROOT to the Android NDK directory.")
endif()

set(ANDROID_ABI armeabi-v7a)
set(ANDROID_PLATFORM android-21)
set(ANDROID_STL c++_static)
set(ANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES ON)

include("${_sharpcpp_ndk}/build/cmake/android.toolchain.cmake")
