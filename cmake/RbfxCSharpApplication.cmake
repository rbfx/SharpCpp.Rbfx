# One call per application: native executable that hosts the .NET runtime and loads a C# plugin.
# On Android the call makes an APK: a .NET Android head starts the runtime and loads the native application.
# On Emscripten the call makes a web folder: a .NET WebAssembly head starts the runtime, and the native application
# is linked into the runtime.
# On iOS the call makes an app bundle: a .NET iOS head starts the runtime, and the native application is linked
# into the executable of the app.
#
# rbfx_csharp_add_application(<name>
#   [SOURCES <files...>]
#   [CSHARP_DIR <dir>]
#   [PLUGIN_NAME <plugin>]
#   [CSPROJ <csproj>]
#   [USERCODE <targets...>]
#   [PLUGINS <targets...>]
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
#   name of the plugin (PLUGIN_NAME, or the file name of CSPROJ without the extension). The launcher finds
#   <plugin>.dll and <plugin>.runtimeconfig.json of the plugin in the folder of the executable.
# - Builds the plugin project with dotnet into the folder of the executable
#   (rbfx_csharp_add_plugin) and copies the native wrapper of the C# bindings to the same folder.
#   On Windows it also copies the host library and nethost.dll.
# - The plugin target depends on the executable, so the executable cannot depend on the plugin.
#   The target <name>.CSharp is part of the default build and builds the plugin and the wrapper.
#   Build it (or the target "all") to get a complete application.
#
# SOURCES       Native source files of the launcher. Without SOURCES the function uses the default launcher
#               launcher/RbfxCSharpLauncher.cpp of this module folder and adds PlayerLibrary to LINK_LIBRARIES.
#               The target PlayerLibrary must exist. The default launcher is a PlayerApplication. It sets the
#               engine plugins to the linked plugins and the managed plugin, starts the .NET runtime and loads the
#               managed plugin with StartDotNetPlugin (Urho3D/CSharp/DotNetPlugin.h). It stops the runtime in Stop.
# CSHARP_DIR    Folder with the C# sources of the plugin (all *.cs files below it, except bin and obj).
#               The default is ${CMAKE_CURRENT_SOURCE_DIR}/CSharp. The folder must exist.
# PLUGIN_NAME   Assembly name and root namespace of the plugin. The default is <name>.Managed.
# CSPROJ        The C# project that the user maintains. The function then writes no project. The project must
#               have the line <Import Project="$(SharpCppRbfxPluginProps)" />. CSPROJ and CSHARP_DIR or
#               PLUGIN_NAME together are an error.
# USERCODE      Targets (from rbfx_csharp_add_usercode) that the plugin build waits for. The plugin references the
#               managed project of each target (the target property SHARPCPP_RBFX_MANAGED_PROJECT).
# PLUGINS       Targets of the native engine plugins that the launcher loads. The function calls
#               target_link_plugins(<name> <PLUGINS>) on every platform. A static plugin goes into the
#               application. A shared plugin is a separate library. On Android the head packs each shared or
#               module target of PLUGINS, in the given order, and then each shared library of USERCODE.
#               The packing is before lib<name>.so and without duplicates.
#               On desktop the function copies each shared library of USERCODE to the folder of the executable.
# LINK_LIBRARIES Libraries that the executable links with (PRIVATE).
# CONSOLE       Windows only. Build a console program instead of a GUI program.
# PROPERTIES    MSBuild properties for the plugin project, as Name=Value. They come after the properties
#               that this function sets for the package of SharpCpp.Rbfx.
#
# Without CSPROJ the function writes these files to ${CMAKE_CURRENT_BINARY_DIR}/csharp/<plugin>/ (only when the
# content changes): <plugin>.csproj, Directory.Build.props and Directory.Build.targets (both are an empty project,
# so that the MSBuild files of a parent folder are not imported). It always writes SharpCppRbfx.Plugin.props there.
# The file imports sdk/SharpCppRbfxPlugin.props of this module folder (compiler options and the references to
# SharpCpp.Rbfx) and has a ProjectReference for each USERCODE target. The MSBuild property SharpCppRbfxPluginProps
# is the path of the file.
#
# Target properties set on <name>: SHARPCPP_RBFX_PLUGIN_NAME, SHARPCPP_RBFX_PLUGIN_PROJECT,
# SHARPCPP_RBFX_USERCODE (the list of the USERCODE targets), SHARPCPP_RBFX_PLUGINS (the list of the PLUGINS targets) and
# SHARPCPP_RBFX_APPLICATION_OUTPUT (the product of
# the application: <name>.apk on Android, the web folder on Emscripten, <name>.app on iOS, empty on desktop).
# RESOURCES, APPLICATION_ID
#               Accepted and ignored on desktop. Android and iOS use them, Emscripten uses RESOURCES (see below).
#
# Android (single-configuration generator, NDK toolchain, SharpCpp.Rbfx built in shim mode):
# - Creates the shared library <name> (lib<name>.so) from SOURCES. URHO3D_DEFINE_APPLICATION_MAIN exports
#   SDL_main from it. The library links with Urho3D, SharpCpp::RbfxHost and LINK_LIBRARIES.
# - Generates the .NET Android head project in <build>/heads/<name>/ from the templates in heads/android/
#   next to this file. The head references the plugin project, packs the native libraries (the C++ runtime, Urho3D,
#   the wrapper, the host library, the shared libraries in LINK_LIBRARIES and lib<name>.so) and the
#   RESOURCES as assets. A file in RESOURCES becomes an asset with its file name. A folder becomes a folder
#   of assets with its name. A generated file in RESOURCES must come from a custom command in the
#   top-level CMakeLists.txt (for example create_pak of the engine).
# - The application class of the head runs the module initializer of the bindings before the activity
#   starts SDL_main. So DotNetHost::Initialize in the native code finds the runtime, as on desktop.
#   The plugin assembly and the SharpCpp assemblies are trimmer roots.
# - The target <name>.CSharp builds the head with dotnet and copies the debug-signed APK to
#   <name>.apk in the output folder of the library (LIBRARY_OUTPUT_DIRECTORY).
# - APPLICATION_ID is the Android package name. The default is org.rbfx.<name in lower case>.
# - RBFX_CSHARP_ANDROID_SDK_DIR and RBFX_CSHARP_JAVA_SDK_DIR (cache variables, default from the
#   environment variables ANDROID_HOME and JAVA_HOME) go to dotnet as AndroidSdkDirectory and
#   JavaSdkDirectory. RBFX_CSHARP_ANDROID_JAVA_DIR is the folder with the SDL Java sources
#   (org/libsdl/app). The default is the folder in the engine SDK.
#
# Emscripten (single-configuration generator, Emscripten toolchain, static engine and SharpCpp.Rbfx in shim mode):
# - The browser has no hostfxr. The .NET WebAssembly head is the entry. The application, the engine, the wrapper
#   and the host library are static code in dotnet.native.wasm (NativeFileReference items of the head).
# - Creates the object library <name> from SOURCES and heads/wasm/RbfxWebMain.cpp. The .NET runtime has its own
#   main, so SOURCES compile with main=rbfx_application_main. RbfxWebMain.cpp exports it as the C function
#   rbfx_application_start. The library links with Urho3D, SharpCpp::RbfxHost and LINK_LIBRARIES.
# - Generates the head project in <build>/heads/<name>/ from the templates in heads/wasm/ next to this file.
#   At the end of the configure step, CMake collects the static libraries, the objects and the link flags of the
#   link closure of <name> and of SharpCpp::RbfxNative into NativeItems.props of the head. The head references the plugin project.
# - main.js of the head starts the runtime, writes the RESOURCES into the file system of Emscripten and runs
#   Program.Main. Program.Main runs the module initializer of the bindings, so DotNetHost::Initialize finds the
#   runtime. Then main.js calls rbfx_application_start on the browser thread. The engine main loop then runs there.
# - A file in RESOURCES becomes a file in the root folder with its file name. A folder becomes a folder with its
#   name. A generated file in RESOURCES must come from a custom command in the top-level CMakeLists.txt.
# - The target <name>.CSharp publishes the head with dotnet (Release) and copies the web folder (index.html, main.js,
#   _framework and the RESOURCES) to <name>/ in the runtime output folder (CMAKE_RUNTIME_OUTPUT_DIRECTORY).
#   The runtime uses threads, so the web server must send the headers of cross-origin isolation.
#
# iOS (CMAKE_SYSTEM_NAME iOS, device arm64, single-configuration generator, static engine SDK ios-clang-arm64-lib,
# SharpCpp.Rbfx in shim mode):
# - An iOS app has one executable, and the .NET iOS SDK links it. So the .NET iOS head is the entry. The application,
#   the engine, the wrapper and the host library are static code in the executable (NativeReference items of the head).
# - Creates the static library <name> (lib<name>.a) from SOURCES and heads/ios/RbfxIosMain.cpp. On iOS
#   URHO3D_DEFINE_APPLICATION_MAIN defines SDL_main. RbfxIosMain.cpp exports the C function rbfx_application_start.
#   It gives SDL_main to SDL_UIKitRunApp. The library links with Urho3D, SharpCpp::RbfxHost and LINK_LIBRARIES.
# - Generates the head project in <build>/heads/<name>/ from the templates in heads/ios/ next to this file. At the end
#   of the configure step, CMake collects the static libraries and the link flags of the link closure of <name>, of
#   SharpCpp::RbfxNative and of USERCODE into NativeItems.props of the head. The head references the plugin project.
# - Program.Main of the head runs the module initializer of the bindings, so DotNetHost::Initialize finds the runtime.
#   Then it calls rbfx_application_start. SDL starts UIKit and calls SDL_main when the application is ready.
# - The application library, the wrapper, the host library and the USERCODE libraries are force-loaded: the managed
#   side finds their exports with __Internal. The other libraries are not force-loaded. The engine library has the
#   main of SDL, and the executable has the main of .NET.
# - A file in RESOURCES becomes a file in the root folder of the app bundle with its file name. A folder becomes a
#   folder with its name. A generated file in RESOURCES must come from a custom command in the top-level CMakeLists.txt.
# - APPLICATION_ID is the bundle identifier. The default is org.rbfx.<name in lower case>.
# - The target <name>.CSharp builds the head with dotnet and copies the app bundle to <name>.app in the runtime output
#   folder (CMAKE_RUNTIME_OUTPUT_DIRECTORY). The head does not sign the app, unless the MSBuild properties ask for it
#   (EnableCodeSigning, CodesignKey and CodesignProvision in PROPERTIES or RBFX_CSHARP_MSBUILD_PROPERTIES).
# Sets the target properties of the application. It reads the variables plugin_name, plugin_project,
# arg_USERCODE and arg_PLUGINS of the caller.
function(_rbfx_csharp_set_plugin_properties name)
  set_target_properties(
    ${name}
    PROPERTIES
      SHARPCPP_RBFX_PLUGIN_NAME "${plugin_name}"
      SHARPCPP_RBFX_PLUGIN_PROJECT "${plugin_project}"
      SHARPCPP_RBFX_USERCODE "${arg_USERCODE}"
      SHARPCPP_RBFX_PLUGINS "${arg_PLUGINS}")
