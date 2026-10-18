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

# rbfx_csharp_dotnet_depend(<target> <upstream>...)
#
# <target> is built after each upstream target (add_dependencies). When <target> and an upstream target have a stamp file
# (the property SHARPCPP_RBFX_DOTNET_STAMP: the targets of rbfx_csharp_add_dotnet_project in the depfile mode and the
# generate targets), the stamp of <target> also depends on the stamp of the upstream target. The depfile of <target> does not
# list the outputs of the upstream targets, so without this file dependency <target> would not build again after an upstream
# build. The call must be in the directory that made <target>. An upstream target without a stamp is only an order dependency.
function(rbfx_csharp_dotnet_depend target)
  add_dependencies(${target} ${ARGN})
  get_target_property(own_stamp ${target} SHARPCPP_RBFX_DOTNET_STAMP)
  if(NOT own_stamp)
    return()
  endif()
  foreach(upstream IN LISTS ARGN)
    if(TARGET ${upstream})
      get_target_property(upstream_stamp ${upstream} SHARPCPP_RBFX_DOTNET_STAMP)
      if(upstream_stamp)
        add_custom_command(OUTPUT "${own_stamp}" APPEND DEPENDS "${upstream_stamp}")
      endif()
    endif()
  endforeach()
endfunction()

# rbfx_csharp_add_dotnet_project(<target>
#   PROJECT <csproj>
#   [OUTPUT_DIRECTORY <dir>]
#   [BYPRODUCTS <files>]
#   [DEPENDS <targets>]
#   [PROPERTIES <Name=Value>...])
#
# PROPERTIES adds one MSBuild property for each item to the dotnet command line as -p:Name=Value.
#
# Skip of the dotnet run: With the generators Ninja and Makefiles (CMake 3.21 or newer), the build writes a stamp file and a
# depfile. The file SharpCpp.Rbfx.Depfile.targets (next to this file in the installed SDK, else in the folder eng) makes the
# nested MSBuild run list its inputs in the depfile: sources, project files, imported files, referenced assemblies, the
# folders of the sources and the inputs of the referenced projects. CMake starts dotnet only when one input is newer than
# the stamp file or one stamp of an upstream build (DEPENDS, see rbfx_csharp_dotnet_depend) or the file with the command line.
# The other generators (Visual Studio, Xcode) and older CMake versions run dotnet at each build.
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
  set(dotnet_command
    "${CMAKE_COMMAND}" -E env --unset=TARGETNAME RbfxNativeBuildDriver=cmake "${DOTNET_EXECUTABLE}")

  # Generators with DEPFILE support (the generator expressions in OUTPUT and DEPFILE need CMake 3.21): the command
  # runs only when an input of the project changed. The depfile comes from eng/SharpCpp.Rbfx.Depfile.targets, which
  # the nested MSBuild run loads into each project. It lists the sources and the other inputs of the project and of
  # the projects that it references. The stamp file is the output of the command. The command touches it last. The
  # other generators (Visual Studio, Xcode) and CMake before 3.21 run the build at each build of the target.
  set(use_depfile OFF)
  if(CMAKE_VERSION VERSION_GREATER_EQUAL 3.21 AND CMAKE_GENERATOR MATCHES "Ninja|Makefiles")
    set(depfile_targets "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/sdk/SharpCpp.Rbfx.Depfile.targets")
    if(NOT EXISTS "${depfile_targets}")
      set(depfile_targets "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/../eng/SharpCpp.Rbfx.Depfile.targets")
    endif()
    if(EXISTS "${depfile_targets}")
      set(use_depfile ON)
    endif()
  endif()

  if(use_depfile)
    cmake_path(ABSOLUTE_PATH arg_PROJECT BASE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" NORMALIZE OUTPUT_VARIABLE depfile_project)
    set(stamp "${CMAKE_CURRENT_BINARY_DIR}/CMakeFiles/${target}.$<CONFIG>.stamp")
    # MSBuild files that the project imports and that MSBuild does not list: the MSBuild files of the SDK and the
    # files that CMake gives to the written Directory.Build files.
    file(GLOB depfile_extra_inputs
      "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/sdk/*.props"
      "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/sdk/*.targets")
    foreach(imported_file IN ITEMS "${RBFX_CSHARP_MSBUILD_PROPS}" "${RBFX_CSHARP_MSBUILD_TARGETS}")
      if(imported_file AND EXISTS "${imported_file}")
        list(APPEND depfile_extra_inputs "${imported_file}")
      endif()
    endforeach()
    list(REMOVE_DUPLICATES depfile_extra_inputs)
    list(JOIN depfile_extra_inputs "%3B" depfile_extra_inputs)
    # The nested run loads the file of the depfile with CustomAfterMicrosoftCommonTargets. A value of the user for this
    # property (environment or PROPERTIES) goes to the file, which imports it.
    set(chained_targets "$ENV{CustomAfterMicrosoftCommonTargets}")
    foreach(property IN LISTS RBFX_CSHARP_MSBUILD_PROPERTIES arg_PROPERTIES)
      if(property MATCHES "^CustomAfterMicrosoftCommonTargets=(.*)$")
        set(chained_targets "${CMAKE_MATCH_1}")
      endif()
    endforeach()
    # The outputs of the command are not inputs: ninja reports a cycle for a file that is both.
    set(depfile_outputs ${arg_BYPRODUCTS})
    list(JOIN depfile_outputs "%3B" depfile_outputs)
    set(dotnet_arguments_depfile
      "-p:RbfxDepfile=${stamp}.d"
      "-p:RbfxDepfileStamp=${stamp}"
      "-p:RbfxDepfileProject=${depfile_project}"
      "-p:RbfxDepfileExtraInputs=${depfile_extra_inputs}"
      "-p:RbfxDepfileOutputs=${depfile_outputs}"
      "-p:CustomAfterMicrosoftCommonTargets=${depfile_targets}")
    if(chained_targets)
      list(APPEND dotnet_arguments_depfile "-p:RbfxDepfileChainTargets=${chained_targets}")
    endif()
    # The Makefile generators do not run a command again when its command line changes. The command line is the content of
    # a file for each configuration, and the file is an input of the command. CMake writes the file only when the content
    # changes.
    set(command_line ${dotnet_command} ${dotnet_arguments} ${dotnet_arguments_depfile})
    list(JOIN command_line "\n" command_line_content)
    set(command_line_file "${CMAKE_CURRENT_BINARY_DIR}/CMakeFiles/${target}.$<CONFIG>.args")
    file(GENERATE OUTPUT "${command_line_file}" CONTENT "${command_line_content}\n")
    # The policy CMP0116 NEW makes the Ninja generators transform the depfile, so the output in it matches the output
    # of the rule. A consumer can have an old minimum version of CMake. The command records the policy when it is added.
    cmake_policy(PUSH)
    if(POLICY CMP0116)
      cmake_policy(SET CMP0116 NEW)
    endif()
    add_custom_command(
      OUTPUT "${stamp}"
      BYPRODUCTS ${arg_BYPRODUCTS}
      DEPFILE "${stamp}.d"
      COMMAND ${dotnet_command} ${dotnet_arguments} ${dotnet_arguments_depfile}
      COMMAND "${CMAKE_COMMAND}" -E touch "${stamp}"
      DEPENDS "${command_line_file}"
      WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}"
      COMMENT "Building ${arg_PROJECT} with dotnet"
      VERBATIM)
    cmake_policy(POP)
    add_custom_target(${target} DEPENDS "${stamp}")
    # Later dotnet builds and the generate commands read the stamp to find the build of this target.
    set_property(TARGET ${target} PROPERTY SHARPCPP_RBFX_DOTNET_STAMP "${stamp}")
  else()
    add_custom_target(
      ${target}
      COMMAND ${dotnet_command} ${dotnet_arguments}
      BYPRODUCTS ${arg_BYPRODUCTS}
      WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}"
      COMMENT "Building ${arg_PROJECT} with dotnet"
      VERBATIM)
  endif()
  if(arg_DEPENDS)
    rbfx_csharp_dotnet_depend(${target} ${arg_DEPENDS})
  endif()
  # The consumer build of the source tree builds the managed bindings project itself. A project that
  # references it builds after it, because two dotnet builds of the same project must not run together.
  if(TARGET sharpcpp-rbfx-build-managed AND NOT "${target}" STREQUAL "sharpcpp-rbfx-build-managed")
    rbfx_csharp_dotnet_depend(${target} sharpcpp-rbfx-build-managed)
  endif()
endfunction()

# rbfx_csharp_add_plugin_build(<target> PROJECT <csproj> HOST <executable target>
#   [DEPENDS <targets>] [PROPERTIES <Name=Value>...])
#
# DEPENDS are targets that build before the plugin (for example the generate targets of the bindings that the plugin
# references). See rbfx_csharp_dotnet_depend for the file dependency on their stamps.
# PROPERTIES goes to rbfx_csharp_add_dotnet_project() as MSBuild properties (-p:Name=Value).
# Builds a managed plugin next to the host executable. In the build tree of this repository, the target
# builds with the target sharpcpp-rbfx-native, so a normal CMake build produces the plugin. In a consumer
# project, the caller adds the target to the build (for example with add_dependencies).
function(rbfx_csharp_add_plugin_build target)
  cmake_parse_arguments(PARSE_ARGV 1 arg "" "PROJECT;HOST" "DEPENDS;PROPERTIES")
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
  set(plugin_dependencies ${arg_HOST} ${arg_DEPENDS})
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
