cmake_minimum_required(VERSION 3.28)

project(SharpCppRbfx LANGUAGES CXX)

option(RBFX_BUILD_USERCODE "Build the phase-two user-code integration fixtures." ON)
if(CMAKE_CROSSCOMPILING OR EMSCRIPTEN OR ANDROID OR CMAKE_SYSTEM_NAME STREQUAL "iOS")
  set(_rbfx_build_host_default OFF)
else()
  set(_rbfx_build_host_default ON)
endif()
option(RBFX_BUILD_HOST "Build the SharpCpp.Rbfx.Host library that hosts the .NET runtime." ${_rbfx_build_host_default})
if(RBFX_BUILD_USERCODE AND RBFX_BUILD_HOST)
  set(_rbfx_build_examples_default ON)
else()
  set(_rbfx_build_examples_default OFF)
endif()
option(RBFX_BUILD_EXAMPLES "Build the Rbfx.NativeHost example." ${_rbfx_build_examples_default})
if(CMAKE_SYSTEM_NAME STREQUAL "iOS" OR EMSCRIPTEN)
  set(RBFX_NATIVE_LIBRARY_TYPE STATIC)
else()
  set(RBFX_NATIVE_LIBRARY_TYPE SHARED)
endif()

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

if(APPLE)
  set(RBFX_RUNTIME_RPATH "@loader_path")
else()
  set(RBFX_RUNTIME_RPATH "$ORIGIN")
endif()

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
set(
  RBFX_SDK_PARSE_EXTRA_OPTIONS
  ""
  CACHE STRING
  "Additional compiler options required to parse the installed rbfx SDK.")
option(
  RBFX_SDK_PARSE_USE_PLATFORM_COMPILER_INCLUDES
  "Use the target platform compiler's builtin Clang headers when parsing the rbfx SDK."
  OFF)

if(NOT EXISTS "${RBFX_SDK_ROOT}/share/Urho3D/CMake/Urho3DConfig.cmake")
  message(FATAL_ERROR "Set RBFX_SDK_ROOT to an installed rbfx SDK before configuring SharpCpp.Rbfx.")
endif()
if(NOT EXISTS "${RBFX_SDK_ROOT}/include/Urho3D")
  message(FATAL_ERROR "rbfx SDK headers were not found at '${RBFX_SDK_ROOT}/include/Urho3D'.")
endif()

function(sharpcpp_find_rbfx_sdk)
  if(RBFX_NATIVE_LIBRARY_TYPE STREQUAL "STATIC")
    set(BUILD_SHARED_LIBS OFF)
  else()
    set(BUILD_SHARED_LIBS ON)
  endif()
  set(CMAKE_FIND_ROOT_PATH_MODE_PACKAGE BOTH)
  set(CMAKE_DISABLE_FIND_PACKAGE_TBB TRUE)
  set(CMAKE_DISABLE_FIND_PACKAGE_Vulkan TRUE)
  # Urho3DConfig.cmake finds the Urho3DThirdParty package of the same SDK with the default search.
  list(PREPEND CMAKE_PREFIX_PATH "${RBFX_SDK_ROOT}")
  find_package(
    Urho3D 0.0.0 CONFIG REQUIRED
    PATHS
      "${RBFX_SDK_ROOT}"
      "${RBFX_SDK_ROOT}/share/Urho3D/CMake"
    NO_DEFAULT_PATH NO_CMAKE_FIND_ROOT_PATH)
endfunction()

sharpcpp_find_rbfx_sdk()
set(RBFX_URHO3D_TARGET Urho3D)

