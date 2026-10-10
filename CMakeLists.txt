cmake_minimum_required(VERSION 3.28)

project(SharpCppRbfx LANGUAGES CXX)

get_filename_component(_sharpcpp_default_root "${CMAKE_CURRENT_SOURCE_DIR}/../SharpCpp" ABSOLUTE)
set(SHARPCPP_ROOT "${_sharpcpp_default_root}" CACHE PATH "Path to the SharpCpp checkout used to generate the bindings.")
if(NOT EXISTS "${SHARPCPP_ROOT}/include/SharpCpp/Marshaller.hpp")
  message(FATAL_ERROR "SharpCpp was not found at '${SHARPCPP_ROOT}'. Set SHARPCPP_ROOT to a SharpCpp checkout.")
endif()

set(
  SHARPCPP_NATIVE_OUTPUT_ROOT
  "${CMAKE_CURRENT_SOURCE_DIR}/build/bin"
  CACHE PATH
  "Root directory for native artifacts.")
set(
  SHARPCPP_MANAGED_OUTPUT_ROOT
  "${CMAKE_CURRENT_SOURCE_DIR}/build/bin"
  CACHE PATH
  "Root directory containing the prebuilt managed generators.")
set(
  SHARPCPP_GENERATED_ROOT
  "${CMAKE_BINARY_DIR}/generated"
  CACHE PATH
  "Root directory for generated binding sources.")

# SharpCpp provides the SharpCpp::NativeSupport header target; its own E2E graph stays disabled.
set(SHARPCPP_BUILD_E2E OFF CACHE BOOL "" FORCE)
add_subdirectory("${SHARPCPP_ROOT}" sharpcpp EXCLUDE_FROM_ALL)

list(APPEND CMAKE_MODULE_PATH "${SHARPCPP_ROOT}/cmake")
include(SharpCppOutput)
include(SharpCppClang)
find_program(DOTNET_EXECUTABLE dotnet REQUIRED)

get_filename_component(_rbfx_default_sdk_root "${CMAKE_CURRENT_SOURCE_DIR}/../../rbfx-sdk" ABSOLUTE)
if(DEFINED ENV{RBFX_SDK_ROOT} AND EXISTS "$ENV{RBFX_SDK_ROOT}")
  set(_rbfx_default_sdk_root "$ENV{RBFX_SDK_ROOT}")
endif()

set(RBFX_SDK_ROOT "${_rbfx_default_sdk_root}" CACHE PATH "Path to the installed rbfx SDK root.")
set(
  RBFX_SDK_PARSE_LANGUAGE_STANDARD
  "gnu++20"
  CACHE STRING
  "C++ standard used while parsing the installed rbfx SDK.")

if(NOT EXISTS "${RBFX_SDK_ROOT}/share/Urho3D/CMake/Urho3DConfig.cmake")
  message(FATAL_ERROR "Set RBFX_SDK_ROOT to an installed rbfx SDK before configuring SharpCpp.Rbfx.")
endif()
if(NOT EXISTS "${RBFX_SDK_ROOT}/include/Urho3D")
  message(FATAL_ERROR "rbfx SDK headers were not found at '${RBFX_SDK_ROOT}/include/Urho3D'.")
endif()

function(sharpcpp_find_rbfx_sdk)
  set(BUILD_SHARED_LIBS ON)
  set(CMAKE_DISABLE_FIND_PACKAGE_TBB TRUE)
  set(CMAKE_DISABLE_FIND_PACKAGE_Vulkan TRUE)
  # Urho3DConfig.cmake finds the Urho3DThirdParty package of the same SDK with the default search.
  list(PREPEND CMAKE_PREFIX_PATH "${RBFX_SDK_ROOT}")
  find_package(
    Urho3D 0.0.0 CONFIG REQUIRED
    PATHS
      "${RBFX_SDK_ROOT}"
      "${RBFX_SDK_ROOT}/share/Urho3D/CMake"
    NO_DEFAULT_PATH)
endfunction()

sharpcpp_find_rbfx_sdk()
set(RBFX_URHO3D_TARGET Urho3D)

