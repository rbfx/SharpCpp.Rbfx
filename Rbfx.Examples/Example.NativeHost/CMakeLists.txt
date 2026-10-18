add_executable(Rbfx.NativeHost main.cpp)
target_compile_features(Rbfx.NativeHost PRIVATE cxx_std_20)
target_link_libraries(Rbfx.NativeHost PRIVATE Urho3D SharpCpp::RbfxHost)
if(WIN32)
  # The sample is a console program with command line options, so the engine macro must define main.
  target_compile_definitions(Rbfx.NativeHost PRIVATE URHO3D_WIN32_CONSOLE)
endif()
if(CMAKE_SYSTEM_NAME STREQUAL "Linux" AND RBFX_NATIVE_LIBRARY_TYPE STREQUAL "SHARED")
  # Urho3D must be a startup dependency of the executable. The runtime loads the libraries of the
  # bindings later with dlopen, and then the TLS block of Urho3D does not fit into the static TLS.
  target_link_options(Rbfx.NativeHost PRIVATE LINKER:--no-as-needed)
endif()
set_target_properties(
  Rbfx.NativeHost
  PROPERTIES
    CXX_SCAN_FOR_MODULES OFF
    BUILD_RPATH "${RBFX_RUNTIME_RPATH}"
    INSTALL_RPATH "${RBFX_RUNTIME_RPATH}")
sharpcpp_set_native_output(Rbfx.NativeHost)

# The managed plugin is the C# project next to the executable. CMake builds it with dotnet.
rbfx_csharp_add_plugin_build(
  Rbfx.NativeHost.Net
  PROJECT "${CMAKE_CURRENT_SOURCE_DIR}/Rbfx.NativeHost.Net/Rbfx.NativeHost.Net.csproj"
  HOST Rbfx.NativeHost)