endfunction()

# Links the native plugins into the application. It reads the variables arg_PLUGINS and default_launcher of the caller.
# The default launcher needs the list of the linked plugins, also when the list is empty.
function(_rbfx_csharp_link_plugins name)
  if(arg_PLUGINS OR default_launcher)
    target_link_plugins(${name} "${arg_PLUGINS}")
  endif()
endfunction()

function(rbfx_csharp_add_application name)
  cmake_parse_arguments(
    PARSE_ARGV 1 arg "CONSOLE" "CSHARP_DIR;PLUGIN_NAME;CSPROJ;APPLICATION_ID" "SOURCES;USERCODE;PLUGINS;LINK_LIBRARIES;RESOURCES;PROPERTIES")
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()
  # Without SOURCES the application uses the default launcher. It is a PlayerApplication, so it needs PlayerLibrary.
  set(default_launcher FALSE)
  if(NOT arg_SOURCES)
    if(NOT TARGET PlayerLibrary)
      message(FATAL_ERROR "rbfx_csharp_add_application(${name}): SOURCES is not set, and the default launcher needs the target PlayerLibrary, which does not exist.")
    endif()
    set(default_launcher TRUE)
    set(arg_SOURCES "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/launcher/RbfxCSharpLauncher.cpp")
    list(APPEND arg_LINK_LIBRARIES PlayerLibrary)
  endif()
  if(CMAKE_SYSTEM_NAME MATCHES "^(tvOS|visionOS|watchOS)$")
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): only the desktop platforms, Android, iOS and Emscripten are supported.")
  endif()

  if(arg_CSPROJ AND (arg_CSHARP_DIR OR arg_PLUGIN_NAME))
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): CSPROJ cannot be used with CSHARP_DIR or PLUGIN_NAME.")
  endif()
  foreach(usercode IN LISTS arg_USERCODE)
    if(NOT TARGET ${usercode})
      message(FATAL_ERROR "rbfx_csharp_add_application(${name}): USERCODE target '${usercode}' does not exist. Call rbfx_csharp_add_usercode first.")
    endif()
  endforeach()

  foreach(plugin IN LISTS arg_PLUGINS)
    if(NOT TARGET ${plugin})
      message(FATAL_ERROR "rbfx_csharp_add_application(${name}): PLUGINS target '${plugin}' does not exist.")
    endif()
  endforeach()

  # The plugin project: the project of the user, or the project that this function writes.
  if(arg_CSPROJ)
    cmake_path(ABSOLUTE_PATH arg_CSPROJ BASE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" NORMALIZE OUTPUT_VARIABLE plugin_project)
    get_filename_component(plugin_name "${plugin_project}" NAME_WLE)
  endif()
  set(plugin_directory)
  if(NOT arg_CSPROJ)
    if(arg_PLUGIN_NAME)
      set(plugin_name "${arg_PLUGIN_NAME}")
    else()
      set(plugin_name "${name}.Managed")
    endif()
    if(arg_CSHARP_DIR)
      cmake_path(ABSOLUTE_PATH arg_CSHARP_DIR BASE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" NORMALIZE OUTPUT_VARIABLE csharp_dir)
    else()
      set(csharp_dir "${CMAKE_CURRENT_SOURCE_DIR}/CSharp")
    endif()
    if(NOT IS_DIRECTORY "${csharp_dir}")
      message(FATAL_ERROR "rbfx_csharp_add_application(${name}): the C# folder '${csharp_dir}' does not exist. Make it, or set CSHARP_DIR.")
    endif()
    set(plugin_directory "${CMAKE_CURRENT_BINARY_DIR}/csharp/${plugin_name}")
    set(plugin_project "${plugin_directory}/${plugin_name}.csproj")
  endif()

  # The props file of the plugin. A project of the user imports it with $(SharpCppRbfxPluginProps).
  set(plugin_props_directory "${CMAKE_CURRENT_BINARY_DIR}/csharp/${plugin_name}")
  set(plugin_props "${plugin_props_directory}/SharpCppRbfx.Plugin.props")
  set(RBFX_PLUGIN_APPLICATION "${name}")
  set(RBFX_PLUGIN_NAME "${plugin_name}")
  set(RBFX_PLUGIN_DIR "${csharp_dir}")
  set(RBFX_PLUGIN_SDK_PROPS "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/sdk/SharpCppRbfxPlugin.props")
  set(RBFX_PLUGIN_PROJECT_REFERENCES)
  foreach(usercode IN LISTS arg_USERCODE)
    get_target_property(usercode_project ${usercode} SHARPCPP_RBFX_MANAGED_PROJECT)
    if(NOT usercode_project)
      message(FATAL_ERROR "rbfx_csharp_add_application(${name}): USERCODE target '${usercode}' has no SHARPCPP_RBFX_MANAGED_PROJECT property.")
    endif()
    string(APPEND RBFX_PLUGIN_PROJECT_REFERENCES "    <ProjectReference Include=\"${usercode_project}\" />\n")
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

  if(ANDROID)
    _rbfx_csharp_add_android_application(${name})
    _rbfx_csharp_set_plugin_properties(${name})
    _rbfx_csharp_link_plugins(${name})
    return()
  endif()
  if(EMSCRIPTEN)
    _rbfx_csharp_add_wasm_application(${name})
    _rbfx_csharp_set_plugin_properties(${name})
    _rbfx_csharp_link_plugins(${name})
    return()
  endif()
  if(CMAKE_SYSTEM_NAME STREQUAL "iOS")
    _rbfx_csharp_add_ios_application(${name})
    _rbfx_csharp_set_plugin_properties(${name})
    _rbfx_csharp_link_plugins(${name})
    return()
  endif()

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
  # The installed executable finds the host library and the native wrapper in its own folder.
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

  # The plugin is built with dotnet next to the executable.
  rbfx_csharp_add_plugin(
    ${name}.Plugin
    PROJECT "${plugin_project}"
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
  # The shared libraries of the USERCODE targets and the shared or module libraries of the PLUGINS targets also go
  # to the folder of the executable.
  set(usercode_libraries)
  foreach(usercode IN LISTS arg_USERCODE arg_PLUGINS)
    get_target_property(usercode_type ${usercode} TYPE)
    if(usercode_type STREQUAL "SHARED_LIBRARY" OR (usercode_type STREQUAL "MODULE_LIBRARY" AND usercode IN_LIST arg_PLUGINS))
      list(APPEND usercode_libraries ${usercode})
      list(APPEND copy_commands
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different
          "$<TARGET_FILE:${usercode}>" "$<TARGET_FILE_DIR:${name}>")
    endif()
  endforeach()
  add_custom_target(
    ${name}.Native
    ${copy_commands}
    COMMENT "Copying the native wrapper of the C# bindings"
    VERBATIM)
  add_dependencies(${name}.Native ${name} ${usercode_libraries})
  if(NOT SharpCppRbfx_IS_SDK)
    add_dependencies(${name}.Native SharpCpp::RbfxNative SharpCpp::RbfxHost)
  endif()

  # The desktop application is the executable in its folder, so there is no separate output.
  set_target_properties(${name} PROPERTIES SHARPCPP_RBFX_APPLICATION_OUTPUT "")
  add_custom_target(${name}.CSharp ALL DEPENDS ${name}.Plugin ${name}.Native)
  _rbfx_csharp_set_plugin_properties(${name})
  _rbfx_csharp_link_plugins(${name})
endfunction()

# The Android part of rbfx_csharp_add_application. It reads the variables arg_*, plugin_name and
# plugin_properties of the caller.
# Writes the trimmer root items of the USERCODE targets into RBFX_HEAD_USERCODE_ROOTS (a head project fragment). Only native code
# calls the entry points of these managed assemblies, so the trimmer must keep them.
function(_rbfx_csharp_usercode_trim_roots out_variable)
  set(items "")
  foreach(usercode IN LISTS ARGN)
    if(NOT TARGET ${usercode})
      message(FATAL_ERROR "rbfx_csharp_add_application: USERCODE target '${usercode}' does not exist. Call rbfx_csharp_add_usercode first.")
    endif()
    get_target_property(assembly ${usercode} SHARPCPP_RBFX_USERCODE_ASSEMBLY)
    if(assembly)
      string(APPEND items "    <TrimmerRootAssembly Include=\"${assembly}\" RootMode=\"All\" />\n")
    endif()
  endforeach()
  set(${out_variable} "${items}" PARENT_SCOPE)
endfunction()

function(_rbfx_csharp_add_android_application name)
  get_property(is_multi_config GLOBAL PROPERTY GENERATOR_IS_MULTI_CONFIG)
  if(is_multi_config)
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): Android needs a single-configuration generator (for example Ninja).")
  endif()

  # The ABI of the NDK and the runtime identifier of .NET.
  if(CMAKE_ANDROID_ARCH_ABI)
    set(abi "${CMAKE_ANDROID_ARCH_ABI}")
  else()
    set(abi "${ANDROID_ABI}")
  endif()
  if(abi STREQUAL "arm64-v8a")
    set(rid android-arm64)
  elseif(abi STREQUAL "armeabi-v7a")
    set(rid android-arm)
  elseif(abi STREQUAL "x86_64")
    set(rid android-x64)
  elseif(abi STREQUAL "x86")
    set(rid android-x86)
  else()
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): the Android ABI '${abi}' is not known.")
  endif()

  # .NET for Android supports API level 21 and higher.
  set(min_api 21)
  if(CMAKE_SYSTEM_VERSION GREATER min_api)
    set(min_api "${CMAKE_SYSTEM_VERSION}")
  endif()

  if(arg_APPLICATION_ID)
    set(application_id "${arg_APPLICATION_ID}")
  else()
    string(TOLOWER "${name}" application_id)
    string(REGEX REPLACE "[^a-z0-9_]" "_" application_id "${application_id}")
    if(application_id MATCHES "^[0-9_]")
      set(application_id "app${application_id}")
    endif()
    set(application_id "org.rbfx.${application_id}")
  endif()

  # The SDL Java sources. The engine SDK has them in share/Urho3D/Android/java. An engine source tree
  # has them in the SDL source folder.
  set(java_candidates
    "${Urho3D_DIR}/../Android/java"
    "${Urho3D_PACKAGE_ROOT}/share/Urho3D/Android/java"
    "${Urho3D_DIR}/../Source/ThirdParty/SDL/android-project/app/src/main/java")
  set(java_default "")
  foreach(candidate IN LISTS java_candidates)
    if(EXISTS "${candidate}/org/libsdl/app/SDLActivity.java")
      get_filename_component(java_default "${candidate}" ABSOLUTE)
      break()
    endif()
  endforeach()
  set(RBFX_CSHARP_ANDROID_JAVA_DIR "${java_default}" CACHE PATH "Folder with the SDL Java sources (org/libsdl/app)")
  if(NOT EXISTS "${RBFX_CSHARP_ANDROID_JAVA_DIR}/org/libsdl/app/SDLActivity.java")
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): the SDL Java sources are not found. Set RBFX_CSHARP_ANDROID_JAVA_DIR.")
  endif()
  set(RBFX_CSHARP_ANDROID_SDK_DIR "$ENV{ANDROID_HOME}" CACHE PATH "Android SDK for the .NET Android head (AndroidSdkDirectory)")
  set(RBFX_CSHARP_JAVA_SDK_DIR "$ENV{JAVA_HOME}" CACHE PATH "JDK for the .NET Android head (JavaSdkDirectory)")

  # The application library. The Java activity loads it last and runs SDL_main from it.
  add_library(${name} SHARED ${arg_SOURCES})
  target_link_libraries(${name} PRIVATE Urho3D SharpCpp::RbfxHost ${arg_LINK_LIBRARIES})
  target_compile_definitions(${name} PRIVATE "SHARPCPP_RBFX_PLUGIN_NAME=\"${plugin_name}\"")

  # The native libraries of the APK, in the load order.
  set(libraries)
  if(ANDROID_STL STREQUAL "c++_shared" OR CMAKE_ANDROID_STL_TYPE STREQUAL "c++_shared")
    set(stl_library "")
    foreach(triple IN ITEMS "${CMAKE_ANDROID_ARCH_TRIPLE}" "${ANDROID_TOOLCHAIN_NAME}" "${CMAKE_LIBRARY_ARCHITECTURE}")
      foreach(sysroot IN ITEMS "${CMAKE_SYSROOT}" "${ANDROID_TOOLCHAIN_ROOT}/sysroot")
        if(triple AND sysroot AND EXISTS "${sysroot}/usr/lib/${triple}/libc++_shared.so")
          set(stl_library "${sysroot}/usr/lib/${triple}/libc++_shared.so")
          break()
        endif()
      endforeach()
      if(stl_library)
        break()
      endif()
    endforeach()
    if(NOT stl_library)
      message(FATAL_ERROR "rbfx_csharp_add_application(${name}): libc++_shared.so is not found in the NDK sysroot.")
    endif()
    list(APPEND libraries "${stl_library}")
  endif()
  list(APPEND libraries "$<TARGET_FILE:Urho3D>" "$<TARGET_FILE:SharpCpp::RbfxNative>")
  set(library_targets Urho3D SharpCpp::RbfxNative)
  get_target_property(host_type SharpCpp::RbfxHost TYPE)
  if(host_type STREQUAL "SHARED_LIBRARY")
    list(APPEND libraries "$<TARGET_FILE:SharpCpp::RbfxHost>")
    list(APPEND library_targets SharpCpp::RbfxHost)
  endif()
  foreach(library IN LISTS arg_LINK_LIBRARIES)
    if(TARGET ${library} AND NOT library IN_LIST library_targets)
      get_target_property(library_type ${library} TYPE)
      if(library_type MATCHES "^(SHARED|MODULE)_LIBRARY$")
        list(APPEND libraries "$<TARGET_FILE:${library}>")
        list(APPEND library_targets ${library})
      endif()
    endif()
  endforeach()
  # The native plugins and the shared libraries of the USERCODE targets. The Java activity loads them before the
  # application library.
  set(plugin_targets)
  foreach(library IN LISTS arg_PLUGINS arg_USERCODE)
    if(TARGET ${library} AND NOT library IN_LIST library_targets)
      get_target_property(library_type ${library} TYPE)
      if(library_type MATCHES "^(SHARED|MODULE)_LIBRARY$")
        list(APPEND libraries "$<TARGET_FILE:${library}>")
        list(APPEND library_targets ${library})
        list(APPEND plugin_targets ${library})
      endif()
    endif()
  endforeach()
  list(APPEND libraries "$<TARGET_FILE:${name}>")

  # The head build copies the libraries into one folder. A copy has the content of a symbolic link, and
  # the APK must not have links.
  set(head_dir "${CMAKE_BINARY_DIR}/heads/${name}")
  set(library_dir "${head_dir}/libs/${abi}")
  set(items "")
  set(java_libraries "")
  foreach(library IN LISTS libraries)
    string(APPEND items "    <AndroidNativeLibrary Include=\"${library_dir}/$<PATH:GET_FILENAME,${library}>\" Abi=\"${abi}\" />\n")
    if(java_libraries)
      string(APPEND java_libraries ", ")
    endif()
    string(APPEND java_libraries "\"$<PATH:GET_FILENAME,${library}>\"")
  endforeach()
  set(resource_files)
  foreach(resource IN LISTS arg_RESOURCES)
    get_filename_component(resource "${resource}" ABSOLUTE BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}")
    get_filename_component(resource_name "${resource}" NAME)
    if(IS_DIRECTORY "${resource}")
      string(APPEND items "    <AndroidAsset Include=\"${resource}/**/*\" Link=\"Assets/${resource_name}/%(RecursiveDir)%(Filename)%(Extension)\" />\n")
    else()
      string(APPEND items "    <AndroidAsset Include=\"${resource}\" Link=\"Assets/${resource_name}\" />\n")
      list(APPEND resource_files "${resource}")
    endif()
  endforeach()

  # The head project. configure_file sets the values, file(GENERATE) evaluates the generator expressions.
  set(template_dir "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/heads/android")
  set(RBFX_HEAD_NAME "${name}")
  set(RBFX_HEAD_RID "${rid}")
  set(RBFX_HEAD_MIN_API "${min_api}")
  set(RBFX_HEAD_APPLICATION_ID "${application_id}")
  set(RBFX_HEAD_DIR "${head_dir}")
  set(RBFX_HEAD_TEMPLATE_DIR "${template_dir}")
  set(RBFX_HEAD_SDL_JAVA_DIR "${RBFX_CSHARP_ANDROID_JAVA_DIR}")
  set(RBFX_HEAD_PLUGIN "${plugin_project}")
  set(RBFX_HEAD_PLUGIN_NAME "${plugin_name}")
  _rbfx_csharp_usercode_trim_roots(RBFX_HEAD_USERCODE_ROOTS ${arg_USERCODE})
  set(RBFX_HEAD_ITEMS "${items}")
  set(RBFX_HEAD_JAVA_LIBRARIES "${java_libraries}")
  set(head_project "${head_dir}/${name}.Head.csproj")
  configure_file("${template_dir}/Head.Android.csproj.in" "${head_dir}/generate/Head.csproj" @ONLY)
  configure_file("${template_dir}/UrhoActivity.java.in" "${head_dir}/generate/UrhoActivity.java" @ONLY)
  configure_file("${template_dir}/AndroidManifest.xml.in" "${head_dir}/AndroidManifest.xml" @ONLY)
  file(GENERATE OUTPUT "${head_project}" INPUT "${head_dir}/generate/Head.csproj")
  file(GENERATE OUTPUT "${head_dir}/java/io/urho3d/UrhoActivity.java" INPUT "${head_dir}/generate/UrhoActivity.java")

  # A generator expression with a target name does not work in BYPRODUCTS, so read the property.
  get_target_property(output_dir ${name} LIBRARY_OUTPUT_DIRECTORY)
  if(NOT output_dir)
    set(output_dir "${CMAKE_CURRENT_BINARY_DIR}")
  endif()

  # The head build. .NET for Android knows the configurations Debug and Release only.
  set(configuration "$<IF:$<CONFIG:Debug>,Debug,Release>")
  set(apk "${head_dir}/bin/${configuration}/${application_id}-Signed.apk")
  set(dotnet_arguments
    build "${head_project}"
    -c "${configuration}"
    --nologo
    -p:RbfxNativeBuildDriver=cmake
    "-p:RbfxNativeBuildDir=${CMAKE_BINARY_DIR}"
    -p:UseSharedCompilation=false
    -nodeReuse:false)
  if(RBFX_CSHARP_ANDROID_SDK_DIR)
    list(APPEND dotnet_arguments "-p:AndroidSdkDirectory=${RBFX_CSHARP_ANDROID_SDK_DIR}")
  endif()
  if(RBFX_CSHARP_JAVA_SDK_DIR)
    list(APPEND dotnet_arguments "-p:JavaSdkDirectory=${RBFX_CSHARP_JAVA_SDK_DIR}")
  endif()
  foreach(property IN LISTS RBFX_CSHARP_MSBUILD_PROPERTIES plugin_properties arg_PROPERTIES)
    list(APPEND dotnet_arguments "-p:${property}")
  endforeach()
  if(RBFX_CSHARP_BUILD_MANAGED)
    add_custom_target(
      ${name}.Head
      COMMAND "${CMAKE_COMMAND}" -E make_directory "${library_dir}"
      COMMAND "${CMAKE_COMMAND}" -E copy_if_different ${libraries} "${library_dir}/"
      COMMAND "${DOTNET_EXECUTABLE}" ${dotnet_arguments}
      COMMAND "${CMAKE_COMMAND}" -E copy_if_different "${apk}" "${output_dir}/${name}.apk"
      BYPRODUCTS "${output_dir}/${name}.apk"
      WORKING_DIRECTORY "${head_dir}"
      COMMENT "Building the Android head of ${name} with dotnet"
      VERBATIM)
  else()
    add_custom_target(${name}.Head)
  endif()
  add_dependencies(${name}.Head ${name})
  if(arg_USERCODE)
    add_dependencies(${name}.Head ${arg_USERCODE})
  endif()
  # The head packs the shared libraries of the plugins, so they must be built first.
  if(plugin_targets)
    add_dependencies(${name}.Head ${plugin_targets})
  endif()
  if(resource_files)
    # A file in RESOURCES can be the output of a custom command (for example create_pak of the engine).
    # Only a target in the directory of the command runs it. The commands (create_pak) are in the directory of the
    # call, so the target <name>.Resources is made there, at the end of the directory.
    # A deferred call reads the variables when it runs, so the values go into the code now.
    set(defer_code "cmake_language(DEFER DIRECTORY [==[${CMAKE_CURRENT_SOURCE_DIR}]==] CALL _rbfx_csharp_add_resource_target [==[${name}.Resources]==]")
    foreach(resource IN LISTS resource_files)
      string(APPEND defer_code " [==[${resource}]==]")
    endforeach()
    cmake_language(EVAL CODE "${defer_code})")
    add_dependencies(${name}.Head ${name}.Resources)
  endif()
  foreach(dependency IN ITEMS sharpcpp-rbfx-build-managed sharpcpp-rbfx-runtime)
    if(TARGET ${dependency})
      add_dependencies(${name}.Head ${dependency})
    endif()
  endforeach()
  if(NOT SharpCppRbfx_IS_SDK)
    add_dependencies(${name}.Head SharpCpp::RbfxNative SharpCpp::RbfxHost)
  endif()

  set_target_properties(${name} PROPERTIES SHARPCPP_RBFX_APPLICATION_OUTPUT "${output_dir}/${name}.apk")
  add_custom_target(${name}.CSharp ALL DEPENDS ${name}.Head)
