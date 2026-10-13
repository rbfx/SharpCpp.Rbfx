list(APPEND CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/../cmake")
find_package(NetHost REQUIRED)

add_library(sharpcpp-rbfx-host ${RBFX_NATIVE_LIBRARY_TYPE} src/DotNetHost.cpp)
add_library(SharpCpp::RbfxHost ALIAS sharpcpp-rbfx-host)

target_compile_features(sharpcpp-rbfx-host PUBLIC cxx_std_20)
target_include_directories(
  sharpcpp-rbfx-host
  PUBLIC
    "$<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>")
target_link_libraries(
  sharpcpp-rbfx-host
  PRIVATE
    NetHost::NetHost
    ${CMAKE_DL_LIBS})

if(RBFX_NATIVE_LIBRARY_TYPE STREQUAL "STATIC")
  target_compile_definitions(sharpcpp-rbfx-host PUBLIC URHO3D_DOTNETHOST_STATIC)
else()
  target_compile_definitions(sharpcpp-rbfx-host PRIVATE URHO3D_DOTNETHOST_EXPORTS)
endif()

# Build-tree fallback of the dotnet root. The build tree is not committed.
if(NETHOST_DOTNET_ROOT)
  target_compile_definitions(
    sharpcpp-rbfx-host
    PRIVATE SHARPCPP_RBFX_DEFAULT_DOTNET_ROOT="${NETHOST_DOTNET_ROOT}")
endif()

set_target_properties(
  sharpcpp-rbfx-host
  PROPERTIES
    CXX_SCAN_FOR_MODULES OFF
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON
    POSITION_INDEPENDENT_CODE ON
    OUTPUT_NAME "SharpCpp.Rbfx.Host"
    BUILD_RPATH "${RBFX_RUNTIME_RPATH}"
    INSTALL_RPATH "${RBFX_RUNTIME_RPATH}")
sharpcpp_set_native_output(sharpcpp-rbfx-host)

# A shared nethost on Windows needs nethost.dll next to the host library at run time.
if(NETHOST_RUNTIME_LIBRARY AND RBFX_NATIVE_LIBRARY_TYPE STREQUAL "SHARED")
  add_custom_command(
    TARGET sharpcpp-rbfx-host POST_BUILD
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different
      "${NETHOST_RUNTIME_LIBRARY}"
      "$<TARGET_FILE_DIR:sharpcpp-rbfx-host>"
    VERBATIM)
endif()
