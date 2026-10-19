# One call per C# plugin: the project of the managed code that a native launcher loads.
#
# rbfx_csharp_add_plugin(<plugin>
#   [CSHARP_DIR <dir>]
#   [CSPROJ <csproj>]
#   [REFERENCES <bindings targets...>]
#   [PROPERTIES <Name=Value>...])
#
# The function makes the target <plugin> (add_custom_target). It is the handle of the plugin. The name of the target is
# also the assembly name and the root namespace of the plugin. The function does not build the plugin. The packaging step
# of the application builds it (rbfx_csharp_package_application, rbfx_csharp_add_plugin_build).
#
# CSHARP_DIR    Folder with the C# sources of the plugin (all *.cs files below it, except bin and obj).
#               The default is ${CMAKE_CURRENT_SOURCE_DIR}/CSharp. The folder must exist.
# CSPROJ        The C# project that the user maintains. The function then writes no project. The project must
#               have the line <Import Project="$(SharpCppRbfxPluginProps)" />. The builds of CMake set
#               SharpCppRbfxPluginProps as an environment variable. CSPROJ and CSHARP_DIR together are
#               an error. The name <plugin> must be the name of the project file without the extension. Another
#               name is an error, because the project sets the assembly name.
# REFERENCES    Targets (from rbfx_csharp_add_bindings) that the plugin references. The plugin references the
#               managed project of each target (the target property SHARPCPP_RBFX_MANAGED_PROJECT).
# PROPERTIES    MSBuild properties for the plugin project, as Name=Value. They come after the properties
#               that this function sets for the package of SharpCpp.Rbfx.
#
# A relative path is relative to CMAKE_CURRENT_SOURCE_DIR.
#
# Without CSPROJ the function writes <plugin>.csproj to ${CMAKE_CURRENT_BINARY_DIR}/csharp/<plugin>/ (only when the
# content changes). It always writes SharpCppRbfx.Plugin.props there. The file sets the properties of the package of
# SharpCpp.Rbfx and the PROPERTIES. It imports sdk/SharpCppRbfxPlugin.props of this module folder (compiler options and
# the references to SharpCpp.Rbfx) and has a ProjectReference for each REFERENCES target.
# The plugin builds into ${CMAKE_BINARY_DIR}/bin/<configuration>, also in a build of the IDE. The written project gets
# this folder from the Directory.Build.props of the build folder (rbfx_csharp_write_build_output_files). A CSPROJ
# outside of the build folder must import a copy of BuildOutput.props from its own Directory.Build.props.
#
# Target properties set on <plugin>:
#   SHARPCPP_RBFX_PLUGIN_NAME              The name of the plugin.
#   SHARPCPP_RBFX_PLUGIN_PROJECT           The path of the project.
#   SHARPCPP_RBFX_PLUGIN_REFERENCES        The list of the REFERENCES targets.
#   SHARPCPP_RBFX_PLUGIN_MSBUILD_PROPERTIES The PROPERTIES. SharpCppRbfx.Plugin.props has them already. The heads also
#                                          write them into their project files.
#   SHARPCPP_RBFX_PLUGIN_ENVIRONMENT       The environment variables of a build of the project, as Name=Value. With
#                                          CSPROJ it has SharpCppRbfxPluginProps, the path of SharpCppRbfx.Plugin.props.

