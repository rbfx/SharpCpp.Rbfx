rbfx_csharp_add_usercode(SharpCppRbfxUserCode_Native
  HEADERS include/RbfxUserCode/RotatingComponent.hpp
  SOURCES src/RotatingComponent.cpp
  INCLUDE_DIRECTORIES include
  NAMESPACE RbfxUserCode
  ASSEMBLY_NAME Rbfx.UserCode.Bindings
  LIBRARY_NAME SharpCpp.Rbfx.UserCode.Native
  SYMBOL_PREFIX scrbfxuc
  GENERATED_DIRECTORY rbfx-usercode)
