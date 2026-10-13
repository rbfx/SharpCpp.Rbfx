# FindNetHost.cmake
#
# Finds the .NET native hosting headers (nethost.h, hostfxr.h, coreclr_delegates.h)
# and the nethost library from the Microsoft.NETCore.App.Host.<rid> pack.
#
# Search order for the dotnet root or the pack native directory:
#   1. NETHOST_ROOT cache variable (a dotnet root or the native directory of the pack)
#   2. DOTNET_ROOT environment variable
#   3. the root reported by "dotnet --list-runtimes"
#
# Options:
#   NETHOST_PREFER_SHARED  Use the shared nethost library instead of the static one.
#                          The default is ON for MSVC and when BUILD_SHARED_LIBS is ON.
#                          Otherwise the default is OFF. The cached value of an existing
#                          build tree is kept.
#   NETHOST_RID            Runtime identifier of the host pack. Empty (the default) detects it:
#                          win-, osx-, linux- or linux-musl- (a musl loader in /lib) and x64 or arm64.
#
# The static libnethost.lib of the pack is built with /MT. It does not link with code that
# uses /MD, so MSVC links the import library nethost.lib and needs nethost.dll at run time.
#
# Result variables:
#   NetHost_FOUND, NETHOST_INCLUDE_DIR, NETHOST_LIBRARY, NETHOST_IS_STATIC, NETHOST_DOTNET_ROOT
#   NETHOST_RUNTIME_LIBRARY  Path of the shared library that is needed at run time. It is nethost.dll
#                            on Windows, libnethost.so on Linux and libnethost.dylib on macOS.
#                            Set only for a shared link.
#
# Imported target:
#   NetHost::NetHost

include(FindPackageHandleStandardArgs)

set(NETHOST_ROOT "" CACHE PATH "Dotnet root or native directory of the Microsoft.NETCore.App.Host pack.")
# The linkage follows BUILD_SHARED_LIBS. MSVC always links the shared library: the static one uses /MT.
if(MSVC OR BUILD_SHARED_LIBS)
  set(_nethost_prefer_shared_default ON)
else()
  set(_nethost_prefer_shared_default OFF)
endif()
option(NETHOST_PREFER_SHARED "Link the shared nethost library instead of the static one." ${_nethost_prefer_shared_default})

# Runtime identifier of the target platform. NETHOST_RID overrides the detection.
set(NETHOST_RID "" CACHE STRING "Runtime identifier of the Microsoft.NETCore.App.Host pack (empty: detect).")
if(CMAKE_SYSTEM_PROCESSOR MATCHES "^(aarch64|arm64|ARM64)$")
  set(_nethost_arch arm64)
else()
  set(_nethost_arch x64)
endif()
if(NETHOST_RID)
  set(_nethost_rid "${NETHOST_RID}")
elseif(CMAKE_SYSTEM_NAME STREQUAL "Windows")
  set(_nethost_rid "win-${_nethost_arch}")
elseif(CMAKE_SYSTEM_NAME STREQUAL "Darwin")
  set(_nethost_rid "osx-${_nethost_arch}")
else()
  # A musl C library (Alpine) has its dynamic loader at /lib/ld-musl-<arch>.so.1 of the target root.
  file(GLOB _nethost_musl_loader "${CMAKE_SYSROOT}/lib/ld-musl-*.so.1")
  if(_nethost_musl_loader)
    set(_nethost_rid "linux-musl-${_nethost_arch}")
  else()
    set(_nethost_rid "linux-${_nethost_arch}")
  endif()
  unset(_nethost_musl_loader)
endif()