# rbfx_csharp_write_solution()
#
# Writes <CMAKE_BINARY_DIR>/<CMAKE_PROJECT_NAME>.slnx for the IDE and for "dotnet build". The solution has the managed projects
# of the plugins and of the bindings that the project makes. In the source mode it also has the projects that they reference
# (RBFX_CSHARP_BINDINGS_PROJECTS) in the folder Dependencies. The installed SDK has the assemblies of the dependencies, so the
# solution has no project for them. The configurations of the solution are the configurations of the CMake tree.
# The solution is in CMAKE_BINARY_DIR, so a build of the solution has the same SolutionDir as the dotnet builds of CMake.
# The two builds then share the build output (rbfx_csharp_write_build_output_files).
# The project files of the solution are outputs of the build tree, so the solution lives in the build tree too.
# rbfx_csharp_add_plugin schedules the function once for the end of the top level folder, so that it sees all plugins and bindings.
# The function reads the global properties that rbfx_csharp_add_plugin records, not the variables of the folder.
function(rbfx_csharp_write_solution)
  set(local_projects)
  get_property(plugins GLOBAL PROPERTY SHARPCPP_RBFX_SOLUTION_PLUGINS)
  foreach(plugin IN LISTS plugins)
    get_target_property(project ${plugin} SHARPCPP_RBFX_PLUGIN_PROJECT)
    list(APPEND local_projects "${project}")
  endforeach()
  get_property(bindings GLOBAL PROPERTY SHARPCPP_RBFX_BINDINGS_TARGETS)
  foreach(target IN LISTS bindings)
    get_target_property(project ${target} SHARPCPP_RBFX_MANAGED_PROJECT)
    if(project)
      list(APPEND local_projects "${project}")
    endif()
  endforeach()
  get_property(dependency_projects GLOBAL PROPERTY SHARPCPP_RBFX_SOLUTION_DEPENDENCIES)
  list(REMOVE_DUPLICATES local_projects)
  list(REMOVE_DUPLICATES dependency_projects)
  list(REMOVE_ITEM dependency_projects ${local_projects})

  rbfx_csharp_configurations(configurations)
  set(content "<Solution>\n  <Configurations>\n")
  foreach(configuration IN LISTS configurations)
    string(APPEND content "    <BuildType Name=\"${configuration}\" />\n")
  endforeach()
  string(APPEND content "  </Configurations>\n")
  foreach(project IN LISTS local_projects)
    string(APPEND content "  <Project Path=\"${project}\" />\n")
  endforeach()
  if(dependency_projects)
    string(APPEND content "  <Folder Name=\"/Dependencies/\">\n")
    foreach(project IN LISTS dependency_projects)
      string(APPEND content "    <Project Path=\"${project}\" />\n")
    endforeach()
    string(APPEND content "  </Folder>\n")
  endif()
  string(APPEND content "</Solution>\n")
  # file(CONFIGURE) writes only when the content changes, so the IDE does not reload an unchanged solution.
  file(CONFIGURE OUTPUT "${CMAKE_BINARY_DIR}/${CMAKE_PROJECT_NAME}.slnx" CONTENT "${content}" @ONLY NEWLINE_STYLE UNIX)
endfunction()

