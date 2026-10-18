# One call per project: the C# application with its bindings, its data and its install rules.
# The module calls rbfx_csharp_add_bindings (RbfxCSharpAddBindings.cmake), rbfx_csharp_add_plugin (RbfxCSharpPlugin.cmake),
# rbfx_csharp_add_launcher (RbfxCSharpLauncher.cmake), rbfx_csharp_add_data (RbfxCSharpData.cmake) and
# rbfx_csharp_package_application (RbfxCSharpApplication.cmake).
#
# rbfx_csharp_find_core_data(<out_var>)
#   Sets <out_var> to the folder CoreData of the rbfx SDK. The first choice is ${Urho3D_PACKAGE_ROOT}/bin/CoreData.
#   Some mobile SDKs have no CoreData. Then the function looks for bin/CoreData in the folders of CMAKE_PREFIX_PATH
#   (the host SDK). Without a result, <out_var> is <out_var>-NOTFOUND.
#
# rbfx_csharp_install_application(<name> [DESTINATION <dir>])
#   Adds the install rules of a launcher that rbfx_csharp_package_application packaged. The default DESTINATION is
#   bin/$<CONFIG>. The function reads the properties SHARPCPP_RBFX_PACKAGED_PLUGIN and SHARPCPP_RBFX_PACKAGED_DATA of the
#   launcher, the properties of the plugin target (rbfx_csharp_add_plugin) and the properties of the data target
#   (rbfx_csharp_add_data).
#   Desktop: the executable, the shared engine library, the native bindings library, the shared host library
#   with the shared nethost library, the folders of the data (SHARPCPP_RBFX_DATA_DIRECTORIES, with CoreData) and its
#   files (SHARPCPP_RBFX_DATA_FILES: the ResourceRoot.ini that maps the folders), the shared libraries of the bindings
#   targets (SHARPCPP_RBFX_PLUGIN_REFERENCES of the plugin) and of the native plugins (SHARPCPP_RBFX_PLUGINS of the
#   launcher), the plugin assembly with its runtimeconfig and deps files, the assemblies of the bindings targets and the
#   SharpCpp assemblies, all in DESTINATION. In the shim mode the NativeAOT head library <plugin>.Head<suffix>
#   (SHARPCPP_RBFX_HEAD_LIBRARY of the launcher) is in DESTINATION too, and the install has no nethost library.
#   Android: the APK. Emscripten: the web folder, as DESTINATION/<name>. iOS: the app bundle.
#   The function stops with an error when <name> is not from rbfx_csharp_package_application.
#
# rbfx_csharp_add_project(<name>
#   [SOURCES <files...>]
#   [CSHARP_DIR <dir>]
#   [CSPROJ <csproj>]
#   [HEADERS <headers...>]
#   [INCLUDE_DIRECTORIES <dirs...>]
#   [IMPLEMENTATION <target>]
#   [SEPARATE_LIBRARY]
#   [NAMESPACE <namespace>]
#   [BINDINGS_TARGETS <targets...>]
#   [PLUGINS <targets...>]
#   [LINK_LIBRARIES <libraries...>]
#   [DATA <dirs...>]
#   [RESOURCES <files...>]
#   [APPLICATION_ID <id>]
#   [CONSOLE]
#   [PROPERTIES <Name=Value>...]
#   [INSTALL]
#   [INSTALL_DESTINATION <dir>])
#
#   A relative path is relative to CMAKE_CURRENT_SOURCE_DIR.
#   The function calls these functions in this order:
#   1. rbfx_csharp_add_bindings(<name>.Bindings), only with HEADERS.
#   2. rbfx_csharp_add_plugin(<plugin>): CSHARP_DIR, CSPROJ, PROPERTIES, and the bindings and BINDINGS_TARGETS as REFERENCES. The plugin target
#      is <name>.Net, or the file name of CSPROJ without the extension.
#   3. rbfx_csharp_add_launcher(<name>): SOURCES, PLUGINS, LINK_LIBRARIES and CONSOLE.
#   4. rbfx_csharp_add_data(<name>.Data): DATA as DIRECTORIES. The data target is always made, so CoreData is also
#      packed without DATA.
#   5. rbfx_csharp_package_application(<name>): the plugin, the data, RESOURCES and APPLICATION_ID.
#   HEADERS         The C++ headers to bind to C#. The bindings target is <name>.Bindings.
#   INCLUDE_DIRECTORIES  Include directories of HEADERS. The default is the folders of HEADERS.
#   IMPLEMENTATION  The target that implements HEADERS. By default the generated C++ code is a source of this target
#                   (integration mode, see rbfx_csharp_add_bindings), so it must be a shared or module library, and it
#                   must use the keyword signature of target_link_libraries.
#   SEPARATE_LIBRARY  Passed to rbfx_csharp_add_bindings: the generated code goes into the separate library
#                   <name>.Bindings.Native, and IMPLEMENTATION is linked to it (also a static library).
#   NAMESPACE       The C# namespace of the bindings.
#   BINDINGS_TARGETS Targets that rbfx_csharp_add_bindings already made, for example the bindings of another plugin. They
#                   are REFERENCES of the plugin after the bindings of the project. The packaging and the install rules
#                   handle them in the same way as the bindings of the project. Bindings in the same namespace need
#                   different BINDINGS_CLASS values (the default is different for each target name).
#   DATA            Resource folders. They go to rbfx_csharp_add_data(<name>.Data) as DIRECTORIES. It packs the folders
#                   and CoreData into paks on Emscripten, Android and iOS. On desktop the install rules copy the folders
#                   (rbfx_csharp_install_application). See RbfxCSharpData.cmake.
#   INSTALL         Calls rbfx_csharp_install_application(<name>), with INSTALL_DESTINATION as DESTINATION.
#   The other keywords have the meaning that is in the module of the function that gets them.

