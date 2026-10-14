# One call per application: native executable that hosts the .NET runtime and loads a C# plugin.
#
# rbfx_csharp_add_application(<name>
#   SOURCES <files...>
#   PLUGIN <csproj>
#   [USERCODE <targets...>]
#   [LINK_LIBRARIES <libraries...>]
#   [RESOURCES <files...>]
#   [APPLICATION_ID <id>]
#   [CONSOLE]
#   [PROPERTIES <Name=Value>...])
#
# Desktop (Linux, Windows, macOS):
# - Creates the executable <name> from SOURCES and links it with Urho3D, SharpCpp::RbfxHost and
#   LINK_LIBRARIES. On Windows the executable is a GUI program, unless CONSOLE is set. On macOS it is
#   a plain executable, because the .NET runtime cannot be hosted inside an application bundle.
# - Defines SHARPCPP_RBFX_PLUGIN_NAME for the executable. The value is the string with the assembly
#   name of the plugin (the file name of PLUGIN without the extension). The launcher finds
#   <name>.dll and <name>.runtimeconfig.json of the plugin in the folder of the executable.
# - Builds the plugin project PLUGIN with dotnet into the folder of the executable
#   (rbfx_csharp_add_plugin) and copies the native wrapper of the C# bindings to the same folder.
#   On Windows it also copies the host library and nethost.dll.
# - The plugin target depends on the executable, so the executable cannot depend on the plugin.
#   The target <name>.CSharp is part of the default build and builds the plugin and the wrapper.
#   Build it (or the target "all") to get a complete application.
#
# SOURCES       Native source files of the launcher.
# PLUGIN        The C# project of the application plugin.
# USERCODE      Targets (from rbfx_csharp_add_usercode) that the plugin build waits for.
# LINK_LIBRARIES Libraries that the executable links with (PRIVATE).
# CONSOLE       Windows only. Build a console program instead of a GUI program.
# PROPERTIES    MSBuild properties for the plugin project, as Name=Value. They come after the properties
#               that this function sets for the package of SharpCpp.Rbfx.
# RESOURCES, APPLICATION_ID
#               Accepted and ignored on desktop. Mobile and web platforms use them (packaged resources
#               and the identifier of the application).
function(rbfx_csharp_add_application name)
  cmake_parse_arguments(
    PARSE_ARGV 1 arg "CONSOLE" "PLUGIN;APPLICATION_ID" "SOURCES;USERCODE;LINK_LIBRARIES;RESOURCES;PROPERTIES")
  if(NOT arg_SOURCES OR NOT arg_PLUGIN)
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): SOURCES and PLUGIN are required.")
  endif()
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()
  if(ANDROID OR EMSCRIPTEN OR CMAKE_SYSTEM_NAME MATCHES "^(iOS|tvOS|visionOS)$")
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): only the desktop platforms are supported.")
  endif()

  get_filename_component(plugin_name "${arg_PLUGIN}" NAME_WLE)

  set(executable_options)
  if(WIN32 AND NOT arg_CONSOLE)
    set(executable_options WIN32)
  endif()
  add_executable(${name} ${executable_options} ${arg_SOURCES})
  if(WIN32 AND arg_CONSOLE)
    # The engine entry macro then defines main instead of WinMain.
    target_compile_definitions(${name} PRIVATE URHO3D_WIN32_CONSOLE)
  endif()
  # The plugin goes to the folder of the executable. Multi-config generators add a folder for each
  # configuration, so the output folder must have the configuration in it.
  get_target_property(output_dir ${name} RUNTIME_OUTPUT_DIRECTORY)
  if(NOT output_dir)
    set(output_dir "${CMAKE_CURRENT_BINARY_DIR}")
  endif()
  get_property(is_multi_config GLOBAL PROPERTY GENERATOR_IS_MULTI_CONFIG)
  if(is_multi_config AND NOT output_dir MATCHES "\\$<")
    set(output_dir "${output_dir}/$<CONFIG>")
  endif()
  set_target_properties(${name} PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${output_dir}")
  target_link_libraries(${name} PRIVATE Urho3D SharpCpp::RbfxHost ${arg_LINK_LIBRARIES})
  target_compile_definitions(${name} PRIVATE "SHARPCPP_RBFX_PLUGIN_NAME=\"${plugin_name}\"")
  if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
    # Urho3D must be a startup dependency of the executable. The runtime loads the libraries of the
    # bindings later with dlopen, and then the TLS block of Urho3D does not fit into the static TLS.
    target_link_options(${name} PRIVATE LINKER:--no-as-needed)
  endif()

  # Properties of the C# project. The project reads them as MSBuild properties.
  if(SharpCppRbfx_IS_SDK)
    set(plugin_properties SharpCppRbfxIsSdk=true "SharpCppRbfxManagedDir=${SharpCppRbfx_MANAGED_DIR}")
  else()
    set(plugin_properties SharpCppRbfxIsSdk=false "SharpCppRbfxPackageRoot=${SharpCppRbfx_PACKAGE_ROOT}")
  endif()

  # The plugin is built with dotnet next to the executable.
  rbfx_csharp_add_plugin(
    ${name}.Plugin
    PROJECT "${arg_PLUGIN}"
    HOST ${name}
    PROPERTIES ${plugin_properties} ${arg_PROPERTIES})
  if(arg_USERCODE)
    add_dependencies(${name}.Plugin ${arg_USERCODE})
  endif()

  # The C# bindings load the native wrapper from the folder of the executable. The folder must exist
  # before the copy.
  set(copy_commands
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different
      "$<TARGET_FILE:SharpCpp::RbfxNative>" "$<TARGET_FILE_DIR:${name}>")
  if(WIN32)
    # The loader looks for the DLLs in the folder of the executable.
    get_target_property(host_type SharpCpp::RbfxHost TYPE)
    if(host_type STREQUAL "SHARED_LIBRARY")
      list(APPEND copy_commands
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different
          "$<TARGET_FILE:SharpCpp::RbfxHost>" "$<TARGET_FILE_DIR:${name}>"
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different
          "$<TARGET_FILE_DIR:SharpCpp::RbfxHost>/nethost.dll" "$<TARGET_FILE_DIR:${name}>")
    endif()
  endif()
  add_custom_target(
    ${name}.Native
    ${copy_commands}
    COMMENT "Copying the native wrapper of the C# bindings"
    VERBATIM)
  add_dependencies(${name}.Native ${name})
  if(NOT SharpCppRbfx_IS_SDK)
    add_dependencies(${name}.Native SharpCpp::RbfxNative SharpCpp::RbfxHost)
  endif()

  add_custom_target(${name}.CSharp ALL DEPENDS ${name}.Plugin ${name}.Native)
endfunction()
