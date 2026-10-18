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
# Resources: the default editor looks for CoreData and EditorData in the folder of the executable. It also reads
# ResourceRoot.ini, which the engine looks for in the folder of the executable and then in the parent folders. A build tree
# has no resource folders next to the executable, so put a ResourceRoot.ini in the project folder (or in the build folder).
# Each line is <folder>=<path>, and the path is relative to the file. Example for an rbfx SDK:
#   CoreData=<path to the SDK>/bin/CoreData
#   EditorData=<path to the SDK>/bin/EditorData
# The function does not write this file.
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
  # The SDK ships EditorLibrary in the configurations Debug and RelWithDebInfo only. Without a mapping, a Release build links
  # the Debug library, and on Windows that fails (_ITERATOR_DEBUG_LEVEL and RuntimeLibrary differ). The root CMakeLists.txt
  # maps the imported configurations of Urho3D and the libraries it links. EditorLibrary is not one of them, so map it here
  # in the same way: RelWithDebInfo, else Release, else the first configuration.
  get_target_property(editor_is_imported EditorLibrary IMPORTED)
  get_target_property(editor_configurations EditorLibrary IMPORTED_CONFIGURATIONS)
  if(editor_is_imported AND editor_configurations)
    if("RELWITHDEBINFO" IN_LIST editor_configurations)
      set(editor_package_configuration RelWithDebInfo)
    elseif("RELEASE" IN_LIST editor_configurations)
      set(editor_package_configuration Release)
    else()
      list(GET editor_configurations 0 editor_package_configuration)
    endif()
    set_target_properties(EditorLibrary PROPERTIES
      MAP_IMPORTED_CONFIG_RELEASE "${editor_package_configuration}"
      MAP_IMPORTED_CONFIG_MINSIZEREL "${editor_package_configuration}")
  endif()
  # target_link_plugins reads the template folder of the engine. A find_package(Urho3D) in a function scope does not
  # leave the variable in the scope of the caller. The folder is the include folder of the rbfx SDK, or the source folder of the target Urho3D in source mode.
  if(NOT URHO3D_TEMPLATE_DIR)
    if(NOT editor_is_imported)
      # Source mode. The template folder is the source folder of the Urho3D target (rbfx/Source/Urho3D).
      get_target_property(urho3d_source_dir Urho3D SOURCE_DIR)
      if(NOT urho3d_source_dir)
        message(FATAL_ERROR "rbfx_csharp_add_editor(${name}): the target Urho3D has no SOURCE_DIR.")
      endif()
      set(URHO3D_TEMPLATE_DIR "${urho3d_source_dir}")
    else()
      if(NOT Urho3D_DIR)
        message(FATAL_ERROR "rbfx_csharp_add_editor(${name}): Urho3D_DIR is not set. Call find_package(Urho3D) first.")
      endif()
      get_filename_component(URHO3D_TEMPLATE_DIR "${Urho3D_DIR}/../../../include" ABSOLUTE)
    endif()
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
  # The default editor calls LinkedPlugins::RegisterStaticPlugins, which target_link_plugins defines. The launcher
  # function calls target_link_plugins only for a non-empty PLUGINS list, so the empty list needs the call here.
  if(NOT arg_PLUGINS)
    target_link_plugins(${name} "")
  endif()

  _rbfx_csharp_editor_host_setup(${name})
endfunction()

