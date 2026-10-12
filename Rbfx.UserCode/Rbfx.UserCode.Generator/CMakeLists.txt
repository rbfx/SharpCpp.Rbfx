add_custom_command(
  OUTPUT "${RBFX_USERCODE_GENERATED_STAMP}"
  COMMAND "${CMAKE_COMMAND}"
          "-DGENERATOR_DLL=${SHARPCPP_RBFX_USERCODE_GENERATOR_DLL}"
          "-DGENERATED_STAMP=${RBFX_USERCODE_GENERATED_STAMP}"
          "-DGENERATED_DIR=${RBFX_USERCODE_GENERATED_ROOT}"
          -P "${SHARPCPP_ROOT}/cmake/SharpCppGeneratedStaleness.cmake"
  COMMAND "${CMAKE_COMMAND}" -E make_directory "${RBFX_USERCODE_GENERATED_ROOT}"
  COMMAND "${CMAKE_COMMAND}" -E env
          "SHARPCPP_RBFX_CLANG_RESOURCE_DIRECTORY=${SHARPCPP_CLANG_RESOURCE_DIRECTORY}"
          "${DOTNET_EXECUTABLE}" "${SHARPCPP_RBFX_USERCODE_GENERATOR_DLL}"
          --sdk-root "${RBFX_SDK_ROOT}"
          --flags-file "${RBFX_SDK_FLAGS_FILE}"
          --header "${CMAKE_CURRENT_SOURCE_DIR}/../include/RbfxUserCode/RotatingComponent.hpp"
          --include-directory "${CMAKE_CURRENT_SOURCE_DIR}/../include"
          --assembly-name "Rbfx.UserCode.Bindings"
          --namespace "RbfxUserCode"
          --bindings-class "Bindings"
          --library-name "SharpCpp.Rbfx.UserCode.Native"
          --file-stem "Rbfx.UserCode"
          --symbol-prefix "scrbfxuc"
          --sharpcpp-include "${SHARPCPP_ROOT}/include"
          --rbfx-include "${PROJECT_SOURCE_DIR}/include"
          --generated-root "${RBFX_USERCODE_GENERATED_ROOT}"
          --import-binding-metadata "${RBFX_METADATA_FILE}"
          --log-file "${RBFX_USERCODE_GENERATOR_LOG_FILE}"
  COMMAND "${CMAKE_COMMAND}" -E touch "${RBFX_USERCODE_GENERATED_STAMP}"
  DEPENDS
    "${SHARPCPP_RBFX_USERCODE_GENERATOR_DLL}"
    "${RBFX_SDK_FLAGS_FILE}"
    "${RBFX_GENERATED_STAMP}"
    "${RBFX_USERCODE_NATIVE_SOURCE}"
    ${RBFX_USERCODE_INPUT_HEADERS}
    ${RBFX_SDK_INPUT_HEADERS}
  WORKING_DIRECTORY "${PROJECT_SOURCE_DIR}"
  VERBATIM
  USES_TERMINAL)

add_custom_target(
  sharpcpp-generate-rbfx-usercode
  DEPENDS "${RBFX_USERCODE_GENERATED_STAMP}")
add_dependencies(sharpcpp-generate-rbfx-usercode sharpcpp-generate-rbfx)
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${RBFX_USERCODE_GENERATED_STAMP}")
