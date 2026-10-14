file(MAKE_DIRECTORY "${RBFX_GENERATED_ROOT}")
file(GLOB_RECURSE RBFX_GENERATED_CPP_FILES CONFIGURE_DEPENDS "${RBFX_GENERATED_ROOT}/*.g.cpp")
if(NOT RBFX_GENERATED_CPP_FILES)
  file(WRITE "${RBFX_GENERATED_ROOT}/.stub.cpp" "// Replaced by generated binding sources.\n")
  set(RBFX_GENERATED_CPP_FILES "${RBFX_GENERATED_ROOT}/.stub.cpp")
endif()

# Package mode: the engine natives come from the runtime package, so the engine wrapper is not compiled.
# The runtime target still waits for the generator, because the user-code generators read its output.
if(RBFX_CSHARP_BINDINGS_PACKAGE_VERSION)
  add_custom_target(sharpcpp-rbfx-runtime)
  add_dependencies(sharpcpp-rbfx-runtime sharpcpp-generate-rbfx)
  return()
endif()

set_source_files_properties(
  ${RBFX_GENERATED_CPP_FILES}
  PROPERTIES OBJECT_DEPENDS "${RBFX_GENERATED_STAMP}")
add_library(
  SharpCppRbfx_Native
  ${RBFX_NATIVE_LIBRARY_TYPE}
    ${RBFX_GENERATED_CPP_FILES}
    "${CMAKE_CURRENT_SOURCE_DIR}/Core/WorkQueueRuntime.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/Interop/ManagedAttributeAccessor.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/Interop/ManagedObjectFactory.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/Interop/ManagedTypeInfoRegistry.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/Interop/RefCountedRuntime.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/Interop/ScriptRuntime.cpp")
add_dependencies(SharpCppRbfx_Native sharpcpp-generate-rbfx)
target_compile_features(SharpCppRbfx_Native PRIVATE cxx_std_20)
if(MSVC)
  # A unity source of the generated wrappers exceeds the section limit of the default object format.
  target_compile_options(SharpCppRbfx_Native PRIVATE /bigobj)
endif()
set_target_properties(
  SharpCppRbfx_Native
  PROPERTIES
    CXX_SCAN_FOR_MODULES OFF
    POSITION_INDEPENDENT_CODE ON
    UNITY_BUILD ON
    UNITY_BUILD_BATCH_SIZE 100
    OUTPUT_NAME "SharpCpp.Rbfx.Native"
    BUILD_RPATH "${RBFX_RUNTIME_RPATH}"
    INSTALL_RPATH "${RBFX_RUNTIME_RPATH}")
target_include_directories(
  SharpCppRbfx_Native
  PRIVATE
    "${RBFX_GENERATED_ROOT}"
    "${CMAKE_CURRENT_SOURCE_DIR}/../include")
target_link_libraries(
  SharpCppRbfx_Native
  PRIVATE
    SharpCpp::NativeSupport
    "${RBFX_URHO3D_TARGET}")
target_compile_options(
  SharpCppRbfx_Native
  PRIVATE $<$<COMPILE_LANG_AND_ID:CXX,GNU,Clang,AppleClang>:-Wno-attributes>)
sharpcpp_set_native_output(SharpCppRbfx_Native)

set(RBFX_RUNTIME_COPY_COMMAND
  "${CMAKE_COMMAND}"
  -Dtarget_file=$<SHELL_PATH:$<TARGET_FILE:SharpCppRbfx_Native>>
  -Doutput_dir=$<SHELL_PATH:$<TARGET_FILE_DIR:SharpCppRbfx_Native>>
  -Durho_dir=$<SHELL_PATH:$<TARGET_FILE_DIR:${RBFX_URHO3D_TARGET}>>
  -Durho_library_file=$<SHELL_PATH:$<TARGET_FILE:${RBFX_URHO3D_TARGET}>>
  -Dconfig=$<CONFIG>
  -P "$<SHELL_PATH:${RBFX_RUNTIME_DEPENDENCIES_SCRIPT}>")

if(RBFX_NATIVE_LIBRARY_TYPE STREQUAL "SHARED" AND NOT CMAKE_CROSSCOMPILING)
  add_custom_command(
    TARGET SharpCppRbfx_Native
    POST_BUILD
    COMMAND ${RBFX_RUNTIME_COPY_COMMAND}
    VERBATIM)
  add_custom_target(
    sharpcpp-rbfx-runtime
    COMMAND ${RBFX_RUNTIME_COPY_COMMAND}
    DEPENDS SharpCppRbfx_Native
    VERBATIM)
else()
  # Cross-target dependencies are staged from the SDK instead of the host loader.
  add_custom_target(sharpcpp-rbfx-runtime DEPENDS SharpCppRbfx_Native)
endif()
