# Native implementation of the user types. The bindings target links it.
# A shared bindings library links a static library. A static bindings library (iOS, wasm) takes the
# object files of the implementation, so the one archive of the bindings target carries all the code.
if(RBFX_NATIVE_LIBRARY_TYPE STREQUAL "STATIC")
  set(EXAMPLE_USERCODE_IMPL_TYPE OBJECT)
else()
  set(EXAMPLE_USERCODE_IMPL_TYPE STATIC)
endif()
add_library(Example.UserCode.Impl ${EXAMPLE_USERCODE_IMPL_TYPE} RotatingComponent.cpp TextTools.cpp)
target_compile_features(Example.UserCode.Impl PRIVATE cxx_std_20)
target_include_directories(Example.UserCode.Impl PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(Example.UserCode.Impl PUBLIC "${RBFX_URHO3D_TARGET}")
set_target_properties(
  Example.UserCode.Impl
  PROPERTIES
    CXX_SCAN_FOR_MODULES OFF
    POSITION_INDEPENDENT_CODE ON)

# Bindings target. It holds the generated wrappers and links the implementation.
rbfx_csharp_add_wrapper(Example.UserCode
  HEADERS RotatingComponent.hpp TextTools.hpp
  INCLUDE_DIRECTORIES .
  IMPLEMENTATION Example.UserCode.Impl
  NAMESPACE RbfxExamples
  ASSEMBLY_NAME Example.UserCode.Bindings
  LIBRARY_NAME Example.UserCode.Native
  SYMBOL_PREFIX rbfxex
  GENERATED_DIRECTORY rbfx-example-usercode)
