# One call per set of data: the resource folders of an application.
#
# rbfx_csharp_add_data(<data>
#   [DIRECTORIES <dirs...>]
#   [NO_CORE_DATA])
#
# The function makes the target <data> (add_custom_target) in the folder of the call. The target is the handle of the data.
# rbfx_csharp_package_application uses it (DATA <data>).
#
# DIRECTORIES   Resource folders. A relative path is relative to CMAKE_CURRENT_SOURCE_DIR. A folder is a resource
#               root with the name of the folder. Two folders with the same name are an error. On desktop a folder
#               with the name CoreData is skipped (the folder CoreData of the rbfx SDK is used), unless NO_CORE_DATA is set.
# NO_CORE_DATA  Do not add the folder CoreData of the rbfx SDK (rbfx_csharp_find_core_data). Without this option the
#               function adds CoreData as the first folder. On desktop a missing CoreData folder is skipped. On
#               Android, Emscripten and iOS it is an error. Without DIRECTORIES and with NO_CORE_DATA, the data is empty.
#               On desktop, empty data is allowed and the function writes no ResourceRoot.ini. On Android, Emscripten
#               and iOS, empty data is an error.
#
# Android, Emscripten and iOS: the application reads the data from paks. The function packs each folder into
# ${CMAKE_CURRENT_BINARY_DIR}/<data>.Resources/<folder name>.pak with create_pak. The target <data> depends on the
# paks. The commands of create_pak work only for the targets of the folder of the call, so the target <data> is in
# this folder. The head of the application depends on the target.
# Desktop: the application reads the folders from the disk, so the function makes no pak. It writes
# ${CMAKE_CURRENT_BINARY_DIR}/<data>.Install/ResourceRoot.ini (file(GENERATE)). The file has one <folder>=<folder> line
# for each folder. The install rules (rbfx_csharp_install_application) copy the folders and the file next to the executable.
# The engine reads ResourceRoot.ini from the folder of the executable first.
#
# Target properties set on <data>:
# - SHARPCPP_RBFX_DATA: TRUE (the mark of a target of this function).
# - SHARPCPP_RBFX_DATA_DIRECTORIES: the absolute paths of the folders, with CoreData first.
# - SHARPCPP_RBFX_DATA_FILES: the paks (Android, Emscripten, iOS), or the file ResourceRoot.ini (desktop, empty if the data has no folder).
function(rbfx_csharp_add_data data)
  cmake_parse_arguments(PARSE_ARGV 1 arg "NO_CORE_DATA" "" "DIRECTORIES")
  if(arg_UNPARSED_ARGUMENTS)
    message(FATAL_ERROR "rbfx_csharp_add_data(${data}): unknown arguments: ${arg_UNPARSED_ARGUMENTS}")
  endif()
  if(TARGET ${data})
    message(FATAL_ERROR "rbfx_csharp_add_data(${data}): the target '${data}' exists already.")
  endif()
  set(is_pak_platform FALSE)
  if(EMSCRIPTEN OR ANDROID OR CMAKE_SYSTEM_NAME STREQUAL "iOS")
    set(is_pak_platform TRUE)
  endif()

  set(directories)
  if(NOT arg_NO_CORE_DATA)
    rbfx_csharp_find_core_data(core_data)
    if(core_data)
      list(APPEND directories "${core_data}")
    elseif(is_pak_platform)
      message(FATAL_ERROR "rbfx_csharp_add_data(${data}): CoreData is not found in '${Urho3D_PACKAGE_ROOT}/bin' or in CMAKE_PREFIX_PATH.")
    endif()
  endif()
  foreach(directory IN LISTS arg_DIRECTORIES)
    cmake_path(ABSOLUTE_PATH directory BASE_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}" NORMALIZE)
    cmake_path(GET directory FILENAME directory_name)
    if(NOT is_pak_platform AND directory_name STREQUAL "CoreData" AND NOT arg_NO_CORE_DATA)
      # On desktop the engine finds CoreData next to the executable, so a folder with this name is skipped.
      message(STATUS "rbfx_csharp_add_data(${data}): the folder '${directory}' is skipped. The folder CoreData of the rbfx SDK is used.")
      continue()
    endif()
    list(APPEND directories "${directory}")
  endforeach()
  if(NOT directories AND is_pak_platform)
    message(FATAL_ERROR "rbfx_csharp_add_data(${data}): the data has no folder. Set DIRECTORIES, or remove NO_CORE_DATA.")
  endif()
  set(names)
  foreach(directory IN LISTS directories)
    cmake_path(GET directory FILENAME directory_name)
    list(APPEND names "${directory_name}")
  endforeach()
  set(unique_names "${names}")
  list(REMOVE_DUPLICATES unique_names)
  list(LENGTH names name_count)
  list(LENGTH unique_names unique_count)
  if(NOT name_count EQUAL unique_count)
    message(FATAL_ERROR "rbfx_csharp_add_data(${data}): two folders have the same name (${names}). The folder CoreData is also a folder of the data.")
  endif()

  set(files)
  if(is_pak_platform)
    # Mobile and Web applications read the data from paks that the head packs as resources.
    set(pak_directory "${CMAKE_CURRENT_BINARY_DIR}/${data}.Resources")
    foreach(directory IN LISTS directories)
      cmake_path(GET directory FILENAME directory_name)
      create_pak("${directory}" "${pak_directory}/${directory_name}.pak")
      list(APPEND files "${pak_directory}/${directory_name}.pak")
    endforeach()
  else()
    # The root of each folder is the folder itself, next to the executable.
    if(names)
      set(resource_root_text "")
      foreach(directory_name IN LISTS names)
        string(APPEND resource_root_text "${directory_name}=${directory_name}\n")
      endforeach()
      set(resource_root_file "${CMAKE_CURRENT_BINARY_DIR}/${data}.Install/ResourceRoot.ini")
      file(GENERATE OUTPUT "${resource_root_file}" CONTENT "${resource_root_text}")
      set(files "${resource_root_file}")
    endif()
  endif()

  if(is_pak_platform)
    add_custom_target(${data} DEPENDS ${files})
  else()
    add_custom_target(${data})
  endif()
  set_target_properties(
    ${data}
    PROPERTIES
      SHARPCPP_RBFX_DATA TRUE
      SHARPCPP_RBFX_DATA_DIRECTORIES "${directories}"
      SHARPCPP_RBFX_DATA_FILES "${files}")
endfunction()
