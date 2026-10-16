# Sets the run path of the ELF editor executable. package.sh uses it on Linux.
# cmake -DEDITOR_FILE=<file> -DEDITOR_RPATH=<run path> -P SetEditorRpath.cmake
if(NOT EDITOR_FILE OR NOT EDITOR_RPATH)
  message(FATAL_ERROR "Set EDITOR_FILE and EDITOR_RPATH.")
endif()
file(RPATH_SET FILE "${EDITOR_FILE}" NEW_RPATH "${EDITOR_RPATH}")
