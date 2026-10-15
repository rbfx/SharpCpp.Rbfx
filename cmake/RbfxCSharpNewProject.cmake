# Makes a new C# application project for rbfx from the templates in the folder project of this module folder.
#
# Usage:
#   cmake -DNAME=<Name> -DDESTINATION=<dir> -P <path>/RbfxCSharpNewProject.cmake
#
# NAME          Name of the project and of the executable. It starts with a letter. It has only letters, digits
#               and underscores. The C# plugin is <Name>.Managed and its class is <Name>Plugin.
# DESTINATION   The folder for the project. It must not exist or it must be empty.
#
# Each file *.in of the template folder goes through configure_file (@ONLY) without the suffix .in.
# The file CSharp/Plugin.cs becomes CSharp/<Name>Plugin.cs.
# The script works from the source tree (cmake/) and from the module folder of an installed SDK.

cmake_minimum_required(VERSION 3.28)

if(NOT DEFINED NAME OR NAME STREQUAL "")
  message(FATAL_ERROR "RbfxCSharpNewProject: set NAME. Usage: cmake -DNAME=<Name> -DDESTINATION=<dir> -P RbfxCSharpNewProject.cmake")
endif()
if(NOT NAME MATCHES "^[A-Za-z][A-Za-z0-9_]*$")
  message(FATAL_ERROR "RbfxCSharpNewProject: NAME '${NAME}' is not valid. Use a letter, then letters, digits or underscores.")
endif()
if(NOT DEFINED DESTINATION OR DESTINATION STREQUAL "")
  message(FATAL_ERROR "RbfxCSharpNewProject: set DESTINATION. Usage: cmake -DNAME=<Name> -DDESTINATION=<dir> -P RbfxCSharpNewProject.cmake")
endif()

set(template_dir "${CMAKE_CURRENT_LIST_DIR}/project")
if(NOT IS_DIRECTORY "${template_dir}")
  message(FATAL_ERROR "RbfxCSharpNewProject: the template folder '${template_dir}' does not exist.")
endif()

cmake_path(ABSOLUTE_PATH DESTINATION BASE_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}" NORMALIZE OUTPUT_VARIABLE destination)
if(EXISTS "${destination}" AND NOT IS_DIRECTORY "${destination}")
  message(FATAL_ERROR "RbfxCSharpNewProject: DESTINATION '${destination}' exists and is not a folder.")
endif()
if(IS_DIRECTORY "${destination}")
  file(GLOB destination_entries LIST_DIRECTORIES true "${destination}/*" "${destination}/.[!.]*" "${destination}/..?*")
  if(destination_entries)
    message(FATAL_ERROR "RbfxCSharpNewProject: DESTINATION '${destination}' is not empty.")
  endif()
endif()

file(GLOB_RECURSE template_files RELATIVE "${template_dir}" "${template_dir}/*" "${template_dir}/.gitignore")
list(REMOVE_DUPLICATES template_files)
foreach(relative IN LISTS template_files)
  set(target "${relative}")
  if(target STREQUAL "CSharp/Plugin.cs.in")
    set(target "CSharp/${NAME}Plugin.cs.in")
  endif()
  if(target MATCHES "\\.in$")
    string(REGEX REPLACE "\\.in$" "" target "${target}")
    configure_file("${template_dir}/${relative}" "${destination}/${target}" @ONLY)
  else()
    configure_file("${template_dir}/${relative}" "${destination}/${target}" COPYONLY)
  endif()
endforeach()

message(STATUS "RbfxCSharpNewProject: made the project ${NAME} in ${destination}")
message(STATUS "Configure it with: cmake -S \"${destination}\" -B \"${destination}/build\" -DCMAKE_PREFIX_PATH=\"<engine SDK>;<SharpCpp.Rbfx SDK>\"")