function(sharpcpp_map_imported_rbfx_configurations target_name)
  get_target_property(is_imported "${target_name}" IMPORTED)
  get_target_property(imported_configurations "${target_name}" IMPORTED_CONFIGURATIONS)
  if(is_imported AND imported_configurations)
    if("RELWITHDEBINFO" IN_LIST imported_configurations)
      set(package_configuration RelWithDebInfo)
    elseif("RELEASE" IN_LIST imported_configurations)
      set(package_configuration Release)
    else()
      list(GET imported_configurations 0 package_configuration)
    endif()
    set_target_properties(
      "${target_name}"
      PROPERTIES
        MAP_IMPORTED_CONFIG_RELEASE "${package_configuration}"
        MAP_IMPORTED_CONFIG_MINSIZEREL "${package_configuration}")
  endif()

  get_target_property(link_libraries "${target_name}" INTERFACE_LINK_LIBRARIES)
  if(CMAKE_SYSTEM_NAME STREQUAL "iOS" AND link_libraries)
    # SDK exports can retain the producer's absolute Xcode framework paths.
    # Resolve framework names against the consuming toolchain's selected SDK.
    set(normalized_link_libraries)
    foreach(link_library IN LISTS link_libraries)
      if(IS_ABSOLUTE "${link_library}" AND link_library MATCHES "/([^/]+)\\.framework$")
        list(APPEND normalized_link_libraries "-framework ${CMAKE_MATCH_1}")
      else()
        list(APPEND normalized_link_libraries "${link_library}")
      endif()
    endforeach()
    set_property(TARGET "${target_name}" PROPERTY INTERFACE_LINK_LIBRARIES "${normalized_link_libraries}")
  endif()
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

include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/ExportRbfxSdkParseFlags.cmake")
sharpcpp_export_rbfx_sdk_parse_flags(
  "${RBFX_URHO3D_TARGET}"
  "${RBFX_SDK_FLAGS_FILE}"
  "${RBFX_NATIVE_LIBRARY_TYPE}"
  "${RBFX_SDK_PARSE_LANGUAGE_STANDARD}")

file(GLOB_RECURSE RBFX_SDK_INPUT_HEADERS CONFIGURE_DEPENDS
  "${RBFX_SDK_ROOT}/include/Urho3D/*.h"
  "${RBFX_SDK_ROOT}/include/Urho3D/*.hpp"
  "${RBFX_SDK_ROOT}/include/RmlUi/*.h"
  "${RBFX_SDK_ROOT}/include/RmlUi/*.hpp"
  "${RBFX_SDK_ROOT}/include/ImGui/*.h"
  "${RBFX_SDK_ROOT}/include/ImGui/*.hpp")

include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/RbfxCSharpAddUsercode.cmake")

add_subdirectory(SharpCpp.Rbfx.Generator)
add_subdirectory(SharpCpp.Rbfx.Bindings)
if(RBFX_BUILD_USERCODE)
  add_subdirectory(Rbfx.UserCode)
endif()
if(RBFX_BUILD_HOST)
  add_subdirectory(Rbfx.Host)
endif()
if(RBFX_BUILD_EXAMPLES)
  add_subdirectory(Rbfx.Examples/Example.NativeHost)
endif()

add_custom_target(sharpcpp-rbfx-generate DEPENDS sharpcpp-generate-rbfx)
add_custom_target(sharpcpp-rbfx-native DEPENDS sharpcpp-rbfx-runtime)
get_property(_rbfx_csharp_add_usercode_targets GLOBAL PROPERTY SHARPCPP_RBFX_WRAP_TARGETS)
foreach(_wrap_target IN LISTS _rbfx_csharp_add_usercode_targets)
  add_dependencies(sharpcpp-rbfx-generate sharpcpp-generate-${_wrap_target})
  add_dependencies(sharpcpp-rbfx-native ${_wrap_target})
endforeach()

if(RBFX_BUILD_HOST)
  add_dependencies(sharpcpp-rbfx-native sharpcpp-rbfx-host)
endif()
if(RBFX_BUILD_EXAMPLES)
  add_dependencies(sharpcpp-rbfx-native Rbfx.NativeHost)
endif()

add_custom_target(sharpcpp-rbfx-native-all ALL DEPENDS sharpcpp-rbfx-native)
