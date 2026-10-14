# Install step script: copies the user-code generator from its build output folder.
# The build output folder holds the files of many projects. This script reads the dependency
# list of the generator (Rbfx.UserCode.Generator.deps.json) and copies only the files that
# the generator needs, so the installed folder runs with "dotnet Rbfx.UserCode.Generator.dll".
# It needs no dotnet at install time.
#
# Input variables:
#   RBFX_GENERATOR_SOURCE_DIR  - the folder with Rbfx.UserCode.Generator.dll
#   RBFX_GENERATOR_INSTALL_DIR - the destination folder (DESTDIR is already included)

set(_name "Rbfx.UserCode.Generator")
set(_deps_file "${RBFX_GENERATOR_SOURCE_DIR}/${_name}.deps.json")
foreach(_required IN ITEMS "${_name}.dll" "${_name}.deps.json" "${_name}.runtimeconfig.json")
  if(NOT EXISTS "${RBFX_GENERATOR_SOURCE_DIR}/${_required}")
    message(FATAL_ERROR "The user-code generator file is missing: ${RBFX_GENERATOR_SOURCE_DIR}/${_required}. Build the managed projects (dotnet build SharpCpp.Rbfx.slnx) before the install step.")
  endif()
endforeach()

set(_files
  "${_name}.dll"
  "${_name}.pdb"
  "${_name}.deps.json"
  "${_name}.runtimeconfig.json")
# Each entry is a path relative to the source folder. The entry keeps its sub folder.
set(_runtime_targets)

file(READ "${_deps_file}" _deps_json)
string(JSON _targets GET "${_deps_json}" "targets")
string(JSON _target_count LENGTH "${_targets}")
if(_target_count LESS 1)
  message(FATAL_ERROR "No target in ${_deps_file}")
endif()
string(JSON _target_name MEMBER "${_targets}" 0)
string(JSON _libraries GET "${_targets}" "${_target_name}")
string(JSON _library_count LENGTH "${_libraries}")
math(EXPR _library_last "${_library_count} - 1")
foreach(_library_index RANGE 0 ${_library_last})
  string(JSON _library_name MEMBER "${_libraries}" ${_library_index})
  string(JSON _library GET "${_libraries}" "${_library_name}")
  foreach(_section IN ITEMS runtime native)
    string(JSON _entries ERROR_VARIABLE _error GET "${_library}" "${_section}")
    if(_error STREQUAL "NOTFOUND")
      string(JSON _entry_count LENGTH "${_entries}")
      if(_entry_count GREATER 0)
        math(EXPR _entry_last "${_entry_count} - 1")
        foreach(_entry_index RANGE 0 ${_entry_last})
          string(JSON _entry MEMBER "${_entries}" ${_entry_index})
          get_filename_component(_entry_file "${_entry}" NAME)
          list(APPEND _files "${_entry_file}")
        endforeach()
      endif()
    endif()
  endforeach()
  string(JSON _entries ERROR_VARIABLE _error GET "${_library}" "runtimeTargets")
  if(_error STREQUAL "NOTFOUND")
    string(JSON _entry_count LENGTH "${_entries}")
    if(_entry_count GREATER 0)
      math(EXPR _entry_last "${_entry_count} - 1")
      foreach(_entry_index RANGE 0 ${_entry_last})
        string(JSON _entry MEMBER "${_entries}" ${_entry_index})
        list(APPEND _runtime_targets "${_entry}")
      endforeach()
    endif()
  endif()
endforeach()

list(REMOVE_DUPLICATES _files)
foreach(_file IN LISTS _files)
  if(EXISTS "${RBFX_GENERATOR_SOURCE_DIR}/${_file}")
    file(INSTALL "${RBFX_GENERATOR_SOURCE_DIR}/${_file}" DESTINATION "${RBFX_GENERATOR_INSTALL_DIR}")
  elseif(NOT _file MATCHES "\\.pdb$")
    message(FATAL_ERROR "A dependency of the user-code generator is missing: ${RBFX_GENERATOR_SOURCE_DIR}/${_file}")
  endif()
endforeach()
# Native libraries (libclang) are in runtimes/<rid>/native. Install every RID that the deps file lists.
foreach(_entry IN LISTS _runtime_targets)
  get_filename_component(_entry_directory "${_entry}" DIRECTORY)
  if(NOT EXISTS "${RBFX_GENERATOR_SOURCE_DIR}/${_entry}")
    message(FATAL_ERROR "A native file of the user-code generator is missing: ${RBFX_GENERATOR_SOURCE_DIR}/${_entry}")
  endif()
  file(INSTALL "${RBFX_GENERATOR_SOURCE_DIR}/${_entry}" DESTINATION "${RBFX_GENERATOR_INSTALL_DIR}/${_entry_directory}")
endforeach()
