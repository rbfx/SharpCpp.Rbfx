# Build managed projects from CMake.
#
# MSBuild is the driver of the native build when it builds SharpCpp.Rbfx.slnx: target RbfxBuildNative
# of native/SharpCpp.Rbfx.Native runs CMake under a lock. CMake is the driver when it builds these
# targets. The settings file of the build folder sets RbfxNativeBuildDriver=cmake. This disables RbfxBuildNative
# in the nested MSBuild run, so there is no second native build and no lock wait. The native tree configured by
# MSBuild sets RBFX_CSHARP_BUILD_MANAGED=OFF, so these targets do nothing there.
#
# Build output: CMAKE_BINARY_DIR is the owner of the .NET build output (BuildOutput.props). All managed files go to
# ${CMAKE_BINARY_DIR}/bin/<configuration>, next to the native files and the executables. CMake does not copy them.
# The intermediate files go to ${CMAKE_BINARY_DIR}/obj/<project>.

option(
  RBFX_CSHARP_BUILD_MANAGED
  "Build the managed projects of the examples from CMake. MSBuild sets OFF when it drives the native build."
  ON)

# rbfx_csharp_configurations(<variable>)
#
# Sets <variable> to the configuration set of the CMake tree: CMAKE_CONFIGURATION_TYPES for a multi-config generator, else
# CMAKE_BUILD_TYPE (Release when it is empty).
function(rbfx_csharp_configurations variable)
  get_property(is_multi_config GLOBAL PROPERTY GENERATOR_IS_MULTI_CONFIG)
  if(is_multi_config AND CMAKE_CONFIGURATION_TYPES)
    set(${variable} ${CMAKE_CONFIGURATION_TYPES} PARENT_SCOPE)
  elseif(CMAKE_BUILD_TYPE)
    set(${variable} "${CMAKE_BUILD_TYPE}" PARENT_SCOPE)
  else()
    set(${variable} Release PARENT_SCOPE)
  endif()
endfunction()

# rbfx_csharp_msbuild_arguments(<variable>)
#
# Sets <variable> to the dotnet arguments of each nested MSBuild run: "-c $<CONFIG>", "--nologo", SolutionDir and the options
# that stop the compiler server and the build nodes after the run. SolutionDir is the only global property that selects the
# build output. It makes CMAKE_BINARY_DIR the owner of the build output of all projects, also of the referenced projects.
# A build of the solution of rbfx_csharp_write_solution gets the same SolutionDir.
function(rbfx_csharp_msbuild_arguments variable)
  set(${variable}
    -c "$<CONFIG>"
    --nologo
    "-p:SolutionDir=${CMAKE_BINARY_DIR}/"
    -p:UseSharedCompilation=false
    -nodeReuse:false
    PARENT_SCOPE)
endfunction()

# rbfx_csharp_package_properties(<variable>)
#
# Sets <variable> to the MSBuild properties that select the package of SharpCpp.Rbfx, as a list of Name=Value. The installed
# SDK gives SharpCppRbfxIsSdk=true and SharpCppRbfxManagedDir. The source tree gives SharpCppRbfxIsSdk=false and
# SharpCppRbfxPackageRoot.
function(rbfx_csharp_package_properties variable)
  if(SharpCppRbfx_IS_SDK)
    set(${variable} SharpCppRbfxIsSdk=true "SharpCppRbfxManagedDir=${SharpCppRbfx_MANAGED_DIR}" PARENT_SCOPE)
  else()
    set(${variable} SharpCppRbfxIsSdk=false "SharpCppRbfxPackageRoot=${SharpCppRbfx_PACKAGE_ROOT}" PARENT_SCOPE)
  endif()
endfunction()

# rbfx_csharp_msbuild_property_elements(<variable> <Name=Value>...)
#
# Sets <variable> to one MSBuild property element for each Name=Value item, one element on each line. A generated project
# file sets its properties with these elements. They are then not global properties, so the referenced projects do not get them.
function(rbfx_csharp_msbuild_property_elements variable)
  set(elements "")
  foreach(property IN LISTS ARGN)
    if(property MATCHES "^([^=]+)=(.*)$")
      string(APPEND elements "    <${CMAKE_MATCH_1}>${CMAKE_MATCH_2}</${CMAKE_MATCH_1}>\n")
    endif()
  endforeach()
  set(${variable} "${elements}" PARENT_SCOPE)
