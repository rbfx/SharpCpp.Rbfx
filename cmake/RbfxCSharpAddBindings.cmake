# rbfx_csharp_add_bindings(<target> HEADERS <header>... [keywords])
#
# Generates a C# bindings over user C++ headers that use the rbfx SDK. The function also writes the managed bindings
# project at configure time. It never runs MSBuild. See build/PLAN-dotnet-hosting.md, section 3.3.
# The implementation of the wrapped headers is a separate target that the caller passes in IMPLEMENTATION.
# There are two modes:
#   Integration (default, with one IMPLEMENTATION target): the generated C++ glue is a source of the IMPLEMENTATION
#     target itself (target_sources of a small file that includes the generated file, the include directories, cxx_std_20
#     and a dependency on the generation).
#     <target> is an INTERFACE library that holds the properties of the bindings. There is no native library of the
#     bindings: the C# code imports the entry points from the library of the IMPLEMENTATION target. Its name is the
#     OUTPUT_NAME of that target (or the target name), so set OUTPUT_NAME before this call. The IMPLEMENTATION target
#     must be a SHARED, MODULE, STATIC or OBJECT library of the project (not imported, not an alias). A static or
#     object library needs RBFX_NATIVE_LIBRARY_TYPE STATIC (iOS, Emscripten): the code is in the executable and the
#     import name is "__Internal" ("lib<name>" on Emscripten). On another platform it is an error. The entry points
#     have the visibility "default" (SHARPCPP_EXPORT), so a library that hides its symbols still exports them.
#     The library path, the install rules and the run-time path are the ones of the IMPLEMENTATION target.
#     The IMPLEMENTATION target must use the keyword signature of target_link_libraries (PUBLIC, PRIVATE), because the
#     function adds links to it. Otherwise use SEPARATE_LIBRARY. An object library is only for Emscripten.
#   Separate library (keyword SEPARATE_LIBRARY, or no IMPLEMENTATION): <target> is a native library with the
#     generated code. It links the IMPLEMENTATION targets PUBLIC.
# The property SHARPCPP_RBFX_NATIVE_TARGET of <target> names the target that holds the native code (the IMPLEMENTATION
# target, or <target> itself). Use rbfx_csharp_bindings_native_target(<target> <out_var>) to read it.
#
# Keywords with one value:
#   NAMESPACE            C# namespace of the bindings. Default: <target> without "." and "-".
#   ASSEMBLY_NAME        Name of the managed assembly. Default: <target>.
#   BINDINGS_CLASS       Name of the generated static partial class in NAMESPACE that holds the module initializer and
#                        the callback entry points. Two assemblies that share a namespace need different names, or
#                        the compiler reports CS0436 in the code that references both. Default: <target> with every
#                        character that is not a letter, a digit or "_" replaced by "_", followed by "Bindings"
#                        unless the result already ends with "Bindings" (for example Plugin_Core_SamplePlugin_Bindings).
#   LIBRARY_NAME         Name of the native library without prefix and suffix. Default: <target>.Native.
#                        Only for the separate-library mode. The integration mode takes the name from the
#                        IMPLEMENTATION target and stops with an error when LIBRARY_NAME is present.
#                        The generated C# imports "lib<LIBRARY_NAME>" for a static library on Emscripten,
#                        "__Internal" for another static library, and LIBRARY_NAME otherwise.
#   SYMBOL_PREFIX        Prefix of the exported native symbols. Default: lower-case <target> with letters and digits only.
#   GENERATED_DIRECTORY  Directory below SHARPCPP_GENERATED_ROOT for the generated sources. Default: <target>.
#   LIBRARY_TYPE         SHARED or STATIC. Default: RBFX_NATIVE_LIBRARY_TYPE. Only for the separate-library mode.
#   SEPARATE_LIBRARY     (option, no value) Make the separate-library mode, also when IMPLEMENTATION is present.
#   CSPROJ               Path of a managed project that the user maintains. The function does not write
#                        a project file when this keyword is present. The project files that the function
#                        writes are the same, except the csproj.
# Keywords with a list of values:
#   HEADERS              Header files to wrap. Required.
#   INCLUDE_DIRECTORIES  Include directories of the headers.
#   IMPLEMENTATION       Integration mode: the one library target that implements the headers and gets the glue.
#                        Separate-library mode: libraries that implement the headers. The function links them PUBLIC.
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
#   SHARPCPP_RBFX_NATIVE_TARGET     The target with the native code (see above).
#
# Cache variables (defined by the top-level CMakeLists.txt):
#   RBFX_CSHARP_BINDINGS_PROJECTS         Projects that the written csproj references (one ProjectReference for each path).
#   RBFX_CSHARP_BINDINGS_PACKAGE_VERSION  When set, the csproj references the package SharpCpp.Rbfx.Bindings
#                                         of this version instead of the projects.
#   RBFX_CSHARP_MSBUILD_PROPS, RBFX_CSHARP_MSBUILD_TARGETS  Files that the written Directory.Build.* files import.
#
# Required variables. The top-level CMakeLists.txt defines them before this file is included. In a consumer
# project, SharpCppRbfxConfig.cmake of the installed SDK defines them and includes this file:
#   SHARPCPP_GENERATED_ROOT, SHARPCPP_NATIVE_OUTPUT_ROOT, SHARPCPP_MANAGED_OUTPUT_ROOT,
#   SHARPCPP_RBFX_SHARPCPP_INCLUDE_DIRECTORY (--sharpcpp-include), SHARPCPP_RBFX_RBFX_INCLUDE_DIRECTORY (--rbfx-include),
#   SHARPCPP_RBFX_STALENESS_SCRIPT (SharpCppGeneratedStaleness.cmake),
#   DOTNET_EXECUTABLE, SHARPCPP_CLANG_RESOURCE_DIRECTORY,
#   RBFX_ENGINE_MODE, RBFX_SDK_FLAGS_FILE, RBFX_METADATA_FILE,
#   RBFX_ENGINE_UMBRELLA_DIRECTORY (the folder of SharpCpp.Rbfx.ParseUmbrella.hpp, which the bindings includes),
#   RBFX_GENERATED_STAMP (empty in package mode, which has no engine generator),
#   RBFX_SDK_INPUT_HEADERS, RBFX_NATIVE_LIBRARY_TYPE, RBFX_RUNTIME_RPATH, RBFX_URHO3D_TARGET,
#   SHARPCPP_RBFX_USERCODE_GENERATOR_DLL.
# Optional variables (the installed SDK does not set them): RBFX_CSHARP_BINDINGS_PROJECTS, RBFX_CSHARP_BINDINGS_PACKAGE_VERSION,
#   RBFX_CSHARP_MSBUILD_PROPS, RBFX_CSHARP_MSBUILD_TARGETS, RBFX_CSHARP_MSBUILD_PROPERTIES,
#   RBFX_USERCODE_ENGINE_ARGUMENTS (the engine arguments of the user-code generator, empty in source mode).
# Required commands: sharpcpp_set_native_output() (SharpCppOutput.cmake). Required target: SharpCpp::NativeSupport.
#
# The targets sharpcpp-generate-rbfx and sharpcpp-rbfx-runtime exist only in the build tree of this repository.
# The function adds a dependency on them only when they exist.

