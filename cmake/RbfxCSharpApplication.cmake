# One call per application: the packaging of a launcher and a C# plugin. On desktop the plugin goes next to the
# executable. On Android the call makes an APK: a .NET Android head starts the runtime and loads the native application.
# On Emscripten the call makes a web folder: a .NET WebAssembly head starts the runtime, and the native application
# is linked into the runtime.
# On iOS the call makes an app bundle: a .NET iOS head starts the runtime, and the native application is linked
# into the executable of the app.
#
# rbfx_csharp_package_application(<launcher>
#   PLUGIN <plugin>
#   [DATA <data>]
#   [RESOURCES <files or folders...>]
#   [APPLICATION_ID <id>]
#   [HEAD_SOURCES <C# files...>]
#   [PROPERTIES <Name=Value>...])
#
# The function does what is left after the three functions that make the parts of an application:
# rbfx_csharp_add_launcher (RbfxCSharpLauncher.cmake), rbfx_csharp_add_plugin (RbfxCSharpPlugin.cmake) and
# rbfx_csharp_add_data (RbfxCSharpData.cmake). rbfx_csharp_add_project calls them in this order.
#
# <launcher>    The target from rbfx_csharp_add_launcher. The function must be called in the source folder of the
#               launcher (the folder of the call of rbfx_csharp_add_launcher). Else the function stops with an error.
#               The reason: the function schedules calls with cmake_language(DEFER DIRECTORY) for the end of this
#               folder. At that time all targets and link libraries of the folder are known (see the native items of
#               the Emscripten and iOS heads).
# PLUGIN        The target from rbfx_csharp_add_plugin. The launcher loads this plugin.
# DATA          The target from rbfx_csharp_add_data. On Android, Emscripten and iOS the paks of the data (the target
#               property SHARPCPP_RBFX_DATA_FILES) are resources of the head, and the head depends on the target. On
#               desktop the data is not copied to the folder of the executable. The install rules read it
#               (rbfx_csharp_install_application).
# RESOURCES     Files and folders that the head packs as resources, in addition to the paks of DATA. Relative paths
#               are relative to CMAKE_CURRENT_SOURCE_DIR. Accepted and ignored on desktop. A file can be the output of
#               a custom command (for example create_pak of the engine). The command must be in the source folder of
#               the launcher, because only a target of the folder of the command can depend on its output. Use
#               DATA for the resource folders of the application.
# APPLICATION_ID  The Android package name or the iOS bundle identifier. The default is org.rbfx.<launcher in lower
#               case>. Accepted and ignored on desktop and on Emscripten.
# HEAD_SOURCES  C# files of the head assembly (the application assembly). Relative paths are relative to
#               CMAKE_CURRENT_SOURCE_DIR. Each file becomes a Compile item of the head project. Every head template
#               declares the class Program with the partial methods OnManagedStartup and OnManagedShutdown, and a file
#               can implement them. OnManagedStartup runs before the engine Context exists. Do not use engine objects
#               in the hooks. Use them to register types and to set options, for example
#               Urho3D.Plugins.ManagedTypeRegistries.Add(typeof(Program).Assembly). The hostfxr mode on desktop
#               has no head: CMake ignores HEAD_SOURCES there.
# PROPERTIES    More MSBuild properties for the build of the plugin, as Name=Value. They come after the properties of
#               the plugin (SHARPCPP_RBFX_PLUGIN_MSBUILD_PROPERTIES of the plugin target).
#
# The function does these steps on all platforms:
# - It defines SHARPCPP_RBFX_PLUGIN_NAME for the launcher. The value is the string with the assembly name of the plugin.
#   The launcher finds <plugin>.dll and <plugin>.runtimeconfig.json of the plugin in the folder of the executable.
# - It reads the wrapper targets from the property SHARPCPP_RBFX_PLUGIN_REFERENCES of the plugin and the native plugin
#   targets from the property SHARPCPP_RBFX_PLUGINS of the launcher.
#
# Desktop (Linux, Windows, macOS):
# - Builds the plugin project with dotnet into the folder of the executable
#   (rbfx_csharp_add_plugin_build, the target <launcher>.Plugin) and copies the native wrapper of the C# bindings to the
#   same folder (the target <launcher>.Native). On Windows it also copies the host library and nethost.dll.
#   The function also copies each shared library of the wrapper targets and the shared and module libraries of the
#   native plugins to the folder of the executable. On macOS the executable is a plain executable, because the .NET
#   runtime cannot be hosted inside an application bundle.
# - The plugin target depends on the executable, so the executable cannot depend on the plugin.
#   The target <launcher>.CSharp is part of the default build and builds the plugin and the wrapper.
#   Build it (or the target "all") to get a complete application.
#
# Desktop in shim mode (RBFX_HOST_MODE=shim, or SharpCppRbfx_HOST_MODE of the installed SDK):
# - Publishes the NativeAOT head <build>/heads/<launcher>/<launcher>.Head.csproj (templates in cmake/heads/desktop) and
#   copies the shared library <plugin>.Head<suffix> to the folder of the executable (the target <launcher>.Head). The head
#   has the plugin and the bindings in it. The target <launcher>.Native copies the native libraries as in hostfxr mode.
#
# On Android the head packs each shared or module target of the native plugins, in the given order, and then each shared
# library of the wrapper targets. The packing is before lib<launcher>.so and without duplicates.
#
# Target properties set on <launcher>: SHARPCPP_RBFX_PACKAGED_PLUGIN (the plugin target), SHARPCPP_RBFX_PACKAGED_DATA (the
# data target, or empty) and SHARPCPP_RBFX_APPLICATION_OUTPUT (the product of the application: <launcher>.apk on Android,
# the web folder on Emscripten, <launcher>.app on iOS, empty on desktop).
#
# Android (single-configuration generator, NDK toolchain, SharpCpp.Rbfx built in shim mode):
# - The launcher is the shared library <name> (lib<name>.so). URHO3D_DEFINE_APPLICATION_MAIN exports SDL_main from it.
# - Generates the .NET Android head project in <build>/heads/<name>/ from the templates in heads/android/
#   next to this file. The head references the plugin project, packs the native libraries (the C++ runtime, Urho3D,
#   the wrapper, the host library, the shared libraries in LINK_LIBRARIES of the launcher and lib<name>.so) and the
#   RESOURCES and the paks of DATA as assets. A file in RESOURCES becomes an asset with its file name. A folder becomes a folder
#   of assets with its name.
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
# - The launcher is the object library <name>. The .NET runtime has its own main, so its sources compile with
#   main=rbfx_application_main. RbfxWebMain.cpp exports it as the C function rbfx_application_start.
# - Generates the head project in <build>/heads/<name>/ from the templates in heads/wasm/ next to this file.
#   At the end of the configure step, CMake collects the static libraries, the objects and the link flags of the
#   link closure of <name> and of SharpCpp::RbfxNative into NativeItems.props of the head. The head references the plugin project.
# - main.js of the head starts the runtime, writes the RESOURCES into the file system of Emscripten and runs
#   Program.Main. Program.Main runs the module initializer of the bindings, so DotNetHost::Initialize finds the
#   runtime. Then main.js calls rbfx_application_start on the browser thread. The engine main loop then runs there.
# - A file in RESOURCES becomes a file in the root folder with its file name. A folder becomes a folder with its
#   name.
# - The target <name>.CSharp publishes the head with dotnet (configuration of CMake) and copies the web folder (index.html, main.js,
#   _framework and the RESOURCES) to <name>/ in the runtime output folder (CMAKE_RUNTIME_OUTPUT_DIRECTORY).
#   The runtime uses threads, so the web server must send the headers of cross-origin isolation.
#
# iOS (CMAKE_SYSTEM_NAME iOS, device arm64, single-configuration generator, static engine SDK ios-clang-arm64-lib,
# SharpCpp.Rbfx in shim mode):
# - An iOS app has one executable, and the .NET iOS SDK links it. So the .NET iOS head is the entry. The application,
#   the engine, the wrapper and the host library are static code in the executable (NativeReference items of the head).
# - The launcher is the static library <name> (lib<name>.a). On iOS URHO3D_DEFINE_APPLICATION_MAIN defines SDL_main.
#   RbfxIosMain.cpp exports the C function rbfx_application_start. It gives SDL_main to SDL_UIKitRunApp.
# - Generates the head project in <build>/heads/<name>/ from the templates in heads/ios/ next to this file. At the end
#   of the configure step, CMake collects the static libraries and the link flags of the link closure of <name>, of
#   SharpCpp::RbfxNative and of the wrapper targets into NativeItems.props of the head. The head references the plugin project.
# - Program.Main of the head runs the module initializer of the bindings, so DotNetHost::Initialize finds the runtime.
#   Then it calls rbfx_application_start. SDL starts UIKit and calls SDL_main when the application is ready.
# - The application library, the wrapper, the host library and the libraries of the wrapper targets are force-loaded: the managed
#   side finds their exports with __Internal. The other libraries are not force-loaded. The engine library has the
#   main of SDL, and the executable has the main of .NET.
# - A file in RESOURCES becomes a file in the root folder of the app bundle with its file name. A folder becomes a
#   folder with its name.
# - APPLICATION_ID is the bundle identifier. The default is org.rbfx.<name in lower case>.
# - The target <name>.CSharp builds the head with dotnet and copies the app bundle to <name>.app in the runtime output
#   folder (CMAKE_RUNTIME_OUTPUT_DIRECTORY). The head does not sign the app, unless the MSBuild properties ask for it
#   (EnableCodeSigning, CodesignKey and CodesignProvision in PROPERTIES or RBFX_CSHARP_MSBUILD_PROPERTIES).

