cmake_minimum_required(VERSION 3.28)

project(SharpCppRbfx VERSION 0.0.0 LANGUAGES CXX)

option(RBFX_BUILD_USERCODE "Build the phase-two user-code integration fixtures." ON)
if(CMAKE_CROSSCOMPILING OR EMSCRIPTEN OR ANDROID OR CMAKE_SYSTEM_NAME STREQUAL "iOS")
  set(_rbfx_build_host_default OFF)
else()
  set(_rbfx_build_host_default ON)
endif()
option(RBFX_BUILD_HOST "Build the SharpCpp.Rbfx.Host library that hosts the .NET runtime." ${_rbfx_build_host_default})
if(RBFX_BUILD_HOST AND PROJECT_IS_TOP_LEVEL)
  set(_rbfx_install_sdk_default ON)
else()
  set(_rbfx_install_sdk_default OFF)
endif()
option(RBFX_INSTALL_SDK "Install the CMake SDK (host library, headers, package files). Needs RBFX_BUILD_HOST." ${_rbfx_install_sdk_default})
if(RBFX_INSTALL_SDK AND NOT RBFX_BUILD_HOST)
  message(STATUS "RBFX_INSTALL_SDK is ignored because RBFX_BUILD_HOST is OFF.")
  set(RBFX_INSTALL_SDK OFF)
endif()
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
# The linkage of nethost follows this value. The Android toolchains read only a -DBUILD_SHARED_LIBS
# cache value, because they run before this line.
if(RBFX_NATIVE_LIBRARY_TYPE STREQUAL "STATIC")
  set(BUILD_SHARED_LIBS OFF)
else()
  set(BUILD_SHARED_LIBS ON)
endif()

get_filename_component(_sharpcpp_default_root "${CMAKE_CURRENT_SOURCE_DIR}/../SharpCpp" ABSOLUTE)
set(SHARPCPP_ROOT "${_sharpcpp_default_root}" CACHE PATH "Path to the SharpCpp checkout used to generate the bindings.")
if(NOT EXISTS "${SHARPCPP_ROOT}/include/SharpCpp/Marshaller.hpp")
  message(FATAL_ERROR "SharpCpp was not found at '${SHARPCPP_ROOT}'. Set SHARPCPP_ROOT to a SharpCpp checkout.")
endif()

# The top-level developer build writes to <repo>/build/bin. A consumer tree (add_subdirectory or the
# find_package shim) writes below its own build directory. A value from the user always wins.
if(PROJECT_IS_TOP_LEVEL)
  set(_rbfx_default_native_output_root "${CMAKE_CURRENT_SOURCE_DIR}/build/bin")
  set(_rbfx_default_managed_output_root "${CMAKE_CURRENT_SOURCE_DIR}/build/bin")
else()
  set(_rbfx_default_native_output_root "${CMAKE_BINARY_DIR}/SharpCppRbfx/bin")
  set(_rbfx_default_managed_output_root "${CMAKE_BINARY_DIR}/SharpCppRbfx/managed")
endif()
set(
  SHARPCPP_NATIVE_OUTPUT_ROOT
  "${_rbfx_default_native_output_root}"
  CACHE PATH
  "Root directory for native artifacts.")