# The variables that the function reads. The one list is used for the check in the function and by the
# shim package (cmake/SharpCppRbfx), which copies them from the directory of the source tree to the
# scope of the consumer. Keep it equal to the "Required variables" above.
set_property(GLOBAL PROPERTY SHARPCPP_RBFX_USERCODE_INPUT_VARIABLES
  SHARPCPP_GENERATED_ROOT SHARPCPP_NATIVE_OUTPUT_ROOT SHARPCPP_MANAGED_OUTPUT_ROOT
  SHARPCPP_RBFX_SHARPCPP_INCLUDE_DIRECTORY SHARPCPP_RBFX_RBFX_INCLUDE_DIRECTORY SHARPCPP_RBFX_STALENESS_SCRIPT
  DOTNET_EXECUTABLE SHARPCPP_CLANG_RESOURCE_DIRECTORY
  RBFX_ENGINE_MODE RBFX_SDK_FLAGS_FILE RBFX_METADATA_FILE
  RBFX_ENGINE_UMBRELLA_DIRECTORY RBFX_GENERATED_STAMP
  RBFX_SDK_INPUT_HEADERS RBFX_NATIVE_LIBRARY_TYPE RBFX_RUNTIME_RPATH RBFX_URHO3D_TARGET
  SHARPCPP_RBFX_USERCODE_GENERATOR_DLL)