endfunction()

# The Emscripten part of rbfx_csharp_add_application. It reads the variables arg_*, plugin_name and
# plugin_properties of the caller.
#
# The entry of the application: the browser has no hostfxr, so the .NET WebAssembly runtime starts first. The managed
# side must attach before the native code calls DotNetHost::Initialize. The engine must run on the browser thread:
# SDL, WebGL and the input events of the canvas are there, and the threaded runtime runs Program.Main on a worker.
# The engine also calls emscripten_set_main_loop_arg with simulate_infinite_loop. Emscripten then throws a JavaScript
# exception through all frames below it. Managed frames must not be there. So Program.Main only attaches and returns,
# and main.js calls the native entry from JavaScript on the browser thread.
function(_rbfx_csharp_add_wasm_application name)
  get_property(is_multi_config GLOBAL PROPERTY GENERATOR_IS_MULTI_CONFIG)
  if(is_multi_config)
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): Emscripten needs a single-configuration generator (for example Ninja).")
  endif()
  get_target_property(engine_type Urho3D TYPE)
  if(NOT engine_type STREQUAL "STATIC_LIBRARY")
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): Emscripten needs the static engine SDK.")
  endif()

  set(template_dir "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/heads/wasm")
  set(head_dir "${CMAKE_BINARY_DIR}/heads/${name}")
  # The C name of the native entry. heads/wasm/RbfxWebMain.cpp defines it.
  set(entry rbfx_application_start)

  # The application code. An object library links all its objects, as an executable does. So a source file with
  # only static registrations stays in the link. The definition renames main of URHO3D_DEFINE_APPLICATION_MAIN,
  # because the .NET runtime defines main.
  add_library(${name} OBJECT ${arg_SOURCES} "${template_dir}/RbfxWebMain.cpp")
  target_link_libraries(${name} PRIVATE Urho3D SharpCpp::RbfxHost ${arg_LINK_LIBRARIES})
  target_compile_definitions(${name} PRIVATE "SHARPCPP_RBFX_PLUGIN_NAME=\"${plugin_name}\"" main=rbfx_application_main)
  # The web options of the engine (-pthread, -sUSE_PTHREADS=1, the file system options and the JavaScript libraries of
  # libdatachannel-wasm). CMake does not link an object library, so these options only go to the head
  # (_rbfx_csharp_collect_native_link_items reads them).
  web_executable(${name})

  # The RESOURCES. The head build copies them next to index.html (heads/wasm/CopyResources.cmake).
  set(resource_paths)
  set(resource_files)
  foreach(resource IN LISTS arg_RESOURCES)
    get_filename_component(resource "${resource}" ABSOLUTE BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}")
    list(APPEND resource_paths "${resource}")
    if(NOT IS_DIRECTORY "${resource}")
      list(APPEND resource_files "${resource}")
    endif()
  endforeach()
  list(JOIN resource_paths "\n" resource_text)
  file(GENERATE OUTPUT "${head_dir}/resources.txt" CONTENT "${resource_text}\n")

  # The head project and the web files.
  set(RBFX_HEAD_NAME "${name}")
  set(RBFX_HEAD_ENTRY "${entry}")
  set(RBFX_HEAD_TEMPLATE_DIR "${template_dir}")
  set(RBFX_HEAD_PLUGIN "${plugin_project}")
  set(RBFX_HEAD_PLUGIN_NAME "${plugin_name}")
  _rbfx_csharp_usercode_trim_roots(RBFX_HEAD_USERCODE_ROOTS ${arg_USERCODE})
  set(head_project "${head_dir}/${name}.Head.csproj")
  configure_file("${template_dir}/Head.Wasm.csproj.in" "${head_project}" @ONLY)
  configure_file("${template_dir}/index.html.in" "${head_dir}/wwwroot/index.html" @ONLY)
  configure_file("${template_dir}/main.js.in" "${head_dir}/wwwroot/main.js" @ONLY)

  # The native items need the complete link closure. A later target_link_libraries in this directory can still add to it, so the
  # file is written at the end of the directory of this call. Imported targets are visible only there.
  # A deferred call reads the variables when it runs, so the values go into the code now.
  cmake_language(EVAL CODE
    "cmake_language(DEFER DIRECTORY [==[${CMAKE_CURRENT_SOURCE_DIR}]==] CALL _rbfx_csharp_write_wasm_native_items [==[${name}]==] [==[${head_dir}/NativeItems.props]==])")

  # The web folder of the application.
  if(CMAKE_RUNTIME_OUTPUT_DIRECTORY)
    set(web_dir "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}/${name}")
  else()
    set(web_dir "${CMAKE_CURRENT_BINARY_DIR}/${name}")
  endif()

  # The head build. The bindings of a source build must compile for browser-wasm (RbfxTargetRid, see
  # eng/SharpCpp.Rbfx.Build.props): then their import names are the file names of the static libraries.
  set(dotnet_arguments
    publish "${head_project}"
    -c Release
    --nologo
    -p:RbfxNativeBuildDriver=cmake
    "-p:RbfxNativeBuildDir=${CMAKE_BINARY_DIR}"
    -p:RbfxTargetRid=browser-wasm
    -p:UseSharedCompilation=false
    -nodeReuse:false)
  foreach(property IN LISTS RBFX_CSHARP_MSBUILD_PROPERTIES plugin_properties arg_PROPERTIES)
    list(APPEND dotnet_arguments "-p:${property}")
  endforeach()
  if(RBFX_CSHARP_BUILD_MANAGED)
    add_custom_target(
      ${name}.Head
      COMMAND "${DOTNET_EXECUTABLE}" ${dotnet_arguments}
      COMMAND "${CMAKE_COMMAND}" -E copy_directory "${head_dir}/bin/publish/wwwroot" "${web_dir}"
      COMMAND "${CMAKE_COMMAND}" "-DRESOURCE_LIST=${head_dir}/resources.txt" "-DOUTPUT_DIR=${web_dir}"
        -P "${template_dir}/CopyResources.cmake"
      BYPRODUCTS "${web_dir}/index.html" "${web_dir}/resources.json"
      WORKING_DIRECTORY "${head_dir}"
      COMMENT "Building the WebAssembly head of ${name} with dotnet"
      VERBATIM)
  else()
    add_custom_target(${name}.Head)
  endif()
  add_dependencies(${name}.Head ${name})
  if(arg_USERCODE)
    add_dependencies(${name}.Head ${arg_USERCODE})
  endif()
  if(resource_files)
    # A file in RESOURCES can be the output of a custom command (for example create_pak of the engine).
    # See the Android part for the deferred target.
    set(defer_code "cmake_language(DEFER DIRECTORY [==[${CMAKE_CURRENT_SOURCE_DIR}]==] CALL _rbfx_csharp_add_resource_target [==[${name}.Resources]==]")
    foreach(resource IN LISTS resource_files)
      string(APPEND defer_code " [==[${resource}]==]")
    endforeach()
    cmake_language(EVAL CODE "${defer_code})")
    add_dependencies(${name}.Head ${name}.Resources)
  endif()
  foreach(dependency IN ITEMS sharpcpp-rbfx-build-managed sharpcpp-rbfx-runtime)
    if(TARGET ${dependency})
      add_dependencies(${name}.Head ${dependency})
    endif()
  endforeach()
  if(NOT SharpCppRbfx_IS_SDK)
    add_dependencies(${name}.Head SharpCpp::RbfxNative SharpCpp::RbfxHost)
  endif()

  set_target_properties(${name} PROPERTIES SHARPCPP_RBFX_APPLICATION_OUTPUT "${web_dir}")
  add_custom_target(${name}.CSharp ALL DEPENDS ${name}.Head)