function(sharpcpp_map_imported_rbfx_configurations target_name)
  get_target_property(is_imported "${target_name}" IMPORTED)
  if(is_imported)
    set_target_properties(
      "${target_name}"
      PROPERTIES
        MAP_IMPORTED_CONFIG_RELEASE RelWithDebInfo
        MAP_IMPORTED_CONFIG_MINSIZEREL RelWithDebInfo)
  endif()

  get_target_property(link_libraries "${target_name}" INTERFACE_LINK_LIBRARIES)
  foreach(link_library IN LISTS link_libraries)
    if(TARGET "${link_library}")
      get_property(already_mapped GLOBAL PROPERTY "SHARPCPP_RBFX_MAPPED_${link_library}")
      if(NOT already_mapped)
        set_property(GLOBAL PROPERTY "SHARPCPP_RBFX_MAPPED_${link_library}" TRUE)
        sharpcpp_map_imported_rbfx_configurations("${link_library}")
      endif()
    endif()
  endforeach()
endfunction()

sharpcpp_map_imported_rbfx_configurations("${RBFX_URHO3D_TARGET}")

macro(sharpcpp_collect_rbfx_target_property output target_name property_name)
  get_target_property(_property_values "${target_name}" "${property_name}")
  if(_property_values)
    list(APPEND "${output}" ${_property_values})
  endif()

  get_target_property(_link_values "${target_name}" INTERFACE_LINK_LIBRARIES)
  foreach(_link_value IN LISTS _link_values)
    if(TARGET "${_link_value}" AND NOT "${_link_value}" IN_LIST _sharpcpp_checked_rbfx_libraries)
      list(APPEND _sharpcpp_checked_rbfx_libraries "${_link_value}")
      sharpcpp_collect_rbfx_target_property("${output}" "${_link_value}" "${property_name}")
    endif()
  endforeach()
endmacro()

set(_sharpcpp_checked_rbfx_libraries)
sharpcpp_collect_rbfx_target_property(
  RBFX_URHO3D_INCLUDE_DIRECTORIES "${RBFX_URHO3D_TARGET}" INTERFACE_INCLUDE_DIRECTORIES)
set(_sharpcpp_checked_rbfx_libraries)
sharpcpp_collect_rbfx_target_property(
  RBFX_URHO3D_COMPILE_DEFINITIONS "${RBFX_URHO3D_TARGET}" INTERFACE_COMPILE_DEFINITIONS)
set(_sharpcpp_checked_rbfx_libraries)
sharpcpp_collect_rbfx_target_property(
  RBFX_URHO3D_COMPILE_OPTIONS "${RBFX_URHO3D_TARGET}" INTERFACE_COMPILE_OPTIONS)

list(REMOVE_DUPLICATES RBFX_URHO3D_INCLUDE_DIRECTORIES)
list(REMOVE_DUPLICATES RBFX_URHO3D_COMPILE_DEFINITIONS)
list(REMOVE_DUPLICATES RBFX_URHO3D_COMPILE_OPTIONS)

set(RBFX_GENERATED_ROOT "${SHARPCPP_GENERATED_ROOT}/rbfx")
set(RBFX_GENERATED_STAMP "${RBFX_GENERATED_ROOT}/generate.stamp")
set(RBFX_METADATA_FILE "${RBFX_GENERATED_ROOT}/SharpCpp.Rbfx.Bindings.json")
set(RBFX_SDK_FLAGS_FILE "${CMAKE_CURRENT_BINARY_DIR}/sdk-flags/$<CONFIG>/urho3d-sdk-flags.txt")
set(RBFX_GENERATOR_LOG_FILE "${RBFX_GENERATED_ROOT}/generator.log")
set(RBFX_RUNTIME_DEPENDENCIES_SCRIPT "${CMAKE_CURRENT_BINARY_DIR}/CopyRbfxRuntimeDependencies.cmake")
set(
  RBFX_TEST_RUNSETTINGS_FILE
  "${SHARPCPP_NATIVE_OUTPUT_ROOT}/$<CONFIG>/SharpCpp.Rbfx.Tests.runsettings")
