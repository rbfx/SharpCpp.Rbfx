# Build managed projects from CMake.
#
# MSBuild is the driver of the native build when it builds SharpCpp.Rbfx.slnx: target RbfxBuildNative
# of native/SharpCpp.Rbfx.Native runs CMake under a lock. CMake is the driver when it builds these
# targets. The property RbfxNativeBuildDriver=cmake disables RbfxBuildNative in the nested MSBuild
# run, so there is no second native build and no lock wait. The native tree configured by
# MSBuild sets RBFX_CSHARP_BUILD_MANAGED=OFF, so these targets do nothing there.
#
# The generators SharpCpp.Rbfx.Generator.dll and Rbfx.UserCode.Generator.dll must exist in the managed
# output directory. A consumer tree builds them (RBFX_CSHARP_BUILD_GENERATORS in the root CMakeLists.txt).
# The top-level build of this repository needs them built first: build SharpCpp.Rbfx.slnx with dotnet.

option(
  RBFX_CSHARP_BUILD_MANAGED
  "Build the managed projects of the examples from CMake. MSBuild sets OFF when it drives the native build."
  ON)

# rbfx_csharp_msbuild_configuration_arguments(<variable>)
#
# Sets <variable> to the dotnet arguments that give the configuration set of CMake to the nested MSBuild run: "-c $<CONFIG>"
# and "-p:Configurations=<list>". The list is CMAKE_CONFIGURATION_TYPES for a multi-config generator, else CMAKE_BUILD_TYPE
# (Release when it is empty). The driving build system defines the configuration set, so the managed projects have the
# configurations of the CMake tree, and the native library paths of both sides use the same name. MSBuild reads a
# semicolon in a -p value as a separator, so the list uses %3B.
function(rbfx_csharp_msbuild_configuration_arguments variable)
  get_property(is_multi_config GLOBAL PROPERTY GENERATOR_IS_MULTI_CONFIG)
  if(is_multi_config AND CMAKE_CONFIGURATION_TYPES)
    set(configurations ${CMAKE_CONFIGURATION_TYPES})
  elseif(CMAKE_BUILD_TYPE)
    set(configurations "${CMAKE_BUILD_TYPE}")
  else()
    set(configurations Release)
  endif()
  list(JOIN configurations "%3B" configurations)
  set(${variable} -c "$<CONFIG>" "-p:Configurations=${configurations}" PARENT_SCOPE)
endfunction()

# rbfx_csharp_add_dotnet_project(<target>
#   PROJECT <csproj>
#   [OUTPUT_DIRECTORY <dir>]
#   [BYPRODUCTS <files>]
#   [DEPENDS <targets>]
#   [PROPERTIES <Name=Value>...])
#
# PROPERTIES adds one MSBuild property for each item to the dotnet command line as -p:Name=Value.
function(rbfx_csharp_add_dotnet_project target)
  cmake_parse_arguments(PARSE_ARGV 1 arg "" "PROJECT;OUTPUT_DIRECTORY" "BYPRODUCTS;DEPENDS;PROPERTIES")
  if(NOT arg_PROJECT)
    message(FATAL_ERROR "rbfx_csharp_add_dotnet_project(${target}): PROJECT is required.")
  endif()
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_add_dotnet_project(${target}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()

  if(NOT RBFX_CSHARP_BUILD_MANAGED)
    add_custom_target(${target})
    return()
  endif()

  rbfx_csharp_msbuild_configuration_arguments(configuration_arguments)
  set(dotnet_arguments
    build "${arg_PROJECT}"
    ${configuration_arguments}
    --nologo
    -p:RbfxNativeBuildDriver=cmake
    "-p:RbfxNativeBuildDir=${CMAKE_BINARY_DIR}"
    -p:UseSharedCompilation=false
    -nodeReuse:false)
  if(arg_OUTPUT_DIRECTORY)
    # Not -p:OutDir. A global OutDir goes to the referenced projects, and they build again each time the output folder
    # changes. The plugin project reads RbfxPluginOutDir in SharpCppRbfxPlugin.props and sets OutDir for itself.
    list(APPEND dotnet_arguments "-p:RbfxPluginOutDir=${arg_OUTPUT_DIRECTORY}/")
  endif()
  # RBFX_CSHARP_MSBUILD_PROPERTIES comes first, so that a PROPERTIES item of the caller wins.
  foreach(property IN LISTS RBFX_CSHARP_MSBUILD_PROPERTIES arg_PROPERTIES)
    list(APPEND dotnet_arguments "-p:${property}")
  endforeach()

  # The Xcode generator exports TARGETNAME to a script phase. MSBuild reads it as the property TargetName and
  # gives every project the same output name. The command removes the variable.
  # The command also sets RbfxNativeBuildDriver as an environment variable. MSBuild reads it as a property in
  # each project, also if a global property does not reach a project (for example after a mangled command line
  # of a generator). The global property -p:RbfxNativeBuildDriver wins when it is present.
  add_custom_target(
    ${target}
    COMMAND "${CMAKE_COMMAND}" -E env --unset=TARGETNAME RbfxNativeBuildDriver=cmake "${DOTNET_EXECUTABLE}" ${dotnet_arguments}
    BYPRODUCTS ${arg_BYPRODUCTS}
    WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}"
    COMMENT "Building ${arg_PROJECT} with dotnet"
    VERBATIM)
  if(arg_DEPENDS)
    add_dependencies(${target} ${arg_DEPENDS})
  endif()
  # The consumer build of the source tree builds the managed bindings project itself. A project that
  # references it builds after it, because two dotnet builds of the same project must not run together.
  if(TARGET sharpcpp-rbfx-build-managed AND NOT "${target}" STREQUAL "sharpcpp-rbfx-build-managed")
    add_dependencies(${target} sharpcpp-rbfx-build-managed)
  endif()
