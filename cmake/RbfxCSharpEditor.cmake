# The native target of the rbfx editor that hosts the .NET runtime. It uses rbfx_csharp_add_launcher
# (RbfxCSharpLauncher.cmake), so the platform rules of the launcher apply. Desktop only.
#
# rbfx_csharp_add_editor(<name>
#   [SOURCES <files...>]
#   [PLUGINS <native plugin targets...>]
#   [LINK_LIBRARIES <libraries...>]
#   [CONSOLE])
#
# The function does nothing, and prints a status message, when the target EditorLibrary does not exist (the rbfx SDK
# has no editor). It is a fatal error on Android, iOS and Emscripten.
#
# SOURCES       Native source files of the editor. Without SOURCES the function uses the default editor
#               launcher/RbfxCSharpEditor.cpp of this module folder. The default editor is an EditorApplication.
#               It starts the .NET runtime in Start and stops it in Stop. It registers the managed plugin
#               SharpCpp.Rbfx.EditorHost (the folder SharpCpp.Rbfx.EditorHost of this repository) with DotNetHost::RegisterPlugin.
#               The plugin is registered and not loaded, so it is not in the loaded plugins that the project saves.
#               That plugin registers the managed plugins of the folder ManagedPlugins next to the executable. The editor
#               registers it again each time that the engine creates a new PluginManager (a project opens).
#               The runtime config is SharpCpp.Rbfx.EditorHost.runtimeconfig.json in the folder of the executable.
#               The folder of the executable is <build folder>/<name>.bin, unless CMAKE_RUNTIME_OUTPUT_DIRECTORY is set.
# PLUGINS       Targets of the native engine plugins that the editor links. See rbfx_csharp_add_launcher.
# LINK_LIBRARIES Other libraries that the editor links with (PRIVATE). EditorLibrary is always linked.
# CONSOLE       Windows only. Build a console program.
#
# The function builds SharpCpp.Rbfx.EditorHost.dll and its runtimeconfig.json with dotnet, into the folder of the
# executable, before the executable links (the target <name>.Plugin).
#
# The function defines SHARPCPP_RBFX_EDITOR_NAME (the string <name>) and SHARPCPP_RBFX_EDITOR_HOST_NAME (the name of
# the bootstrap plugin) for the sources.
function(rbfx_csharp_add_editor name)
  cmake_parse_arguments(PARSE_ARGV 1 arg "CONSOLE" "" "SOURCES;PLUGINS;LINK_LIBRARIES")
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_add_editor(${name}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()
  if(ANDROID OR CMAKE_SYSTEM_NAME STREQUAL "iOS" OR EMSCRIPTEN)
    message(FATAL_ERROR "rbfx_csharp_add_editor(${name}): the editor is a desktop program. Android, iOS and Emscripten are not supported.")
  endif()
  if(NOT TARGET EditorLibrary)
    message(STATUS "rbfx_csharp_add_editor(${name}): the target EditorLibrary does not exist, so the editor is not built.")
    return()
  endif()
  # target_link_plugins reads the template folder of the engine. A find_package(Urho3D) in a function scope does not
  # leave the variable in the scope of the caller. The folder is the include folder of the rbfx SDK.
  if(NOT URHO3D_TEMPLATE_DIR)
    if(NOT Urho3D_DIR)
      message(FATAL_ERROR "rbfx_csharp_add_editor(${name}): Urho3D_DIR is not set. Call find_package(Urho3D) first.")
    endif()
    get_filename_component(URHO3D_TEMPLATE_DIR "${Urho3D_DIR}/../../../include" ABSOLUTE)
  endif()
  if(NOT arg_SOURCES)
    set(arg_SOURCES "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/launcher/RbfxCSharpEditor.cpp")
  endif()

  set(launcher_options)
  if(arg_CONSOLE)
    list(APPEND launcher_options CONSOLE)
  endif()
  # The bootstrap plugin builds into the folder of the executable, with the managed assemblies that it references. So the
  # folder of the executable is its own folder, and not the folder of the build.
  if(NOT CMAKE_RUNTIME_OUTPUT_DIRECTORY)
    set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/${name}.bin")
  endif()
  rbfx_csharp_add_launcher(${name}
    SOURCES ${arg_SOURCES}
    PLUGINS ${arg_PLUGINS}
    LINK_LIBRARIES EditorLibrary ${arg_LINK_LIBRARIES}
    ${launcher_options})
  target_compile_definitions(${name} PRIVATE SHARPCPP_RBFX_EDITOR_NAME="${name}" SHARPCPP_RBFX_EDITOR_HOST_NAME="SharpCpp.Rbfx.EditorHost")
  # The default editor calls LinkedPlugins::RegisterStaticPlugins, which target_link_plugins defines. The launcher
  # function calls target_link_plugins only for a non-empty PLUGINS list, so the empty list needs the call here.
  if(NOT arg_PLUGINS)
    target_link_plugins(${name} "")
  endif()

  # The bootstrap plugin. The repository has the sources in SharpCpp.Rbfx.EditorHost, the SDK in editor-host.
  # rbfx_csharp_add_plugin writes the project, also for the source mode (SharpCppRbfx_PACKAGE_ROOT).
  if(NOT SharpCppRbfx_IS_SDK)
    cmake_path(ABSOLUTE_PATH CMAKE_CURRENT_FUNCTION_LIST_DIR BASE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" NORMALIZE OUTPUT_VARIABLE function_dir)
    cmake_path(APPEND function_dir ".." OUTPUT_VARIABLE SharpCppRbfx_PACKAGE_ROOT)
    cmake_path(NORMAL_PATH SharpCppRbfx_PACKAGE_ROOT)
    set(host_sources "${SharpCppRbfx_PACKAGE_ROOT}/SharpCpp.Rbfx.EditorHost")
  else()
    set(host_sources "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/editor-host")
  endif()
  # One plugin target for all editors.
  if(NOT TARGET SharpCpp.Rbfx.EditorHost)
    rbfx_csharp_add_plugin(SharpCpp.Rbfx.EditorHost CSHARP_DIR "${host_sources}")
  endif()
  get_target_property(host_project SharpCpp.Rbfx.EditorHost SHARPCPP_RBFX_PLUGIN_PROJECT)
  get_target_property(host_properties SharpCpp.Rbfx.EditorHost SHARPCPP_RBFX_PLUGIN_MSBUILD_PROPERTIES)
  get_target_property(output_dir ${name} RUNTIME_OUTPUT_DIRECTORY)
  set(host_dependencies)
  if(TARGET sharpcpp-rbfx-runtime)
    list(APPEND host_dependencies sharpcpp-rbfx-runtime)
  endif()
  rbfx_csharp_add_dotnet_project(
    ${name}.Plugin
    PROJECT "${host_project}"
    OUTPUT_DIRECTORY "${output_dir}"
    BYPRODUCTS
      "${output_dir}/SharpCpp.Rbfx.EditorHost.dll"
      "${output_dir}/SharpCpp.Rbfx.EditorHost.runtimeconfig.json"
    DEPENDS ${host_dependencies}
    PROPERTIES ${host_properties})
  # All editors build the same generated project, with the same obj folder. Two dotnet builds of it must not run together,
  # so each build waits for the build of the previous editor.
  get_property(previous_plugin_build GLOBAL PROPERTY SHARPCPP_RBFX_EDITOR_LAST_PLUGIN_BUILD)
  if(previous_plugin_build)
    add_dependencies(${name}.Plugin ${previous_plugin_build})
  endif()
  set_property(GLOBAL PROPERTY SHARPCPP_RBFX_EDITOR_LAST_PLUGIN_BUILD ${name}.Plugin)
  add_dependencies(${name} ${name}.Plugin)

  # The C# bindings load the native wrapper from the folder of the executable.
  if(TARGET SharpCpp::RbfxNative)
    add_dependencies(${name} SharpCpp::RbfxNative)
    add_custom_command(
      TARGET ${name} POST_BUILD
      COMMAND "${CMAKE_COMMAND}" -E copy_if_different "$<TARGET_FILE:SharpCpp::RbfxNative>" "$<TARGET_FILE_DIR:${name}>"
      VERBATIM)
  endif()
  if(WIN32)
    # The loader looks for the DLLs in the folder of the executable.
    get_target_property(host_type SharpCpp::RbfxHost TYPE)
    if(host_type STREQUAL "SHARED_LIBRARY")
      add_custom_command(
        TARGET ${name} POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different "$<TARGET_FILE:SharpCpp::RbfxHost>" "$<TARGET_FILE_DIR:${name}>"
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different "$<TARGET_FILE_DIR:SharpCpp::RbfxHost>/nethost.dll" "$<TARGET_FILE_DIR:${name}>"
        VERBATIM)
    endif()
  endif()
endfunction()
