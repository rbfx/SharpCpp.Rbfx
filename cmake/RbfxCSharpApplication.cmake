# The folder of this file. The templates of the application heads are in heads/ next to it.
set(_rbfx_csharp_application_dir "${CMAKE_CURRENT_LIST_DIR}")

# One call per application: native executable that hosts the .NET runtime and loads a C# plugin.
# On Android the call makes an APK: a .NET Android head starts the runtime and loads the native application.
# On Emscripten the call makes a web folder: a .NET WebAssembly head starts the runtime, and the native application
# is linked into the runtime.
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
#               Accepted and ignored on desktop. Android uses them, Emscripten uses RESOURCES (see below).
#
# Android (single-configuration generator, NDK toolchain, SharpCpp.Rbfx built in shim mode):
# - Creates the shared library <name> (lib<name>.so) from SOURCES. URHO3D_DEFINE_APPLICATION_MAIN exports
#   SDL_main from it. The library links with Urho3D, SharpCpp::RbfxHost and LINK_LIBRARIES.
# - Generates the .NET Android head project in <build>/heads/<name>/ from the templates in heads/android/
#   next to this file. The head references PLUGIN, packs the native libraries (the C++ runtime, Urho3D,
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
#   link closure of <name> and of SharpCpp::RbfxNative into NativeItems.props of the head. The head references PLUGIN.
# - main.js of the head starts the runtime, writes the RESOURCES into the file system of Emscripten and runs
#   Program.Main. Program.Main runs the module initializer of the bindings, so DotNetHost::Initialize finds the
#   runtime. Then main.js calls rbfx_application_start on the browser thread. The engine main loop then runs there.
# - A file in RESOURCES becomes a file in the root folder with its file name. A folder becomes a folder with its
#   name. A generated file in RESOURCES must come from a custom command in the top-level CMakeLists.txt.
# - The target <name>.CSharp publishes the head with dotnet (Release) and copies the web folder (index.html, main.js,
#   _framework and the RESOURCES) to <name>/ in the runtime output folder (CMAKE_RUNTIME_OUTPUT_DIRECTORY).
#   The runtime uses threads, so the web server must send the headers of cross-origin isolation.
function(rbfx_csharp_add_application name)
  cmake_parse_arguments(
    PARSE_ARGV 1 arg "CONSOLE" "PLUGIN;APPLICATION_ID" "SOURCES;USERCODE;LINK_LIBRARIES;RESOURCES;PROPERTIES")
  if(NOT arg_SOURCES OR NOT arg_PLUGIN)
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): SOURCES and PLUGIN are required.")
  endif()
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()
  if(CMAKE_SYSTEM_NAME MATCHES "^(iOS|tvOS|visionOS)$")
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): only the desktop platforms, Android and Emscripten are supported.")
  endif()

  get_filename_component(plugin_name "${arg_PLUGIN}" NAME_WLE)

  # Properties of the C# project. The project reads them as MSBuild properties.
  if(SharpCppRbfx_IS_SDK)
    set(plugin_properties SharpCppRbfxIsSdk=true "SharpCppRbfxManagedDir=${SharpCppRbfx_MANAGED_DIR}")
  else()
    set(plugin_properties SharpCppRbfxIsSdk=false "SharpCppRbfxPackageRoot=${SharpCppRbfx_PACKAGE_ROOT}")
  endif()

  if(ANDROID)
    _rbfx_csharp_add_android_application(${name})
    return()
  endif()
  if(EMSCRIPTEN)
    _rbfx_csharp_add_wasm_application(${name})
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
  if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
    # Urho3D must be a startup dependency of the executable. The runtime loads the libraries of the
    # bindings later with dlopen, and then the TLS block of Urho3D does not fit into the static TLS.
    target_link_options(${name} PRIVATE LINKER:--no-as-needed)
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