# Optional variables. They can be undefined or empty (the installed SDK does not set them).
# RBFX_CSHARP_MSBUILD_PROPERTIES is a list of Name=Value items. The written Directory.Build.props sets them. The installed
# SDK sets SharpCppRbfxIsSdk and SharpCppRbfxManagedDir.
set_property(GLOBAL PROPERTY SHARPCPP_RBFX_USERCODE_OPTIONAL_VARIABLES
  RBFX_CSHARP_BINDINGS_PROJECTS RBFX_CSHARP_BINDINGS_PACKAGE_VERSION
  RBFX_CSHARP_MSBUILD_PROPS RBFX_CSHARP_MSBUILD_TARGETS RBFX_CSHARP_MSBUILD_PROPERTIES
  RBFX_USERCODE_ENGINE_ARGUMENTS)

define_property(GLOBAL PROPERTY SHARPCPP_RBFX_BINDINGS_TARGETS
  BRIEF_DOCS "Targets created by rbfx_csharp_add_bindings()"
  FULL_DOCS "Targets created by rbfx_csharp_add_bindings(). The top-level CMakeLists.txt adds them to the aggregate targets.")

# rbfx_csharp_bindings_native_target(<bindings target> <out_var>)
# Sets <out_var> to the target that holds the native code of the bindings target of rbfx_csharp_add_bindings(): the
# IMPLEMENTATION target (integration mode) or the bindings target itself (separate-library mode). For another target
# <out_var> is the target itself.
function(rbfx_csharp_bindings_native_target bindings out_var)
  set(native "${bindings}")
  if(TARGET ${bindings})
    get_target_property(host ${bindings} SHARPCPP_RBFX_NATIVE_TARGET)
    if(host)
      set(native "${host}")
    endif()
  endif()
  set(${out_var} "${native}" PARENT_SCOPE)
endfunction()