# Dotnet root and newest runtime version from "dotnet --list-runtimes".
set(_nethost_runtime_root "")
set(_nethost_runtime_version "")
if(DOTNET_EXECUTABLE)
  execute_process(
    COMMAND "${DOTNET_EXECUTABLE}" --list-runtimes
    OUTPUT_VARIABLE _nethost_runtimes
    RESULT_VARIABLE _nethost_runtimes_result
    ERROR_QUIET
    OUTPUT_STRIP_TRAILING_WHITESPACE)
  if(_nethost_runtimes_result EQUAL 0)
    string(REPLACE "\n" ";" _nethost_runtime_lines "${_nethost_runtimes}")
    foreach(_line IN LISTS _nethost_runtime_lines)
      if(_line MATCHES "^Microsoft\\.NETCore\\.App ([0-9]+\\.[0-9]+\\.[0-9]+)[^ ]* \\[(.*)\\]")
        set(_version "${CMAKE_MATCH_1}")
        if(NOT _nethost_runtime_version OR _version VERSION_GREATER _nethost_runtime_version)
          set(_nethost_runtime_version "${_version}")
          # The listed path is <root>/shared/Microsoft.NETCore.App.
          get_filename_component(_nethost_runtime_root "${CMAKE_MATCH_2}/../.." ABSOLUTE)
        endif()
      endif()
    endforeach()
  endif()
endif()

# Candidate roots in search order.
set(_nethost_candidates)
if(NETHOST_ROOT)
  file(TO_CMAKE_PATH "${NETHOST_ROOT}" _nethost_root_cmake)
  list(APPEND _nethost_candidates "${_nethost_root_cmake}")
endif()
if(DEFINED ENV{DOTNET_ROOT} AND NOT "$ENV{DOTNET_ROOT}" STREQUAL "")
  file(TO_CMAKE_PATH "$ENV{DOTNET_ROOT}" _nethost_env_root_cmake)
  list(APPEND _nethost_candidates "${_nethost_env_root_cmake}")
endif()
if(_nethost_runtime_root)
  list(APPEND _nethost_candidates "${_nethost_runtime_root}")
endif()

set(_nethost_native_dir "")
set(_nethost_found_root "")
foreach(_candidate IN LISTS _nethost_candidates)
  if(EXISTS "${_candidate}/nethost.h")
    set(_nethost_native_dir "${_candidate}")
    set(_nethost_found_root "${_nethost_runtime_root}")
    break()
  endif()
  set(_pack_base "${_candidate}/packs/Microsoft.NETCore.App.Host.${_nethost_rid}")
  if(NOT IS_DIRECTORY "${_pack_base}")
    continue()
  endif()
  set(_pack_native "")
  if(_nethost_runtime_version
     AND EXISTS "${_pack_base}/${_nethost_runtime_version}/runtimes/${_nethost_rid}/native/nethost.h")
    set(_pack_native "${_pack_base}/${_nethost_runtime_version}/runtimes/${_nethost_rid}/native")
  else()
    file(GLOB _pack_versions LIST_DIRECTORIES TRUE RELATIVE "${_pack_base}" "${_pack_base}/*")
    set(_newest "")
    foreach(_version IN LISTS _pack_versions)
      if(EXISTS "${_pack_base}/${_version}/runtimes/${_nethost_rid}/native/nethost.h")
        if(NOT _newest OR _version VERSION_GREATER _newest)
          set(_newest "${_version}")
        endif()
      endif()
    endforeach()
    if(_newest)
      set(_pack_native "${_pack_base}/${_newest}/runtimes/${_nethost_rid}/native")
    endif()
  endif()
  if(_pack_native)
    set(_nethost_native_dir "${_pack_native}")
    set(_nethost_found_root "${_candidate}")
    break()
  endif()
endforeach()

if(NOT _nethost_found_root)
  set(_nethost_found_root "${_nethost_runtime_root}")
endif()

