# The folder of this file. The templates of the application heads are in heads/ next to it.
set(_rbfx_csharp_application_dir "${CMAKE_CURRENT_LIST_DIR}")

# One call per application: native executable that hosts the .NET runtime and loads a C# plugin.
# On Android the call makes an APK: a .NET Android head starts the runtime and loads the native application.
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
#               Accepted and ignored on desktop. Android uses them (see below).
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
function(rbfx_csharp_add_application name)
  cmake_parse_arguments(
    PARSE_ARGV 1 arg "CONSOLE" "PLUGIN;APPLICATION_ID" "SOURCES;USERCODE;LINK_LIBRARIES;RESOURCES;PROPERTIES")
  if(NOT arg_SOURCES OR NOT arg_PLUGIN)
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): SOURCES and PLUGIN are required.")
  endif()
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()
  if(EMSCRIPTEN OR CMAKE_SYSTEM_NAME MATCHES "^(iOS|tvOS|visionOS)$")
    message(FATAL_ERROR "rbfx_csharp_add_application(${name}): only the desktop platforms and Android are supported.")
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

# The target that makes the files in RESOURCES of an Android application (see above).
function(_rbfx_csharp_add_resource_target target)
  add_custom_target(${target} DEPENDS ${ARGN})
endfunction()