endfunction()

# rbfx_csharp_add_plugin_build(<target> PROJECT <csproj> HOST <executable target>
#   [PROPERTIES <Name=Value>...])
#
# PROPERTIES goes to rbfx_csharp_add_dotnet_project() as MSBuild properties (-p:Name=Value).
# Builds a managed plugin next to the host executable. In the build tree of this repository, the target
# builds with the target sharpcpp-rbfx-native, so a normal CMake build produces the plugin. In a consumer
# project, the caller adds the target to the build (for example with add_dependencies).
function(rbfx_csharp_add_plugin_build target)
  cmake_parse_arguments(PARSE_ARGV 1 arg "" "PROJECT;HOST" "PROPERTIES")
  if(NOT arg_PROJECT OR NOT arg_HOST)
    message(FATAL_ERROR "rbfx_csharp_add_plugin_build(${target}): PROJECT and HOST are required.")
  endif()
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_add_plugin_build(${target}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()

  get_filename_component(plugin_name "${arg_PROJECT}" NAME_WLE)
  if(NOT TARGET ${arg_HOST})
    message(FATAL_ERROR "rbfx_csharp_add_plugin_build(${target}): HOST '${arg_HOST}' is not a target.")
  endif()
  # A generator expression with a target name does not work in BYPRODUCTS, so read the property.
  get_target_property(host_directory ${arg_HOST} RUNTIME_OUTPUT_DIRECTORY)
  if(NOT host_directory)
    message(FATAL_ERROR "rbfx_csharp_add_plugin_build(${target}): HOST '${arg_HOST}' has no RUNTIME_OUTPUT_DIRECTORY.")
  endif()
  # The engine bindings project reads the generated sources and the plugin loads the native bindings.
  # The target sharpcpp-rbfx-runtime exists only in the build tree of this repository. A consumer of the
  # installed SDK has the bindings already.
  set(plugin_dependencies ${arg_HOST})
  if(TARGET sharpcpp-rbfx-runtime)
    list(APPEND plugin_dependencies sharpcpp-rbfx-runtime)
  endif()
  rbfx_csharp_add_dotnet_project(
    ${target}
    PROJECT "${arg_PROJECT}"
    OUTPUT_DIRECTORY "${host_directory}"
    BYPRODUCTS
      "${host_directory}/${plugin_name}.dll"
      "${host_directory}/${plugin_name}.runtimeconfig.json"
    DEPENDS ${plugin_dependencies}
    PROPERTIES ${arg_PROPERTIES})
  set_property(GLOBAL APPEND PROPERTY SHARPCPP_RBFX_PLUGIN_TARGETS ${target})
endfunction()
