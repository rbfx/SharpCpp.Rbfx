rbfx_csharp_add_usercode(Example.UserCode
  HEADERS include/RbfxExamples/RotatingComponent.hpp include/RbfxExamples/TextTools.hpp
  SOURCES src/RotatingComponent.cpp src/TextTools.cpp
  INCLUDE_DIRECTORIES include
  NAMESPACE RbfxExamples
  ASSEMBLY_NAME Example.UserCode.Bindings
  LIBRARY_NAME Example.UserCode.Native
  SYMBOL_PREFIX rbfxex
  GENERATED_DIRECTORY rbfx-example-usercode)