endfunction()

# rbfx_csharp_write_build_output_files()
#
# Makes CMAKE_BINARY_DIR the owner of the .NET build output. The function writes these files there (only when the content
# changes):
#   Directory.Build.props       Sets BuildOutputOwnerDir and imports BuildOutput.props. The projects that CMake writes below
#                               the build folder use it.
#   Directory.Build.targets     An empty project. MSBuild then does not import the files of a parent folder.
#   BuildOutput.Settings.props  The settings of the owner. The root is CMAKE_BINARY_DIR, so the managed files go to
#                               bin/<configuration>. The file also sets the configuration set of the CMake tree and
#                               RbfxNativeBuildDriver=cmake, so a build of the solution in the IDE starts no native build.
# BuildOutput.props comes from the folder sdk next to this file (installed SDK), else from the folder eng of the repository.
# The module calls the function when RBFX_CSHARP_BUILD_MANAGED is ON.
function(rbfx_csharp_write_build_output_files)
  set(build_output_props "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/sdk/BuildOutput.props")
  if(NOT EXISTS "${build_output_props}")
    cmake_path(SET build_output_props NORMALIZE "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/../eng/BuildOutput.props")
  endif()
  if(NOT EXISTS "${build_output_props}")
    message(FATAL_ERROR "BuildOutput.props was not found next to ${CMAKE_CURRENT_FUNCTION_LIST_FILE}.")
  endif()
  # The managed files and the native files share one folder for each configuration.
  foreach(root IN ITEMS SHARPCPP_MANAGED_OUTPUT_ROOT SHARPCPP_NATIVE_OUTPUT_ROOT)
    if(DEFINED ${root})
      cmake_path(COMPARE "${${root}}" EQUAL "${CMAKE_BINARY_DIR}/bin" root_is_default)
      if(NOT root_is_default)
        message(FATAL_ERROR "${root} is '${${root}}'. With RBFX_CSHARP_BUILD_MANAGED=ON it must be '${CMAKE_BINARY_DIR}/bin', the output folder of the dotnet builds. "
          "An old build folder can keep the old value in its cache. Configure again with -U${root}.")
      endif()
    endif()
  endforeach()

  set(generated "<!-- Generated by rbfx_csharp_write_build_output_files() (RbfxCSharpDotNet.cmake). Do not edit. -->")
  set(content "<Project>\n  ${generated}\n  <PropertyGroup>\n")
  string(APPEND content "    <BuildOutputOwnerDir>$(MSBuildThisFileDirectory)</BuildOutputOwnerDir>\n  </PropertyGroup>\n")
  string(APPEND content "  <Import Project=\"${build_output_props}\" />\n</Project>\n")
  file(CONFIGURE OUTPUT "${CMAKE_BINARY_DIR}/Directory.Build.props" CONTENT "${content}" @ONLY NEWLINE_STYLE UNIX)
  file(CONFIGURE OUTPUT "${CMAKE_BINARY_DIR}/Directory.Build.targets" CONTENT "<Project />\n" @ONLY NEWLINE_STYLE UNIX)

  rbfx_csharp_configurations(configurations)
  set(content "<Project>\n  ${generated}\n  <PropertyGroup>\n")
  string(APPEND content "    <BuildOutputRoot>$(MSBuildThisFileDirectory)</BuildOutputRoot>\n")
  string(APPEND content "    <Configurations>${configurations}</Configurations>\n")
  string(APPEND content "    <RbfxNativeBuildDriver>cmake</RbfxNativeBuildDriver>\n")
  string(APPEND content "    <RbfxNativeBuildDir>${CMAKE_BINARY_DIR}</RbfxNativeBuildDir>\n")
  string(APPEND content "  </PropertyGroup>\n</Project>\n")
  file(CONFIGURE OUTPUT "${CMAKE_BINARY_DIR}/BuildOutput.Settings.props" CONTENT "${content}" @ONLY NEWLINE_STYLE UNIX)
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
#   [BYPRODUCTS <files>]
#   [DEPENDS <targets>]
#   [ENVIRONMENT <Name=Value>...]
#   [PROPERTIES <Name=Value>...])
#
# BYPRODUCTS are files that the build writes, normally in ${CMAKE_BINARY_DIR}/bin/$<CONFIG>.
# ENVIRONMENT sets one environment variable for each item. MSBuild reads them as properties in each project, but they are
# not global properties.
# PROPERTIES adds one MSBuild property for each item to the dotnet command line as -p:Name=Value. A global property goes
# to the referenced projects too.
#
# Skip of the dotnet run: With the generators Ninja and Makefiles (CMake 3.21 or newer), the build writes a stamp file and a
# depfile. The file SharpCpp.Rbfx.Depfile.targets (next to this file in the installed SDK, else in the folder eng) makes the
# nested MSBuild run list its inputs in the depfile: sources, project files, imported files, referenced assemblies, the
# folders of the sources and the inputs of the referenced projects. CMake starts dotnet only when one input is newer than
# the stamp file or one stamp of an upstream build (DEPENDS, see rbfx_csharp_dotnet_depend) or the file with the command line.
# The other generators (Visual Studio, Xcode) and older CMake versions run dotnet at each build.
function(rbfx_csharp_add_dotnet_project target)
  cmake_parse_arguments(PARSE_ARGV 1 arg "" "PROJECT" "BYPRODUCTS;DEPENDS;ENVIRONMENT;PROPERTIES")
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

  rbfx_csharp_msbuild_arguments(msbuild_arguments)
  set(dotnet_arguments build "${arg_PROJECT}" ${msbuild_arguments})
  foreach(property IN LISTS arg_PROPERTIES)
    list(APPEND dotnet_arguments "-p:${property}")
  endforeach()

  # The Xcode generator exports TARGETNAME to a script phase. MSBuild reads it as the property TargetName and
  # gives every project the same output name. The command removes the variable.
  # The command also sets RbfxNativeBuildDriver as an environment variable. MSBuild reads it as a property in
  # each project, also in a project that does not import the settings file of the build folder.
  set(dotnet_environment --unset=TARGETNAME RbfxNativeBuildDriver=cmake ${arg_ENVIRONMENT})

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
      "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/sdk/*.targets"
      "${CMAKE_BINARY_DIR}/Directory.Build.*"
      "${CMAKE_BINARY_DIR}/BuildOutput.Settings.props")
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
    foreach(property IN LISTS arg_PROPERTIES)
      if(property MATCHES "^CustomAfterMicrosoftCommonTargets=(.*)$")
        set(chained_targets "${CMAKE_MATCH_1}")
      endif()
    endforeach()
    # The outputs of the command are not inputs: ninja reports a cycle for a file that is both.
    set(depfile_outputs ${arg_BYPRODUCTS})
    list(JOIN depfile_outputs "%3B" depfile_outputs)
    # The values of the depfile are different for each command. They are environment variables, not global properties.
    # A global property goes to the referenced projects and is a part of the identity of a project instance. Then two
    # commands would build a shared project with different global properties into the same folder. MSBuild reads an
    # environment variable as a property and unescapes %3B.
    list(APPEND dotnet_environment
      "RbfxDepfile=${stamp}.d"
      "RbfxDepfileStamp=${stamp}"
      "RbfxDepfileProject=${depfile_project}"
      "RbfxDepfileExtraInputs=${depfile_extra_inputs}"
      "RbfxDepfileOutputs=${depfile_outputs}")
    if(chained_targets)
      list(APPEND dotnet_environment "RbfxDepfileChainTargets=${chained_targets}")
    endif()
    list(APPEND dotnet_arguments "-p:CustomAfterMicrosoftCommonTargets=${depfile_targets}")
  endif()
  set(dotnet_command "${CMAKE_COMMAND}" -E env ${dotnet_environment} "${DOTNET_EXECUTABLE}")

  if(use_depfile)
    # The Makefile generators do not run a command again when its command line changes. The command line is the content of
    # a file for each configuration, and the file is an input of the command. CMake writes the file only when the content
    # changes.
    set(command_line ${dotnet_command} ${dotnet_arguments})
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
      COMMAND ${dotnet_command} ${dotnet_arguments}
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

