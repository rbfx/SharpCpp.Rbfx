include(GNUInstallDirs)
list(APPEND CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/../cmake")
find_package(NetHost REQUIRED)

add_library(sharpcpp-rbfx-host ${RBFX_NATIVE_LIBRARY_TYPE} src/DotNetHost.cpp)
add_library(SharpCpp::RbfxHost ALIAS sharpcpp-rbfx-host)

target_compile_features(sharpcpp-rbfx-host PUBLIC cxx_std_20)
target_include_directories(
  sharpcpp-rbfx-host
  PUBLIC
    "$<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>"
    "$<INSTALL_INTERFACE:${CMAKE_INSTALL_INCLUDEDIR}>")
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

# Build-tree fallback of the dotnet root. Turn it OFF for a build that you install and ship,
# because the host library would carry a path of the build machine.
option(RBFX_HOST_DEFAULT_DOTNET_ROOT "Compile the dotnet root of this machine into the host library as a fallback." ON)
if(NETHOST_DOTNET_ROOT AND RBFX_HOST_DEFAULT_DOTNET_ROOT)
  target_compile_definitions(
    sharpcpp-rbfx-host
    PRIVATE SHARPCPP_RBFX_DEFAULT_DOTNET_ROOT="${NETHOST_DOTNET_ROOT}")
endif()

# The nethost header comes from the dotnet pack of this machine (see RBFX_REMAP_SOURCE_PATHS in the root file).
if(RBFX_REMAP_SOURCE_PATHS AND NETHOST_DOTNET_ROOT AND NOT MSVC AND CMAKE_CXX_COMPILER_ID MATCHES "GNU|Clang")
  target_compile_options(
    sharpcpp-rbfx-host
    PRIVATE
      "-ffile-prefix-map=${NETHOST_DOTNET_ROOT}=dotnet"
      "-fdebug-prefix-map=${NETHOST_DOTNET_ROOT}=dotnet")
endif()

set_target_properties(
  sharpcpp-rbfx-host
  PROPERTIES
    CXX_SCAN_FOR_MODULES OFF
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON
    POSITION_INDEPENDENT_CODE ON
    OUTPUT_NAME "SharpCpp.Rbfx.Host"
    EXPORT_NAME RbfxHost
    BUILD_WITH_INSTALL_RPATH ON
    BUILD_RPATH "${RBFX_RUNTIME_RPATH}"
    INSTALL_RPATH "${RBFX_RUNTIME_RPATH}")
sharpcpp_set_native_output(sharpcpp-rbfx-host)

# A shared nethost needs its library next to the host library at run time. The host library
# finds it there through the "$ORIGIN" or "@loader_path" rpath (RBFX_RUNTIME_RPATH) on Linux
# and macOS, and through the directory of the DLL on Windows.
if(NETHOST_RUNTIME_LIBRARY AND RBFX_NATIVE_LIBRARY_TYPE STREQUAL "SHARED")
  add_custom_command(
    TARGET sharpcpp-rbfx-host POST_BUILD
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different
      "${NETHOST_RUNTIME_LIBRARY}"
      "$<TARGET_FILE_DIR:sharpcpp-rbfx-host>"
    VERBATIM)
endif()

# Install rules. They run only for "cmake --install" and do not change the build.
# The host links NetHost::NetHost privately. A shared host needs nothing from the consumer.
# A static host exports "$<LINK_ONLY:NetHost::NetHost>", so SharpCppRbfxConfig.cmake finds NetHost.
if(RBFX_INSTALL_SDK)
  install(
    TARGETS sharpcpp-rbfx-host
    EXPORT SharpCppRbfxTargets
    RUNTIME DESTINATION "${CMAKE_INSTALL_BINDIR}"
    LIBRARY DESTINATION "${CMAKE_INSTALL_LIBDIR}"
    ARCHIVE DESTINATION "${CMAKE_INSTALL_LIBDIR}")
  install(
    FILES
      include/Urho3D/CSharp/DotNetHost.h
      include/Urho3D/CSharp/DotNetHostExport.h
    DESTINATION "${CMAKE_INSTALL_INCLUDEDIR}/Urho3D/CSharp")
  install(
    EXPORT SharpCppRbfxTargets
    NAMESPACE SharpCpp::
    FILE SharpCppRbfxTargets.cmake
    DESTINATION "${CMAKE_INSTALL_LIBDIR}/cmake/SharpCppRbfx")

  # The installed host library finds nethost through its "$ORIGIN" or "@loader_path" rpath on
  # Linux and macOS. On Windows the loader looks in the directory of the application, so the consumer puts nethost.dll next to its executable.
  if(NETHOST_RUNTIME_LIBRARY AND RBFX_NATIVE_LIBRARY_TYPE STREQUAL "SHARED")
    if(WIN32)
      install(FILES "${NETHOST_RUNTIME_LIBRARY}" DESTINATION "${CMAKE_INSTALL_BINDIR}")
    else()
      install(FILES "${NETHOST_RUNTIME_LIBRARY}" DESTINATION "${CMAKE_INSTALL_LIBDIR}")
    endif()
  endif()
endif()