# The Android part of rbfx_csharp_add_application. It reads the variables arg_*, plugin_name and
# plugin_properties of the caller.
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
  set(template_dir "${_rbfx_csharp_application_dir}/heads/android")
  set(RBFX_HEAD_NAME "${name}")
  set(RBFX_HEAD_RID "${rid}")
  set(RBFX_HEAD_MIN_API "${min_api}")
  set(RBFX_HEAD_APPLICATION_ID "${application_id}")
  set(RBFX_HEAD_DIR "${head_dir}")
  set(RBFX_HEAD_TEMPLATE_DIR "${template_dir}")
  set(RBFX_HEAD_SDL_JAVA_DIR "${RBFX_CSHARP_ANDROID_JAVA_DIR}")
  get_filename_component(RBFX_HEAD_PLUGIN "${arg_PLUGIN}" ABSOLUTE BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}")
  set(RBFX_HEAD_PLUGIN_NAME "${plugin_name}")
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
  if(resource_files)
    # A file in RESOURCES can be the output of a custom command (for example create_pak of the engine).
    # Only a target in the directory of the command runs it. The usual place is the top-level
    # CMakeLists.txt, so the target <name>.Resources is made there, at the end of the directory.
    # A deferred call reads the variables when it runs, so the values go into the code now.
    set(defer_code "cmake_language(DEFER DIRECTORY [==[${CMAKE_SOURCE_DIR}]==] CALL _rbfx_csharp_add_resource_target [==[${name}.Resources]==]")
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

  set(template_dir "${_rbfx_csharp_application_dir}/heads/wasm")
  set(head_dir "${CMAKE_BINARY_DIR}/heads/${name}")
  # The C name of the native entry. heads/wasm/RbfxWebMain.cpp defines it.
  set(entry rbfx_application_start)

  # The application code. An object library links all its objects, as an executable does. So a source file with
  # only static registrations stays in the link. The definition renames main of URHO3D_DEFINE_APPLICATION_MAIN,
  # because the .NET runtime defines main.
  add_library(${name} OBJECT ${arg_SOURCES} "${template_dir}/RbfxWebMain.cpp")
  target_link_libraries(${name} PRIVATE Urho3D SharpCpp::RbfxHost ${arg_LINK_LIBRARIES})
  target_compile_definitions(${name} PRIVATE "SHARPCPP_RBFX_PLUGIN_NAME=\"${plugin_name}\"" main=rbfx_application_main)
  # The network code of the engine needs the JavaScript libraries of libdatachannel-wasm (as web_executable of the
  # engine adds them). CMake does not link an object library, so these options only go to the head.
  if(TARGET datachannel-wasm)
    foreach(candidate IN ITEMS
        "${Urho3D_PACKAGE_ROOT}/include/libdatachannel-wasm/wasm/js"
        "${rbfx_SOURCE_DIR}/Source/ThirdParty/libdatachannel-wasm/wasm/js")
      if(EXISTS "${candidate}/webrtc.js")
        target_link_options(${name} PRIVATE
          "SHELL:--js-library \"${candidate}/webrtc.js\""
          "SHELL:--js-library \"${candidate}/websocket.js\"")
        break()
      endif()
    endforeach()
  endif()

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
  get_filename_component(RBFX_HEAD_PLUGIN "${arg_PLUGIN}" ABSOLUTE BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}")
  set(RBFX_HEAD_PLUGIN_NAME "${plugin_name}")
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
    set(defer_code "cmake_language(DEFER DIRECTORY [==[${CMAKE_SOURCE_DIR}]==] CALL _rbfx_csharp_add_resource_target [==[${name}.Resources]==]")
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

  add_custom_target(${name}.CSharp ALL DEPENDS ${name}.Head)
endfunction()

# Writes NativeItems.props of the WebAssembly head of <name> (see above). It walks the link closure of the object
# library <name> and of SharpCpp::RbfxNative. A static library becomes a NativeFileReference, and an object library
# gives its objects. A flag or a system library (-l<name>) and the INTERFACE_LINK_OPTIONS of the targets go to
# EmccExtraLDFlags. The file names keep the names of the libraries, so the P/Invoke table of the runtime finds
# libSharpCpp.Rbfx.Native and libSharpCpp.Rbfx.Host. Generator expressions are evaluated in the context of <name>.
function(_rbfx_csharp_write_wasm_native_items name output)
  set(files "$<TARGET_OBJECTS:${name}>")
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
  list(APPEND queue SharpCpp::RbfxNative)
  set(visited ${name})
  list(LENGTH queue count)
  while(count GREATER 0)
    list(POP_FRONT queue item)
    list(LENGTH queue count)
    if(item MATCHES "^\\$<LINK_ONLY:(.*)>$")
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
        list(APPEND files "$<TARGET_OBJECTS:${item}>")
      elseif(type MATCHES "^(SHARED|MODULE)_LIBRARY$")
        message(FATAL_ERROR "rbfx_csharp_add_application(${name}): ${item} is a shared library. The WebAssembly head links static libraries only.")
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

# The target that makes the files in RESOURCES of an Android or Emscripten application (see above).
function(_rbfx_csharp_add_resource_target target)
  add_custom_target(${target} DEPENDS ${ARGN})
endfunction()
