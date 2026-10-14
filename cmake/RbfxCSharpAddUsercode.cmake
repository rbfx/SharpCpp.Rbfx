# rbfx_csharp_add_usercode(<target> HEADERS <header>... [keywords])
#
# Generates C# bindings for user C++ headers that use the rbfx SDK. The function creates the bindings
# target <target>: the native library with the generated wrapper code. The implementation of the wrapped
# headers is a separate target that the caller passes in LINK_LIBRARIES. See build/PLAN-dotnet-hosting.md, section 3.3.
# The function also writes the managed bindings project at configure time. It never runs MSBuild.
#
# Keywords with one value:
#   NAMESPACE            C# namespace of the bindings. Default: <target> without "." and "-".
#   ASSEMBLY_NAME        Name of the managed assembly. Default: <target>.
#   LIBRARY_NAME         Name of the native library without prefix and suffix. Default: <target>.Native.
#                        The generated C# imports "lib<LIBRARY_NAME>" for a static library on Emscripten,
#                        "__Internal" for another static library, and LIBRARY_NAME otherwise.
#   SYMBOL_PREFIX        Prefix of the exported native symbols. Default: lower-case <target> with letters and digits only.
#   GENERATED_DIRECTORY  Directory below SHARPCPP_GENERATED_ROOT for the generated sources. Default: <target>.
#   LIBRARY_TYPE         SHARED or STATIC. Default: RBFX_NATIVE_LIBRARY_TYPE.
#   CSPROJ               Path of a managed project that the user maintains. The function does not write
#                        a project file when this keyword is present. The project files that the function
#                        writes are the same, except the csproj.
# Keywords with a list of values:
#   HEADERS              Header files to wrap. Required.
#   INCLUDE_DIRECTORIES  Include directories of the headers.
#   LINK_LIBRARIES       Libraries to link PUBLIC. Pass the target that implements the headers.
#
# Files written to ${SHARPCPP_GENERATED_ROOT}/usercode/<target>/ (only when the content changes):
#   Directory.Build.props, Directory.Build.targets  Import RBFX_CSHARP_MSBUILD_PROPS and RBFX_CSHARP_MSBUILD_TARGETS.
#   <target>.props                                  MSBuild properties of the wrapped library.
#   <ASSEMBLY_NAME>.csproj                          Managed bindings project (not written when CSPROJ is given).
#
# Target properties set on <target>:
#   SHARPCPP_RBFX_MANAGED_PROJECT   Path of the managed project (the CSPROJ value, or the written csproj).
#   SHARPCPP_RBFX_MANAGED_PROPS     Path of <target>.props.
#   SHARPCPP_RBFX_MANAGED_ASSEMBLY  Path of the managed assembly (a generator expression).
#
# Cache variables (defined by the top-level CMakeLists.txt):
#   RBFX_CSHARP_BINDINGS_PROJECTS         Projects that the written csproj references (one ProjectReference for each path).
#   RBFX_CSHARP_BINDINGS_PACKAGE_VERSION  When set, the csproj references the package SharpCpp.Rbfx.Bindings
#                                         of this version instead of the projects.
#   RBFX_CSHARP_MSBUILD_PROPS, RBFX_CSHARP_MSBUILD_TARGETS  Files that the written Directory.Build.* files import.
#
# Required variables (defined by the top-level CMakeLists.txt before this file is included):
#   SHARPCPP_ROOT, SHARPCPP_GENERATED_ROOT, SHARPCPP_NATIVE_OUTPUT_ROOT, SHARPCPP_MANAGED_OUTPUT_ROOT,
#   DOTNET_EXECUTABLE, SHARPCPP_CLANG_RESOURCE_DIRECTORY,
#   RBFX_SDK_ROOT, RBFX_SDK_FLAGS_FILE, RBFX_GENERATED_ROOT, RBFX_METADATA_FILE,
#   RBFX_ENGINE_UMBRELLA_DIRECTORY (the folder of SharpCpp.Rbfx.ParseUmbrella.hpp, which the wrapper includes),
#   RBFX_GENERATED_STAMP (empty in package mode, which has no engine generator),
#   RBFX_SDK_INPUT_HEADERS, RBFX_NATIVE_LIBRARY_TYPE, RBFX_RUNTIME_RPATH, RBFX_URHO3D_TARGET,
#   SHARPCPP_RBFX_USERCODE_GENERATOR_DLL, RBFX_CSHARP_BINDINGS_PROJECTS, RBFX_CSHARP_BINDINGS_PACKAGE_VERSION,
#   RBFX_CSHARP_MSBUILD_PROPS, RBFX_CSHARP_MSBUILD_TARGETS.

