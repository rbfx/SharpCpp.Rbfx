# Example: a desktop NativeAOT application in shim mode. The native launcher starts the engine and loads the NativeAOT
# head ExampleDesktopAot.Head. The head loads the C# plugin DesktopAot, which creates a scene with a rotating component.
# See ../README.md. Run the executable with --headless to stop after three updates.
#
# Configure with (from this folder, the presets include the presets of the repository):
#   cmake --preset linux-gcc-x64-dll -B <build dir> -DCMAKE_PREFIX_PATH="<rbfx SDK>;<SharpCpp.Rbfx>/cmake/SharpCppRbfx" \
#     -DRBFX_HOST_MODE=shim -DRBFX_BUILD_HOST=ON
cmake_minimum_required(VERSION 3.28)
project(ExampleDesktopAot LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib)
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin/$<CONFIG>)
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin/$<CONFIG>)

if(NOT RBFX_HOST_MODE STREQUAL "shim")
  message(FATAL_ERROR "Example.DesktopAot needs -DRBFX_HOST_MODE=shim.")
endif()

# The desktop rbfx SDK has shared libraries.
if(NOT DEFINED BUILD_SHARED_LIBS)
  set(BUILD_SHARED_LIBS ON)
endif()
find_package(Urho3D REQUIRED)
find_package(SharpCppRbfx CONFIG REQUIRED)

# The engine reads ResourceRoot.ini from the first folder above the executable that has it (build/bin/<configuration> -> build).
rbfx_csharp_find_core_data(CORE_DATA_DIR)
if(CORE_DATA_DIR)
  file(RELATIVE_PATH CORE_DATA_RELATIVE "${CMAKE_BINARY_DIR}" "${CORE_DATA_DIR}")
  file(WRITE "${CMAKE_BINARY_DIR}/ResourceRoot.ini" "CoreData=${CORE_DATA_RELATIVE}\n")
endif()

rbfx_csharp_add_plugin(DesktopAot CSPROJ CSharp/DesktopAot.csproj)
rbfx_csharp_add_launcher(ExampleDesktopAot CONSOLE)
rbfx_csharp_package_application(ExampleDesktopAot PLUGIN DesktopAot)

rbfx_csharp_install_application(ExampleDesktopAot)