function(rbfx_csharp_package_application launcher)
  cmake_parse_arguments(PARSE_ARGV 1 arg "" "PLUGIN;DATA;APPLICATION_ID" "RESOURCES;HEAD_SOURCES;PROPERTIES")
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_package_application(${launcher}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()
  if(NOT TARGET ${launcher})
    message(FATAL_ERROR "rbfx_csharp_package_application(${launcher}): the target '${launcher}' does not exist. Call rbfx_csharp_add_launcher first.")
  endif()
  get_target_property(is_launcher ${launcher} SHARPCPP_RBFX_LAUNCHER)
  if(NOT is_launcher)
    message(FATAL_ERROR "rbfx_csharp_package_application(${launcher}): the target '${launcher}' was not made by rbfx_csharp_add_launcher.")
  endif()
  # The function defers calls to the end of the current folder (cmake_language DEFER DIRECTORY). They must run in the
  # folder of the launcher, where the link libraries of the launcher are complete.
  get_target_property(launcher_directory ${launcher} SOURCE_DIR)
  cmake_path(COMPARE "${launcher_directory}" EQUAL "${CMAKE_CURRENT_SOURCE_DIR}" is_same_directory)
  if(NOT is_same_directory)
    message(FATAL_ERROR "rbfx_csharp_package_application(${launcher}): call the function in the source folder of the launcher '${launcher_directory}'. The current folder is '${CMAKE_CURRENT_SOURCE_DIR}'. The function defers calls to the end of the folder (cmake_language DEFER DIRECTORY), and the deferred calls must run in the folder of the launcher.")
  endif()
  if(NOT arg_PLUGIN)
    message(FATAL_ERROR "rbfx_csharp_package_application(${launcher}): PLUGIN is required.")
  endif()
  if(NOT TARGET ${arg_PLUGIN})
    message(FATAL_ERROR "rbfx_csharp_package_application(${launcher}): PLUGIN target '${arg_PLUGIN}' does not exist. Call rbfx_csharp_add_plugin first.")
  endif()
  get_target_property(plugin_name ${arg_PLUGIN} SHARPCPP_RBFX_PLUGIN_NAME)
  get_target_property(plugin_project ${arg_PLUGIN} SHARPCPP_RBFX_PLUGIN_PROJECT)
  get_target_property(plugin_references ${arg_PLUGIN} SHARPCPP_RBFX_PLUGIN_REFERENCES)
  get_target_property(plugin_properties ${arg_PLUGIN} SHARPCPP_RBFX_PLUGIN_MSBUILD_PROPERTIES)
  if(NOT plugin_name OR NOT plugin_project)
    message(FATAL_ERROR "rbfx_csharp_package_application(${launcher}): PLUGIN target '${arg_PLUGIN}' was not made by rbfx_csharp_add_plugin.")
  endif()
  foreach(list_name IN ITEMS plugin_references plugin_properties)
    if(NOT ${list_name})
      set(${list_name})
    endif()
  endforeach()
  list(APPEND plugin_properties ${arg_PROPERTIES})
  if(arg_DATA)
    if(NOT TARGET ${arg_DATA})
      message(FATAL_ERROR "rbfx_csharp_package_application(${launcher}): DATA target '${arg_DATA}' does not exist. Call rbfx_csharp_add_data first.")
    endif()
    get_target_property(is_data ${arg_DATA} SHARPCPP_RBFX_DATA)
    if(NOT is_data)
      message(FATAL_ERROR "rbfx_csharp_package_application(${launcher}): DATA target '${arg_DATA}' was not made by rbfx_csharp_add_data.")
    endif()
  endif()

  # The wrapper targets that the plugin build waits for. The Android, Web and iOS parts also read them.
  # arg_WRAPPER_TARGETS are the wrapper targets (they have the managed assembly). arg_WRAPPERS are the targets that
  # hold their native code: the wrapper itself (separate library) or the IMPLEMENTATION target (integration).
  set(arg_WRAPPER_TARGETS ${plugin_references})
  set(arg_WRAPPERS)
  foreach(wrapper IN LISTS arg_WRAPPER_TARGETS)
    rbfx_csharp_wrapper_native_target(${wrapper} wrapper_native)
    list(APPEND arg_WRAPPERS ${wrapper_native})
  endforeach()
  list(REMOVE_DUPLICATES arg_WRAPPERS)
  # The native plugins and the libraries of the launcher.
  get_target_property(arg_PLUGINS ${launcher} SHARPCPP_RBFX_PLUGINS)
  get_target_property(launcher_libraries ${launcher} SHARPCPP_RBFX_LAUNCHER_LINK_LIBRARIES)
  foreach(list_name IN ITEMS arg_PLUGINS launcher_libraries)
    if(NOT ${list_name})
      set(${list_name})
    endif()
  endforeach()
  # The plugin name goes to the launcher as a definition, because the launcher does not know the plugin.
  target_compile_definitions(${launcher} PRIVATE "SHARPCPP_RBFX_PLUGIN_NAME=\"${plugin_name}\"")

  # The resources of the head: the paks of the data, and RESOURCES. A relative path is relative to the current folder.
  set(resource_files)
  foreach(resource IN LISTS arg_RESOURCES)
    cmake_path(ABSOLUTE_PATH resource BASE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" NORMALIZE)
    if(NOT IS_DIRECTORY "${resource}")
      list(APPEND resource_files "${resource}")
    endif()
  endforeach()
  set(data_files)
  if(arg_DATA AND (ANDROID OR EMSCRIPTEN OR CMAKE_SYSTEM_NAME STREQUAL "iOS"))
    get_target_property(data_files ${arg_DATA} SHARPCPP_RBFX_DATA_FILES)
    list(APPEND arg_RESOURCES ${data_files})
  endif()

  # The Compile items of HEAD_SOURCES for the head project (RBFX_HEAD_SOURCES_ITEMS in the templates). The head
  # functions below read this variable. It is empty when HEAD_SOURCES is empty.
  set(RBFX_HEAD_SOURCES_ITEMS "")
  foreach(head_source IN LISTS arg_HEAD_SOURCES)
    cmake_path(ABSOLUTE_PATH head_source BASE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" NORMALIZE)
    cmake_path(CONVERT "${head_source}" TO_CMAKE_PATH_LIST head_source NORMALIZE)
    if(NOT EXISTS "${head_source}")
      message(FATAL_ERROR "rbfx_csharp_package_application(${launcher}): HEAD_SOURCES file '${head_source}' does not exist.")
    endif()
    # The path goes into an XML attribute and is an MSBuild item. Escape '%' first (MSBuild), then '&', '<' and '"' (XML).
    # A ';' cannot occur: it splits the list.
    set(head_source_item "${head_source}")
    string(REPLACE "%" "%25" head_source_item "${head_source_item}")
    string(REPLACE "$" "%24" head_source_item "${head_source_item}")
    string(REPLACE "@" "%40" head_source_item "${head_source_item}")
    string(REPLACE "*" "%2A" head_source_item "${head_source_item}")
    string(REPLACE "?" "%3F" head_source_item "${head_source_item}")
    string(REPLACE "&" "&amp;" head_source_item "${head_source_item}")
    string(REPLACE "<" "&lt;" head_source_item "${head_source_item}")
    string(REPLACE "\"" "&quot;" head_source_item "${head_source_item}")
    string(APPEND RBFX_HEAD_SOURCES_ITEMS "    <Compile Include=\"${head_source_item}\" />\n")
  endforeach()

  if(ANDROID)
    _rbfx_csharp_add_android_application(${launcher})
  elseif(EMSCRIPTEN)
    _rbfx_csharp_add_wasm_application(${launcher})
  elseif(CMAKE_SYSTEM_NAME STREQUAL "iOS")
    _rbfx_csharp_add_ios_application(${launcher})
  else()
    # The installed SDK gives SharpCppRbfx_HOST_MODE. The source tree gives the cache variable RBFX_HOST_MODE.
    if(DEFINED RBFX_HOST_MODE)
      set(host_mode "${RBFX_HOST_MODE}")
    else()
      set(host_mode "${SharpCppRbfx_HOST_MODE}")
    endif()
    if(host_mode STREQUAL "shim")
      _rbfx_csharp_add_desktop_shim_application(${launcher})
    else()
      _rbfx_csharp_add_desktop_application(${launcher})
    endif()
  endif()

  if(ANDROID OR EMSCRIPTEN OR CMAKE_SYSTEM_NAME STREQUAL "iOS")
    set(head_target ${launcher}.Head)
    if(resource_files)
      # A file in RESOURCES can be the output of a custom command (for example create_pak of the engine).
      # Only a target of the folder of the command runs it. This is the folder of the launcher.
      add_custom_target(${launcher}.Resources DEPENDS ${resource_files})
      add_dependencies(${head_target} ${launcher}.Resources)
    endif()
    if(arg_DATA)
      # The target of the data runs the commands of the paks.
      add_dependencies(${head_target} ${arg_DATA})
    endif()
  endif()
  set_target_properties(
    ${launcher}
    PROPERTIES
      SHARPCPP_RBFX_PACKAGED_PLUGIN "${arg_PLUGIN}"
      SHARPCPP_RBFX_PACKAGED_DATA "${arg_DATA}")
endfunction()

# The desktop part of rbfx_csharp_package_application (hostfxr mode). It reads the variables arg_*, plugin_name,
# plugin_project and plugin_properties of the caller.
function(_rbfx_csharp_add_desktop_application name)
  # The plugin is built with dotnet next to the executable.
  rbfx_csharp_add_plugin_build(
    ${name}.Plugin
    PROJECT "${plugin_project}"
    HOST ${name}
    PROPERTIES ${plugin_properties})
  if(arg_WRAPPERS)
    add_dependencies(${name}.Plugin ${arg_WRAPPERS})
  endif()

  _rbfx_csharp_add_desktop_native_copy(${name})

  # The desktop application is the executable in its folder, so there is no separate output.
  set_target_properties(${name} PROPERTIES SHARPCPP_RBFX_APPLICATION_OUTPUT "")
  add_custom_target(${name}.CSharp ALL DEPENDS ${name}.Plugin ${name}.Native)
endfunction()

# The target <name>.Native of a desktop application. It copies the native wrapper of the C# bindings, the host library
# (Windows), and the shared libraries of the wrapper targets and of the native plugins to the folder of the executable.
# It reads the variables arg_WRAPPERS, arg_PLUGINS and host_mode of the caller.
function(_rbfx_csharp_add_desktop_native_copy name)
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
          "$<TARGET_FILE:SharpCpp::RbfxHost>" "$<TARGET_FILE_DIR:${name}>")
      # Only the hostfxr host library needs nethost. host_mode is a variable of rbfx_csharp_package_application.
      if(NOT host_mode STREQUAL "shim")
        list(APPEND copy_commands
          COMMAND "${CMAKE_COMMAND}" -E copy_if_different
            "$<TARGET_FILE_DIR:SharpCpp::RbfxHost>/nethost.dll" "$<TARGET_FILE_DIR:${name}>")
      endif()
    endif()
  endif()
  # The shared libraries of the wrapper targets and the shared or module libraries of the native plugin targets also go
  # to the folder of the executable.
  set(wrapper_libraries)
  foreach(wrapper IN LISTS arg_WRAPPERS arg_PLUGINS)
    get_target_property(wrapper_type ${wrapper} TYPE)
    # arg_WRAPPERS has native targets: a wrapper library or the (shared or module) IMPLEMENTATION target of a wrapper.
    if(wrapper_type STREQUAL "SHARED_LIBRARY" OR wrapper_type STREQUAL "MODULE_LIBRARY")
      list(APPEND wrapper_libraries ${wrapper})
      list(APPEND copy_commands
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different
          "$<TARGET_FILE:${wrapper}>" "$<TARGET_FILE_DIR:${name}>")
    endif()
  endforeach()
  add_custom_target(
    ${name}.Native
    ${copy_commands}
    COMMENT "Copying the native wrapper of the C# bindings"
    VERBATIM)
  add_dependencies(${name}.Native ${name} ${wrapper_libraries})
  if(NOT SharpCppRbfx_IS_SDK)
    add_dependencies(${name}.Native SharpCpp::RbfxNative SharpCpp::RbfxHost)
  endif()
endfunction()

# The desktop part of rbfx_csharp_package_application in shim mode. It reads the variables arg_*, plugin_name,
# plugin_project and plugin_properties of the caller.
#
# The native launcher keeps main. The head is a NativeAOT shared library <plugin_name>.Head<suffix> next to the
# executable. It has the plugin and the bindings in it. StartDotNetPlugin (Urho3D/CSharp/DotNetPlugin.h) gives its path
# to DotNetHost::Initialize, which loads it and calls sharpcpp_rbfx_head_attach. There is no hostfxr and no runtimeconfig.
function(_rbfx_csharp_add_desktop_shim_application name)
  # The RID of the head. NativeAOT does not cross-compile to another operating system.
  if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
    set(rid_os linux)
  elseif(CMAKE_SYSTEM_NAME STREQUAL "Darwin")
    set(rid_os osx)
  elseif(CMAKE_SYSTEM_NAME STREQUAL "Windows")
    set(rid_os win)
  else()
    message(FATAL_ERROR "rbfx_csharp_package_application(${name}): the desktop shim head does not support '${CMAKE_SYSTEM_NAME}'.")
  endif()
  set(processor "${CMAKE_SYSTEM_PROCESSOR}")
  if(MSVC)
    # CMAKE_SYSTEM_PROCESSOR is the processor of the host with a Visual Studio generator and -A ARM64 or -A Win32.
    # The platform of the generator (x64, ARM64, Win32) or the architecture of the compiler is the target.
    if(CMAKE_VS_PLATFORM_NAME)
      set(processor "${CMAKE_VS_PLATFORM_NAME}")
    elseif(CMAKE_CXX_COMPILER_ARCHITECTURE_ID)
      set(processor "${CMAKE_CXX_COMPILER_ARCHITECTURE_ID}")
    elseif(CMAKE_C_COMPILER_ARCHITECTURE_ID)
      set(processor "${CMAKE_C_COMPILER_ARCHITECTURE_ID}")
    endif()
  endif()
  if(APPLE AND CMAKE_OSX_ARCHITECTURES)
    list(LENGTH CMAKE_OSX_ARCHITECTURES architecture_count)
    if(NOT architecture_count EQUAL 1)
      message(FATAL_ERROR "rbfx_csharp_package_application(${name}): the desktop shim head needs one architecture in CMAKE_OSX_ARCHITECTURES.")
    endif()
    set(processor "${CMAKE_OSX_ARCHITECTURES}")
  endif()
  string(TOLOWER "${processor}" processor)
  if(processor MATCHES "^(x86_64|amd64|x64)$")
    set(rid_arch x64)
  elseif(processor MATCHES "^(aarch64|arm64)$")
    set(rid_arch arm64)
  elseif(rid_os STREQUAL "win" AND processor MATCHES "^(x86|win32)$")
    set(rid_arch x86)
  else()
    message(FATAL_ERROR "rbfx_csharp_package_application(${name}): the desktop shim head does not support the processor '${processor}'.")
  endif()
  set(rid "${rid_os}-${rid_arch}")

  set(template_dir "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/heads/desktop")
  set(head_dir "${CMAKE_BINARY_DIR}/heads/${name}")

  # The head project.
  set(RBFX_HEAD_NAME "${name}")
  set(RBFX_HEAD_RID "${rid}")
  set(RBFX_HEAD_TEMPLATE_DIR "${template_dir}")
  set(RBFX_HEAD_PLUGIN "${plugin_project}")
  set(RBFX_HEAD_PLUGIN_NAME "${plugin_name}")
  _rbfx_csharp_wrapper_trim_roots(RBFX_HEAD_USERCODE_ROOTS ${arg_WRAPPER_TARGETS})
  set(head_project "${head_dir}/${name}.Head.csproj")
  configure_file("${template_dir}/Head.Desktop.csproj.in" "${head_project}" @ONLY)
  # Empty files stop MSBuild from importing the files of a parent folder (a build folder in a repository).
  foreach(build_file Directory.Build.props Directory.Build.targets)
    file(CONFIGURE OUTPUT "${head_dir}/${build_file}" CONTENT "<Project />\n" @ONLY NEWLINE_STYLE UNIX)
  endforeach()

  _rbfx_csharp_add_desktop_native_copy(${name})

  # The head build. dotnet publish compiles the head, the plugin and the bindings into one native library. NativeAOT
  # gives it the assembly name and the suffix of the platform, without the prefix lib.
  rbfx_csharp_msbuild_configuration_arguments(configuration_arguments)
  set(head_file "${plugin_name}.Head${CMAKE_SHARED_LIBRARY_SUFFIX}")
  set(published_head "${head_dir}/publish/$<CONFIG>/${head_file}")
  set(dotnet_arguments
    publish "${head_project}"
    ${configuration_arguments}
    --nologo
    -p:RbfxNativeBuildDriver=cmake
    "-p:RbfxNativeBuildDir=${CMAKE_BINARY_DIR}"
    -p:UseSharedCompilation=false
    -nodeReuse:false)
  foreach(property IN LISTS RBFX_CSHARP_MSBUILD_PROPERTIES plugin_properties)
    list(APPEND dotnet_arguments "-p:${property}")
  endforeach()
  if(RBFX_CSHARP_BUILD_MANAGED)
    add_custom_target(
      ${name}.Head
      COMMAND "${CMAKE_COMMAND}" -E env --unset=TARGETNAME RbfxNativeBuildDriver=cmake "${DOTNET_EXECUTABLE}" ${dotnet_arguments}
      COMMAND "${CMAKE_COMMAND}" -E copy_if_different "${published_head}" "$<TARGET_FILE_DIR:${name}>/${head_file}"
      WORKING_DIRECTORY "${head_dir}"
      COMMENT "Publishing the NativeAOT desktop head of ${name} with dotnet"
      VERBATIM)
  else()
    add_custom_target(${name}.Head)
  endif()
  add_dependencies(${name}.Head ${name})
  if(arg_WRAPPERS)
    add_dependencies(${name}.Head ${arg_WRAPPERS})
  endif()
  foreach(dependency IN ITEMS sharpcpp-rbfx-build-managed sharpcpp-rbfx-runtime)
    if(TARGET ${dependency})
      add_dependencies(${name}.Head ${dependency})
    endif()
  endforeach()
  if(NOT SharpCppRbfx_IS_SDK)
    add_dependencies(${name}.Head SharpCpp::RbfxNative SharpCpp::RbfxHost)
  endif()

  # The desktop application is the executable in its folder, so there is no separate output. The install rule
  # (rbfx_csharp_install_application) copies the head library SHARPCPP_RBFX_HEAD_LIBRARY next to the executable.
  set_target_properties(
    ${name}
    PROPERTIES
      SHARPCPP_RBFX_APPLICATION_OUTPUT ""
      SHARPCPP_RBFX_HEAD_LIBRARY "${published_head}")
  add_custom_target(${name}.CSharp ALL DEPENDS ${name}.Head ${name}.Native)
endfunction()


# The Android part of rbfx_csharp_package_application. It reads the variables arg_*, plugin_name and
# plugin_properties of the caller.
# Writes the trimmer root items of the wrapper targets into RBFX_HEAD_USERCODE_ROOTS (a head project fragment). Only native code
# calls the entry points of these managed assemblies, so the trimmer must keep them.
function(_rbfx_csharp_wrapper_trim_roots out_variable)
  set(items "")
  foreach(wrapper IN LISTS ARGN)
    if(NOT TARGET ${wrapper})
      message(FATAL_ERROR "rbfx_csharp_package_application: wrapper target '${wrapper}' does not exist. Call rbfx_csharp_add_wrapper first.")
    endif()
    get_target_property(assembly ${wrapper} SHARPCPP_RBFX_WRAPPER_ASSEMBLY)
    if(assembly)
      string(APPEND items "    <TrimmerRootAssembly Include=\"${assembly}\" RootMode=\"All\" />\n")
    endif()
  endforeach()
  set(${out_variable} "${items}" PARENT_SCOPE)
endfunction()

function(_rbfx_csharp_add_android_application name)
  get_property(is_multi_config GLOBAL PROPERTY GENERATOR_IS_MULTI_CONFIG)
  if(is_multi_config)
    message(FATAL_ERROR "rbfx_csharp_package_application(${name}): Android needs a single-configuration generator (for example Ninja).")
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
    message(FATAL_ERROR "rbfx_csharp_package_application(${name}): the Android ABI '${abi}' is not known.")
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
    message(FATAL_ERROR "rbfx_csharp_package_application(${name}): the SDL Java sources are not found. Set RBFX_CSHARP_ANDROID_JAVA_DIR.")
  endif()
  set(RBFX_CSHARP_ANDROID_SDK_DIR "$ENV{ANDROID_HOME}" CACHE PATH "Android SDK for the .NET Android head (AndroidSdkDirectory)")
  set(RBFX_CSHARP_JAVA_SDK_DIR "$ENV{JAVA_HOME}" CACHE PATH "JDK for the .NET Android head (JavaSdkDirectory)")

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
      message(FATAL_ERROR "rbfx_csharp_package_application(${name}): libc++_shared.so is not found in the NDK sysroot.")
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
  foreach(library IN LISTS launcher_libraries)
    if(TARGET ${library} AND NOT library IN_LIST library_targets)
      get_target_property(library_type ${library} TYPE)
      if(library_type MATCHES "^(SHARED|MODULE)_LIBRARY$")
        list(APPEND libraries "$<TARGET_FILE:${library}>")
        list(APPEND library_targets ${library})
      endif()
    endif()
  endforeach()
  # The native plugins and the shared libraries of the wrapper targets. The Java activity loads them before the
  # application library.
  set(plugin_targets)
  foreach(library IN LISTS arg_PLUGINS arg_WRAPPERS)
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
  foreach(resource IN LISTS arg_RESOURCES)
    get_filename_component(resource "${resource}" ABSOLUTE BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}")
    get_filename_component(resource_name "${resource}" NAME)
    if(IS_DIRECTORY "${resource}")
      string(APPEND items "    <AndroidAsset Include=\"${resource}/**/*\" Link=\"Assets/${resource_name}/%(RecursiveDir)%(Filename)%(Extension)\" />\n")
    else()
      string(APPEND items "    <AndroidAsset Include=\"${resource}\" Link=\"Assets/${resource_name}\" />\n")
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
  _rbfx_csharp_wrapper_trim_roots(RBFX_HEAD_USERCODE_ROOTS ${arg_WRAPPER_TARGETS})
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

  # The head build. CMake defines the configuration set (rbfx_csharp_msbuild_configuration_arguments). The workload of .NET
  # for Android tests Debug and Release only, so the head project maps each other configuration to Release (Head.Android.csproj.in).
  rbfx_csharp_msbuild_configuration_arguments(configuration_arguments)
  set(apk "${head_dir}/bin/$<CONFIG>/${application_id}-Signed.apk")
  set(dotnet_arguments
    build "${head_project}"
    ${configuration_arguments}
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
  foreach(property IN LISTS RBFX_CSHARP_MSBUILD_PROPERTIES plugin_properties)
    list(APPEND dotnet_arguments "-p:${property}")
  endforeach()
  if(RBFX_CSHARP_BUILD_MANAGED)
    add_custom_target(
      ${name}.Head
      COMMAND "${CMAKE_COMMAND}" -E make_directory "${library_dir}"
      COMMAND "${CMAKE_COMMAND}" -E copy_if_different ${libraries} "${library_dir}/"
      COMMAND "${CMAKE_COMMAND}" -E env --unset=TARGETNAME RbfxNativeBuildDriver=cmake "${DOTNET_EXECUTABLE}" ${dotnet_arguments}
      COMMAND "${CMAKE_COMMAND}" -E copy_if_different "${apk}" "${output_dir}/${name}.apk"
      BYPRODUCTS "${output_dir}/${name}.apk"
      WORKING_DIRECTORY "${head_dir}"
      COMMENT "Building the Android head of ${name} with dotnet"
      VERBATIM)
  else()
    add_custom_target(${name}.Head)
  endif()
  add_dependencies(${name}.Head ${name})
  if(arg_WRAPPERS)
    add_dependencies(${name}.Head ${arg_WRAPPERS})
  endif()
  # The head packs the shared libraries of the plugins, so they must be built first.
  if(plugin_targets)
    add_dependencies(${name}.Head ${plugin_targets})
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

# The Emscripten part of rbfx_csharp_package_application. It reads the variables arg_*, plugin_name and
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
    message(FATAL_ERROR "rbfx_csharp_package_application(${name}): Emscripten needs a single-configuration generator (for example Ninja).")
  endif()
  get_target_property(engine_type Urho3D TYPE)
  if(NOT engine_type STREQUAL "STATIC_LIBRARY")
    message(FATAL_ERROR "rbfx_csharp_package_application(${name}): Emscripten needs the static engine SDK.")
  endif()

  set(template_dir "${CMAKE_CURRENT_FUNCTION_LIST_DIR}/heads/wasm")
  set(head_dir "${CMAKE_BINARY_DIR}/heads/${name}")
  # The C name of the native entry. heads/wasm/RbfxWebMain.cpp defines it.
  set(entry rbfx_application_start)

  # The RESOURCES. The head build copies them next to index.html (heads/wasm/CopyResources.cmake).
  set(resource_paths)
  foreach(resource IN LISTS arg_RESOURCES)
    get_filename_component(resource "${resource}" ABSOLUTE BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}")
    list(APPEND resource_paths "${resource}")
  endforeach()
  list(JOIN resource_paths "\n" resource_text)
  file(GENERATE OUTPUT "${head_dir}/resources.txt" CONTENT "${resource_text}\n")

  # The head project and the web files.
  set(RBFX_HEAD_NAME "${name}")
  set(RBFX_HEAD_ENTRY "${entry}")
  set(RBFX_HEAD_TEMPLATE_DIR "${template_dir}")
  set(RBFX_HEAD_PLUGIN "${plugin_project}")
  set(RBFX_HEAD_PLUGIN_NAME "${plugin_name}")
  _rbfx_csharp_wrapper_trim_roots(RBFX_HEAD_USERCODE_ROOTS ${arg_WRAPPER_TARGETS})
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
  rbfx_csharp_msbuild_configuration_arguments(configuration_arguments)
  set(dotnet_arguments
    publish "${head_project}"
    ${configuration_arguments}
    --nologo
    -p:RbfxNativeBuildDriver=cmake
    "-p:RbfxNativeBuildDir=${CMAKE_BINARY_DIR}"
    -p:RbfxTargetRid=browser-wasm
    -p:UseSharedCompilation=false
    -nodeReuse:false)
  foreach(property IN LISTS RBFX_CSHARP_MSBUILD_PROPERTIES plugin_properties)
    list(APPEND dotnet_arguments "-p:${property}")
  endforeach()
  if(RBFX_CSHARP_BUILD_MANAGED)
    add_custom_target(
      ${name}.Head
      COMMAND "${CMAKE_COMMAND}" -E env --unset=TARGETNAME RbfxNativeBuildDriver=cmake "${DOTNET_EXECUTABLE}" ${dotnet_arguments}
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
  if(arg_WRAPPERS)
    add_dependencies(${name}.Head ${arg_WRAPPERS})
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

# The iOS part of rbfx_csharp_package_application. It reads the variables arg_*, plugin_name and
# plugin_properties of the caller.
#
# The entry of the application: an iOS app has one executable, and the .NET iOS SDK links it. So the runtime starts
# first, and Program.Main of the head attaches the managed side before the native code calls DotNetHost::Initialize.
# Then Program.Main calls the native entry. It gives SDL_main to SDL_UIKitRunApp, which runs UIApplicationMain with
# the application delegate of SDL. The engine main loop then runs in the callbacks of UIKit.
function(_rbfx_csharp_add_ios_application name)
  get_property(is_multi_config GLOBAL PROPERTY GENERATOR_IS_MULTI_CONFIG)
  if(is_multi_config)
    message(FATAL_ERROR "rbfx_csharp_package_application(${name}): iOS needs a single-configuration generator (for example Ninja).")
  endif()
  get_target_property(engine_type Urho3D TYPE)
  if(NOT engine_type STREQUAL "STATIC_LIBRARY")
    message(FATAL_ERROR "rbfx_csharp_package_application(${name}): iOS needs the static engine SDK.")
  endif()
  # The engine SDK and the SharpCpp.Rbfx SDK of iOS are for devices (ios-arm64).
  if(CMAKE_OSX_SYSROOT MATCHES "[Ss]imulator" OR (CMAKE_OSX_ARCHITECTURES AND NOT CMAKE_OSX_ARCHITECTURES STREQUAL "arm64"))
    message(FATAL_ERROR "rbfx_csharp_package_application(${name}): iOS supports devices with arm64 only (ios-arm64).")
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


  # The RESOURCES. They go into the root folder of the app bundle.
  set(items "")
  foreach(resource IN LISTS arg_RESOURCES)
    get_filename_component(resource "${resource}" ABSOLUTE BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}")
    get_filename_component(resource_name "${resource}" NAME)
    if(IS_DIRECTORY "${resource}")
      string(APPEND items "    <BundleResource Include=\"${resource}/**/*\" Link=\"${resource_name}/%(RecursiveDir)%(Filename)%(Extension)\" />\n")
    else()
      string(APPEND items "    <BundleResource Include=\"${resource}\" Link=\"${resource_name}\" />\n")
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
  _rbfx_csharp_wrapper_trim_roots(RBFX_HEAD_USERCODE_ROOTS ${arg_WRAPPER_TARGETS})
  set(RBFX_HEAD_ITEMS "${items}")
  set(head_project "${head_dir}/${name}.Head.csproj")
  configure_file("${template_dir}/Head.Ios.csproj.in" "${head_project}" @ONLY)

  # The native items need the complete link closure (see the Emscripten part).
  set(defer_code "cmake_language(DEFER DIRECTORY [==[${CMAKE_CURRENT_SOURCE_DIR}]==] CALL _rbfx_csharp_write_ios_native_items [==[${name}]==] [==[${head_dir}/NativeItems.props]==]")
  foreach(wrapper IN LISTS arg_WRAPPERS)
    string(APPEND defer_code " [==[${wrapper}]==]")
  endforeach()
  cmake_language(EVAL CODE "${defer_code})")

  if(CMAKE_RUNTIME_OUTPUT_DIRECTORY)
    set(output_dir "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}")
  else()
    set(output_dir "${CMAKE_CURRENT_BINARY_DIR}")
  endif()

  # The head build. The bindings of a source build must compile for ios-arm64 (RbfxTargetRid, see
  # eng/SharpCpp.Rbfx.Build.props): then their import name is __Internal. The app bundle has the assembly name.
  rbfx_csharp_msbuild_configuration_arguments(configuration_arguments)
  set(app_bundle "${head_dir}/bin/$<CONFIG>/${name}.Head.app")
  set(dotnet_arguments
    build "${head_project}"
    ${configuration_arguments}
    --nologo
    -p:RbfxNativeBuildDriver=cmake
    "-p:RbfxNativeBuildDir=${CMAKE_BINARY_DIR}"
    "-p:RbfxTargetRid=${rid}"
    -p:UseSharedCompilation=false
    -nodeReuse:false)
  foreach(property IN LISTS RBFX_CSHARP_MSBUILD_PROPERTIES plugin_properties)
    list(APPEND dotnet_arguments "-p:${property}")
  endforeach()
  if(RBFX_CSHARP_BUILD_MANAGED)
    add_custom_target(
      ${name}.Head
      COMMAND "${CMAKE_COMMAND}" -E env --unset=TARGETNAME RbfxNativeBuildDriver=cmake "${DOTNET_EXECUTABLE}" ${dotnet_arguments}
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
  if(arg_WRAPPERS)
    add_dependencies(${name}.Head ${arg_WRAPPERS})
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
        message(FATAL_ERROR "rbfx_csharp_package_application(${name}): ${item} is a shared library. The ${kind} head links static libraries only.")
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
    elseif(item STREQUAL "-framework" OR item STREQUAL "-weak_framework")
      # The name of the framework is the next item.
      set(framework_option "${item}")
      list(POP_FRONT queue item)
      list(LENGTH queue count)
      if(NOT item STREQUAL "")
        list(APPEND flags "${framework_option} ${item}")
      endif()
    elseif(item MATCHES "^-" OR item MATCHES "^SHELL:" OR item MATCHES "\\$<")
      list(APPEND flags "${item}")
    elseif(IS_ABSOLUTE "${item}" AND (item MATCHES "(^|/)([^/]+)\\.framework(/([^/]+))?$"))
      # The static libraries of the engine link a system framework as a path:
      # <sdk>/System/Library/Frameworks/<Name>.framework/<Name>. The SDK of Apple has the stub <Name>.tbd in place of
      # the binary, so the path does not exist. The linker gets the framework by name.
      set(framework_name "${CMAKE_MATCH_2}")
      list(APPEND flags "-framework ${framework_name}")
    elseif(IS_ABSOLUTE "${item}" AND (item MATCHES "\\.tbd$"))
      # A text stub of the SDK: <sdk>/usr/lib/lib<name>.tbd links as -l<name>.
      get_filename_component(stub_name "${item}" NAME_WE)
      string(REGEX REPLACE "^lib" "" stub_name "${stub_name}")
      list(APPEND flags "-l${stub_name}")
    elseif(IS_ABSOLUTE "${item}")
      list(APPEND files "${item}")
    elseif(item MATCHES "::")
      message(FATAL_ERROR "rbfx_csharp_package_application(${name}): the link item ${item} is not a target.")
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

  set(content "<Project>\n  <!-- Written by rbfx_csharp_package_application (RbfxCSharpApplication.cmake). Do not edit. -->\n")
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

# Writes NativeItems.props of the iOS head of <name> (see above). ARGN has the wrapper targets. Every static library
# of the link closure becomes a static NativeReference. The archives of <name>, of the wrapper, of the host library and
# of the wrapper targets are force-loaded: the managed side finds their exports with __Internal. The other archives
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

  set(content "<Project>\n  <!-- Written by rbfx_csharp_package_application (RbfxCSharpApplication.cmake). Do not edit. -->\n  <ItemGroup>\n")
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