define_property(GLOBAL PROPERTY SHARPCPP_RBFX_WRAP_TARGETS
  BRIEF_DOCS "Targets created by rbfx_csharp_add_usercode()"
  FULL_DOCS "Targets created by rbfx_csharp_add_usercode(). The top-level CMakeLists.txt adds them to the aggregate targets.")

function(rbfx_csharp_add_usercode target)
  cmake_parse_arguments(
    WRAP
    ""
    "NAMESPACE;ASSEMBLY_NAME;LIBRARY_NAME;SYMBOL_PREFIX;GENERATED_DIRECTORY;LIBRARY_TYPE;CSPROJ"
    "HEADERS;INCLUDE_DIRECTORIES;LINK_LIBRARIES"
    ${ARGN})

  if(WRAP_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_add_usercode(${target}): unknown arguments: ${WRAP_UNPARSED_ARGUMENTS}")
  endif()
  if(NOT WRAP_HEADERS)
    message(FATAL_ERROR "rbfx_csharp_add_usercode(${target}): HEADERS is required.")
  endif()

  # Defaults.
  if(NOT WRAP_NAMESPACE)
    string(REGEX REPLACE "[.-]" "" WRAP_NAMESPACE "${target}")
  endif()
  if(NOT WRAP_ASSEMBLY_NAME)
    set(WRAP_ASSEMBLY_NAME "${target}")
  endif()
  if(NOT WRAP_LIBRARY_NAME)
    set(WRAP_LIBRARY_NAME "${target}.Native")
  endif()

  # Two calls with the same assembly name share MSBuild obj and output paths.
  get_property(_existing_assemblies GLOBAL PROPERTY SHARPCPP_RBFX_WRAP_ASSEMBLIES)
  if("${WRAP_ASSEMBLY_NAME}" IN_LIST _existing_assemblies)
    message(FATAL_ERROR "rbfx_csharp_add_usercode(${target}): ASSEMBLY_NAME '${WRAP_ASSEMBLY_NAME}' is already used by another call.")
  endif()
  set_property(GLOBAL APPEND PROPERTY SHARPCPP_RBFX_WRAP_ASSEMBLIES "${WRAP_ASSEMBLY_NAME}")
  if(NOT WRAP_SYMBOL_PREFIX)
    string(TOLOWER "${target}" WRAP_SYMBOL_PREFIX)
    string(REGEX REPLACE "[^a-z0-9]" "" WRAP_SYMBOL_PREFIX "${WRAP_SYMBOL_PREFIX}")
  endif()
  if(NOT WRAP_GENERATED_DIRECTORY)
    set(WRAP_GENERATED_DIRECTORY "${target}")
  endif()
  if(NOT WRAP_LIBRARY_TYPE)
    set(WRAP_LIBRARY_TYPE "${RBFX_NATIVE_LIBRARY_TYPE}")
  endif()

  # The generated file names are <file stem>.Bindings.*. The stem is the assembly name without a ".Bindings" suffix.
  string(REGEX REPLACE "\\.Bindings$" "" file_stem "${WRAP_ASSEMBLY_NAME}")

  set(generated_root "${SHARPCPP_GENERATED_ROOT}/${WRAP_GENERATED_DIRECTORY}")
  set(generated_stamp "${generated_root}/generate.stamp")
  set(generator_log "${generated_root}/generator.log")
  set(generated_cpp "${generated_root}/${file_stem}.Bindings.g.cpp")

  # Managed bindings project. The files are written at configure time only. MSBuild builds them later.
  set(usercode_directory "${SHARPCPP_GENERATED_ROOT}/usercode/${target}")
  set(managed_props "${usercode_directory}/${target}.props")
  if(WRAP_CSPROJ)
    cmake_path(ABSOLUTE_PATH WRAP_CSPROJ BASE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" NORMALIZE OUTPUT_VARIABLE managed_project)
  else()
    set(managed_project "${usercode_directory}/${WRAP_ASSEMBLY_NAME}.csproj")
  endif()
  if(WRAP_LIBRARY_TYPE STREQUAL "STATIC")
    set(native_library_prefix "${CMAKE_STATIC_LIBRARY_PREFIX}")
    set(native_library_suffix "${CMAKE_STATIC_LIBRARY_SUFFIX}")
  else()
    set(native_library_prefix "${CMAKE_SHARED_LIBRARY_PREFIX}")
    set(native_library_suffix "${CMAKE_SHARED_LIBRARY_SUFFIX}")
  endif()
  # The name that the generated C# passes to DllImport. A static library is linked into the executable.
  if(WRAP_LIBRARY_TYPE STREQUAL "STATIC" AND CMAKE_SYSTEM_NAME STREQUAL "Emscripten")
    set(WRAP_IMPORT_LIBRARY_NAME "lib${WRAP_LIBRARY_NAME}")
  elseif(WRAP_LIBRARY_TYPE STREQUAL "STATIC")
    set(WRAP_IMPORT_LIBRARY_NAME "__Internal")
  else()
    set(WRAP_IMPORT_LIBRARY_NAME "${WRAP_LIBRARY_NAME}")
  endif()
  set(WRAP_TARGET "${target}")
  set(WRAP_GENERATED_ROOT "${generated_root}")
  set(WRAP_GENERATED_STAMP "${generated_stamp}")
  set(WRAP_NATIVE_LIBRARY_PATH
      "${SHARPCPP_NATIVE_OUTPUT_ROOT}/$(Configuration)/${native_library_prefix}${WRAP_LIBRARY_NAME}${native_library_suffix}")
  if(RBFX_CSHARP_BINDINGS_PACKAGE_VERSION)
    set(RBFX_CSHARP_MANAGED_REFERENCES
        "    <PackageReference Include=\"SharpCpp.Rbfx.Bindings\" Version=\"${RBFX_CSHARP_BINDINGS_PACKAGE_VERSION}\" />")
  else()
    set(references)
    foreach(reference IN LISTS RBFX_CSHARP_BINDINGS_PROJECTS)
      list(APPEND references "    <ProjectReference Include=\"${reference}\" />")
    endforeach()
    list(JOIN references "\n" RBFX_CSHARP_MANAGED_REFERENCES)
  endif()
  set(templates_directory "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/templates")
  set(managed_files
      "UserCodeDirectory.Build.props.in|${usercode_directory}/Directory.Build.props"
      "UserCodeDirectory.Build.targets.in|${usercode_directory}/Directory.Build.targets"
      "UserCode.props.in|${managed_props}")
  if(NOT WRAP_CSPROJ)
    list(APPEND managed_files "UserCode.csproj.in|${managed_project}")
  endif()
  foreach(managed_file IN LISTS managed_files)
    string(REPLACE "|" ";" managed_file "${managed_file}")
    list(GET managed_file 0 template_name)
    list(GET managed_file 1 output_path)
    set(template_path "${templates_directory}/${template_name}")
    set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${template_path}")
    file(READ "${template_path}" template_content)
    file(CONFIGURE OUTPUT "${output_path}" CONTENT "${template_content}" @ONLY NEWLINE_STYLE UNIX)
  endforeach()

  # Resolve relative paths against the calling directory.
  set(headers)
  foreach(header IN LISTS WRAP_HEADERS)
    cmake_path(ABSOLUTE_PATH header BASE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" NORMALIZE OUTPUT_VARIABLE header_path)
    list(APPEND headers "${header_path}")
  endforeach()
  set(include_directories)
  foreach(directory IN LISTS WRAP_INCLUDE_DIRECTORIES)
    cmake_path(ABSOLUTE_PATH directory BASE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" NORMALIZE OUTPUT_VARIABLE directory_path)
    list(APPEND include_directories "${directory_path}")
  endforeach()

  # One header is passed to the generator directly. Several headers use an umbrella header in the binary directory.
  list(LENGTH headers header_count)
  set(parse_include_directories ${include_directories})
  set(umbrella_dependencies)
  set(umbrella_include_directories)
  if(header_count EQUAL 1)
    list(GET headers 0 parse_header)
  else()
    set(umbrella_directory "${CMAKE_CURRENT_BINARY_DIR}/${target}.wrap")
    set(parse_header "${umbrella_directory}/${target}.wrap.hpp")
    set(umbrella_content "// Generated by rbfx_csharp_add_usercode(${target}). Do not edit.\n#pragma once\n")
    foreach(header IN LISTS headers)
      string(APPEND umbrella_content "#include \"${header}\"\n")
    endforeach()
    set(existing_content "")
    if(EXISTS "${parse_header}")
      file(READ "${parse_header}" existing_content)
    endif()
    if(NOT existing_content STREQUAL umbrella_content)
      file(WRITE "${parse_header}" "${umbrella_content}")
    endif()
    list(APPEND parse_include_directories "${umbrella_directory}")
    list(APPEND umbrella_dependencies "${parse_header}")
    list(APPEND umbrella_include_directories "${umbrella_directory}")
  endif()

  set(include_directory_arguments)
  foreach(directory IN LISTS parse_include_directories)
    list(APPEND include_directory_arguments --include-directory "${directory}")
  endforeach()

  set(include_tree_headers)
  foreach(directory IN LISTS parse_include_directories)
    file(GLOB_RECURSE directory_headers CONFIGURE_DEPENDS "${directory}/*.h" "${directory}/*.hpp")
    list(APPEND include_tree_headers ${directory_headers})
  endforeach()

  # Source mode waits for the engine generator. Package mode has no engine generator: the metadata file comes from the package.
  if(RBFX_GENERATED_STAMP)
    set(engine_generation_dependencies "${RBFX_GENERATED_STAMP}")
  else()
    set(engine_generation_dependencies "${RBFX_METADATA_FILE}")
  endif()

  add_custom_command(
    OUTPUT "${generated_stamp}"
    COMMAND "${CMAKE_COMMAND}"
            "-DGENERATOR_DLL=${SHARPCPP_RBFX_USERCODE_GENERATOR_DLL}"
            "-DGENERATED_STAMP=${generated_stamp}"
            "-DGENERATED_DIR=${generated_root}"
            -P "${SHARPCPP_ROOT}/cmake/SharpCppGeneratedStaleness.cmake"
    COMMAND "${CMAKE_COMMAND}" -E make_directory "${generated_root}"
    COMMAND "${CMAKE_COMMAND}" -E env
            "SHARPCPP_RBFX_CLANG_RESOURCE_DIRECTORY=${SHARPCPP_CLANG_RESOURCE_DIRECTORY}"
            "${DOTNET_EXECUTABLE}" "${SHARPCPP_RBFX_USERCODE_GENERATOR_DLL}"
            --sdk-root "${RBFX_SDK_ROOT}"
            --flags-file "${RBFX_SDK_FLAGS_FILE}"
            --header "${parse_header}"
            ${include_directory_arguments}
            --assembly-name "${WRAP_ASSEMBLY_NAME}"
            --namespace "${WRAP_NAMESPACE}"
            --bindings-class "Bindings"
            --library-name "${WRAP_IMPORT_LIBRARY_NAME}"
            --file-stem "${file_stem}"
            --symbol-prefix "${WRAP_SYMBOL_PREFIX}"
            --sharpcpp-include "${SHARPCPP_ROOT}/include"
            --rbfx-include "${PROJECT_SOURCE_DIR}/include"
            --generated-root "${generated_root}"
            --import-binding-metadata "${RBFX_METADATA_FILE}"
            --log-file "${generator_log}"
    COMMAND "${CMAKE_COMMAND}" -E touch "${generated_stamp}"
    DEPENDS
      "${SHARPCPP_RBFX_USERCODE_GENERATOR_DLL}"
      "${RBFX_SDK_FLAGS_FILE}"
      ${engine_generation_dependencies}
      ${headers}
      ${include_tree_headers}
      ${umbrella_dependencies}
      ${RBFX_SDK_INPUT_HEADERS}
    WORKING_DIRECTORY "${PROJECT_SOURCE_DIR}"
    VERBATIM
    USES_TERMINAL)

  add_custom_target(
    sharpcpp-generate-${target}
    DEPENDS "${generated_stamp}")
  if(TARGET sharpcpp-generate-rbfx)
    add_dependencies(sharpcpp-generate-${target} sharpcpp-generate-rbfx)
  endif()
  set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${generated_stamp}")

  set_source_files_properties(
    "${generated_cpp}"
    PROPERTIES
      GENERATED TRUE
      OBJECT_DEPENDS "${generated_stamp}")
  add_library(${target} ${WRAP_LIBRARY_TYPE} "${generated_cpp}")
  add_dependencies(${target} sharpcpp-generate-${target} sharpcpp-rbfx-runtime)
  target_compile_features(${target} PRIVATE cxx_std_20)
  if(MSVC)
    # A unity source of the generated wrappers exceeds the section limit of the default object format.
    target_compile_options(${target} PRIVATE /bigobj)
  endif()
  set_target_properties(
    ${target}
    PROPERTIES
      CXX_SCAN_FOR_MODULES OFF
      UNITY_BUILD ON
      UNITY_BUILD_BATCH_SIZE 100
      OUTPUT_NAME "${WRAP_LIBRARY_NAME}"
      BUILD_RPATH "${RBFX_RUNTIME_RPATH}"
      INSTALL_RPATH "${RBFX_RUNTIME_RPATH}"
      SHARPCPP_RBFX_GENERATED_DIR "${generated_root}"
      SHARPCPP_RBFX_GENERATED_STAMP "${generated_stamp}"
      SHARPCPP_RBFX_ASSEMBLY_NAME "${WRAP_ASSEMBLY_NAME}"
      SHARPCPP_RBFX_LIBRARY_NAME "${WRAP_LIBRARY_NAME}"
      SHARPCPP_RBFX_MANAGED_PROJECT "${managed_project}"
      SHARPCPP_RBFX_MANAGED_PROPS "${managed_props}"
      SHARPCPP_RBFX_MANAGED_ASSEMBLY "${SHARPCPP_MANAGED_OUTPUT_ROOT}/$<CONFIG>/${WRAP_ASSEMBLY_NAME}.dll")
  target_include_directories(
    ${target}
    PRIVATE
      "${generated_root}"
      "${RBFX_ENGINE_UMBRELLA_DIRECTORY}"
      ${umbrella_include_directories}
    PUBLIC
      ${include_directories})
  target_link_libraries(
    ${target}
    PUBLIC
      ${WRAP_LINK_LIBRARIES}
    PRIVATE
      SharpCpp::NativeSupport
      "${RBFX_URHO3D_TARGET}")
  target_compile_options(
    ${target}
    PRIVATE $<$<COMPILE_LANG_AND_ID:CXX,GNU,Clang,AppleClang>:-Wno-attributes>)
  sharpcpp_set_native_output(${target})

  set_property(GLOBAL APPEND PROPERTY SHARPCPP_RBFX_WRAP_TARGETS "${target}")
endfunction()