set(
  SHARPCPP_RBFX_GENERATOR_DLL
  "${SHARPCPP_MANAGED_OUTPUT_ROOT}/$<CONFIG>/SharpCpp.Rbfx.Generator.dll")
set(
  SHARPCPP_RBFX_USERCODE_GENERATOR_DLL
  "${SHARPCPP_MANAGED_OUTPUT_ROOT}/$<CONFIG>/Rbfx.UserCode.Generator.dll")

if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
  string(CONCAT _rbfx_test_runsettings_content
    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
    "<RunSettings>\n"
    "  <RunConfiguration>\n"
    "    <EnvironmentVariables>\n"
    "      <LD_LIBRARY_PATH>${SHARPCPP_NATIVE_OUTPUT_ROOT}/$<CONFIG>/</LD_LIBRARY_PATH>\n"
    "      <LD_PRELOAD>${SHARPCPP_NATIVE_OUTPUT_ROOT}/$<CONFIG>/libUrho3D.so</LD_PRELOAD>\n"
    "    </EnvironmentVariables>\n"
    "  </RunConfiguration>\n"
    "</RunSettings>\n")
else()
  set(_rbfx_test_runsettings_content
    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<RunSettings>\n</RunSettings>\n")
endif()
file(GENERATE OUTPUT "${RBFX_TEST_RUNSETTINGS_FILE}" CONTENT "${_rbfx_test_runsettings_content}")

configure_file(
  "${CMAKE_CURRENT_SOURCE_DIR}/CopyRbfxRuntimeDependencies.cmake.in"
  "${RBFX_RUNTIME_DEPENDENCIES_SCRIPT}"
  @ONLY)

string(CONCAT _rbfx_sdk_flags_content
  "compiler_path=${CMAKE_CXX_COMPILER}\n"
  "language_standard=${RBFX_SDK_PARSE_LANGUAGE_STANDARD}\n")
if(RBFX_URHO3D_INCLUDE_DIRECTORIES)
  list(JOIN RBFX_URHO3D_INCLUDE_DIRECTORIES "\ninclude_directory=" _rbfx_sdk_include_directories)
  string(APPEND _rbfx_sdk_flags_content "include_directory=${_rbfx_sdk_include_directories}\n")
endif()
if(RBFX_URHO3D_COMPILE_DEFINITIONS)
  list(JOIN RBFX_URHO3D_COMPILE_DEFINITIONS "\ndefine=" _rbfx_sdk_compile_definitions)
  string(APPEND _rbfx_sdk_flags_content "define=${_rbfx_sdk_compile_definitions}\n")
endif()
if(RBFX_URHO3D_COMPILE_OPTIONS)
  list(JOIN RBFX_URHO3D_COMPILE_OPTIONS "\nclang_argument=" _rbfx_sdk_compile_options)
  string(APPEND _rbfx_sdk_flags_content "clang_argument=${_rbfx_sdk_compile_options}\n")
endif()
file(GENERATE OUTPUT "${RBFX_SDK_FLAGS_FILE}" CONTENT "${_rbfx_sdk_flags_content}")

file(GLOB_RECURSE RBFX_SDK_INPUT_HEADERS CONFIGURE_DEPENDS
  "${RBFX_SDK_ROOT}/include/Urho3D/*.h"
  "${RBFX_SDK_ROOT}/include/Urho3D/*.hpp"
  "${RBFX_SDK_ROOT}/include/RmlUi/*.h"
  "${RBFX_SDK_ROOT}/include/RmlUi/*.hpp"
  "${RBFX_SDK_ROOT}/include/ImGui/*.h"
  "${RBFX_SDK_ROOT}/include/ImGui/*.hpp")

add_subdirectory(SharpCpp.Rbfx.Generator)
add_subdirectory(SharpCpp.Rbfx.Bindings)
add_subdirectory(Rbfx.UserCode)

add_custom_target(
  sharpcpp-rbfx-native
  DEPENDS
    sharpcpp-rbfx-runtime
    SharpCppRbfxUserCode_Native)

add_custom_target(sharpcpp-rbfx-native-all ALL DEPENDS sharpcpp-rbfx-native)