function(rbfx_csharp_find_core_data out_var)
  set(core_data "${Urho3D_PACKAGE_ROOT}/bin/CoreData")
  if(NOT EXISTS "${core_data}")
    # The cache variable keeps the search result.
    find_path(_RBFX_CSHARP_CORE_DATA_ROOT bin/CoreData PATHS ${CMAKE_PREFIX_PATH} NO_DEFAULT_PATH NO_CMAKE_FIND_ROOT_PATH)
    if(_RBFX_CSHARP_CORE_DATA_ROOT)
      set(core_data "${_RBFX_CSHARP_CORE_DATA_ROOT}/bin/CoreData")
    else()
      set(core_data "${out_var}-NOTFOUND")
    endif()
  endif()
  set(${out_var} "${core_data}" PARENT_SCOPE)
endfunction()

function(rbfx_csharp_install_application name)
  cmake_parse_arguments(PARSE_ARGV 1 arg "" "DESTINATION" "")
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_install_application(${name}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()
  if(NOT arg_DESTINATION)
    set(arg_DESTINATION "bin/$<CONFIG>")
  endif()
  if(NOT TARGET ${name})
    message(FATAL_ERROR "rbfx_csharp_install_application(${name}): the target does not exist. Call rbfx_csharp_package_application first.")
  endif()
  get_target_property(plugin ${name} SHARPCPP_RBFX_PACKAGED_PLUGIN)
  get_target_property(output ${name} SHARPCPP_RBFX_APPLICATION_OUTPUT)
  if(NOT plugin OR output STREQUAL "output-NOTFOUND")
    message(FATAL_ERROR "rbfx_csharp_install_application(${name}): the target was not packaged by rbfx_csharp_package_application.")
  endif()
  get_target_property(plugin_name ${plugin} SHARPCPP_RBFX_PLUGIN_NAME)
  get_target_property(data ${name} SHARPCPP_RBFX_PACKAGED_DATA)

  if(ANDROID)
    install(FILES "${output}" DESTINATION "${arg_DESTINATION}")
    return()
  endif()
  if(EMSCRIPTEN)
    install(DIRECTORY "${output}/" DESTINATION "${arg_DESTINATION}/${name}")
    return()
  endif()
  if(CMAKE_SYSTEM_NAME STREQUAL "iOS")
    install(DIRECTORY "${output}" DESTINATION "${arg_DESTINATION}" USE_SOURCE_PERMISSIONS)
    return()
  endif()

  get_target_property(bindings ${plugin} SHARPCPP_RBFX_PLUGIN_REFERENCES)
  get_target_property(plugins ${name} SHARPCPP_RBFX_PLUGINS)
  foreach(list_name IN ITEMS bindings plugins)
    if(NOT ${list_name})
      set(${list_name})
    endif()
  endforeach()
  # The bindings targets (they have the managed assembly) and the targets with their native code. In the integration
  # mode the native code is in the IMPLEMENTATION target of the bindings.
  set(bindings_natives)
  foreach(bindings_target IN LISTS bindings)
    rbfx_csharp_bindings_native_target(${bindings_target} bindings_native)
    list(APPEND bindings_natives ${bindings_native})
  endforeach()

  install(TARGETS ${name} RUNTIME DESTINATION "${arg_DESTINATION}" BUNDLE DESTINATION "${arg_DESTINATION}")
  install(FILES "$<TARGET_FILE:SharpCpp::RbfxNative>" DESTINATION "${arg_DESTINATION}")
  get_target_property(host_type SharpCpp::RbfxHost TYPE)
  if(host_type STREQUAL "SHARED_LIBRARY")
    install(FILES "$<TARGET_FILE:SharpCpp::RbfxHost>" DESTINATION "${arg_DESTINATION}")
    # The shared hostfxr host library needs the shared nethost library (nethost.dll, libnethost.so or libnethost.dylib)
    # in its folder. It is next to the host library. The shim host library has no nethost.
    if(DEFINED RBFX_HOST_MODE)
      set(host_mode "${RBFX_HOST_MODE}")
    else()
      set(host_mode "${SharpCppRbfx_HOST_MODE}")
    endif()
    if(NOT host_mode STREQUAL "shim")
      install(
        FILES "$<TARGET_FILE_DIR:SharpCpp::RbfxHost>/${CMAKE_SHARED_LIBRARY_PREFIX}nethost${CMAKE_SHARED_LIBRARY_SUFFIX}"
        DESTINATION "${arg_DESTINATION}")
    endif()
  endif()

  # The shared engine library. The installed executable finds it in its own folder (INSTALL_RPATH, or the DLL
  # folder on Windows). A static engine is in the executable.
  get_target_property(engine_type Urho3D TYPE)
  if(engine_type STREQUAL "SHARED_LIBRARY")
    install(FILES "$<TARGET_FILE:Urho3D>" DESTINATION "${arg_DESTINATION}")
  endif()

  # The data folders (CoreData is one of them) go next to the executable. ResourceRoot.ini in the same folder maps them
  # to the aliases of the engine. The engine reads the file from the folder of the executable first.
  if(data)
    get_target_property(data_directories ${data} SHARPCPP_RBFX_DATA_DIRECTORIES)
    get_target_property(data_files ${data} SHARPCPP_RBFX_DATA_FILES)
    foreach(data_directory IN LISTS data_directories)
      install(DIRECTORY "${data_directory}" DESTINATION "${arg_DESTINATION}")
    endforeach()
    foreach(data_file IN LISTS data_files)
      install(FILES "${data_file}" DESTINATION "${arg_DESTINATION}")
    endforeach()
  endif()

  # The NativeAOT head library of the shim mode (<plugin>.Head<suffix>). The launcher loads it from this folder.
  get_target_property(head_library ${name} SHARPCPP_RBFX_HEAD_LIBRARY)
  if(head_library)
    install(FILES "${head_library}" DESTINATION "${arg_DESTINATION}")
  endif()

  # The shared libraries of the bindings targets and of the native plugins. A static library is in the executable.
  set(patterns PATTERN "${plugin_name}.*" PATTERN "SharpCpp.*.dll")
  set(libraries ${bindings_natives} ${plugins})
  list(REMOVE_DUPLICATES libraries)
  foreach(library IN LISTS libraries)
    get_target_property(library_type ${library} TYPE)
    if(library_type STREQUAL "SHARED_LIBRARY" OR library_type STREQUAL "MODULE_LIBRARY")
      get_target_property(library_imported ${library} IMPORTED)
      if(library_imported)
        install(FILES "$<TARGET_FILE:${library}>" DESTINATION "${arg_DESTINATION}")
      else()
        # The libraries load each other (for example the plugin of the application needs the plugins of PLUGINS).
        # The installed file has the run-time path of its folder. install(TARGETS) writes the install run-time
        # path, so the run-time path is the same if another install rule installs the same target.
        if(APPLE)
          set_property(TARGET ${library} APPEND PROPERTY INSTALL_RPATH "@loader_path")
        elseif(UNIX)
          set_property(TARGET ${library} APPEND PROPERTY INSTALL_RPATH "$ORIGIN")
        endif()
        install(TARGETS ${library} LIBRARY DESTINATION "${arg_DESTINATION}" RUNTIME DESTINATION "${arg_DESTINATION}")
      endif()
    endif()
  endforeach()
  foreach(library IN LISTS bindings)
    get_target_property(assembly ${library} SHARPCPP_RBFX_BINDINGS_ASSEMBLY)
    if(assembly)
      list(APPEND patterns PATTERN "${assembly}.*")
    endif()
  endforeach()

  # The managed files that the dotnet build wrote next to the executable.
  install(
    DIRECTORY "$<TARGET_FILE_DIR:${name}>/"
    DESTINATION "${arg_DESTINATION}"
    FILES_MATCHING ${patterns})
endfunction()

function(rbfx_csharp_add_project name)
  cmake_parse_arguments(
    PARSE_ARGV 1 arg "CONSOLE;INSTALL;SEPARATE_LIBRARY"
    "CSHARP_DIR;CSPROJ;IMPLEMENTATION;NAMESPACE;APPLICATION_ID;INSTALL_DESTINATION"
    "SOURCES;HEADERS;INCLUDE_DIRECTORIES;BINDINGS_TARGETS;PLUGINS;LINK_LIBRARIES;DATA;RESOURCES;PROPERTIES")
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_add_project(${name}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()

  # Relative paths are relative to the source folder of the caller.
  foreach(keyword IN ITEMS CSHARP_DIR CSPROJ)
    if(arg_${keyword})
      cmake_path(ABSOLUTE_PATH arg_${keyword} BASE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" NORMALIZE)
    endif()
  endforeach()
  foreach(keyword IN ITEMS SOURCES HEADERS INCLUDE_DIRECTORIES DATA RESOURCES)
    set(paths)
    foreach(path IN LISTS arg_${keyword})
      cmake_path(ABSOLUTE_PATH path BASE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" NORMALIZE)
      list(APPEND paths "${path}")
    endforeach()
    set(arg_${keyword} "${paths}")
  endforeach()

  # The bindings of the C++ headers.
  if(arg_HEADERS)
    if(NOT COMMAND rbfx_csharp_add_bindings)
      message(FATAL_ERROR "rbfx_csharp_add_project(${name}): HEADERS needs rbfx_csharp_add_bindings, which does not exist.")
    endif()
    set(include_directories ${arg_INCLUDE_DIRECTORIES})
    if(NOT include_directories)
      foreach(header IN LISTS arg_HEADERS)
        cmake_path(GET header PARENT_PATH header_directory)
        list(APPEND include_directories "${header_directory}")
      endforeach()
      list(REMOVE_DUPLICATES include_directories)
    endif()
    set(bindings_options HEADERS ${arg_HEADERS} INCLUDE_DIRECTORIES ${include_directories})
    if(arg_IMPLEMENTATION)
      list(APPEND bindings_options IMPLEMENTATION ${arg_IMPLEMENTATION})
    endif()
    if(arg_SEPARATE_LIBRARY)
      list(APPEND bindings_options SEPARATE_LIBRARY)
    endif()
    if(arg_NAMESPACE)
      list(APPEND bindings_options NAMESPACE ${arg_NAMESPACE})
    endif()
    rbfx_csharp_add_bindings(${name}.Bindings ${bindings_options})
  endif()

  # The plugin: <name>.Net, or the project of the user.
  set(plugin_options)
  if(arg_CSPROJ)
    get_filename_component(plugin "${arg_CSPROJ}" NAME_WLE)
    foreach(suffix IN ITEMS "" .Plugin .Native .Data .Bindings .Head .CSharp .Resources)
      if(plugin STREQUAL "${name}${suffix}")
        message(FATAL_ERROR "rbfx_csharp_add_project(${name}): the name of the project file '${arg_CSPROJ}' is '${plugin}'. The plugin target has this name. It clashes with the target '${name}${suffix}' that the function makes. Rename the project file (or the project).")
      endif()
    endforeach()
    list(APPEND plugin_options CSPROJ "${arg_CSPROJ}")
  else()
    set(plugin ${name}.Net)
    if(arg_CSHARP_DIR)
      list(APPEND plugin_options CSHARP_DIR "${arg_CSHARP_DIR}")
    endif()
  endif()
  set(plugin_references)
  if(arg_HEADERS)
    list(APPEND plugin_references ${name}.Bindings)
  endif()
  foreach(bindings IN LISTS arg_BINDINGS_TARGETS)
    if(NOT TARGET ${bindings})
      message(FATAL_ERROR "rbfx_csharp_add_project(${name}): BINDINGS_TARGETS item '${bindings}' is not a target. Make it with rbfx_csharp_add_bindings before this call.")
    endif()
    list(APPEND plugin_references ${bindings})
  endforeach()
  if(plugin_references)
    list(APPEND plugin_options REFERENCES ${plugin_references})
  endif()
  if(arg_PROPERTIES)
    list(APPEND plugin_options PROPERTIES ${arg_PROPERTIES})
  endif()
  rbfx_csharp_add_plugin(${plugin} ${plugin_options})

  # The launcher.
  set(launcher_options)
  foreach(keyword IN ITEMS SOURCES PLUGINS LINK_LIBRARIES)
    if(arg_${keyword})
      list(APPEND launcher_options ${keyword} ${arg_${keyword}})
    endif()
  endforeach()
  if(arg_CONSOLE)
    list(APPEND launcher_options CONSOLE)
  endif()
  rbfx_csharp_add_launcher(${name} ${launcher_options})

  # The data. The target is always made, because CoreData is also data.
  set(data_options)
  if(arg_DATA)
    list(APPEND data_options DIRECTORIES ${arg_DATA})
  endif()
  rbfx_csharp_add_data(${name}.Data ${data_options})

  # The packaging.
  set(package_options PLUGIN ${plugin} DATA ${name}.Data)
  if(arg_RESOURCES)
    list(APPEND package_options RESOURCES ${arg_RESOURCES})
  endif()
  if(arg_APPLICATION_ID)
    list(APPEND package_options APPLICATION_ID "${arg_APPLICATION_ID}")
  endif()
  rbfx_csharp_package_application(${name} ${package_options})

  if(arg_INSTALL)
    if(arg_INSTALL_DESTINATION)
      rbfx_csharp_install_application(${name} DESTINATION "${arg_INSTALL_DESTINATION}")
    else()
      rbfx_csharp_install_application(${name})
    endif()
  endif()
endfunction()
