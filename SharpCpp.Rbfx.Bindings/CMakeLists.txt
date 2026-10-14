# Package mode: the engine natives come from the runtime package, so the engine wrapper is neither generated nor compiled.
# The user-code generators read the engine metadata from the package.
if(RBFX_CSHARP_BINDINGS_PACKAGE_VERSION)
  add_custom_target(sharpcpp-rbfx-runtime)
  return()
endif()

file(MAKE_DIRECTORY "${RBFX_GENERATED_ROOT}")
# The generator writes the list of its sources. CMake re-runs when the list changes (see the generator directory).
set(RBFX_GENERATED_CPP_FILES)
if(EXISTS "${RBFX_GENERATED_SOURCE_LIST}")
  file(STRINGS "${RBFX_GENERATED_SOURCE_LIST}" _generated_source_names)
  foreach(_generated_source_name IN LISTS _generated_source_names)
    list(APPEND RBFX_GENERATED_CPP_FILES "${RBFX_GENERATED_ROOT}/${_generated_source_name}")
  endforeach()
endif()
if(NOT RBFX_GENERATED_CPP_FILES)
  file(WRITE "${RBFX_GENERATED_ROOT}/.stub.cpp" "// Replaced by generated binding sources.\n")
  set(RBFX_GENERATED_CPP_FILES "${RBFX_GENERATED_ROOT}/.stub.cpp")
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
    # Only the install-style rpath: the run-time libraries are copied next to the wrapper, so the
    # file has no rpath entry with the build directory.
    BUILD_WITH_INSTALL_RPATH ON
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

# Install rules of the SDK. They run only for "cmake --install" and do not change the build.
# The wrapper joins the export set of the host library, so SharpCppRbfxConfig.cmake defines
# SharpCpp::RbfxNative. A static wrapper is not installed, because its private link
# dependencies are not in the export set.
if(RBFX_INSTALL_SDK AND RBFX_NATIVE_LIBRARY_TYPE STREQUAL "SHARED")
  include(GNUInstallDirs)
  set(_rbfx_sdk_metadata_dir "${CMAKE_INSTALL_DATADIR}/SharpCppRbfx/metadata")
  set_target_properties(SharpCppRbfx_Native PROPERTIES EXPORT_NAME RbfxNative)
  install(
    TARGETS SharpCppRbfx_Native
    EXPORT SharpCppRbfxTargets
    RUNTIME DESTINATION "${CMAKE_INSTALL_BINDIR}"
    LIBRARY DESTINATION "${CMAKE_INSTALL_LIBDIR}"
    ARCHIVE DESTINATION "${CMAKE_INSTALL_LIBDIR}")

  # The engine library and its runtime files go next to the wrapper. The "$ORIGIN" or
  # "@loader_path" rpath of the wrapper finds them there, and Windows finds them in the DLL directory.
  # The install step runs the script that the build uses, with the install directory as destination.
  if(WIN32)
    set(_rbfx_sdk_runtime_dir "${CMAKE_INSTALL_BINDIR}")
  else()
    set(_rbfx_sdk_runtime_dir "${CMAKE_INSTALL_LIBDIR}")
  endif()
  if(NOT CMAKE_CROSSCOMPILING)
    install(CODE "
      if(IS_ABSOLUTE \"${_rbfx_sdk_runtime_dir}\")
        set(_rbfx_destination_dir \"\$ENV{DESTDIR}${_rbfx_sdk_runtime_dir}\")
      else()
        set(_rbfx_destination_dir \"\$ENV{DESTDIR}\${CMAKE_INSTALL_PREFIX}/${_rbfx_sdk_runtime_dir}\")
      endif()
      execute_process(
        COMMAND \"${CMAKE_COMMAND}\"
          \"-Dtarget_file=$<TARGET_FILE:SharpCppRbfx_Native>\"
          \"-Doutput_dir=$<TARGET_FILE_DIR:SharpCppRbfx_Native>\"
          \"-Ddestination_dir=\${_rbfx_destination_dir}\"
          \"-Durho_dir=$<TARGET_FILE_DIR:${RBFX_URHO3D_TARGET}>\"
          \"-Durho_library_file=$<TARGET_FILE:${RBFX_URHO3D_TARGET}>\"
          \"-Dconfig=$<CONFIG>\"
          -P \"${RBFX_RUNTIME_DEPENDENCIES_SCRIPT}\"
        RESULT_VARIABLE _rbfx_runtime_result)
      if(NOT _rbfx_runtime_result EQUAL 0)
        message(FATAL_ERROR \"Cannot install the runtime files of the wrapper (result: \${_rbfx_runtime_result}).\")
      endif()")
  endif()

  # The generator writes these files during the build. Stop with a clear message when they are missing.
  install(CODE "
    foreach(_rbfx_generated_file IN ITEMS
        \"${RBFX_METADATA_FILE}\"
        \"${RBFX_ENGINE_UMBRELLA_DIRECTORY}/SharpCpp.Rbfx.ParseUmbrella.hpp\")
      if(NOT EXISTS \"\${_rbfx_generated_file}\")
        message(FATAL_ERROR \"The generated file is missing: \${_rbfx_generated_file}. Build the project before the install step.\")
      endif()
    endforeach()")
  install(
    FILES
      "${RBFX_METADATA_FILE}"
      "${RBFX_ENGINE_UMBRELLA_DIRECTORY}/SharpCpp.Rbfx.ParseUmbrella.hpp"
    DESTINATION "${_rbfx_sdk_metadata_dir}")
endif()