function(rbfx_csharp_add_bindings target)
  cmake_parse_arguments(
    WRAP
    "SEPARATE_LIBRARY"
    "NAMESPACE;ASSEMBLY_NAME;BINDINGS_CLASS;LIBRARY_NAME;SYMBOL_PREFIX;GENERATED_DIRECTORY;LIBRARY_TYPE;CSPROJ"
    "HEADERS;INCLUDE_DIRECTORIES;IMPLEMENTATION"
    ${ARGN})

  if(WRAP_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_add_bindings(${target}): unknown arguments: ${WRAP_UNPARSED_ARGUMENTS}")
  endif()
  if(NOT WRAP_HEADERS)
    message(FATAL_ERROR "rbfx_csharp_add_bindings(${target}): HEADERS is required.")
  endif()

  get_property(_required_variables GLOBAL PROPERTY SHARPCPP_RBFX_USERCODE_INPUT_VARIABLES)
  foreach(_required_variable IN LISTS _required_variables)
    if(NOT DEFINED ${_required_variable})
      message(FATAL_ERROR
        "rbfx_csharp_add_bindings(${target}): the variable ${_required_variable} is not defined. "
        "Call find_package(SharpCppRbfx CONFIG REQUIRED) in this directory or above it first.")
    endif()
  endforeach()

  # Defaults.
  if(NOT WRAP_NAMESPACE)
    string(REGEX REPLACE "[.-]" "" WRAP_NAMESPACE "${target}")
  endif()
  if(NOT WRAP_ASSEMBLY_NAME)
    set(WRAP_ASSEMBLY_NAME "${target}")
  endif()
  if(NOT WRAP_BINDINGS_CLASS)
    string(MAKE_C_IDENTIFIER "${target}" WRAP_BINDINGS_CLASS)
    if(NOT WRAP_BINDINGS_CLASS MATCHES "Bindings$")
      string(APPEND WRAP_BINDINGS_CLASS "Bindings")
    endif()
  endif()
  if(NOT WRAP_BINDINGS_CLASS MATCHES "^[A-Za-z_][A-Za-z0-9_]*$")
    message(FATAL_ERROR "rbfx_csharp_add_bindings(${target}): BINDINGS_CLASS '${WRAP_BINDINGS_CLASS}' is not a C# identifier.")
  endif()

  # The mode. Integration is the default with one IMPLEMENTATION target.
  set(WRAP_INTEGRATION OFF)
  if(WRAP_IMPLEMENTATION AND NOT WRAP_SEPARATE_LIBRARY)
    set(WRAP_INTEGRATION ON)
  endif()
  if(WRAP_INTEGRATION)
    list(LENGTH WRAP_IMPLEMENTATION implementation_count)
    if(NOT implementation_count EQUAL 1)
      message(FATAL_ERROR "rbfx_csharp_add_bindings(${target}): IMPLEMENTATION has ${implementation_count} items. The default mode needs one target. Use SEPARATE_LIBRARY for a list.")
    endif()
    if(WRAP_LIBRARY_NAME OR WRAP_LIBRARY_TYPE)
      message(FATAL_ERROR "rbfx_csharp_add_bindings(${target}): LIBRARY_NAME and LIBRARY_TYPE belong to the library of IMPLEMENTATION '${WRAP_IMPLEMENTATION}'. Remove them, or add SEPARATE_LIBRARY.")
    endif()
    set(implementation "${WRAP_IMPLEMENTATION}")
    if(NOT TARGET ${implementation})
      message(FATAL_ERROR "rbfx_csharp_add_bindings(${target}): IMPLEMENTATION '${implementation}' is not a target. Make it before this call, or add SEPARATE_LIBRARY.")
    endif()
    get_target_property(aliased ${implementation} ALIASED_TARGET)
    if(aliased)
      set(implementation "${aliased}")
    endif()
    get_target_property(implementation_imported ${implementation} IMPORTED)
    get_target_property(implementation_type ${implementation} TYPE)
    if(implementation_imported OR NOT implementation_type MATCHES "^(SHARED_LIBRARY|MODULE_LIBRARY|STATIC_LIBRARY|OBJECT_LIBRARY)$")
      message(FATAL_ERROR "rbfx_csharp_add_bindings(${target}): IMPLEMENTATION '${implementation}' is not a SHARED, MODULE, STATIC or OBJECT library of this project (type ${implementation_type}). Add SEPARATE_LIBRARY.")
    endif()
    if(implementation_type MATCHES "^(SHARED|MODULE)_LIBRARY$")
      set(WRAP_LIBRARY_TYPE "SHARED")
    elseif(RBFX_NATIVE_LIBRARY_TYPE STREQUAL "STATIC")
      # The iOS head force-loads $<TARGET_FILE:...>, which an object library does not have. The wasm head uses
      # $<TARGET_OBJECTS:...>.
      if(implementation_type STREQUAL "OBJECT_LIBRARY" AND NOT CMAKE_SYSTEM_NAME STREQUAL "Emscripten")
        message(FATAL_ERROR "rbfx_csharp_add_bindings(${target}): IMPLEMENTATION '${implementation}' is an object library. Only Emscripten supports it in the default mode. Add SEPARATE_LIBRARY, or make '${implementation}' a static library.")
      endif()
      set(WRAP_LIBRARY_TYPE "STATIC")
    else()
      message(FATAL_ERROR "rbfx_csharp_add_bindings(${target}): IMPLEMENTATION '${implementation}' is a static or object library, but RBFX_NATIVE_LIBRARY_TYPE is not STATIC. The C# code cannot load it. Add SEPARATE_LIBRARY, or make '${implementation}' a shared library.")
    endif()
    get_target_property(WRAP_LIBRARY_NAME ${implementation} OUTPUT_NAME)
    if(NOT WRAP_LIBRARY_NAME)
      set(WRAP_LIBRARY_NAME "${implementation}")
    endif()
  elseif(NOT WRAP_LIBRARY_NAME)
    set(WRAP_LIBRARY_NAME "${target}.Native")
  endif()

  # Two calls with the same assembly name share MSBuild obj and output paths.
  get_property(_existing_assemblies GLOBAL PROPERTY SHARPCPP_RBFX_WRAP_ASSEMBLIES)
  if("${WRAP_ASSEMBLY_NAME}" IN_LIST _existing_assemblies)
    message(FATAL_ERROR "rbfx_csharp_add_bindings(${target}): ASSEMBLY_NAME '${WRAP_ASSEMBLY_NAME}' is already used by another call.")
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
  # The integration mode has no library of its own. The library of IMPLEMENTATION is in the output folder of the
  # project, which is not known here, so the managed project does not check the path.
  if(WRAP_INTEGRATION)
    set(WRAP_NATIVE_LIBRARY_PATH "")
  else()
    set(WRAP_NATIVE_LIBRARY_PATH
        "${SHARPCPP_NATIVE_OUTPUT_ROOT}/$(Configuration)/${native_library_prefix}${WRAP_LIBRARY_NAME}${native_library_suffix}")
  endif()
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
  # MSBuild properties that the written Directory.Build.props sets before it imports the common props.
  # A CMake-driven build owns the build output (RbfxCSharpDotNet.cmake). The common props then use the build folder as
  # the owner, also in a build of the project alone. The MSBuild-driven build keeps the owner of the repository.
  set(root_properties ${RBFX_CSHARP_MSBUILD_PROPERTIES})
  if(RBFX_CSHARP_BUILD_MANAGED)
    list(APPEND root_properties "BuildOutputOwnerDir=${CMAKE_BINARY_DIR}/")
  endif()
  set(RBFX_CSHARP_MSBUILD_ROOT_PROPERTIES "")
  if(root_properties)
    set(RBFX_CSHARP_MSBUILD_ROOT_PROPERTIES "  <PropertyGroup>\n")
    foreach(property IN LISTS root_properties)
      if(property MATCHES "^([^=]+)=(.*)$")
        string(APPEND RBFX_CSHARP_MSBUILD_ROOT_PROPERTIES "    <${CMAKE_MATCH_1}>${CMAKE_MATCH_2}</${CMAKE_MATCH_1}>\n")
      endif()
    endforeach()
    string(APPEND RBFX_CSHARP_MSBUILD_ROOT_PROPERTIES "  </PropertyGroup>\n")
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
    set(umbrella_content "// Generated by rbfx_csharp_add_bindings(${target}). Do not edit.\n#pragma once\n")
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

  # The source-tree build of a consumer builds the generators with dotnet. In the depfile mode of
  # rbfx_csharp_add_dotnet_project, the stamps of these builds list all the sources of the generators, so a change of a
  # source of the generators runs the generate command. Else the command depends on the DLL.
  set(generator_build_dependencies "${SHARPCPP_RBFX_USERCODE_GENERATOR_DLL}")
  foreach(generator_build IN ITEMS sharpcpp-rbfx-build-generator sharpcpp-rbfx-build-usercode-generator)
    if(TARGET ${generator_build})
      get_target_property(generator_build_stamp ${generator_build} SHARPCPP_RBFX_DOTNET_STAMP)
      if(generator_build_stamp)
        list(APPEND generator_build_dependencies "${generator_build_stamp}")
      endif()
    endif()
  endforeach()

  add_custom_command(
    OUTPUT "${generated_stamp}"
    COMMAND "${CMAKE_COMMAND}"
            "-DGENERATOR_DLL=${SHARPCPP_RBFX_USERCODE_GENERATOR_DLL}"
            "-DGENERATED_STAMP=${generated_stamp}"
            "-DGENERATED_DIR=${generated_root}"
            -P "${SHARPCPP_RBFX_STALENESS_SCRIPT}"
    COMMAND "${CMAKE_COMMAND}" -E make_directory "${generated_root}"
    COMMAND "${CMAKE_COMMAND}" -E env
            "SHARPCPP_RBFX_CLANG_RESOURCE_DIRECTORY=${SHARPCPP_CLANG_RESOURCE_DIRECTORY}"
            "${DOTNET_EXECUTABLE}" "${SHARPCPP_RBFX_USERCODE_GENERATOR_DLL}"
            ${RBFX_USERCODE_ENGINE_ARGUMENTS}
            --flags-file "${RBFX_SDK_FLAGS_FILE}"
            --header "${parse_header}"
            ${include_directory_arguments}
            --assembly-name "${WRAP_ASSEMBLY_NAME}"
            --namespace "${WRAP_NAMESPACE}"
            --bindings-class "${WRAP_BINDINGS_CLASS}"
            --library-name "${WRAP_IMPORT_LIBRARY_NAME}"
            --file-stem "${file_stem}"
            --symbol-prefix "${WRAP_SYMBOL_PREFIX}"
            --sharpcpp-include "${SHARPCPP_RBFX_SHARPCPP_INCLUDE_DIRECTORY}"
            --rbfx-include "${SHARPCPP_RBFX_RBFX_INCLUDE_DIRECTORY}"
            --generated-root "${generated_root}"
            --import-binding-metadata "${RBFX_METADATA_FILE}"
            --log-file "${generator_log}"
    COMMAND "${CMAKE_COMMAND}" -E touch "${generated_stamp}"
    DEPENDS
      ${generator_build_dependencies}
      "${RBFX_SDK_FLAGS_FILE}"
      ${engine_generation_dependencies}
      ${headers}
      ${include_tree_headers}
      ${umbrella_dependencies}
      ${RBFX_SDK_INPUT_HEADERS}
    WORKING_DIRECTORY "${PROJECT_SOURCE_DIR}"
    VERBATIM
    USES_TERMINAL)

  # The generated file is not a source of the native target. The native target compiles a small file that includes it.
  # The small file exists at configure time. A generated source has no rule in the directory of the native target
  # (the IMPLEMENTATION target can be in another directory than this call). The multi-config Ninja generators then
  # fail with a missing rule, and the Xcode generator cannot find the input file. The compiler finds the generated
  # file through the include path, and the build order comes from the stamp (OBJECT_DEPENDS) and from the dependency
  # of the native target on the generate target.
  set(glue_directory "${CMAKE_CURRENT_BINARY_DIR}/${target}.glue")
  set(glue_cpp "${glue_directory}/${file_stem}.Bindings.glue.cpp")
  set(glue_content "// Generated by rbfx_csharp_add_bindings(${target}). Do not edit.\n#include \"${file_stem}.Bindings.g.cpp\"\n")
  set(existing_content "")
  if(EXISTS "${glue_cpp}")
    file(READ "${glue_cpp}" existing_content)
  endif()
  if(NOT existing_content STREQUAL glue_content)
    file(WRITE "${glue_cpp}" "${glue_content}")
  endif()

  add_custom_target(
    sharpcpp-generate-${target}
    DEPENDS "${generated_stamp}")
  # The dotnet builds that read the generated sources wait for this stamp (rbfx_csharp_dotnet_depend).
  set_property(TARGET sharpcpp-generate-${target} PROPERTY SHARPCPP_RBFX_DOTNET_STAMP "${generated_stamp}")
  if(TARGET sharpcpp-generate-rbfx)
    add_dependencies(sharpcpp-generate-${target} sharpcpp-generate-rbfx)
  endif()
  set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${generated_stamp}")

  set(generated_cpp_options
      "$<$<COMPILE_LANG_AND_ID:CXX,GNU,Clang,AppleClang>:-Wno-attributes>"
      # A unity source of the generated bindings exceeds the section limit of the default object format.
      "$<$<COMPILE_LANG_AND_ID:CXX,MSVC>:/bigobj;/wd5030>")
  set(bindings_properties
      SHARPCPP_RBFX_GENERATED_DIR "${generated_root}"
      SHARPCPP_RBFX_BINDINGS_ASSEMBLY "${WRAP_ASSEMBLY_NAME}"
      SHARPCPP_RBFX_GENERATED_STAMP "${generated_stamp}"
      SHARPCPP_RBFX_ASSEMBLY_NAME "${WRAP_ASSEMBLY_NAME}"
      SHARPCPP_RBFX_LIBRARY_NAME "${WRAP_LIBRARY_NAME}"
      SHARPCPP_RBFX_MANAGED_PROJECT "${managed_project}"
      SHARPCPP_RBFX_MANAGED_PROPS "${managed_props}"
      SHARPCPP_RBFX_MANAGED_ASSEMBLY "${SHARPCPP_MANAGED_OUTPUT_ROOT}/$<CONFIG>/${WRAP_ASSEMBLY_NAME}.dll")

  if(WRAP_INTEGRATION)
    # Integration mode: the glue is a source of the IMPLEMENTATION target. <target> only holds the properties.
    add_library(${target} INTERFACE)
    set_target_properties(${target} PROPERTIES ${bindings_properties} SHARPCPP_RBFX_NATIVE_TARGET "${implementation}")
    set_source_files_properties(
      "${glue_cpp}"
      TARGET_DIRECTORY ${implementation}
      PROPERTIES
        OBJECT_DEPENDS "${generated_stamp}"
        CXX_SCAN_FOR_MODULES OFF
        SKIP_UNITY_BUILD_INCLUSION ON
        COMPILE_OPTIONS "${generated_cpp_options}")
    add_dependencies(${implementation} sharpcpp-generate-${target})
    if(TARGET sharpcpp-rbfx-runtime)
      add_dependencies(${implementation} sharpcpp-rbfx-runtime)
    endif()
    target_sources(${implementation} PRIVATE "${glue_cpp}")
    # The glue needs C++20. This also sets the standard of the other sources of the IMPLEMENTATION target.
    target_compile_features(${implementation} PRIVATE cxx_std_20)
    target_include_directories(
      ${implementation}
      PRIVATE
        "${generated_root}"
        "${RBFX_ENGINE_UMBRELLA_DIRECTORY}"
        ${umbrella_include_directories}
        ${include_directories})
    target_link_libraries(
      ${implementation}
      PRIVATE
        SharpCpp::NativeSupport
        "${RBFX_URHO3D_TARGET}")
  else()
    set_source_files_properties(
      "${glue_cpp}"
      PROPERTIES
        OBJECT_DEPENDS "${generated_stamp}")
    add_library(${target} ${WRAP_LIBRARY_TYPE} "${glue_cpp}")
    add_dependencies(${target} sharpcpp-generate-${target})
    if(TARGET sharpcpp-rbfx-runtime)
      add_dependencies(${target} sharpcpp-rbfx-runtime)
    endif()
    target_compile_features(${target} PRIVATE cxx_std_20)
    if(MSVC)
      # A unity source of the generated bindings exceeds the section limit of the default object format.
      target_compile_options(${target} PRIVATE /bigobj /wd5030)
    endif()
    if(CMAKE_GENERATOR MATCHES "Visual Studio")
      # The Visual Studio generator compiles sources one by one without /MP.
      target_compile_options(${target} PRIVATE "$<$<COMPILE_LANG_AND_ID:CXX,MSVC>:/MP>")
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
        SHARPCPP_RBFX_NATIVE_TARGET "${target}"
        ${bindings_properties})
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
        ${WRAP_IMPLEMENTATION}
      PRIVATE
        SharpCpp::NativeSupport
        "${RBFX_URHO3D_TARGET}")
    target_compile_options(
      ${target}
      PRIVATE $<$<COMPILE_LANG_AND_ID:CXX,GNU,Clang,AppleClang>:-Wno-attributes>)
    sharpcpp_set_native_output(${target})
  endif()
  # The source-tree build of a consumer builds the user-code generator itself.
  if(TARGET sharpcpp-rbfx-build-usercode-generator)
    add_dependencies(sharpcpp-generate-${target} sharpcpp-rbfx-build-usercode-generator)
  endif()

  set_property(GLOBAL APPEND PROPERTY SHARPCPP_RBFX_BINDINGS_TARGETS "${target}")
endfunction()
