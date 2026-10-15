# This is a dummy package configuration file for SharpCpp.Rbfx. It lets a consumer use the source
# tree (through add_subdirectory()) with the same find_package() call as for the installed SDK:
#
#   list(APPEND CMAKE_PREFIX_PATH "<SharpCpp.Rbfx>/cmake/SharpCppRbfx")
#   find_package(Urho3D REQUIRED)                    # first: the installed rbfx SDK, shared library
#   find_package(SharpCppRbfx CONFIG REQUIRED)
#
# Notes:
# - The engine must come from an installed rbfx SDK. Call find_package(Urho3D) before this package.
#   A static engine is supported only on iOS and Emscripten.
# - SHARPCPP_ROOT is the SharpCpp checkout. The default is the folder ../SharpCpp next to this
#   repository. Set SHARPCPP_ROOT before find_package() when the checkout is in another place.
# - Set RBFX_BUILD_USERCODE, RBFX_BUILD_EXAMPLES, RBFX_INSTALL_SDK or RBFX_BUILD_HOST before
#   find_package() to change the defaults below.
# - The first call adds the source tree. A later call, also from a sibling directory, only sets the
#   variables of the package.

# Source root directory
get_filename_component(SharpCppRbfx_SOURCE_ROOT "${CMAKE_CURRENT_LIST_DIR}/../../../.." ABSOLUTE)

# Include SharpCpp.Rbfx as in-source build, once
if(NOT TARGET sharpcpp-rbfx-native-all)
  if(NOT TARGET Urho3D)
    message(FATAL_ERROR
      "SharpCppRbfx: the target Urho3D does not exist. "
      "Call find_package(Urho3D) with an installed rbfx SDK before find_package(SharpCppRbfx).")
  endif()
  get_target_property(_sharpcpp_rbfx_engine_type Urho3D TYPE)
  if(_sharpcpp_rbfx_engine_type STREQUAL "STATIC_LIBRARY" AND NOT CMAKE_SYSTEM_NAME STREQUAL "iOS" AND NOT EMSCRIPTEN)
    message(FATAL_ERROR
      "SharpCppRbfx: the target Urho3D is a static library. "
      "A static engine is supported only on iOS and Emscripten.")
  endif()
  unset(_sharpcpp_rbfx_engine_type)

  # Defaults for a consumer
  foreach(_sharpcpp_rbfx_option IN ITEMS RBFX_BUILD_USERCODE RBFX_BUILD_EXAMPLES RBFX_INSTALL_SDK)
    if(NOT DEFINED ${_sharpcpp_rbfx_option})
      set(${_sharpcpp_rbfx_option} OFF)
    endif()
  endforeach()
  if(NOT DEFINED RBFX_BUILD_HOST)
    set(RBFX_BUILD_HOST ON)
  endif()
  unset(_sharpcpp_rbfx_option)

  add_subdirectory("${SharpCppRbfx_SOURCE_ROOT}" "${CMAKE_BINARY_DIR}/SharpCppRbfx")
endif()
# The functions rbfx_csharp_add_project(), rbfx_csharp_package_application(), rbfx_csharp_add_plugin(), rbfx_csharp_add_launcher(), rbfx_csharp_add_data(), rbfx_csharp_add_plugin_build(), rbfx_csharp_add_dotnet_project() and rbfx_csharp_add_wrapper()
# are global. The root CMakeLists.txt of this repository includes their modules.

# rbfx_csharp_add_wrapper() reads variables that the root CMakeLists.txt of the source tree sets in the
# scope of its own directory. Copy them to the scope of this call. The list is the one that the function
# checks. A later call, also from a sibling directory, copies them again.
get_property(_sharpcpp_rbfx_input_variables GLOBAL PROPERTY SHARPCPP_RBFX_USERCODE_INPUT_VARIABLES)
get_property(_sharpcpp_rbfx_optional_variables GLOBAL PROPERTY SHARPCPP_RBFX_USERCODE_OPTIONAL_VARIABLES)
# The property DEFINITION gives an empty string for an empty and for an undefined variable. Copy a
# variable (also an empty one) only when the directory of the source tree defines it.
get_directory_property(_sharpcpp_rbfx_defined_variables DIRECTORY "${SharpCppRbfx_SOURCE_ROOT}" VARIABLES)
foreach(_sharpcpp_rbfx_variable IN LISTS _sharpcpp_rbfx_input_variables _sharpcpp_rbfx_optional_variables)
  if(_sharpcpp_rbfx_variable IN_LIST _sharpcpp_rbfx_defined_variables)
    get_directory_property(_sharpcpp_rbfx_value DIRECTORY "${SharpCppRbfx_SOURCE_ROOT}" DEFINITION ${_sharpcpp_rbfx_variable})
    set(${_sharpcpp_rbfx_variable} "${_sharpcpp_rbfx_value}")
  endif()
endforeach()
unset(_sharpcpp_rbfx_input_variables)
unset(_sharpcpp_rbfx_optional_variables)
unset(_sharpcpp_rbfx_defined_variables)
unset(_sharpcpp_rbfx_variable)
unset(_sharpcpp_rbfx_value)

# Provide path to package root
set(SharpCppRbfx_PACKAGE_ROOT "${SharpCppRbfx_SOURCE_ROOT}")

# The folder of the managed assemblies (Bindings, Support, Std, Ea) that "dotnet build" writes.
# The value is a generator expression.
set(SharpCppRbfx_MANAGED_DIR "${SHARPCPP_MANAGED_OUTPUT_ROOT}/$<CONFIG>")

# This package is not an installed SDK
set(SharpCppRbfx_IS_SDK OFF)

# Mark SharpCppRbfx as found
set(SharpCppRbfx_FOUND TRUE)
