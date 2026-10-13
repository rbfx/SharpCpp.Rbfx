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
# The C++ runtime follows a -DBUILD_SHARED_LIBS cache value. Shared is the default for Android.
if(DEFINED BUILD_SHARED_LIBS AND NOT BUILD_SHARED_LIBS)
  set(ANDROID_STL c++_static)
else()
  set(ANDROID_STL c++_shared)
endif()
set(ANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES ON)

include("${_sharpcpp_ndk}/build/cmake/android.toolchain.cmake")
