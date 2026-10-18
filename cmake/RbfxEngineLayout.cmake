# sharpcpp_resolve_rbfx_engine_layout(<target>)
#
# Finds out how the rbfx engine target <target> is provided and sets the layout variables in the scope of the caller.
# An IMPORTED target comes from an installed SDK (mode SDK). Any other target is built from the engine sources
# (mode SOURCE). In SOURCE mode the engine headers are the headers that the target compiles (the property SOURCES
# and the header sets of the target), because the source tree also has the headers of the disabled modules.
# The function writes them to the file SharpCppRbfxEngineHeaders.txt in the current binary directory
# (one absolute path per line) and passes it with --engine-header-list. The function sets these variables:
#   RBFX_ENGINE_MODE                  SDK or SOURCE.
#   RBFX_ENGINE_ROOT                  SDK mode: the root of the installed SDK. SOURCE mode: the root of the rbfx checkout.
#   RBFX_ENGINE_GENERATOR_ARGUMENTS   The arguments of the generator that select the engine headers.
#   RBFX_USERCODE_ENGINE_ARGUMENTS    The arguments of the user-code generator that select the engine. Empty in SOURCE mode.
#   RBFX_SDK_INPUT_HEADERS            The engine headers that are inputs of the generation.
#                                     SOURCE mode: the list file of the headers is in it.
#   RBFX_ENGINE_RUNTIME_ROOT          SDK mode: the SDK root. Empty in SOURCE mode.
function(sharpcpp_resolve_rbfx_engine_layout target_name)
  get_target_property(target_imported "${target_name}" IMPORTED)
  if(target_imported)
    set(engine_mode SDK)
  else()
    set(engine_mode SOURCE)
  endif()

  if(engine_mode STREQUAL "SDK")
    if(NOT Urho3D_DIR)
      message(FATAL_ERROR
        "SharpCpp.Rbfx: the target Urho3D exists, but it does not come from an installed rbfx SDK. "
        "Add the installed SDK to CMAKE_PREFIX_PATH.")
    endif()
    # Urho3D_DIR is <SDK root>/share/Urho3D/CMake.
    get_filename_component(engine_root "${Urho3D_DIR}/../../.." ABSOLUTE)
    if(NOT EXISTS "${engine_root}/include/Urho3D")
      message(FATAL_ERROR "SharpCpp.Rbfx: the rbfx SDK headers were not found at '${engine_root}/include/Urho3D'.")
    endif()
    set(generator_arguments --sdk-root "${engine_root}")
    set(usercode_arguments --sdk-root "${engine_root}")
    set(runtime_root "${engine_root}")
    file(GLOB_RECURSE input_headers CONFIGURE_DEPENDS
      "${engine_root}/include/Urho3D/*.h"
      "${engine_root}/include/Urho3D/*.hpp"
      "${engine_root}/include/RmlUi/*.h"
      "${engine_root}/include/RmlUi/*.hpp"
      "${engine_root}/include/ImGui/*.h"
      "${engine_root}/include/ImGui/*.hpp")
  else()
    # The source directory of the target is <rbfx>/Source/Urho3D. The generated headers are in its binary directory.
    get_target_property(engine_source_dir "${target_name}" SOURCE_DIR)
    get_target_property(engine_binary_dir "${target_name}" BINARY_DIR)
    if(NOT engine_source_dir OR NOT EXISTS "${engine_source_dir}")
      message(FATAL_ERROR "SharpCpp.Rbfx: the source directory '${engine_source_dir}' of the target ${target_name} was not found.")
    endif()
    get_filename_component(engine_root "${engine_source_dir}/../.." ABSOLUTE)
    get_filename_component(rmlui_dir "${engine_source_dir}/../ThirdParty/RmlUi/Include/RmlUi" ABSOLUTE)
    get_filename_component(rmlui_config_dir "${engine_source_dir}/../ThirdParty/RmlUi/Config" ABSOLUTE)
    get_filename_component(imgui_dir "${engine_source_dir}/../ThirdParty/ImGui" ABSOLUTE)
    set(generator_arguments
      --engine-header-root "Urho3D=${engine_source_dir}"
      --engine-header-root "Urho3D=${engine_binary_dir}")
    # The headers that the target compiles. A relative entry is relative to the source directory of the target.
    get_target_property(target_sources "${target_name}" SOURCES)
    if(NOT target_sources)
      set(target_sources)
    endif()
    get_target_property(target_header_sets "${target_name}" HEADER_SETS)
    if(target_header_sets)
      foreach(header_set IN LISTS target_header_sets)
        get_target_property(header_set_files "${target_name}" "HEADER_SET_${header_set}")
        if(header_set_files)
          list(APPEND target_sources ${header_set_files})
        endif()
      endforeach()
    endif()
    set(engine_headers)
    foreach(source_file IN LISTS target_sources)
      if(source_file MATCHES "\\$<" OR NOT source_file MATCHES "\\.(h|hpp)$")
        continue()
      endif()
      get_filename_component(source_file "${source_file}" ABSOLUTE BASE_DIR "${engine_source_dir}")
      list(APPEND engine_headers "${source_file}")
    endforeach()
    # The headers of the root directory and the generated headers are not in the sources of the target.
    file(GLOB root_headers CONFIGURE_DEPENDS "${engine_source_dir}/*.h" "${engine_source_dir}/*.hpp")
    file(GLOB binary_headers "${engine_binary_dir}/*.h")
    list(APPEND engine_headers ${root_headers} ${binary_headers})
    list(REMOVE_DUPLICATES engine_headers)
    list(SORT engine_headers)
    string(REPLACE ";" "\n" engine_header_list "${engine_headers}")
    set(engine_header_list_file "${CMAKE_CURRENT_BINARY_DIR}/SharpCppRbfxEngineHeaders.txt")
    set(engine_header_list "${engine_header_list}\n")
    # Write the file only if the content changes. A new timestamp starts the generator again.
    set(previous_header_list)
    if(EXISTS "${engine_header_list_file}")
      file(READ "${engine_header_list_file}" previous_header_list)
    endif()
    if(NOT previous_header_list STREQUAL engine_header_list)
      file(WRITE "${engine_header_list_file}" "${engine_header_list}")
    endif()
    list(APPEND generator_arguments --engine-header-list "${engine_header_list_file}")
    set(input_headers "${engine_header_list_file}" ${engine_headers})
    if(EXISTS "${rmlui_dir}")
      list(APPEND generator_arguments --engine-header-root "RmlUi=${rmlui_dir}")
      file(GLOB_RECURSE rmlui_headers CONFIGURE_DEPENDS "${rmlui_dir}/*.h" "${rmlui_dir}/*.hpp")
      list(APPEND input_headers ${rmlui_headers})
      # The configuration headers (RmlUi/Config/RbfxConfig.h) are outside the RmlUi include tree in a source tree.
      if(EXISTS "${rmlui_config_dir}")
        list(APPEND generator_arguments --engine-header-root "RmlUiConfig=${rmlui_config_dir}")
        file(GLOB_RECURSE rmlui_config_headers CONFIGURE_DEPENDS "${rmlui_config_dir}/*.h" "${rmlui_config_dir}/*.hpp")
        list(APPEND input_headers ${rmlui_config_headers})
      endif()
    endif()
    if(EXISTS "${imgui_dir}")
      list(APPEND generator_arguments --engine-header-root "ImGui=${imgui_dir}")
      file(GLOB_RECURSE imgui_headers CONFIGURE_DEPENDS "${imgui_dir}/*.h" "${imgui_dir}/*.hpp")
      list(APPEND input_headers ${imgui_headers})
    endif()
    set(usercode_arguments)
    set(runtime_root)
  endif()

  set(RBFX_ENGINE_MODE "${engine_mode}" PARENT_SCOPE)
  set(RBFX_ENGINE_ROOT "${engine_root}" PARENT_SCOPE)
  set(RBFX_ENGINE_GENERATOR_ARGUMENTS "${generator_arguments}" PARENT_SCOPE)
  set(RBFX_USERCODE_ENGINE_ARGUMENTS "${usercode_arguments}" PARENT_SCOPE)
  set(RBFX_SDK_INPUT_HEADERS "${input_headers}" PARENT_SCOPE)
  set(RBFX_ENGINE_RUNTIME_ROOT "${runtime_root}" PARENT_SCOPE)
endfunction()
