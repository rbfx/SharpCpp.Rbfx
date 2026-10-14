cmake_minimum_required(VERSION 3.28)

foreach(_required
    RBFX_SOURCE_DIR
    RBFX_NATIVE_PRESET
    RBFX_NATIVE_BUILD_DIR
    RBFX_CONFIGURATION
    RBFX_NATIVE_LOCK_FILE
    SHARPCPP_ROOT
    RBFX_SDK_ROOT)
  if(NOT DEFINED ${_required} OR "${${_required}}" STREQUAL "")
    message(FATAL_ERROR "${_required} is required")
  endif()
endforeach()

# Package mode passes the engine metadata file of the runtime package. Source mode leaves it empty, which
# also clears a value that a package build left in the same tree.
if(NOT DEFINED RBFX_ENGINE_METADATA_FILE)
  set(RBFX_ENGINE_METADATA_FILE "")
endif()

get_filename_component(_lock_directory "${RBFX_NATIVE_LOCK_FILE}" DIRECTORY)
file(MAKE_DIRECTORY "${_lock_directory}")
file(LOCK "${RBFX_NATIVE_LOCK_FILE}" GUARD PROCESS TIMEOUT 900)

set(_configure_signature_input
  "${CMAKE_COMMAND}
${RBFX_NATIVE_PRESET}
${RBFX_SOURCE_DIR}
${RBFX_NATIVE_BUILD_DIR}
${SHARPCPP_ROOT}
${RBFX_SDK_ROOT}
${SHARPCPP_MANAGED_OUTPUT_ROOT}
${SHARPCPP_NATIVE_OUTPUT_ROOT}
${RBFX_NATIVE_CONFIGURE_ARGUMENTS}
${RBFX_ENGINE_METADATA_FILE}
RBFX_CSHARP_BUILD_MANAGED=OFF")
string(SHA256 _configure_signature "${_configure_signature_input}")
set(_configure_signature_file "${RBFX_NATIVE_BUILD_DIR}/sharpcpp-rbfx-configure.signature")
set(_configure_native FALSE)
if(NOT EXISTS "${RBFX_NATIVE_BUILD_DIR}/CMakeCache.txt"
    OR NOT EXISTS "${_configure_signature_file}")
  set(_configure_native TRUE)
else()
  file(READ "${_configure_signature_file}" _cached_configure_signature)
  string(STRIP "${_cached_configure_signature}" _cached_configure_signature)
  if(NOT _cached_configure_signature STREQUAL _configure_signature)
    set(_configure_native TRUE)
  endif()
  # A direct CMake configure of the same tree can turn the managed targets on. MSBuild is the driver here.
  file(STRINGS "${RBFX_NATIVE_BUILD_DIR}/CMakeCache.txt" _cached_build_managed
    REGEX "^RBFX_CSHARP_BUILD_MANAGED:[A-Z]+=")
  if(_cached_build_managed)
    string(REGEX REPLACE "^[^=]*=" "" _cached_build_managed "${_cached_build_managed}")
    if(_cached_build_managed)
      set(_configure_native TRUE)
    endif()
  endif()
endif()

if(_configure_native)
  set(_configure_command
    "${CMAKE_COMMAND}"
    --preset "${RBFX_NATIVE_PRESET}"
    -U "MAKECOMMAND"
    -U "Urho3D_DIR"
    -U "Urho3DThirdParty_DIR"
    -U "Urho3DTools_DIR"
    -S "${RBFX_SOURCE_DIR}"
    -B "${RBFX_NATIVE_BUILD_DIR}"
    "-DSHARPCPP_ROOT=${SHARPCPP_ROOT}"
    "-DRBFX_SDK_ROOT=${RBFX_SDK_ROOT}"
    "-DSHARPCPP_MANAGED_OUTPUT_ROOT=${SHARPCPP_MANAGED_OUTPUT_ROOT}"
    "-DSHARPCPP_NATIVE_OUTPUT_ROOT=${SHARPCPP_NATIVE_OUTPUT_ROOT}"
    "-DRBFX_ENGINE_METADATA_FILE=${RBFX_ENGINE_METADATA_FILE}"
    # MSBuild builds the managed projects, so the nested CMake build must not run dotnet.
    "-DRBFX_CSHARP_BUILD_MANAGED=OFF")
  if(NOT "${RBFX_NATIVE_CONFIGURE_ARGUMENTS}" STREQUAL "")
    separate_arguments(_extra_configure_arguments NATIVE_COMMAND "${RBFX_NATIVE_CONFIGURE_ARGUMENTS}")
    list(APPEND _configure_command ${_extra_configure_arguments})
  endif()

  file(REMOVE "${_configure_signature_file}")
  execute_process(
    COMMAND ${_configure_command}
    WORKING_DIRECTORY "${RBFX_SOURCE_DIR}"
    COMMAND_ERROR_IS_FATAL ANY)
  file(WRITE "${_configure_signature_file}" "${_configure_signature}\n")
endif()

# Generate first: the binding libraries glob generated sources at configure time, and
# only a separate build invocation re-checks those globs after generation. Building the
# native target directly on a clean tree would compile only the placeholder source.
execute_process(
  COMMAND
    "${CMAKE_COMMAND}" --build "${RBFX_NATIVE_BUILD_DIR}"
    --config "${RBFX_CONFIGURATION}"
    --target sharpcpp-rbfx-generate
  WORKING_DIRECTORY "${RBFX_SOURCE_DIR}"
  COMMAND_ERROR_IS_FATAL ANY)

execute_process(
  COMMAND
    "${CMAKE_COMMAND}" --build "${RBFX_NATIVE_BUILD_DIR}"
    --config "${RBFX_CONFIGURATION}"
    --target sharpcpp-rbfx-native
    --parallel
  WORKING_DIRECTORY "${RBFX_SOURCE_DIR}"
  COMMAND_ERROR_IS_FATAL ANY)
