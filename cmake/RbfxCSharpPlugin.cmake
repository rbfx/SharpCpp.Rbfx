# One call per C# plugin: the project of the managed code that a native launcher loads.
#
# rbfx_csharp_add_plugin(<plugin>
#   [CSHARP_DIR <dir>]
#   [CSPROJ <csproj>]
#   [REFERENCES <wrapper targets...>]
#   [PROPERTIES <Name=Value>...])
#
# The function makes the target <plugin> (add_custom_target). It is the handle of the plugin. The name of the target is
# also the assembly name and the root namespace of the plugin. The function does not build the plugin. The packaging step
# of the application builds it (rbfx_csharp_package_application, rbfx_csharp_add_plugin_build).
#
# CSHARP_DIR    Folder with the C# sources of the plugin (all *.cs files below it, except bin and obj).
#               The default is ${CMAKE_CURRENT_SOURCE_DIR}/CSharp. The folder must exist.
# CSPROJ        The C# project that the user maintains. The function then writes no project. The project must
#               have the line <Import Project="$(SharpCppRbfxPluginProps)" />. CSPROJ and CSHARP_DIR together are
#               an error. The name <plugin> must be the name of the project file without the extension. Another
#               name is an error, because the project sets the assembly name.
# REFERENCES    Targets (from rbfx_csharp_add_wrapper) that the plugin references. The plugin references the
#               managed project of each target (the target property SHARPCPP_RBFX_MANAGED_PROJECT).
# PROPERTIES    MSBuild properties for the plugin project, as Name=Value. They come after the properties
#               that this function sets for the package of SharpCpp.Rbfx.
#
# A relative path is relative to CMAKE_CURRENT_SOURCE_DIR.
#
# Without CSPROJ the function writes these files to ${CMAKE_CURRENT_BINARY_DIR}/csharp/<plugin>/ (only when the
# content changes): <plugin>.csproj, Directory.Build.props and Directory.Build.targets (both are an empty project,
# so that the MSBuild files of a parent folder are not imported). It always writes SharpCppRbfx.Plugin.props there.
# The file imports sdk/SharpCppRbfxPlugin.props of this module folder (compiler options and the references to
# SharpCpp.Rbfx) and has a ProjectReference for each REFERENCES target. The MSBuild property SharpCppRbfxPluginProps
# is the path of the file.
#
# Target properties set on <plugin>: SHARPCPP_RBFX_PLUGIN_NAME, SHARPCPP_RBFX_PLUGIN_PROJECT,
# SHARPCPP_RBFX_PLUGIN_REFERENCES (the list of the REFERENCES targets) and SHARPCPP_RBFX_PLUGIN_MSBUILD_PROPERTIES
# (the MSBuild properties of the project, as a list of Name=Value).
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
  foreach(wrapper IN LISTS arg_REFERENCES)
    if(NOT TARGET ${wrapper})
      message(FATAL_ERROR "rbfx_csharp_add_plugin(${plugin}): REFERENCES target '${wrapper}' does not exist. Call rbfx_csharp_add_wrapper first.")
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
  set(RBFX_PLUGIN_NAME "${plugin}")
  set(RBFX_PLUGIN_DIR "${csharp_dir}")
  set(RBFX_PLUGIN_SDK_PROPS "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/sdk/SharpCppRbfxPlugin.props")
  set(RBFX_PLUGIN_PROJECT_REFERENCES)
  foreach(wrapper IN LISTS arg_REFERENCES)
    get_target_property(wrapper_project ${wrapper} SHARPCPP_RBFX_MANAGED_PROJECT)
    if(NOT wrapper_project)
      message(FATAL_ERROR "rbfx_csharp_add_plugin(${plugin}): REFERENCES target '${wrapper}' has no SHARPCPP_RBFX_MANAGED_PROJECT property.")
    endif()
    string(APPEND RBFX_PLUGIN_PROJECT_REFERENCES "    <ProjectReference Include=\"${wrapper_project}\" />\n")
  endforeach()
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
  if(NOT arg_CSPROJ)
    # An empty project stops MSBuild from importing the files of a parent folder (the repository of the user).
    foreach(build_file Directory.Build.props Directory.Build.targets)
      file(CONFIGURE OUTPUT "${plugin_directory}/${build_file}" CONTENT "<Project />\n" @ONLY NEWLINE_STYLE UNIX)
    endforeach()
  endif()

  # Properties of the C# project. The project reads them as MSBuild properties.
  if(SharpCppRbfx_IS_SDK)
    set(plugin_properties SharpCppRbfxIsSdk=true "SharpCppRbfxManagedDir=${SharpCppRbfx_MANAGED_DIR}")
  else()
    set(plugin_properties SharpCppRbfxIsSdk=false "SharpCppRbfxPackageRoot=${SharpCppRbfx_PACKAGE_ROOT}")
  endif()
  list(APPEND plugin_properties "SharpCppRbfxPluginProps=${plugin_props}")

  set(msbuild_properties ${plugin_properties} ${arg_PROPERTIES})
  add_custom_target(${plugin})
  set_target_properties(
    ${plugin}
    PROPERTIES
      SHARPCPP_RBFX_PLUGIN_NAME "${plugin}"
      SHARPCPP_RBFX_PLUGIN_PROJECT "${plugin_project}"
      SHARPCPP_RBFX_PLUGIN_REFERENCES "${arg_REFERENCES}"
      SHARPCPP_RBFX_PLUGIN_MSBUILD_PROPERTIES "${msbuild_properties}")
endfunction()
