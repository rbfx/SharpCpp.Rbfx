# Writes the sorted list of the generated *.g.cpp files (relative to GENERATED_DIR) to SOURCE_LIST.
# The file is replaced only when its content changes, so its timestamp tracks the set of sources.
file(GLOB_RECURSE _generated_sources RELATIVE "${GENERATED_DIR}" "${GENERATED_DIR}/*.g.cpp")
list(SORT _generated_sources)
list(JOIN _generated_sources "\n" _generated_content)
file(WRITE "${SOURCE_LIST}.tmp" "${_generated_content}\n")
configure_file("${SOURCE_LIST}.tmp" "${SOURCE_LIST}" COPYONLY)
file(REMOVE "${SOURCE_LIST}.tmp")