set(
  SHARPCPP_MANAGED_OUTPUT_ROOT
  "${_rbfx_default_managed_output_root}"
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
find_program(DOTNET_EXECUTABLE dotnet)
if(NOT DOTNET_EXECUTABLE)
  message(FATAL_ERROR
    "SharpCpp.Rbfx: the dotnet command was not found. The .NET SDK is necessary to run the generators. "
    "Install the .NET SDK or set DOTNET_EXECUTABLE.")
endif()

if(APPLE)
  set(RBFX_RUNTIME_RPATH "@loader_path")
else()
  set(RBFX_RUNTIME_RPATH "$ORIGIN")
endif()

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

# The engine comes from CMAKE_PREFIX_PATH (CMAKE_FIND_ROOT_PATH for web builds, or Urho3D_DIR) through find_package(Urho3D). RBFX_ENGINE_ROOT is
# the root of the installed SDK. The generator reads the engine headers from it. The variable is internal.
# When the including project already has a target Urho3D, it must come from an installed SDK, because the
# root derives from Urho3D_DIR. An engine in source mode is not supported yet.
function(sharpcpp_find_rbfx_sdk)
  if(NOT TARGET Urho3D)
    if(RBFX_NATIVE_LIBRARY_TYPE STREQUAL "STATIC")
      set(BUILD_SHARED_LIBS OFF)
    else()
      set(BUILD_SHARED_LIBS ON)
    endif()
    set(CMAKE_FIND_ROOT_PATH_MODE_PACKAGE BOTH)
    set(CMAKE_DISABLE_FIND_PACKAGE_TBB TRUE)
    set(CMAKE_DISABLE_FIND_PACKAGE_Vulkan TRUE)
    # Web builds of rbfx-ci give the SDK in CMAKE_FIND_ROOT_PATH instead of CMAKE_PREFIX_PATH. Search both.
    find_package(Urho3D CONFIG REQUIRED PATHS ${CMAKE_FIND_ROOT_PATH} NO_CMAKE_FIND_ROOT_PATH)
    if(NOT Urho3D_IS_SDK)
      message(FATAL_ERROR "SharpCpp.Rbfx: find_package(Urho3D) found '${Urho3D_DIR}', which is not an installed rbfx SDK.")
    endif()
  endif()
  if(NOT Urho3D_DIR)
    message(FATAL_ERROR
      "SharpCpp.Rbfx: the target Urho3D exists, but it does not come from an installed rbfx SDK. "
      "An engine in source mode is not supported yet. Add the installed SDK to CMAKE_PREFIX_PATH.")
  endif()
  # Urho3D_DIR is <SDK root>/share/Urho3D/CMake.
  get_filename_component(engine_root "${Urho3D_DIR}/../../.." ABSOLUTE)
  if(NOT EXISTS "${engine_root}/include/Urho3D")
    message(FATAL_ERROR "SharpCpp.Rbfx: the rbfx SDK headers were not found at '${engine_root}/include/Urho3D'.")
  endif()
  set(RBFX_ENGINE_ROOT "${engine_root}" PARENT_SCOPE)
endfunction()

sharpcpp_find_rbfx_sdk()

# Package builds map the source, build, SharpCpp, SDK and dotnet directories to short relative names in
# debug info, __FILE__ and assert strings. The libraries then carry no path of the build machine.
# Developer builds keep the real paths, because a debugger needs them to find the sources.
option(RBFX_REMAP_SOURCE_PATHS "Replace the directories of this machine in the compiled output with short relative names." OFF)
if(RBFX_REMAP_SOURCE_PATHS)
  # The compiler matches a map as a string prefix, and the last matching map wins. A nested directory
  # comes after the directory that contains it. The SharpCpp directory comes first, because its name
  # is a string prefix of the name of this directory (SharpCpp and SharpCpp.Rbfx).
  set(_rbfx_path_maps
    "${SHARPCPP_ROOT}=SharpCpp"
    "${RBFX_ENGINE_ROOT}=rbfx-sdk"
    "${CMAKE_CURRENT_SOURCE_DIR}=."
    "${CMAKE_BINARY_DIR}=build")
  if(MSVC)
    # The compiler removes the prefix from the file names in __FILE__ and in the debug info.
    foreach(_rbfx_path_map IN LISTS _rbfx_path_maps)
      string(REGEX REPLACE "=[^=]*$" "" _rbfx_path "${_rbfx_path_map}")
      file(TO_NATIVE_PATH "${_rbfx_path}" _rbfx_path)
      add_compile_options("$<$<COMPILE_LANGUAGE:CXX>:/d1trimfile:${_rbfx_path}\\>")
    endforeach()
  elseif(CMAKE_CXX_COMPILER_ID MATCHES "GNU|Clang")
    foreach(_rbfx_path_map IN LISTS _rbfx_path_maps)
      add_compile_options("$<$<COMPILE_LANGUAGE:CXX>:-ffile-prefix-map=${_rbfx_path_map}>")
    endforeach()
    # The two older options cover compilers that read only one of them.
    foreach(_rbfx_path_map IN LISTS _rbfx_path_maps)
      add_compile_options("$<$<COMPILE_LANGUAGE:CXX>:-fdebug-prefix-map=${_rbfx_path_map}>")
    endforeach()
  endif()
endif()

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

set(
  RBFX_ENGINE_METADATA_FILE
  ""
  CACHE FILEPATH
  "Package mode: the engine binding metadata that the runtime package carries. When set, the engine wrapper is neither generated nor built.")

set(RBFX_GENERATED_ROOT "${SHARPCPP_GENERATED_ROOT}/rbfx")
if(RBFX_ENGINE_METADATA_FILE OR RBFX_CSHARP_BINDINGS_PACKAGE_VERSION)
  if(NOT RBFX_ENGINE_METADATA_FILE OR NOT RBFX_CSHARP_BINDINGS_PACKAGE_VERSION)
    message(FATAL_ERROR "Package mode needs RBFX_CSHARP_BINDINGS_PACKAGE_VERSION and RBFX_ENGINE_METADATA_FILE together.")
  endif()
  if(NOT EXISTS "${RBFX_ENGINE_METADATA_FILE}")
    message(FATAL_ERROR "RBFX_ENGINE_METADATA_FILE does not exist: ${RBFX_ENGINE_METADATA_FILE}")
  endif()
  set(RBFX_GENERATED_STAMP "")
  set(RBFX_GENERATED_SOURCE_LIST "")
  set(RBFX_METADATA_FILE "${RBFX_ENGINE_METADATA_FILE}")
  # The runtime package carries the umbrella header next to the metadata file.
  get_filename_component(RBFX_ENGINE_UMBRELLA_DIRECTORY "${RBFX_ENGINE_METADATA_FILE}" DIRECTORY)
  if(NOT EXISTS "${RBFX_ENGINE_UMBRELLA_DIRECTORY}/SharpCpp.Rbfx.ParseUmbrella.hpp")
    message(FATAL_ERROR "SharpCpp.Rbfx.ParseUmbrella.hpp is missing next to ${RBFX_ENGINE_METADATA_FILE}")
  endif()
else()
  set(RBFX_GENERATED_STAMP "${RBFX_GENERATED_ROOT}/generate.stamp")
  set(RBFX_GENERATED_SOURCE_LIST "${CMAKE_CURRENT_BINARY_DIR}/sharpcpp-rbfx-generated-sources.txt")
  set(RBFX_METADATA_FILE "${RBFX_GENERATED_ROOT}/SharpCpp.Rbfx.Bindings.json")
  set(RBFX_ENGINE_UMBRELLA_DIRECTORY "${RBFX_GENERATED_ROOT}")
endif()
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
  "${RBFX_ENGINE_ROOT}/include/Urho3D/*.h"
  "${RBFX_ENGINE_ROOT}/include/Urho3D/*.hpp"
  "${RBFX_ENGINE_ROOT}/include/RmlUi/*.h"
  "${RBFX_ENGINE_ROOT}/include/RmlUi/*.hpp"
  "${RBFX_ENGINE_ROOT}/include/ImGui/*.h"
  "${RBFX_ENGINE_ROOT}/include/ImGui/*.hpp")

