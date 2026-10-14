# SharpCpp.Rbfx

C# bindings for rbfx and the .NET hosting support for native rbfx applications.

## CMake SDK

The root `CMakeLists.txt` can install a CMake SDK. The option `RBFX_INSTALL_SDK` controls it.
The default is ON when `RBFX_BUILD_HOST` is ON and this project is the top-level project.
Configure a build that you ship with `-DRBFX_HOST_DEFAULT_DOTNET_ROOT=OFF`. Then the host library has no path of the build machine.
Use one install prefix for each configuration, because the Debug and Release files have the same name.
The install rules do not change the build. Run the install step to use them.

```
cmake --build <build dir> --config <Config>
cmake --install <build dir> --config <Config> --prefix <prefix>
```

A consumer project uses the SDK like this:

```cmake
find_package(SharpCppRbfx CONFIG REQUIRED)
target_link_libraries(my_app PRIVATE SharpCpp::RbfxHost)
```

Set `CMAKE_PREFIX_PATH` to the SDK prefix and to the installed rbfx SDK (or set `Urho3D_DIR`).
Set `BUILD_SHARED_LIBS` in the consumer to the same value as the rbfx SDK. Urho3D checks it.

The installed SDK has these parts:

- The host library `SharpCpp.Rbfx.Host` and the headers `Urho3D/CSharp/DotNetHost.h` and `Urho3D/CSharp/DotNetHostExport.h`.
- The imported target `SharpCpp::RbfxHost`.
- The nethost runtime library next to the host library, when the host library is shared.
  On Linux and macOS the host library finds it through its own run-time path. On Windows the
  consumer must put `nethost.dll` next to its executable.
- `share/SharpCppRbfx/cmake/FindNetHost.cmake`. The package file uses it for a static host library.
- `share/SharpCppRbfx/cmake/RbfxCSharpDotNet.cmake` with `rbfx_csharp_add_dotnet_project()` and `rbfx_csharp_add_plugin()`.
- The native binding wrapper `SharpCpp.Rbfx.Native` with the imported target `SharpCpp::RbfxNative`. This part is in a source-mode build with a shared library type.
  The install step copies the engine shared library and the other runtime files of the wrapper next to it (`lib` on Linux and macOS, `bin` on Windows).
  The wrapper finds them through its own run-time path.
- The binding metadata `SharpCpp.Rbfx.Bindings.json` and `SharpCpp.Rbfx.ParseUmbrella.hpp` in `share/SharpCppRbfx/metadata`.
- The package files `SharpCppRbfxConfig.cmake` and `SharpCppRbfxConfigVersion.cmake`.

- The managed assemblies `SharpCpp.Rbfx.Bindings`, `SharpCpp.Support`, `SharpCpp.Std`, `SharpCpp.Ea` and `SharpCpp.Rbfx.SourceGenerator` in `share/SharpCppRbfx/managed`.
- The user-code generator in `share/SharpCppRbfx/tools/usercode`. Run it with `dotnet <prefix>/share/SharpCppRbfx/tools/usercode/Rbfx.UserCode.Generator.dll`.
  It needs a .NET runtime on the machine. The install step copies the files that the generator needs from the output folder of `dotnet build`.
- The SharpCpp headers in `include/SharpCpp` (`Marshaller.hpp` and its includes, and the `detail` marshaller headers of this repository).
  They are the input of `--sharpcpp-include` and `--rbfx-include`.
- `share/SharpCppRbfx/cmake/SharpCppGeneratedStaleness.cmake`.

The managed files come from the output folder of `dotnet build SharpCpp.Rbfx.slnx` for the same configuration.
The install step does not build them. It stops with a message when a file is missing.

The installed SDK does not have this part today:

- `rbfx_csharp_add_usercode()`.

`rbfx_csharp_add_plugin()` needs the native binding target of the full build tree.
It needs the target `sharpcpp-rbfx-runtime` of the full build tree, so it does not work in a consumer project yet.
The install step needs a finished build, because the generator writes the metadata files and the build links the wrapper.
A cross build does not install the engine runtime files.
