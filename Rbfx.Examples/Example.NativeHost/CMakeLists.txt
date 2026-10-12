rbfx_csharp_add_usercode(Rbfx.NativeHost.Wrapped
  HEADERS include/RbfxNativeHost/Spinner.hpp
  SOURCES src/Spinner.cpp
  INCLUDE_DIRECTORIES include
  NAMESPACE RbfxNativeHost
  ASSEMBLY_NAME Rbfx.NativeHost.Bindings
  LIBRARY_NAME SharpCpp.Rbfx.NativeHost.Native
  SYMBOL_PREFIX scrbfxnh
  GENERATED_DIRECTORY rbfx-nativehost)

add_executable(Rbfx.NativeHost src/main.cpp)
target_compile_features(Rbfx.NativeHost PRIVATE cxx_std_20)
target_link_libraries(Rbfx.NativeHost PRIVATE SharpCpp::RbfxHost)
if(CMAKE_SYSTEM_NAME STREQUAL "Linux" AND RBFX_NATIVE_LIBRARY_TYPE STREQUAL "SHARED")
  # Urho3D must be a startup dependency of the executable. The runtime loads the libraries of the
  # bindings later with dlopen, and then the TLS block of Urho3D does not fit into the static TLS.
  target_link_libraries(Rbfx.NativeHost PRIVATE Urho3D)
  target_link_options(Rbfx.NativeHost PRIVATE LINKER:--no-as-needed)
endif()
set_target_properties(
  Rbfx.NativeHost
  PROPERTIES
    CXX_SCAN_FOR_MODULES OFF
    BUILD_RPATH "${RBFX_RUNTIME_RPATH}"
    INSTALL_RPATH "${RBFX_RUNTIME_RPATH}")
sharpcpp_set_native_output(Rbfx.NativeHost)