set(
  RBFX_CSHARP_BINDINGS_PROJECTS
  "${CMAKE_CURRENT_SOURCE_DIR}/SharpCpp.Ea/SharpCpp.Ea.csproj;${SHARPCPP_ROOT}/src/SharpCpp.Std/SharpCpp.Std.csproj;${SHARPCPP_ROOT}/src/SharpCpp.Support/SharpCpp.Support.csproj;${CMAKE_CURRENT_SOURCE_DIR}/SharpCpp.Rbfx.Bindings/SharpCpp.Rbfx.Bindings.csproj"
  CACHE STRING
  "Managed projects that the bindings project written by rbfx_csharp_add_usercode() references.")
set(
  RBFX_CSHARP_BINDINGS_PACKAGE_VERSION
  ""
  CACHE STRING
  "When set, the written bindings project references the SharpCpp.Rbfx.Bindings package of this version instead of the projects.")
set(
  RBFX_CSHARP_MSBUILD_PROPS
  "${PROJECT_SOURCE_DIR}/Directory.Build.props"
  CACHE FILEPATH
  "MSBuild props file that the written Directory.Build.props imports.")
set(
  RBFX_CSHARP_MSBUILD_TARGETS
  "${PROJECT_SOURCE_DIR}/Directory.Build.targets"
  CACHE FILEPATH
  "MSBuild targets file that the written Directory.Build.targets imports.")