# rbfx_csharp_add_plugin_build(<target> {PLUGIN <plugin target> | PROJECT <csproj>} HOST <executable target>
#   [DEPENDS <targets>] [PROPERTIES <Name=Value>...])
#
# Builds a managed plugin into the folder of the host executable. PLUGIN is a target of rbfx_csharp_add_plugin. PROJECT is
# another project. The plugin and the host share the output folder ${SHARPCPP_MANAGED_OUTPUT_ROOT}/$<CONFIG>
# (${CMAKE_BINARY_DIR}/bin/$<CONFIG>). rbfx_csharp_add_launcher and rbfx_csharp_add_editor set this folder for their
# executables. Another HOST must have the same RUNTIME_OUTPUT_DIRECTORY.
# DEPENDS are targets that build before the plugin (for example the generate targets of the bindings that the plugin
# references). See rbfx_csharp_dotnet_depend for the file dependency on their stamps.
# PROPERTIES goes to rbfx_csharp_add_dotnet_project() as MSBuild properties (-p:Name=Value).
# In the build tree of this repository, the target builds with the target sharpcpp-rbfx-native, so a normal CMake build
# produces the plugin. In a consumer project, the caller adds the target to the build (for example with add_dependencies).
function(rbfx_csharp_add_plugin_build target)
  cmake_parse_arguments(PARSE_ARGV 1 arg "" "PLUGIN;PROJECT;HOST" "DEPENDS;PROPERTIES")
  if(NOT arg_HOST OR (NOT arg_PLUGIN AND NOT arg_PROJECT) OR (arg_PLUGIN AND arg_PROJECT))
    message(FATAL_ERROR "rbfx_csharp_add_plugin_build(${target}): HOST and one of PLUGIN or PROJECT are required.")
  endif()
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_add_plugin_build(${target}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()

  set(plugin_environment)
  if(arg_PLUGIN)
    get_target_property(arg_PROJECT ${arg_PLUGIN} SHARPCPP_RBFX_PLUGIN_PROJECT)
    get_target_property(plugin_environment ${arg_PLUGIN} SHARPCPP_RBFX_PLUGIN_ENVIRONMENT)
    if(NOT arg_PROJECT)
      message(FATAL_ERROR "rbfx_csharp_add_plugin_build(${target}): PLUGIN '${arg_PLUGIN}' was not made by rbfx_csharp_add_plugin.")
    endif()
    if(NOT plugin_environment)
      set(plugin_environment)
    endif()
  endif()

  get_filename_component(plugin_name "${arg_PROJECT}" NAME_WLE)
  if(NOT TARGET ${arg_HOST})
    message(FATAL_ERROR "rbfx_csharp_add_plugin_build(${target}): HOST '${arg_HOST}' is not a target.")
  endif()
  set(output_directory "${SHARPCPP_MANAGED_OUTPUT_ROOT}/$<CONFIG>")
  get_target_property(host_directory ${arg_HOST} RUNTIME_OUTPUT_DIRECTORY)
  if(RBFX_CSHARP_BUILD_MANAGED AND NOT host_directory STREQUAL output_directory)
    message(FATAL_ERROR "rbfx_csharp_add_plugin_build(${target}): the RUNTIME_OUTPUT_DIRECTORY of HOST '${arg_HOST}' is '${host_directory}'. It must be '${output_directory}', the output folder of the plugin.")
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
    BYPRODUCTS
      "${output_directory}/${plugin_name}.dll"
      "${output_directory}/${plugin_name}.runtimeconfig.json"
    DEPENDS ${plugin_dependencies}
    ENVIRONMENT ${plugin_environment}
    PROPERTIES ${arg_PROPERTIES})
  set_property(GLOBAL APPEND PROPERTY SHARPCPP_RBFX_PLUGIN_TARGETS ${target})
endfunction()

if(RBFX_CSHARP_BUILD_MANAGED)
  rbfx_csharp_write_build_output_files()
endif()
