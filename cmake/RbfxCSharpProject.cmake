# One call per project: the C# application with its user-code bindings, its data and its install rules.
# The module uses rbfx_csharp_add_application (RbfxCSharpApplication.cmake) and rbfx_csharp_add_usercode
# (RbfxCSharpAddUserCode.cmake).
#
# rbfx_csharp_find_core_data(<out_var>)
#   Sets <out_var> to the folder CoreData of the rbfx SDK. The first choice is ${Urho3D_PACKAGE_ROOT}/bin/CoreData.
#   Some mobile SDKs have no CoreData. Then the function looks for bin/CoreData in the folders of CMAKE_PREFIX_PATH
#   (the host SDK). Without a result, <out_var> is <out_var>-NOTFOUND.
#
# rbfx_csharp_install_application(<name> [DESTINATION <dir>])
#   Adds the install rules of an application from rbfx_csharp_add_application. The default DESTINATION is
#   bin/$<CONFIG>.
#   Desktop: the executable, the shared engine library, the native wrapper of the bindings, the shared host library
#   with the shared nethost library, the folders of DATA and CoreData with a ResourceRoot.ini that maps them, the
#   shared libraries of USERCODE and PLUGINS, the plugin assembly with its runtimeconfig and deps files, the
#   assemblies of USERCODE and the SharpCpp assemblies, all in DESTINATION. The function skips CoreData when the
#   SDK has no CoreData folder.
#   Android: the APK. Emscripten: the web folder, as DESTINATION/<name>. iOS: the app bundle.
#   The function stops with an error when <name> is not from rbfx_csharp_add_application.
#
# rbfx_csharp_add_project(<name>
#   [SOURCES <files...>]
#   [CSHARP_DIR <dir>]
#   [PLUGIN_NAME <plugin>]
#   [CSPROJ <csproj>]
#   [HEADERS <headers...>]
#   [INCLUDE_DIRECTORIES <dirs...>]
#   [IMPLEMENTATION <target>]
#   [NAMESPACE <namespace>]
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
#   HEADERS         The C++ headers to bind to C#. The function calls rbfx_csharp_add_usercode(<name>.UserCode)
#                   and passes the target to the application as USERCODE.
#   INCLUDE_DIRECTORIES  Include directories of HEADERS. The default is the folders of HEADERS.
#   IMPLEMENTATION  The target that implements HEADERS. It is the LINK_LIBRARIES of the bindings.
#   NAMESPACE       The C# namespace of the bindings.
#   DATA            Resource folders. On Emscripten, Android and iOS the function packs each folder into
#                   ${CMAKE_CURRENT_BINARY_DIR}/<name>.Resources/<folder name>.pak with create_pak, adds CoreData.pak
#                   (rbfx_csharp_find_core_data) and appends the paks to RESOURCES. Two paks with the same name are
#                   an error. On desktop the install rules copy the folders (rbfx_csharp_install_application).
#   INSTALL         Calls rbfx_csharp_install_application(<name>), with INSTALL_DESTINATION as DESTINATION.
#   The other keywords go to rbfx_csharp_add_application (see RbfxCSharpApplication.cmake).

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
    message(FATAL_ERROR "rbfx_csharp_install_application(${name}): the target does not exist. Call rbfx_csharp_add_application first.")
  endif()
  get_target_property(plugin_name ${name} SHARPCPP_RBFX_PLUGIN_NAME)
  get_target_property(output ${name} SHARPCPP_RBFX_APPLICATION_OUTPUT)
  if(NOT plugin_name OR output STREQUAL "output-NOTFOUND")
    message(FATAL_ERROR "rbfx_csharp_install_application(${name}): the target was not made by rbfx_csharp_add_application.")
  endif()

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

  get_target_property(usercode ${name} SHARPCPP_RBFX_USERCODE)
  get_target_property(plugins ${name} SHARPCPP_RBFX_PLUGINS)
  foreach(list_name IN ITEMS usercode plugins)
    if(NOT ${list_name})
      set(${list_name})
    endif()
  endforeach()

  install(TARGETS ${name} RUNTIME DESTINATION "${arg_DESTINATION}" BUNDLE DESTINATION "${arg_DESTINATION}")
  install(FILES "$<TARGET_FILE:SharpCpp::RbfxNative>" DESTINATION "${arg_DESTINATION}")
  get_target_property(host_type SharpCpp::RbfxHost TYPE)
  if(host_type STREQUAL "SHARED_LIBRARY")
    install(FILES "$<TARGET_FILE:SharpCpp::RbfxHost>" DESTINATION "${arg_DESTINATION}")
    # The shared host library needs the shared nethost library (nethost.dll, libnethost.so or libnethost.dylib)
    # in its folder. It is next to the host library.
    install(
      FILES "$<TARGET_FILE_DIR:SharpCpp::RbfxHost>/${CMAKE_SHARED_LIBRARY_PREFIX}nethost${CMAKE_SHARED_LIBRARY_SUFFIX}"
      DESTINATION "${arg_DESTINATION}")
  endif()

  # The shared engine library. The installed executable finds it in its own folder (INSTALL_RPATH, or the DLL
  # folder on Windows). A static engine is in the executable.
  get_target_property(engine_type Urho3D TYPE)
  if(engine_type STREQUAL "SHARED_LIBRARY")
    install(FILES "$<TARGET_FILE:Urho3D>" DESTINATION "${arg_DESTINATION}")
  endif()

  # The data folders and CoreData go next to the executable. ResourceRoot.ini in the same folder maps them to the
  # aliases of the engine. The engine reads the file from the folder of the executable first.
  get_target_property(data_directories ${name} SHARPCPP_RBFX_DATA)
  if(NOT data_directories)
    set(data_directories)
  endif()
  set(resource_root_text "")
  rbfx_csharp_find_core_data(core_data)
  if(core_data)
    install(DIRECTORY "${core_data}" DESTINATION "${arg_DESTINATION}")
    string(APPEND resource_root_text "CoreData=CoreData\n")
  endif()
  foreach(data IN LISTS data_directories)
    cmake_path(GET data FILENAME data_name)
    if(data_name STREQUAL "CoreData")
      continue()
    endif()
    install(DIRECTORY "${data}" DESTINATION "${arg_DESTINATION}")
    string(APPEND resource_root_text "${data_name}=${data_name}\n")
  endforeach()
  if(resource_root_text)
    set(resource_root_file "${CMAKE_CURRENT_BINARY_DIR}/${name}.Install/ResourceRoot.ini")
    file(GENERATE OUTPUT "${resource_root_file}" CONTENT "${resource_root_text}")
    install(FILES "${resource_root_file}" DESTINATION "${arg_DESTINATION}")
  endif()

  # The shared libraries of USERCODE and PLUGINS. A static library is in the executable.
  set(patterns PATTERN "${plugin_name}.*" PATTERN "SharpCpp.*.dll")
  set(libraries ${usercode} ${plugins})
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
  foreach(library IN LISTS usercode)
    get_target_property(assembly ${library} SHARPCPP_RBFX_USERCODE_ASSEMBLY)
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
    PARSE_ARGV 1 arg "CONSOLE;INSTALL"
    "CSHARP_DIR;PLUGIN_NAME;CSPROJ;IMPLEMENTATION;NAMESPACE;APPLICATION_ID;INSTALL_DESTINATION"
    "SOURCES;HEADERS;INCLUDE_DIRECTORIES;PLUGINS;LINK_LIBRARIES;DATA;RESOURCES;PROPERTIES")
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
  set(usercode_arguments)
  if(arg_HEADERS)
    if(NOT COMMAND rbfx_csharp_add_usercode)
      message(FATAL_ERROR "rbfx_csharp_add_project(${name}): HEADERS needs rbfx_csharp_add_usercode, which does not exist.")
    endif()
    set(include_directories ${arg_INCLUDE_DIRECTORIES})
    if(NOT include_directories)
      foreach(header IN LISTS arg_HEADERS)
        cmake_path(GET header PARENT_PATH header_directory)
        list(APPEND include_directories "${header_directory}")
      endforeach()
      list(REMOVE_DUPLICATES include_directories)
    endif()
    set(usercode_options HEADERS ${arg_HEADERS} INCLUDE_DIRECTORIES ${include_directories})
    if(arg_IMPLEMENTATION)
      list(APPEND usercode_options LINK_LIBRARIES ${arg_IMPLEMENTATION})
    endif()
    if(arg_NAMESPACE)
      list(APPEND usercode_options NAMESPACE ${arg_NAMESPACE})
    endif()
    rbfx_csharp_add_usercode(${name}.UserCode ${usercode_options})
    set(usercode_arguments USERCODE ${name}.UserCode)
  endif()

  # Mobile and Web applications read the data from paks that the head packs as resources.
  set(resources ${arg_RESOURCES})
  if(EMSCRIPTEN OR ANDROID OR CMAKE_SYSTEM_NAME STREQUAL "iOS")
    set(pak_directory "${CMAKE_CURRENT_BINARY_DIR}/${name}.Resources")
    set(pak_names)
    foreach(data IN LISTS arg_DATA)
      cmake_path(GET data FILENAME data_name)
      list(APPEND pak_names "${data_name}")
      list(APPEND data_paks "${data}")
    endforeach()
    rbfx_csharp_find_core_data(core_data)
    if(NOT core_data)
      message(FATAL_ERROR "rbfx_csharp_add_project(${name}): CoreData is not found in '${Urho3D_PACKAGE_ROOT}/bin' or in CMAKE_PREFIX_PATH.")
    endif()
    list(APPEND pak_names CoreData)
    list(APPEND data_paks "${core_data}")
    set(duplicates "${pak_names}")
    list(REMOVE_DUPLICATES duplicates)
    list(LENGTH pak_names pak_count)
    list(LENGTH duplicates unique_count)
    if(NOT pak_count EQUAL unique_count)
      message(FATAL_ERROR "rbfx_csharp_add_project(${name}): two paks have the same name (${pak_names}). The folder CoreData is also a pak.")
    endif()
    foreach(data IN LISTS data_paks)
      cmake_path(GET data FILENAME data_name)
      create_pak("${data}" "${pak_directory}/${data_name}.pak")
      list(APPEND resources "${pak_directory}/${data_name}.pak")
    endforeach()
  endif()

  # The remaining keywords go to the application as they are.
  set(application_options)
  foreach(keyword IN ITEMS SOURCES PLUGINS LINK_LIBRARIES PROPERTIES)
    if(arg_${keyword})
      list(APPEND application_options ${keyword} ${arg_${keyword}})
    endif()
  endforeach()
  if(resources)
    list(APPEND application_options RESOURCES ${resources})
  endif()
  foreach(keyword IN ITEMS CSHARP_DIR PLUGIN_NAME CSPROJ APPLICATION_ID)
    if(arg_${keyword})
      list(APPEND application_options ${keyword} "${arg_${keyword}}")
    endif()
  endforeach()
  if(arg_CONSOLE)
    list(APPEND application_options CONSOLE)
  endif()
  rbfx_csharp_add_application(${name} ${usercode_arguments} ${application_options})
  # The install rules read the data folders.
  set_target_properties(${name} PROPERTIES SHARPCPP_RBFX_DATA "${arg_DATA}")

  if(arg_INSTALL)
    if(arg_INSTALL_DESTINATION)
      rbfx_csharp_install_application(${name} DESTINATION "${arg_INSTALL_DESTINATION}")
    else()
      rbfx_csharp_install_application(${name})
    endif()
  endif()
endfunction()