# Inputs of RbfxCSharpAddUserCode.cmake. SharpCppRbfxConfig.cmake sets them to the installed files.
set(SHARPCPP_RBFX_SHARPCPP_INCLUDE_DIRECTORY "${SHARPCPP_ROOT}/include")
set(SHARPCPP_RBFX_RBFX_INCLUDE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/include")
set(SHARPCPP_RBFX_STALENESS_SCRIPT "${SHARPCPP_ROOT}/cmake/SharpCppGeneratedStaleness.cmake")

include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/RbfxCSharpAddUserCode.cmake")
include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/RbfxCSharpDotNet.cmake")

# A consumer tree (add_subdirectory or the find_package shim) needs only CMAKE_PREFIX_PATH. This build
# runs "dotnet build" for the generators into SHARPCPP_MANAGED_OUTPUT_ROOT. The top-level developer build
# expects built generators (dotnet build SharpCpp.Rbfx.slnx). MSBuild sets RBFX_CSHARP_BUILD_MANAGED=OFF.
if(PROJECT_IS_TOP_LEVEL)
  set(_rbfx_build_generators_default OFF)
else()
  set(_rbfx_build_generators_default ON)
endif()
option(
  RBFX_CSHARP_BUILD_GENERATORS
  "Build the managed generators with dotnet from this CMake build. It needs RBFX_CSHARP_BUILD_MANAGED."
  ${_rbfx_build_generators_default})
# A consumer tree: every dotnet build that CMake starts or writes gets the same output root and the same
# intermediate root, so that SHARPCPP_MANAGED_OUTPUT_ROOT holds all the managed assemblies.
# The MSBuild-driven and the top-level developer builds keep the root of eng/SharpCpp.Rbfx.Build.props.
if(NOT PROJECT_IS_TOP_LEVEL)
  set(RBFX_CSHARP_MSBUILD_PROPERTIES
    "SharpCppManagedOutputRoot=${SHARPCPP_MANAGED_OUTPUT_ROOT}"
    "SharpCppNativeOutputRoot=${SHARPCPP_NATIVE_OUTPUT_ROOT}"
    "SharpCppBuildRoot=${CMAKE_BINARY_DIR}/SharpCppRbfx/dotnet")
endif()
if(RBFX_CSHARP_BUILD_GENERATORS AND RBFX_CSHARP_BUILD_MANAGED)
  # The projects share the referenced projects. Run them one after the other.
  rbfx_csharp_add_dotnet_project(
    sharpcpp-rbfx-build-generator
    PROJECT "${CMAKE_CURRENT_SOURCE_DIR}/SharpCpp.Rbfx.Generator/SharpCpp.Rbfx.Generator.csproj"
    BYPRODUCTS "${SHARPCPP_RBFX_GENERATOR_DLL}")
  rbfx_csharp_add_dotnet_project(
    sharpcpp-rbfx-build-usercode-generator
    PROJECT "${CMAKE_CURRENT_SOURCE_DIR}/Rbfx.UserCode.Generator/Rbfx.UserCode.Generator.csproj"
    BYPRODUCTS "${SHARPCPP_RBFX_USERCODE_GENERATOR_DLL}"
    DEPENDS sharpcpp-rbfx-build-generator)
endif()

if(NOT RBFX_ENGINE_METADATA_FILE)
  add_subdirectory(SharpCpp.Rbfx.Generator)
endif()
if(TARGET sharpcpp-generate-rbfx AND TARGET sharpcpp-rbfx-build-generator)
  add_dependencies(sharpcpp-generate-rbfx sharpcpp-rbfx-build-generator)
endif()
add_subdirectory(SharpCpp.Rbfx.Bindings)
# The managed bindings project (and the projects that it references: SharpCpp.Ea, SharpCpp.Std and SharpCpp.Support)
# reads the generated engine sources. A project of the consumer references it.
if(TARGET sharpcpp-rbfx-build-usercode-generator AND TARGET sharpcpp-generate-rbfx AND NOT RBFX_CSHARP_BINDINGS_PACKAGE_VERSION)
  rbfx_csharp_add_dotnet_project(
    sharpcpp-rbfx-build-managed
    PROJECT "${CMAKE_CURRENT_SOURCE_DIR}/SharpCpp.Rbfx.Bindings/SharpCpp.Rbfx.Bindings.csproj"
    BYPRODUCTS
      "${SHARPCPP_MANAGED_OUTPUT_ROOT}/$<CONFIG>/SharpCpp.Rbfx.Bindings.dll"
      "${SHARPCPP_MANAGED_OUTPUT_ROOT}/$<CONFIG>/SharpCpp.Std.dll"
      "${SHARPCPP_MANAGED_OUTPUT_ROOT}/$<CONFIG>/SharpCpp.Ea.dll"
    DEPENDS sharpcpp-rbfx-build-usercode-generator sharpcpp-generate-rbfx)
  # The project checks that the native wrapper exists.
  if(TARGET sharpcpp-rbfx-runtime)
    add_dependencies(sharpcpp-rbfx-build-managed sharpcpp-rbfx-runtime)
  endif()
endif()
if(RBFX_BUILD_USERCODE)
  add_subdirectory(Rbfx.Examples/Example.UserCode)
endif()
if(RBFX_BUILD_HOST)
  add_subdirectory(Rbfx.Host)
endif()
if(RBFX_BUILD_EXAMPLES)
  add_subdirectory(Rbfx.Examples/Example.NativeHost)
endif()

if(RBFX_INSTALL_SDK)
  include(GNUInstallDirs)
  include(CMakePackageConfigHelpers)
  set(_rbfx_sdk_cmake_dir "${CMAKE_INSTALL_LIBDIR}/cmake/SharpCppRbfx")
  set(_rbfx_sdk_module_dir "${CMAKE_INSTALL_DATADIR}/SharpCppRbfx/cmake")
  set(_rbfx_sdk_data_dir "${CMAKE_INSTALL_DATADIR}/SharpCppRbfx")
  set(_rbfx_sdk_include_dir "${CMAKE_INSTALL_INCLUDEDIR}")

  # A static host exports a link dependency on NetHost::NetHost. A shared host needs no NetHost.
  set(SHARPCPP_RBFX_HOST_NEEDS_NETHOST OFF)
  if(RBFX_NATIVE_LIBRARY_TYPE STREQUAL "STATIC")
    set(SHARPCPP_RBFX_HOST_NEEDS_NETHOST ON)
  endif()

  # rbfx_csharp_add_usercode() needs the metadata and the generator. The SDK has them in a source-mode
  # build with a shared library type (see SharpCpp.Rbfx.Bindings/CMakeLists.txt).
  set(SHARPCPP_RBFX_SDK_HAS_USERCODE OFF)
  if(NOT RBFX_CSHARP_BINDINGS_PACKAGE_VERSION AND RBFX_NATIVE_LIBRARY_TYPE STREQUAL "SHARED")
    set(SHARPCPP_RBFX_SDK_HAS_USERCODE ON)
  endif()

  # The managed payload is installed in source mode only (see below).
  set(SHARPCPP_RBFX_SDK_HAS_MANAGED OFF)
  if(NOT RBFX_CSHARP_BINDINGS_PACKAGE_VERSION)
    set(SHARPCPP_RBFX_SDK_HAS_MANAGED ON)
  endif()

  configure_package_config_file(
    "${CMAKE_CURRENT_SOURCE_DIR}/cmake/SharpCppRbfxConfig.cmake.in"
    "${CMAKE_CURRENT_BINARY_DIR}/SharpCppRbfxConfig.cmake"
    INSTALL_DESTINATION "${_rbfx_sdk_cmake_dir}"
    PATH_VARS _rbfx_sdk_module_dir _rbfx_sdk_data_dir _rbfx_sdk_include_dir)

  install(
    FILES
      "${CMAKE_CURRENT_BINARY_DIR}/SharpCppRbfxConfig.cmake"
    DESTINATION "${_rbfx_sdk_cmake_dir}")
  install(
    FILES
      "${CMAKE_CURRENT_SOURCE_DIR}/cmake/FindNetHost.cmake"
      "${CMAKE_CURRENT_SOURCE_DIR}/cmake/RbfxCSharpDotNet.cmake"
    DESTINATION "${_rbfx_sdk_module_dir}")

  # Package mode has no managed build of its own, so the managed payload belongs to source mode only.
  if(NOT RBFX_CSHARP_BINDINGS_PACKAGE_VERSION)
    # The managed payload comes from the output folder of "dotnet build". The install step does not
    # build it. It stops with a message when a file is missing. The set of files is the same as the
    # lib folder and the analyzer folder of the runtime package (packaging/nuget/package.sh).
    set(_rbfx_sdk_share_dir "${CMAKE_INSTALL_DATADIR}/SharpCppRbfx")
    set(_rbfx_sdk_managed_source "${SHARPCPP_MANAGED_OUTPUT_ROOT}/$<CONFIG>")
    install(CODE "
      if(IS_ABSOLUTE \"${_rbfx_sdk_share_dir}\")
        set(_rbfx_share_dir \"\$ENV{DESTDIR}${_rbfx_sdk_share_dir}\")
      else()
        set(_rbfx_share_dir \"\$ENV{DESTDIR}\${CMAKE_INSTALL_PREFIX}/${_rbfx_sdk_share_dir}\")
      endif()
      foreach(_rbfx_assembly IN ITEMS
          SharpCpp.Rbfx.Bindings SharpCpp.Support SharpCpp.Std SharpCpp.Ea SharpCpp.Rbfx.SourceGenerator)
        if(NOT EXISTS \"${_rbfx_sdk_managed_source}/\${_rbfx_assembly}.dll\")
          message(FATAL_ERROR \"The managed assembly is missing: ${_rbfx_sdk_managed_source}/\${_rbfx_assembly}.dll. Run dotnet build before the install step.\")
        endif()
        foreach(_rbfx_extension IN ITEMS dll pdb xml)
          if(EXISTS \"${_rbfx_sdk_managed_source}/\${_rbfx_assembly}.\${_rbfx_extension}\")
            file(INSTALL \"${_rbfx_sdk_managed_source}/\${_rbfx_assembly}.\${_rbfx_extension}\"
              DESTINATION \"\${_rbfx_share_dir}/managed\")
          endif()
        endforeach()
      endforeach()
      set(RBFX_GENERATOR_SOURCE_DIR \"${_rbfx_sdk_managed_source}\")
      set(RBFX_GENERATOR_INSTALL_DIR \"\${_rbfx_share_dir}/tools/usercode\")")
    install(SCRIPT "${CMAKE_CURRENT_SOURCE_DIR}/cmake/RbfxInstallUserCodeGenerator.cmake")
  endif()

  # Headers for the user-code generator: --sharpcpp-include and --rbfx-include. Both trees
  # have the folder SharpCpp, so the files merge in include/SharpCpp.
  install(
    DIRECTORY "${SHARPCPP_ROOT}/include/SharpCpp"
    DESTINATION "${CMAKE_INSTALL_INCLUDEDIR}"
    FILES_MATCHING PATTERN "*.hpp" PATTERN "*.h")
  install(
    DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/include/SharpCpp"
    DESTINATION "${CMAKE_INSTALL_INCLUDEDIR}"
    FILES_MATCHING PATTERN "*.hpp" PATTERN "*.h")
  install(
    FILES
      "${SHARPCPP_ROOT}/cmake/SharpCppGeneratedStaleness.cmake"
      "${SHARPCPP_ROOT}/cmake/SharpCppOutput.cmake"
      "${CMAKE_CURRENT_SOURCE_DIR}/cmake/ExportRbfxSdkParseFlags.cmake"
      "${CMAKE_CURRENT_SOURCE_DIR}/cmake/RbfxCSharpAddUserCode.cmake"
    DESTINATION "${_rbfx_sdk_module_dir}")
  install(
    DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/cmake/templates"
    DESTINATION "${_rbfx_sdk_module_dir}")

  # The builtin Clang headers for the user-code generator. A consumer of the SDK needs no network and no git.
  # The source is often a link to the LLVM checkout (SharpCppClang.cmake). Resolve it, so that the install
  # step copies the files and not the link.
  file(REAL_PATH "${SHARPCPP_CLANG_RESOURCE_DIRECTORY}/include" _rbfx_sdk_clang_headers)
  install(
    DIRECTORY "${_rbfx_sdk_clang_headers}/"
    DESTINATION "${_rbfx_sdk_data_dir}/clang-resource/include"
    PATTERN "CMakeLists.txt" EXCLUDE)
endif()

add_custom_target(sharpcpp-rbfx-generate)
if(TARGET sharpcpp-generate-rbfx)
  add_dependencies(sharpcpp-rbfx-generate sharpcpp-generate-rbfx)
endif()
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
get_property(_rbfx_plugin_targets GLOBAL PROPERTY SHARPCPP_RBFX_PLUGIN_TARGETS)
foreach(_plugin_target IN LISTS _rbfx_plugin_targets)
  add_dependencies(sharpcpp-rbfx-native ${_plugin_target})
endforeach()

add_custom_target(sharpcpp-rbfx-native-all ALL DEPENDS sharpcpp-rbfx-native)