function(rbfx_csharp_add_plugin plugin)
  cmake_parse_arguments(PARSE_ARGV 1 arg "" "CSHARP_DIR;CSPROJ" "REFERENCES;PROPERTIES")
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_add_plugin(${plugin}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()
  if(TARGET ${plugin})
    message(FATAL_ERROR "rbfx_csharp_add_plugin(${plugin}): the target '${plugin}' exists already.")
  endif()
  if(arg_CSPROJ AND arg_CSHARP_DIR)
    message(FATAL_ERROR "rbfx_csharp_add_plugin(${plugin}): CSPROJ cannot be used with CSHARP_DIR.")
  endif()
  if(arg_CSPROJ)
    get_filename_component(csproj_name "${arg_CSPROJ}" NAME_WLE)
    if(NOT plugin STREQUAL csproj_name)
      message(FATAL_ERROR "rbfx_csharp_add_plugin(${plugin}): with CSPROJ the name of the plugin must be the name of the project file without the extension ('${csproj_name}'), because the project sets the assembly name. Use rbfx_csharp_add_plugin(${csproj_name} CSPROJ ...), or rename the project file.")
    endif()
  endif()
  foreach(bindings IN LISTS arg_REFERENCES)
    if(NOT TARGET ${bindings})
      message(FATAL_ERROR "rbfx_csharp_add_plugin(${plugin}): REFERENCES target '${bindings}' does not exist. Call rbfx_csharp_add_bindings first.")
    endif()
  endforeach()

  # The plugin project: the project of the user, or the project that this function writes.
  set(plugin_directory "${CMAKE_CURRENT_BINARY_DIR}/csharp/${plugin}")
  set(csharp_dir)
  if(arg_CSPROJ)
    cmake_path(ABSOLUTE_PATH arg_CSPROJ BASE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" NORMALIZE OUTPUT_VARIABLE plugin_project)
  else()
    if(arg_CSHARP_DIR)
      cmake_path(ABSOLUTE_PATH arg_CSHARP_DIR BASE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" NORMALIZE OUTPUT_VARIABLE csharp_dir)
    else()
      set(csharp_dir "${CMAKE_CURRENT_SOURCE_DIR}/CSharp")
    endif()
    if(NOT IS_DIRECTORY "${csharp_dir}")
      message(FATAL_ERROR "rbfx_csharp_add_plugin(${plugin}): the C# folder '${csharp_dir}' does not exist. Make it, or set CSHARP_DIR.")
    endif()
    set(plugin_project "${plugin_directory}/${plugin}.csproj")
  endif()

  # The props file of the plugin. A project of the user imports it with $(SharpCppRbfxPluginProps).
  set(plugin_props "${plugin_directory}/SharpCppRbfx.Plugin.props")
  set(plugin_environment)
  if(arg_CSPROJ)
    list(APPEND plugin_environment "SharpCppRbfxPluginProps=${plugin_props}")
  endif()
  set(RBFX_PLUGIN_NAME "${plugin}")
  set(RBFX_PLUGIN_DIR "${csharp_dir}")
  set(RBFX_PLUGIN_SDK_PROPS "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/sdk/SharpCppRbfxPlugin.props")
  set(RBFX_PLUGIN_PROJECT_REFERENCES)
  foreach(bindings IN LISTS arg_REFERENCES)
    get_target_property(bindings_project ${bindings} SHARPCPP_RBFX_MANAGED_PROJECT)
    if(NOT bindings_project)
      message(FATAL_ERROR "rbfx_csharp_add_plugin(${plugin}): REFERENCES target '${bindings}' has no SHARPCPP_RBFX_MANAGED_PROJECT property.")
    endif()
    string(APPEND RBFX_PLUGIN_PROJECT_REFERENCES "    <ProjectReference Include=\"${bindings_project}\" />\n")
  endforeach()
  # Properties of the C# project. Only the props file sets them, so they are not global properties of a build, and the
  # referenced projects do not get them.
  rbfx_csharp_package_properties(package_properties)
  rbfx_csharp_msbuild_property_elements(RBFX_PLUGIN_PROPERTIES ${package_properties} ${arg_PROPERTIES})
  # Older versions wrote empty Directory.Build files to this folder. They hide the files of the build folder.
  file(REMOVE "${plugin_directory}/Directory.Build.props" "${plugin_directory}/Directory.Build.targets")
  set(templates_directory "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/templates")
  set(plugin_files "Plugin.props.in|${plugin_props}")
  if(NOT arg_CSPROJ)
    list(APPEND plugin_files "Plugin.csproj.in|${plugin_project}")
  endif()
  foreach(plugin_file IN LISTS plugin_files)
    string(REPLACE "|" ";" plugin_file "${plugin_file}")
    list(GET plugin_file 0 template_name)
    list(GET plugin_file 1 output_path)
    set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${templates_directory}/${template_name}")
    file(READ "${templates_directory}/${template_name}" template_content)
    file(CONFIGURE OUTPUT "${output_path}" CONTENT "${template_content}" @ONLY NEWLINE_STYLE UNIX)
  endforeach()

  add_custom_target(${plugin})
  get_property(solution_scheduled GLOBAL PROPERTY SHARPCPP_RBFX_SOLUTION_SCHEDULED)
  if(NOT solution_scheduled)
    set_property(GLOBAL PROPERTY SHARPCPP_RBFX_SOLUTION_SCHEDULED TRUE)
    cmake_language(DEFER DIRECTORY "${CMAKE_SOURCE_DIR}" CALL rbfx_csharp_write_solution)
  endif()
  set_property(GLOBAL APPEND PROPERTY SHARPCPP_RBFX_SOLUTION_PLUGINS "${plugin}")
  # The solution writer runs in the top level folder. It does not see the variables of this folder.
  if(NOT SharpCppRbfx_IS_SDK)
    set_property(GLOBAL APPEND PROPERTY SHARPCPP_RBFX_SOLUTION_DEPENDENCIES ${RBFX_CSHARP_BINDINGS_PROJECTS})
  endif()
  set_target_properties(
    ${plugin}
    PROPERTIES
      SHARPCPP_RBFX_PLUGIN_NAME "${plugin}"
      SHARPCPP_RBFX_PLUGIN_PROJECT "${plugin_project}"
      SHARPCPP_RBFX_PLUGIN_REFERENCES "${arg_REFERENCES}"
      SHARPCPP_RBFX_PLUGIN_MSBUILD_PROPERTIES "${arg_PROPERTIES}"
      SHARPCPP_RBFX_PLUGIN_ENVIRONMENT "${plugin_environment}")
endfunction()
