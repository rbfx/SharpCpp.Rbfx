# CMake toolchain file for android-arm64. Needs the environment variable ANDROID_NDK_ROOT (or ANDROID_NDK_HOME).
if(DEFINED ENV{ANDROID_NDK_ROOT})
  set(_sharpcpp_ndk "$ENV{ANDROID_NDK_ROOT}")
elseif(DEFINED ENV{ANDROID_NDK_HOME})
  set(_sharpcpp_ndk "$ENV{ANDROID_NDK_HOME}")
else()
  message(FATAL_ERROR "Set ANDROID_NDK_ROOT to the Android NDK directory.")
endif()

set(ANDROID_ABI arm64-v8a)
set(ANDROID_PLATFORM android-21)
# RBFX_ANDROID_STL (c++_shared or c++_static) overrides the C++ runtime. Without it the runtime
# follows a -DBUILD_SHARED_LIBS cache value. Shared is the default for Android.
if(RBFX_ANDROID_STL)
  set(ANDROID_STL "${RBFX_ANDROID_STL}")
elseif(DEFINED BUILD_SHARED_LIBS AND NOT BUILD_SHARED_LIBS)
  set(ANDROID_STL c++_static)
else()
  set(ANDROID_STL c++_shared)
endif()
list(APPEND CMAKE_TRY_COMPILE_PLATFORM_VARIABLES RBFX_ANDROID_STL BUILD_SHARED_LIBS)
set(ANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES ON)

# The NDK files ask for CMake 3.6. CMake reports that this compatibility will be removed. The NDK files run
# at several steps (toolchain file, project() and every try_compile), so the minimum policy version stays set.
# The CMake files of this repository ask for CMake 3.28 or newer, so no message of them is hidden.
if(NOT DEFINED CMAKE_POLICY_VERSION_MINIMUM)
  set(CMAKE_POLICY_VERSION_MINIMUM 3.10)
endif()
include("${_sharpcpp_ndk}/build/cmake/android.toolchain.cmake")
