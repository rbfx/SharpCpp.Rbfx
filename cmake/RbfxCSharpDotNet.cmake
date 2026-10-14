# Build managed projects from CMake.
#
# MSBuild is the driver of the native build when it builds SharpCpp.Rbfx.slnx: target RbfxBuildNative
# of native/SharpCpp.Rbfx.Native runs CMake under a lock. CMake is the driver when it builds these
# targets. The property RbfxNativeBuildDriver=cmake disables RbfxBuildNative in the nested MSBuild
# run, so there is no second native build and no lock wait. The native tree configured by
# MSBuild sets RBFX_CSHARP_BUILD_MANAGED=OFF, so these targets do nothing there.
#
# The generators SharpCpp.Rbfx.Generator.dll and Rbfx.UserCode.Generator.dll must already exist
# in the managed output directory. Build SharpCpp.Rbfx.slnx with dotnet first.

option(
  RBFX_CSHARP_BUILD_MANAGED
  "Build the managed projects of the examples from CMake. MSBuild sets OFF when it drives the native build."
  ON)

# rbfx_csharp_add_dotnet_project(<target>
#   PROJECT <csproj>
#   [OUTPUT_DIRECTORY <dir>]
#   [BYPRODUCTS <files>]
#   [DEPENDS <targets>])
function(rbfx_csharp_add_dotnet_project target)
  cmake_parse_arguments(PARSE_ARGV 1 arg "" "PROJECT;OUTPUT_DIRECTORY" "BYPRODUCTS;DEPENDS")
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

  set(dotnet_arguments
    build "${arg_PROJECT}"
    -c "$<CONFIG>"
    --nologo
    -p:RbfxNativeBuildDriver=cmake
    "-p:RbfxNativeBuildDir=${CMAKE_BINARY_DIR}"
    -p:UseSharedCompilation=false
    -nodeReuse:false)
  if(arg_OUTPUT_DIRECTORY)
    list(APPEND dotnet_arguments "-p:OutDir=${arg_OUTPUT_DIRECTORY}/")
  endif()

  add_custom_target(
    ${target}
    COMMAND "${DOTNET_EXECUTABLE}" ${dotnet_arguments}
    BYPRODUCTS ${arg_BYPRODUCTS}
    WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}"
    COMMENT "Building ${arg_PROJECT} with dotnet"
    VERBATIM)
  if(arg_DEPENDS)
    add_dependencies(${target} ${arg_DEPENDS})
  endif()
endfunction()

# rbfx_csharp_add_plugin(<target> PROJECT <csproj> HOST <executable target>)
#
# Builds a managed plugin next to the host executable. In the build tree of this repository, the target
# builds with the target sharpcpp-rbfx-native, so a normal CMake build produces the plugin. In a consumer
# project, the caller adds the target to the build (for example with add_dependencies).
function(rbfx_csharp_add_plugin target)
  cmake_parse_arguments(PARSE_ARGV 1 arg "" "PROJECT;HOST" "")
  if(NOT arg_PROJECT OR NOT arg_HOST)
    message(FATAL_ERROR "rbfx_csharp_add_plugin(${target}): PROJECT and HOST are required.")
  endif()
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_add_plugin(${target}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()

  get_filename_component(plugin_name "${arg_PROJECT}" NAME_WE)
  if(NOT TARGET ${arg_HOST})
    message(FATAL_ERROR "rbfx_csharp_add_plugin(${target}): HOST '${arg_HOST}' is not a target.")
  endif()
  # A generator expression with a target name does not work in BYPRODUCTS, so read the property.
  get_target_property(host_directory ${arg_HOST} RUNTIME_OUTPUT_DIRECTORY)
  if(NOT host_directory)
    message(FATAL_ERROR "rbfx_csharp_add_plugin(${target}): HOST '${arg_HOST}' has no RUNTIME_OUTPUT_DIRECTORY.")
  endif()
  # The engine bindings project reads the generated sources and the plugin loads the native wrapper.
  # The target sharpcpp-rbfx-runtime exists only in the build tree of this repository. A consumer of the
  # installed SDK has the wrapper already.
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
    DEPENDS ${plugin_dependencies})
  set_property(GLOBAL APPEND PROPERTY SHARPCPP_RBFX_PLUGIN_TARGETS ${target})
endfunction()