endfunction()

# The iOS part of rbfx_csharp_add_application. It reads the variables arg_*, plugin_name and
# plugin_properties of the caller.
#
# The entry of the application: an iOS app has one executable, and the .NET iOS SDK links it. So the runtime starts
# first, and Program.Main of the head attaches the managed side before the native code calls DotNetHost::Initialize.
# Then Program.Main calls the native entry. It gives SDL_main to SDL_UIKitRunApp, which runs UIApplicationMain with
# the application delegate of SDL. The engine main loop then runs in the callbacks of UIKit.
function(_rbfx_csharp_add_ios_application name)
  get_property(is_multi_config GLOBAL PROPERTY GENERATOR_IS_MULTI_CONFIG)
  if(is_multi_config)
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): iOS needs a single-configuration generator (for example Ninja).")
  endif()
  get_target_property(engine_type Urho3D TYPE)
  if(NOT engine_type STREQUAL "STATIC_LIBRARY")
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): iOS needs the static engine SDK.")
  endif()
  # The engine SDK and the SharpCpp.Rbfx SDK of iOS are for devices (ios-arm64).
  if(CMAKE_OSX_SYSROOT MATCHES "[Ss]imulator" OR (CMAKE_OSX_ARCHITECTURES AND NOT CMAKE_OSX_ARCHITECTURES STREQUAL "arm64"))
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): iOS supports devices with arm64 only (ios-arm64).")
  endif()
  set(rid ios-arm64)

  set(min_os 15.0)
  if(CMAKE_OSX_DEPLOYMENT_TARGET)
    set(min_os "${CMAKE_OSX_DEPLOYMENT_TARGET}")
  endif()

  # A bundle identifier has letters, digits, hyphens and periods.
  if(arg_APPLICATION_ID)
    set(application_id "${arg_APPLICATION_ID}")
  else()
    string(TOLOWER "${name}" application_id)
    string(REGEX REPLACE "[^a-z0-9-]" "-" application_id "${application_id}")
    set(application_id "org.rbfx.${application_id}")
  endif()

  set(template_dir "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/heads/ios")
  set(head_dir "${CMAKE_BINARY_DIR}/heads/${name}")

  # The application code. The head force-loads the archive, so a source file with only static registrations stays
  # in the link.
  add_library(${name} STATIC ${arg_SOURCES} "${template_dir}/RbfxIosMain.cpp")
  target_link_libraries(${name} PRIVATE Urho3D SharpCpp::RbfxHost ${arg_LINK_LIBRARIES})
  target_compile_definitions(${name} PRIVATE "SHARPCPP_RBFX_PLUGIN_NAME=\"${plugin_name}\"")

  # The RESOURCES. They go into the root folder of the app bundle.
  set(items "")
  set(resource_files)
  foreach(resource IN LISTS arg_RESOURCES)
    get_filename_component(resource "${resource}" ABSOLUTE BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}")
    get_filename_component(resource_name "${resource}" NAME)
    if(IS_DIRECTORY "${resource}")
      string(APPEND items "    <BundleResource Include=\"${resource}/**/*\" Link=\"${resource_name}/%(RecursiveDir)%(Filename)%(Extension)\" />\n")
    else()
      string(APPEND items "    <BundleResource Include=\"${resource}\" Link=\"${resource_name}\" />\n")
      list(APPEND resource_files "${resource}")
    endif()
  endforeach()

  # The head project.
  set(RBFX_HEAD_NAME "${name}")
  set(RBFX_HEAD_RID "${rid}")
  set(RBFX_HEAD_MIN_OS "${min_os}")
  set(RBFX_HEAD_APPLICATION_ID "${application_id}")
  set(RBFX_HEAD_TEMPLATE_DIR "${template_dir}")
  set(RBFX_HEAD_PLUGIN "${plugin_project}")
  set(RBFX_HEAD_PLUGIN_NAME "${plugin_name}")
  _rbfx_csharp_usercode_trim_roots(RBFX_HEAD_USERCODE_ROOTS ${arg_USERCODE})
  set(RBFX_HEAD_ITEMS "${items}")
  set(head_project "${head_dir}/${name}.Head.csproj")
  configure_file("${template_dir}/Head.Ios.csproj.in" "${head_project}" @ONLY)

  # The native items need the complete link closure (see the Emscripten part).
  set(defer_code "cmake_language(DEFER DIRECTORY [==[${CMAKE_CURRENT_SOURCE_DIR}]==] CALL _rbfx_csharp_write_ios_native_items [==[${name}]==] [==[${head_dir}/NativeItems.props]==]")
  foreach(usercode IN LISTS arg_USERCODE)
    string(APPEND defer_code " [==[${usercode}]==]")
  endforeach()
  cmake_language(EVAL CODE "${defer_code})")

  if(CMAKE_RUNTIME_OUTPUT_DIRECTORY)
    set(output_dir "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}")
  else()
    set(output_dir "${CMAKE_CURRENT_BINARY_DIR}")
  endif()

  # The head build. The bindings of a source build must compile for ios-arm64 (RbfxTargetRid, see
  # eng/SharpCpp.Rbfx.Build.props): then their import name is __Internal. The app bundle has the assembly name.
  set(configuration "$<IF:$<CONFIG:Debug>,Debug,Release>")
  set(app_bundle "${head_dir}/bin/${configuration}/${name}.Head.app")
  set(dotnet_arguments
    build "${head_project}"
    -c "${configuration}"
    --nologo
    -p:RbfxNativeBuildDriver=cmake
    "-p:RbfxNativeBuildDir=${CMAKE_BINARY_DIR}"
    "-p:RbfxTargetRid=${rid}"
    -p:UseSharedCompilation=false
    -nodeReuse:false)
  foreach(property IN LISTS RBFX_CSHARP_MSBUILD_PROPERTIES plugin_properties arg_PROPERTIES)
    list(APPEND dotnet_arguments "-p:${property}")
  endforeach()
  if(RBFX_CSHARP_BUILD_MANAGED)
    add_custom_target(
      ${name}.Head
      COMMAND "${DOTNET_EXECUTABLE}" ${dotnet_arguments}
      COMMAND "${CMAKE_COMMAND}" -E rm -rf "${output_dir}/${name}.app"
      COMMAND "${CMAKE_COMMAND}" -E copy_directory "${app_bundle}" "${output_dir}/${name}.app"
      BYPRODUCTS "${output_dir}/${name}.app/Info.plist"
      WORKING_DIRECTORY "${head_dir}"
      COMMENT "Building the iOS head of ${name} with dotnet"
      VERBATIM)
  else()
    add_custom_target(${name}.Head)
  endif()
  add_dependencies(${name}.Head ${name})
  if(arg_USERCODE)
    add_dependencies(${name}.Head ${arg_USERCODE})
  endif()
  if(resource_files)
    # A file in RESOURCES can be the output of a custom command (for example create_pak of the engine).
    # See the Android part for the deferred target.
    set(defer_code "cmake_language(DEFER DIRECTORY [==[${CMAKE_CURRENT_SOURCE_DIR}]==] CALL _rbfx_csharp_add_resource_target [==[${name}.Resources]==]")
    foreach(resource IN LISTS resource_files)
      string(APPEND defer_code " [==[${resource}]==]")
    endforeach()
    cmake_language(EVAL CODE "${defer_code})")
    add_dependencies(${name}.Head ${name}.Resources)
  endif()
  foreach(dependency IN ITEMS sharpcpp-rbfx-build-managed sharpcpp-rbfx-runtime)
    if(TARGET ${dependency})
      add_dependencies(${name}.Head ${dependency})
    endif()
  endforeach()
  if(NOT SharpCppRbfx_IS_SDK)
    add_dependencies(${name}.Head SharpCpp::RbfxNative SharpCpp::RbfxHost)
  endif()

  set_target_properties(${name} PROPERTIES SHARPCPP_RBFX_APPLICATION_OUTPUT "${output_dir}/${name}.app")
  add_custom_target(${name}.CSharp ALL DEPENDS ${name}.Head)