unset(NETHOST_INCLUDE_DIR CACHE)
unset(NETHOST_LIBRARY CACHE)
unset(NETHOST_RUNTIME_LIBRARY CACHE)
if(_nethost_native_dir)
  find_path(NETHOST_INCLUDE_DIR nethost.h PATHS "${_nethost_native_dir}" NO_DEFAULT_PATH)
  set(_nethost_static_names libnethost.a libnethost.lib)
  set(_nethost_shared_names nethost)
  if(NETHOST_PREFER_SHARED)
    set(_nethost_library_names ${_nethost_shared_names} ${_nethost_static_names})
  else()
    set(_nethost_library_names ${_nethost_static_names} ${_nethost_shared_names})
  endif()
  # The name "nethost" matches nethost.lib (the import library) on MSVC. It does not match libnethost.lib.
  find_library(
    NETHOST_LIBRARY
    NAMES ${_nethost_library_names}
    PATHS "${_nethost_native_dir}"
    NO_DEFAULT_PATH)
endif()

set(NETHOST_DOTNET_ROOT "${_nethost_found_root}" CACHE INTERNAL "Dotnet root that belongs to the nethost pack.")

find_package_handle_standard_args(
  NetHost
  REQUIRED_VARS NETHOST_INCLUDE_DIR NETHOST_LIBRARY
  FAIL_MESSAGE "nethost was not found. Set NETHOST_ROOT or DOTNET_ROOT, or install the Microsoft.NETCore.App.Host.${_nethost_rid} pack.")

if(NetHost_FOUND)
  if(NETHOST_LIBRARY MATCHES "\\.(a|lib)$" AND NOT NETHOST_LIBRARY MATCHES "/nethost\\.lib$")
    set(NETHOST_IS_STATIC TRUE)
  else()
    set(NETHOST_IS_STATIC FALSE)
  endif()
  if(NOT NETHOST_IS_STATIC)
    if(WIN32)
      # The import library and nethost.dll are in the same pack directory.
      get_filename_component(_nethost_library_dir "${NETHOST_LIBRARY}" DIRECTORY)
      find_file(
        NETHOST_RUNTIME_LIBRARY nethost.dll
        PATHS "${_nethost_library_dir}"
        NO_DEFAULT_PATH)
      if(NOT NETHOST_RUNTIME_LIBRARY)
        message(WARNING "nethost.dll was not found next to '${NETHOST_LIBRARY}'. It is not copied to the output.")
      endif()
    else()
      # On Linux and macOS the found library is the run-time library.
      set(NETHOST_RUNTIME_LIBRARY "${NETHOST_LIBRARY}" CACHE FILEPATH "Shared nethost library that is needed at run time." FORCE)
    endif()
  endif()
  if(MSVC AND NETHOST_IS_STATIC)
    message(WARNING "NetHost links the static library '${NETHOST_LIBRARY}'. It uses /MT and can mismatch /MD.")
  endif()
  if(NOT TARGET NetHost::NetHost)
    if(NETHOST_IS_STATIC)
      add_library(NetHost::NetHost STATIC IMPORTED)
      set_target_properties(
        NetHost::NetHost
        PROPERTIES INTERFACE_COMPILE_DEFINITIONS "NETHOST_USE_AS_STATIC")
    else()
      add_library(NetHost::NetHost SHARED IMPORTED)
      if(WIN32)
        set_target_properties(NetHost::NetHost PROPERTIES IMPORTED_IMPLIB "${NETHOST_LIBRARY}")
      endif()
    endif()
    set(_nethost_location "${NETHOST_LIBRARY}")
    if(NETHOST_RUNTIME_LIBRARY)
      set(_nethost_location "${NETHOST_RUNTIME_LIBRARY}")
    endif()
    set_target_properties(
      NetHost::NetHost
      PROPERTIES
        IMPORTED_LOCATION "${_nethost_location}"
        INTERFACE_INCLUDE_DIRECTORIES "${NETHOST_INCLUDE_DIR}")
    if(NETHOST_IS_STATIC AND CMAKE_SYSTEM_NAME STREQUAL "Linux")
      set_property(TARGET NetHost::NetHost APPEND PROPERTY INTERFACE_LINK_LIBRARIES "${CMAKE_DL_LIBS}")
    endif()
  endif()
endif()

mark_as_advanced(NETHOST_INCLUDE_DIR NETHOST_LIBRARY NETHOST_RUNTIME_LIBRARY NETHOST_ROOT)
