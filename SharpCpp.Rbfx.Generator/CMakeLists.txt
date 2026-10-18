# The generate command waits for the build of the generator. In the depfile mode of rbfx_csharp_add_dotnet_project the
# build of the generator has a stamp file, and the depfile of the build lists the C# sources of the generator and of the
# projects that it references. A change of a source file then runs the build of the generator and the generate command.
# Without the stamp, the command depends on the DLL and on the C# files (a glob).
set(RBFX_GENERATOR_BUILD_STAMP)
if(TARGET sharpcpp-rbfx-build-generator)
  get_target_property(RBFX_GENERATOR_BUILD_STAMP sharpcpp-rbfx-build-generator SHARPCPP_RBFX_DOTNET_STAMP)
endif()
if(RBFX_GENERATOR_BUILD_STAMP)
  set(RBFX_GENERATOR_BUILD_DEPENDENCIES "${RBFX_GENERATOR_BUILD_STAMP}")
  file(GLOB_RECURSE RBFX_GENERATOR_INPUTS CONFIGURE_DEPENDS
    "${SHARPCPP_ROOT}/include/SharpCpp/*.hpp"
    "${SHARPCPP_ROOT}/include/SharpCpp/*.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/../include/SharpCpp/*.hpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/../include/SharpCpp/*.h")
else()
  set(RBFX_GENERATOR_BUILD_DEPENDENCIES "${SHARPCPP_RBFX_GENERATOR_DLL}")
  file(GLOB_RECURSE RBFX_GENERATOR_INPUTS CONFIGURE_DEPENDS
    "${SHARPCPP_ROOT}/src/SharpCpp.Model/*.cs"
    "${SHARPCPP_ROOT}/src/SharpCpp.Metadata/*.cs"
    "${SHARPCPP_ROOT}/src/SharpCpp.Parsing/*.cs"
    "${SHARPCPP_ROOT}/src/SharpCpp.Pipeline/*.cs"
    "${SHARPCPP_ROOT}/src/SharpCpp.Rendering/*.cs"
    "${CMAKE_CURRENT_SOURCE_DIR}/*.cs"
    "${SHARPCPP_ROOT}/include/SharpCpp/*.hpp"
    "${SHARPCPP_ROOT}/include/SharpCpp/*.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/../include/SharpCpp/*.hpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/../include/SharpCpp/*.h")
endif()

# The list of generated sources lives outside the generated directory, which the staleness check clears.
# Its timestamp changes only when the set of sources changes, so CMake re-runs only then.
add_custom_command(
  OUTPUT "${RBFX_GENERATED_STAMP}"
  BYPRODUCTS "${RBFX_GENERATED_SOURCE_LIST}"
  COMMAND "${CMAKE_COMMAND}"
          "-DGENERATOR_DLL=${SHARPCPP_RBFX_GENERATOR_DLL}"
          "-DGENERATED_STAMP=${RBFX_GENERATED_STAMP}"
          "-DGENERATED_DIR=${RBFX_GENERATED_ROOT}"
          -P "${SHARPCPP_ROOT}/cmake/SharpCppGeneratedStaleness.cmake"
  COMMAND "${CMAKE_COMMAND}" -E make_directory "${RBFX_GENERATED_ROOT}"
  COMMAND "${CMAKE_COMMAND}" -E env
          "SHARPCPP_RBFX_CLANG_RESOURCE_DIRECTORY=${SHARPCPP_CLANG_RESOURCE_DIRECTORY}"
          "${DOTNET_EXECUTABLE}" "${SHARPCPP_RBFX_GENERATOR_DLL}"
          ${RBFX_ENGINE_GENERATOR_ARGUMENTS}
          --flags-file "${RBFX_SDK_FLAGS_FILE}"
          --generated-root "${RBFX_GENERATED_ROOT}"
          --sharpcpp-include "${SHARPCPP_ROOT}/include"
          --rbfx-include "${PROJECT_SOURCE_DIR}/include"
          --sdk-library "$<TARGET_FILE:${RBFX_URHO3D_TARGET}>"
          --log-file "${RBFX_GENERATOR_LOG_FILE}"
  COMMAND "${CMAKE_COMMAND}"
          "-DGENERATED_DIR=${RBFX_GENERATED_ROOT}"
          "-DSOURCE_LIST=${RBFX_GENERATED_SOURCE_LIST}"
          -P "${PROJECT_SOURCE_DIR}/cmake/RbfxWriteGeneratedSourceList.cmake"
  COMMAND "${CMAKE_COMMAND}" -E touch "${RBFX_GENERATED_STAMP}"
  DEPENDS
    ${RBFX_GENERATOR_BUILD_DEPENDENCIES}
    "${RBFX_SDK_FLAGS_FILE}"
    ${RBFX_SDK_INPUT_HEADERS}
    ${RBFX_GENERATOR_INPUTS}
  WORKING_DIRECTORY "${PROJECT_SOURCE_DIR}"
  VERBATIM
  USES_TERMINAL)

add_custom_target(sharpcpp-generate-rbfx DEPENDS "${RBFX_GENERATED_STAMP}")
# The dotnet builds that read the generated sources wait for this stamp (rbfx_csharp_dotnet_depend).
set_property(TARGET sharpcpp-generate-rbfx PROPERTY SHARPCPP_RBFX_DOTNET_STAMP "${RBFX_GENERATED_STAMP}")
# CMake records only an existing file as a configure dependency.
if(NOT EXISTS "${RBFX_GENERATED_SOURCE_LIST}")
  file(WRITE "${RBFX_GENERATED_SOURCE_LIST}" "")
endif()
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${RBFX_GENERATED_SOURCE_LIST}")