endfunction()

# Collects the native link items of the head of <name>. <kind> is wasm or ios. It walks the link closure of <name>, of
# SharpCpp::RbfxNative and of the targets in ARGN. It sets <files_var> to the static libraries and objects, and
# <flags_var> to the flags, the system libraries (-l<name>) and the INTERFACE_LINK_OPTIONS of the targets.
# wasm: <name> is an object library. An object library gives its objects.
# ios:  <name> is a static library. An object library gives no file: the archive of the static library that links it
#       directly has its objects (the archive of <name> for its own LINK_LIBRARIES).
function(_rbfx_csharp_collect_native_link_items name kind files_var flags_var)
  if(kind STREQUAL "wasm")
    set(files "$<TARGET_OBJECTS:${name}>")
  else()
    set(files "$<TARGET_FILE:${name}>")
  endif()
  set(flags)
  get_target_property(options ${name} LINK_OPTIONS)
  if(options)
    list(APPEND flags ${options})
  endif()
  get_target_property(queue ${name} LINK_LIBRARIES)
  if(NOT queue)
    set(queue)
  endif()
  # The wrapper is not a link dependency of the application. The bindings call it through P/Invoke.
  list(APPEND queue SharpCpp::RbfxNative ${ARGN})
  set(visited ${name})
  list(LENGTH queue count)
  while(count GREATER 0)
    list(POP_FRONT queue item)
    list(LENGTH queue count)
    if(item MATCHES "^\\$<LINK_ONLY:(.*)>$")
      set(item "${CMAKE_MATCH_1}")
    endif()
    # A static library in this build has a build-only link item inside the link-only item.
    if(item MATCHES "^\\$<BUILD_INTERFACE:(.*)>$")
      set(item "${CMAKE_MATCH_1}")
    endif()
    # A target_link_libraries call from another directory adds the markers ::@(<id>) and ::@.
    if(item STREQUAL "" OR item MATCHES "^::@")
      continue()
    endif()
    if(TARGET "${item}")
      get_target_property(aliased "${item}" ALIASED_TARGET)
      if(aliased)
        set(item "${aliased}")
      endif()
      if(item IN_LIST visited)
        continue()
      endif()
      list(APPEND visited "${item}")
      get_target_property(type "${item}" TYPE)
      if(type STREQUAL "STATIC_LIBRARY" OR type STREQUAL "UNKNOWN_LIBRARY")
        list(APPEND files "$<TARGET_FILE:${item}>")
      elseif(type STREQUAL "OBJECT_LIBRARY")
        if(kind STREQUAL "wasm")
          list(APPEND files "$<TARGET_OBJECTS:${item}>")
        endif()
      elseif(type MATCHES "^(SHARED|MODULE)_LIBRARY$")
        message(FATAL_ERROR "rbfx_csharp_add_application(${name}): ${item} is a shared library. The ${kind} head links static libraries only.")
      endif()
      get_target_property(options "${item}" INTERFACE_LINK_OPTIONS)
      if(options)
        list(APPEND flags ${options})
      endif()
      get_target_property(dependencies "${item}" INTERFACE_LINK_LIBRARIES)
      if(dependencies)
        list(APPEND queue ${dependencies})
        list(LENGTH queue count)
      endif()
    elseif(item MATCHES "^-" OR item MATCHES "^SHELL:" OR item MATCHES "\\$<")
      list(APPEND flags "${item}")
    elseif(IS_ABSOLUTE "${item}")
      list(APPEND files "${item}")
    elseif(item MATCHES "::")
      message(FATAL_ERROR "rbfx_csharp_add_application(${name}): the link item ${item} is not a target.")
    else()
      list(APPEND flags "-l${item}")
    endif()
  endwhile()
  list(REMOVE_DUPLICATES files)
  list(REMOVE_DUPLICATES flags)
  set(${files_var} "${files}" PARENT_SCOPE)
  set(${flags_var} "${flags}" PARENT_SCOPE)
