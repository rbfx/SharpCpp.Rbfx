# Smoke test of the NativeAOT desktop head (shim mode). eng/run-aot-test.sh configures, builds and runs it.
# The native launcher starts the engine headless, loads the head AotApp.Head, and the C# plugin AotApp in the
# head stops the engine after three updates.
#
# Configure with:
#   -DCMAKE_PREFIX_PATH="<rbfx SDK>;<SharpCpp.Rbfx>/cmake/SharpCppRbfx" -DRBFX_HOST_MODE=shim -DRBFX_BUILD_HOST=ON
cmake_minimum_required(VERSION 3.28)
project(SharpCppRbfxTestsAot LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib)
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

if(NOT RBFX_HOST_MODE STREQUAL "shim")
  message(FATAL_ERROR "SharpCpp.Rbfx.Tests.Aot needs -DRBFX_HOST_MODE=shim.")
endif()

# The desktop rbfx SDK has shared libraries.
if(NOT DEFINED BUILD_SHARED_LIBS)
  set(BUILD_SHARED_LIBS ON)
endif()
find_package(Urho3D REQUIRED)
find_package(SharpCppRbfx CONFIG REQUIRED)

# The engine reads ResourceRoot.ini from the first folder above the executable that has it (build/bin -> build).
rbfx_csharp_find_core_data(CORE_DATA_DIR)
if(CORE_DATA_DIR)
  file(RELATIVE_PATH CORE_DATA_RELATIVE "${CMAKE_BINARY_DIR}" "${CORE_DATA_DIR}")
  file(WRITE "${CMAKE_BINARY_DIR}/ResourceRoot.ini" "CoreData=${CORE_DATA_RELATIVE}\n")
endif()

rbfx_csharp_add_plugin(AotApp CSPROJ CSharp/AotApp.csproj)
rbfx_csharp_add_launcher(AotTest CONSOLE)
# Head/HeadHooks.cs implements the hooks of the head and has a managed component of the head assembly.
rbfx_csharp_package_application(AotTest PLUGIN AotApp HEAD_SOURCES Head/HeadHooks.cs)
