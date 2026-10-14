# Copies the RESOURCES of an rbfx_csharp_add_application on Emscripten next to index.html and writes resources.json.
# main.js reads the list and writes the files into the file system of Emscripten. The paths are the same as the
# asset paths on Android: a file has its file name, a folder is a folder with its name.
#
# cmake -DRESOURCE_LIST=<file with one absolute path on each line> -DOUTPUT_DIR=<web folder> -P CopyResources.cmake
if(NOT RESOURCE_LIST OR NOT OUTPUT_DIR)
  message(FATAL_ERROR "CopyResources.cmake: RESOURCE_LIST and OUTPUT_DIR are required.")
endif()

# file(COPY_FILE) does not make the destination folder.
function(copy_resource source destination)
  get_filename_component(folder "${destination}" DIRECTORY)
  file(MAKE_DIRECTORY "${folder}")
  file(COPY_FILE "${source}" "${destination}" ONLY_IF_DIFFERENT)
endfunction()

file(STRINGS "${RESOURCE_LIST}" resources)
set(names "")
foreach(resource IN LISTS resources)
  get_filename_component(resource_name "${resource}" NAME)
  if(IS_DIRECTORY "${resource}")
    file(GLOB_RECURSE files LIST_DIRECTORIES false RELATIVE "${resource}" "${resource}/*")
    list(SORT files)
    foreach(file IN LISTS files)
      copy_resource("${resource}/${file}" "${OUTPUT_DIR}/${resource_name}/${file}")
      list(APPEND names "${resource_name}/${file}")
    endforeach()
  elseif(EXISTS "${resource}")
    copy_resource("${resource}" "${OUTPUT_DIR}/${resource_name}")
    list(APPEND names "${resource_name}")
  else()
    message(FATAL_ERROR "CopyResources.cmake: the resource '${resource}' does not exist.")
  endif()
endforeach()

# A JSON array of strings. A file name can have a backslash or a quote.
set(json "[")
set(separator "")
foreach(name IN LISTS names)
  string(REPLACE "\\" "\\\\" name "${name}")
  string(REPLACE "\"" "\\\"" name "${name}")
  string(APPEND json "${separator}\n  \"${name}\"")
  set(separator ",")
endforeach()
string(APPEND json "\n]\n")
file(WRITE "${OUTPUT_DIR}/resources.json.tmp" "${json}")
file(COPY_FILE "${OUTPUT_DIR}/resources.json.tmp" "${OUTPUT_DIR}/resources.json" ONLY_IF_DIFFERENT)
file(REMOVE "${OUTPUT_DIR}/resources.json.tmp")