endfunction()

# Writes NativeItems.props of the WebAssembly head of <name> (see above). A static library or an object becomes a
# NativeFileReference. The flags go to EmccExtraLDFlags. The file names keep the names of the libraries, so the
# P/Invoke table of the runtime finds libSharpCpp.Rbfx.Native and libSharpCpp.Rbfx.Host. Generator expressions are
# evaluated in the context of <name>.
function(_rbfx_csharp_write_wasm_native_items name output)
  _rbfx_csharp_collect_native_link_items(${name} wasm files flags)

  set(content "<Project>\n  <!-- Written by rbfx_csharp_add_application (RbfxCSharpApplication.cmake). Do not edit. -->\n")
  string(APPEND content "  <PropertyGroup>\n    <EmccExtraLDFlags>$(EmccExtraLDFlags)")
  foreach(flag IN LISTS flags)
    # A response file line of emcc. SHELL: keeps the words of one option together.
    string(REGEX REPLACE "^SHELL:" "" flag "${flag}")
    # The head sets the exception mode (WasmEnableExceptionHandling, -fwasm-exceptions). emcc stops when a library
    # sets another mode, and the engine SDK exports DISABLE_EXCEPTION_CATCHING in its link options.
    string(REGEX REPLACE "-s *DISABLE_EXCEPTION_CATCHING=[^ ]*" "" flag "${flag}")
    string(STRIP "${flag}" flag)
    if(flag STREQUAL "")
      continue()
    endif()
    if(NOT flag MATCHES "\\$<")
      # MSBuild and XML special characters. A generator expression keeps its text.
      string(REPLACE "%" "%25" flag "${flag}")
      string(REPLACE "$" "%24" flag "${flag}")
      string(REPLACE "@" "%40" flag "${flag}")
      string(REPLACE "&" "&amp;" flag "${flag}")
      string(REPLACE "<" "&lt;" flag "${flag}")
      string(REPLACE ">" "&gt;" flag "${flag}")
    endif()
    string(APPEND content ";${flag}")
  endforeach()
  string(APPEND content "</EmccExtraLDFlags>\n  </PropertyGroup>\n  <ItemGroup>\n")
  foreach(file IN LISTS files)
    string(APPEND content "    <NativeFileReference Include=\"${file}\" />\n")
  endforeach()
  string(APPEND content "  </ItemGroup>\n</Project>\n")
  file(GENERATE OUTPUT "${output}" CONTENT "${content}" TARGET ${name})
