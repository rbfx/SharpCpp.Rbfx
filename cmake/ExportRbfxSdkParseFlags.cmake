function(_sharpcpp_collect_rbfx_interface_property target_name property_name output_variable visited_targets)
  if(NOT TARGET "${target_name}" OR "${target_name}" IN_LIST visited_targets)
    set("${output_variable}" "" PARENT_SCOPE)
    return()
  endif()

  list(APPEND visited_targets "${target_name}")
  get_target_property(property_values "${target_name}" "${property_name}")
  if(NOT property_values OR property_values STREQUAL "property_values-NOTFOUND")
    set(property_values)
  endif()

  get_target_property(link_values "${target_name}" INTERFACE_LINK_LIBRARIES)
  foreach(link_value IN LISTS link_values)
    if(TARGET "${link_value}")
      _sharpcpp_collect_rbfx_interface_property(
        "${link_value}" "${property_name}" linked_property_values "${visited_targets}")
      list(APPEND property_values ${linked_property_values})
    endif()
  endforeach()

  set("${output_variable}" "${property_values}" PARENT_SCOPE)
endfunction()

function(sharpcpp_export_rbfx_sdk_parse_flags target_name output_file native_library_type language_standard)
  if(NOT TARGET "${target_name}")
    message(FATAL_ERROR "Cannot export rbfx SDK flags: target '${target_name}' does not exist.")
  endif()

  _sharpcpp_collect_rbfx_interface_property(
    "${target_name}" INTERFACE_INCLUDE_DIRECTORIES include_directories "")
  _sharpcpp_collect_rbfx_interface_property(
    "${target_name}" INTERFACE_COMPILE_DEFINITIONS compile_definitions "")
  _sharpcpp_collect_rbfx_interface_property(
    "${target_name}" INTERFACE_COMPILE_OPTIONS compile_options "")
  list(REMOVE_DUPLICATES include_directories)
  list(REMOVE_DUPLICATES compile_definitions)
  list(REMOVE_DUPLICATES compile_options)

  string(CONCAT flags_content
    "compiler_path=${CMAKE_CXX_COMPILER}\n"
    "symbol_tool_path=${CMAKE_NM}\n"
    "language_standard=${language_standard}\n")
  if(native_library_type STREQUAL "STATIC")
    if(EMSCRIPTEN)
      # .NET's NativeFileReference registers static archives by their file stem.
      string(APPEND flags_content "native_library_name=libSharpCpp.Rbfx.Native\n")
    else()
      string(APPEND flags_content "native_library_name=__Internal\n")
    endif()
  endif()

  if(CMAKE_CXX_COMPILER_TARGET)
    string(APPEND flags_content "target_triple=${CMAKE_CXX_COMPILER_TARGET}\n")
  elseif(CMAKE_SYSTEM_NAME STREQUAL "iOS")
    string(APPEND flags_content "target_triple=${CMAKE_OSX_ARCHITECTURES}-apple-ios${CMAKE_OSX_DEPLOYMENT_TARGET}\n")
  endif()

  if(CMAKE_SYSTEM_NAME STREQUAL "Android")
    set(android_sdk_level)
    if(ANDROID_PLATFORM MATCHES "^android-([0-9]+)$")
      set(android_sdk_level "${CMAKE_MATCH_1}")
    elseif(CMAKE_SYSTEM_VERSION MATCHES "^[0-9]+$")
      set(android_sdk_level "${CMAKE_SYSTEM_VERSION}")
    endif()
    if(android_sdk_level)
      string(APPEND flags_content "define=EA_ANDROID_SDK_LEVEL=${android_sdk_level}\n")
    endif()
  endif()

  if((CMAKE_SYSTEM_NAME STREQUAL "iOS" OR CMAKE_SYSTEM_NAME STREQUAL "tvOS") AND
      CMAKE_OSX_SYSROOT AND IS_DIRECTORY "${CMAKE_OSX_SYSROOT}")
    string(APPEND flags_content "sysroot=${CMAKE_OSX_SYSROOT}\n")
  elseif(CMAKE_SYSROOT)
    string(APPEND flags_content "sysroot=${CMAKE_SYSROOT}\n")
  elseif(CMAKE_OSX_SYSROOT AND IS_DIRECTORY "${CMAKE_OSX_SYSROOT}")
    string(APPEND flags_content "sysroot=${CMAKE_OSX_SYSROOT}\n")
  endif()

  if(RBFX_SDK_PARSE_USE_PLATFORM_COMPILER_INCLUDES)
    execute_process(
      COMMAND "${CMAKE_CXX_COMPILER}" -print-resource-dir
      RESULT_VARIABLE resource_directory_result
      OUTPUT_VARIABLE platform_compiler_resource_directory
      OUTPUT_STRIP_TRAILING_WHITESPACE
      ERROR_VARIABLE resource_directory_error)
    if(NOT resource_directory_result EQUAL 0 OR NOT IS_DIRECTORY "${platform_compiler_resource_directory}/include")
      message(FATAL_ERROR
        "Could not query builtin Clang headers from the platform compiler '${CMAKE_CXX_COMPILER}': ${resource_directory_error}")
    endif()
    string(APPEND flags_content "clang_resource_directory=${platform_compiler_resource_directory}\n")
  endif()

  foreach(include_directory IN LISTS include_directories)
    string(APPEND flags_content "include_directory=${include_directory}\n")
  endforeach()
  foreach(compile_definition IN LISTS compile_definitions)
    string(APPEND flags_content "define=${compile_definition}\n")
  endforeach()
  foreach(compile_option IN LISTS compile_options)
    if(NOT compile_option STREQUAL "")
      string(APPEND flags_content "clang_argument=${compile_option}\n")
    endif()
  endforeach()
  foreach(parse_option IN LISTS RBFX_SDK_PARSE_EXTRA_OPTIONS)
    if(NOT parse_option STREQUAL "")
      string(APPEND flags_content "clang_argument=${parse_option}\n")
    endif()
  endforeach()

  # The options can hold $<COMPILE_LANGUAGE:CXX>. CMake then evaluates the content once for each enabled
  # language and fails when a consumer enables C too and the files differ. The condition keeps the CXX
  # evaluation only, which is the one that the parse needs.
  file(GENERATE OUTPUT "${output_file}" CONTENT "${flags_content}" CONDITION "$<COMPILE_LANGUAGE:CXX>")
  set(RBFX_URHO3D_INCLUDE_DIRECTORIES "${include_directories}" PARENT_SCOPE)
  set(RBFX_URHO3D_COMPILE_DEFINITIONS "${compile_definitions}" PARENT_SCOPE)
  set(RBFX_URHO3D_COMPILE_OPTIONS "${compile_options}" PARENT_SCOPE)
endfunction()
