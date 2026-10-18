# One call per launcher: the native target that hosts the .NET runtime. The launcher does not know a C# plugin.
# rbfx_csharp_add_project (RbfxCSharpProject.cmake) calls this function, and then rbfx_csharp_package_application
# (RbfxCSharpApplication.cmake) adds the plugin build and the packaging.
#
# rbfx_csharp_add_launcher(<name>
#   [SOURCES <files...>]
#   [PLUGINS <native plugin targets...>]
#   [LINK_LIBRARIES <libraries...>]
#   [CONSOLE])
#
# The target depends on the platform:
# - Desktop (Linux, Windows, macOS): the executable <name>. On Windows it is a GUI program, unless CONSOLE is set. On Linux
#   it links with --no-as-needed, so Urho3D stays a start-up dependency. Its run-time path is the folder of the executable.
# - Android: the shared library <name> (lib<name>.so). The Java activity loads it last and runs SDL_main from it.
# - Emscripten: the object library <name> from SOURCES and heads/wasm/RbfxWebMain.cpp. The definition main=rbfx_application_main
#   renames the main of URHO3D_DEFINE_APPLICATION_MAIN, because the .NET runtime defines main. The function calls
#   web_executable(<name>) of the engine.
# - iOS: the static library <name> (lib<name>.a) from SOURCES and heads/ios/RbfxIosMain.cpp. The head force-loads the
#   archive, so a source file with only static registrations stays in the link.
# The target links PRIVATE with Urho3D, SharpCpp::RbfxHost and LINK_LIBRARIES.
#
# SOURCES       Native source files of the launcher. Without SOURCES the function uses the default launcher
#               launcher/RbfxCSharpLauncher.cpp of this module folder and adds PlayerLibrary to the linked libraries.
#               The target PlayerLibrary must exist. The default launcher is a PlayerApplication. It sets the
#               engine plugins to the linked plugins and the managed plugin, starts the .NET runtime and loads the
#               managed plugin with StartDotNetPlugin (Urho3D/CSharp/DotNetPlugin.h). It stops the runtime in Stop.
# PLUGINS       Targets of the native engine plugins that the launcher loads. The function calls
#               target_link_plugins(<name> <PLUGINS>). The call is also made with an empty list for the default launcher.
# LINK_LIBRARIES Libraries that the launcher links with (PRIVATE).
# CONSOLE       Windows only. Build a console program instead of a GUI program.
#
# The function does not define SHARPCPP_RBFX_PLUGIN_NAME. rbfx_csharp_package_application defines it.
#
# Target properties set on <name>: SHARPCPP_RBFX_LAUNCHER (ON), SHARPCPP_RBFX_PLUGINS (the list of the PLUGINS targets)
# and SHARPCPP_RBFX_LAUNCHER_LINK_LIBRARIES (all libraries of LINK_LIBRARIES, with PlayerLibrary of the default launcher).
function(rbfx_csharp_add_launcher name)
  cmake_parse_arguments(PARSE_ARGV 1 arg "CONSOLE" "" "SOURCES;PLUGINS;LINK_LIBRARIES")
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_add_launcher(${name}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()
  if(CMAKE_SYSTEM_NAME MATCHES "^(tvOS|visionOS|watchOS)$")
    message(FATAL_ERROR "rbfx_csharp_add_launcher(${name}): only the desktop platforms, Android, iOS and Emscripten are supported.")
  endif()
  # Without SOURCES the launcher is the default launcher. It is a PlayerApplication, so it needs PlayerLibrary.
  set(default_launcher FALSE)
  if(NOT arg_SOURCES)
    if(NOT TARGET PlayerLibrary)
      message(FATAL_ERROR "rbfx_csharp_add_launcher(${name}): SOURCES is not set, and the default launcher needs the target PlayerLibrary, which does not exist.")
    endif()
    set(default_launcher TRUE)
    set(arg_SOURCES "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/launcher/RbfxCSharpLauncher.cpp")
    list(APPEND arg_LINK_LIBRARIES PlayerLibrary)
  endif()
  foreach(plugin IN LISTS arg_PLUGINS)
    if(NOT TARGET ${plugin})
      message(FATAL_ERROR "rbfx_csharp_add_launcher(${name}): PLUGINS target '${plugin}' does not exist.")
    endif()
  endforeach()

  if(ANDROID)
    # The application library. The Java activity loads it last and runs SDL_main from it.
    add_library(${name} SHARED ${arg_SOURCES})
  elseif(EMSCRIPTEN)
    # An object library links all its objects, as an executable does. So a source file with only static registrations
    # stays in the link. The definition renames main of URHO3D_DEFINE_APPLICATION_MAIN, because the .NET runtime
    # defines main.
    add_library(${name} OBJECT ${arg_SOURCES} "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/heads/wasm/RbfxWebMain.cpp")
    target_compile_definitions(${name} PRIVATE main=rbfx_application_main)
  elseif(CMAKE_SYSTEM_NAME STREQUAL "iOS")
    add_library(${name} STATIC ${arg_SOURCES} "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/heads/ios/RbfxIosMain.cpp")
  else()
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
    # The installed executable finds the host library and the native bindings in its own folder.
    if(APPLE)
      set_property(TARGET ${name} APPEND PROPERTY INSTALL_RPATH "@loader_path")
    elseif(UNIX)
      set_property(TARGET ${name} APPEND PROPERTY INSTALL_RPATH "$ORIGIN")
    endif()
    if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
      # Urho3D must be a startup dependency of the executable. The runtime loads the libraries of the
      # bindings later with dlopen, and then the TLS block of Urho3D does not fit into the static TLS.
      target_link_options(${name} PRIVATE LINKER:--no-as-needed)
    endif()
  endif()
  target_link_libraries(${name} PRIVATE Urho3D SharpCpp::RbfxHost ${arg_LINK_LIBRARIES})
  if(EMSCRIPTEN)
    # The web options of the engine (-pthread, -sUSE_PTHREADS=1, the file system options and the JavaScript libraries of
    # libdatachannel-wasm). CMake does not link an object library, so these options only go to the head
    # (_rbfx_csharp_collect_native_link_items reads them).
    web_executable(${name})
  endif()

  # The native plugins that the launcher loads. The default launcher needs the list of the linked plugins, also when
  # the list is empty.
  if(arg_PLUGINS OR default_launcher)
    target_link_plugins(${name} "${arg_PLUGINS}")
  endif()
  set_target_properties(
    ${name}
    PROPERTIES
      SHARPCPP_RBFX_LAUNCHER ON
      SHARPCPP_RBFX_PLUGINS "${arg_PLUGINS}"
      SHARPCPP_RBFX_LAUNCHER_LINK_LIBRARIES "${arg_LINK_LIBRARIES}")
endfunction()