# Internal. Gives the executable target <name> the .NET hosting of the editor: the compile definitions, the include
# folder of RbfxCSharpEditorHost.h, the bootstrap plugin SharpCpp.Rbfx.EditorHost in the folder of the executable, and the
# copies of the native libraries. The target links SharpCpp::RbfxHost (rbfx_csharp_add_launcher does it already).
function(_rbfx_csharp_editor_host_setup name)
  target_compile_definitions(${name} PRIVATE SHARPCPP_RBFX_EDITOR_NAME="${name}" SHARPCPP_RBFX_EDITOR_HOST_NAME="SharpCpp.Rbfx.EditorHost")
  target_include_directories(${name} PRIVATE "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/launcher")
  target_link_libraries(${name} PRIVATE SharpCpp::RbfxHost)

  # The bootstrap plugin builds into the folder of the executable. The folder must be known at configure time. For a
  # target that rbfx_csharp_add_launcher made, this is already so. For another target, use the property, else the binary
  # folder of the target. A multi-config generator adds a folder for each configuration, so the folder has the
  # configuration in it, as in rbfx_csharp_add_launcher.
  get_target_property(output_dir ${name} RUNTIME_OUTPUT_DIRECTORY)
  if(NOT output_dir)
    get_target_property(output_dir ${name} BINARY_DIR)
  endif()
  get_property(is_multi_config GLOBAL PROPERTY GENERATOR_IS_MULTI_CONFIG)
  if(is_multi_config AND NOT output_dir MATCHES "\\$<")
    set(output_dir "${output_dir}/$<CONFIG>")
  endif()
  set_target_properties(${name} PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${output_dir}")
  if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
    # Urho3D must be a startup dependency of the executable (see rbfx_csharp_add_launcher).
    target_link_options(${name} PRIVATE LINKER:--no-as-needed)
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

  # The C# bindings load the native bindings from the folder of the executable.
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

# rbfx_csharp_host_editor(<target> [LAUNCHER <launcher>])
#
# Gives an existing editor executable of a project the .NET hosting of rbfx_csharp_add_editor. The editor of the project
# is an EditorApplication that the project makes with add_executable and links with EditorLibrary and its own plugins
# (target_link_plugins). Call the function after both. The function does not call target_link_plugins.
#
# The function links SharpCpp::RbfxHost, defines SHARPCPP_RBFX_EDITOR_NAME and SHARPCPP_RBFX_EDITOR_HOST_NAME, and adds
# the folder cmake/launcher to the include folders of the target. The sources of the editor include
# "RbfxCSharpEditorHost.h" and use the class Urho3D::RbfxEditorDotNetHost (see that header): Start before
# EditorApplication::Start, OnEditorStarted after it, and Stop after EditorApplication::Stop. As in
# rbfx_csharp_add_editor, the function builds SharpCpp.Rbfx.EditorHost.dll and its runtimeconfig.json into the folder of
# the executable (RUNTIME_OUTPUT_DIRECTORY of the target, else the binary folder, with the configuration folder for a
# multi-config generator) before the executable links, and copies SharpCpp::RbfxNative (and on Windows the host library
# and nethost.dll) next to the executable. All editors share the target SharpCpp.Rbfx.EditorHost.
# LAUNCHER <launcher> registers the managed plugin of the launcher (rbfx_csharp_package_application) in the editor.
# The editor then shows the C# components and the other types of that plugin. The editor only registers the plugin, and
# the project loads it. The function defines SHARPCPP_RBFX_EDITOR_MANAGED_PLUGIN (the name of the plugin), which makes
# RbfxEditorDotNetHost register <executable folder>/<plugin>.dll. The editor target then depends on <launcher>.Plugin, and
# the build of its bootstrap plugin runs after it. The conditions are:
#   - The launcher is defined before the call (add its folder before the folder of the editor).
#   - The launcher uses the hostfxr host mode, and the editor and the launcher have the same output folder.
#   - The plugins are shared libraries. A static plugin build has no shared App.Main library for the DllImport of the
#     bindings, so the editor cannot use the C# plugin then.
# Without LAUNCHER, the editor registers no plugin of the project.
# Desktop only. It is a fatal error on Android, iOS and Emscripten, and for a target that is not an executable.
function(rbfx_csharp_host_editor name)
  cmake_parse_arguments(PARSE_ARGV 1 arg "" "LAUNCHER" "")
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_host_editor(${name}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()
  if(arg_KEYWORDS_MISSING_VALUES)
    message(FATAL_ERROR "rbfx_csharp_host_editor(${name}): LAUNCHER needs a target name.")
  endif()
  if(ANDROID OR CMAKE_SYSTEM_NAME STREQUAL "iOS" OR EMSCRIPTEN)
    message(FATAL_ERROR "rbfx_csharp_host_editor(${name}): the editor is a desktop program. Android, iOS and Emscripten are not supported.")
  endif()
  if(NOT TARGET ${name})
    message(FATAL_ERROR "rbfx_csharp_host_editor(${name}): the target does not exist.")
  endif()
  get_target_property(target_type ${name} TYPE)
  if(NOT target_type STREQUAL "EXECUTABLE")
    message(FATAL_ERROR "rbfx_csharp_host_editor(${name}): the target is not an executable.")
  endif()
  if(NOT TARGET SharpCpp::RbfxHost)
    message(FATAL_ERROR "rbfx_csharp_host_editor(${name}): the target SharpCpp::RbfxHost does not exist.")
  endif()
  if(arg_LAUNCHER)
    # Check the launcher before any change to the editor target.
    if(NOT TARGET ${arg_LAUNCHER})
      message(FATAL_ERROR "rbfx_csharp_host_editor(${name}): the launcher '${arg_LAUNCHER}' does not exist. Add the folder of the launcher before the call.")
    endif()
    get_target_property(launcher_plugin_name ${arg_LAUNCHER} SHARPCPP_RBFX_MANAGED_PLUGIN)
    if(NOT launcher_plugin_name OR NOT TARGET ${arg_LAUNCHER}.Plugin)
      message(FATAL_ERROR "rbfx_csharp_host_editor(${name}): the launcher '${arg_LAUNCHER}' has no managed plugin that dotnet builds. Call rbfx_csharp_package_application for it first. The shim host mode is not supported.")
    endif()
  endif()
  _rbfx_csharp_editor_host_setup(${name})
  if(arg_LAUNCHER)
    # The editor and the launcher must share one output folder, because the editor looks for the plugin next to its own
    # executable. Use the same rule as _rbfx_csharp_editor_host_setup for the folder of the editor.
    get_target_property(editor_directory ${name} RUNTIME_OUTPUT_DIRECTORY)
    get_target_property(launcher_directory ${arg_LAUNCHER} RUNTIME_OUTPUT_DIRECTORY)
    if(editor_directory AND launcher_directory AND NOT editor_directory STREQUAL launcher_directory)
      message(FATAL_ERROR "rbfx_csharp_host_editor(${name}): the output folder of the editor '${editor_directory}' is not the output folder of the launcher '${launcher_directory}'.")
    endif()
    target_compile_definitions(${name} PRIVATE SHARPCPP_RBFX_EDITOR_MANAGED_PLUGIN="${launcher_plugin_name}")
    # The editor needs the managed plugin of the launcher. Two dotnet builds must not run together, so the build of the
    # bootstrap plugin of the editor waits for the build of the plugin of the launcher.
    add_dependencies(${name} ${arg_LAUNCHER}.Plugin ${arg_LAUNCHER}.Native)
    add_dependencies(${name}.Plugin ${arg_LAUNCHER}.Plugin)
  endif()
endfunction()