endfunction()

# Writes NativeItems.props of the iOS head of <name> (see above). ARGN has the USERCODE targets. Every static library
# of the link closure becomes a static NativeReference. The archives of <name>, of the wrapper, of the host library and
# of the USERCODE targets are force-loaded: the managed side finds their exports with __Internal. The other archives
# are not force-loaded: the engine archive has the main of SDL (SDL_uikit_main.c), and the executable has the main of
# .NET. The flags go to LinkerFlags of <name>. The frameworks and the flags -lc++ -liconv are the same as for the
# wrapper in eng/SharpCpp.Rbfx.NativeItems.targets. Generator expressions are evaluated in the context of <name>.
function(_rbfx_csharp_write_ios_native_items name output)
  _rbfx_csharp_collect_native_link_items(${name} ios files flags ${ARGN})

  set(force_load_files)
  foreach(target IN ITEMS ${name} SharpCpp::RbfxNative SharpCpp::RbfxHost ${ARGN})
    if(TARGET "${target}")
      get_target_property(aliased "${target}" ALIASED_TARGET)
      if(aliased)
        set(target "${aliased}")
      endif()
      list(APPEND force_load_files "$<TARGET_FILE:${target}>")
    endif()
  endforeach()

  set(linker_flags "-lc++ -liconv")
  foreach(flag IN LISTS flags)
    string(REGEX REPLACE "^SHELL:" "" flag "${flag}")
    if(flag MATCHES "^LINKER:(.*)$")
      set(flag "-Wl,${CMAKE_MATCH_1}")
    endif()
    string(STRIP "${flag}" flag)
    if(flag STREQUAL "")
      continue()
    endif()
    if(NOT flag MATCHES "\\$<")
      # MSBuild and XML special characters. A generator expression keeps its text.
      string(REPLACE "%" "%25" flag "${flag}")
      string(REPLACE "$" "%24" flag "${flag}")
      string(REPLACE "@" "%40" flag "${flag}")
      string(REPLACE ";" "%3B" flag "${flag}")
      string(REPLACE "&" "&amp;" flag "${flag}")
      string(REPLACE "<" "&lt;" flag "${flag}")
      string(REPLACE ">" "&gt;" flag "${flag}")
    endif()
    string(APPEND linker_flags " ${flag}")
  endforeach()

  set(content "<Project>\n  <!-- Written by rbfx_csharp_add_application (RbfxCSharpApplication.cmake). Do not edit. -->\n  <ItemGroup>\n")
  foreach(file IN LISTS files)
    string(APPEND content "    <NativeReference Include=\"${file}\">\n      <Kind>Static</Kind>\n      <IsCxx>true</IsCxx>\n")
    if(file IN_LIST force_load_files)
      string(APPEND content "      <ForceLoad>true</ForceLoad>\n")
    else()
      string(APPEND content "      <ForceLoad>false</ForceLoad>\n      <SmartLink>true</SmartLink>\n")
    endif()
    if(file STREQUAL "$<TARGET_FILE:${name}>")
      string(APPEND content "      <LinkerFlags>${linker_flags}</LinkerFlags>\n")
      string(APPEND content "      <Frameworks>CoreVideo CoreAudio AudioToolbox AVFoundation CoreBluetooth CoreGraphics CoreMotion Foundation Metal OpenGLES QuartzCore UIKit</Frameworks>\n")
      string(APPEND content "      <WeakFrameworks>GameController CoreHaptics</WeakFrameworks>\n")
    endif()
    string(APPEND content "    </NativeReference>\n")
  endforeach()
  string(APPEND content "  </ItemGroup>\n</Project>\n")
  file(GENERATE OUTPUT "${output}" CONTENT "${content}" TARGET ${name})
endfunction()

# The target that makes the files in RESOURCES of an Android, iOS or Emscripten application (see above).
function(_rbfx_csharp_add_resource_target target)
  add_custom_target(${target} DEPENDS ${ARGN})
endfunction()
