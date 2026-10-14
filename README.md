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
- The package files `SharpCppRbfxConfig.cmake` and `SharpCppRbfxConfigVersion.cmake`.

The installed SDK does not have these parts today:

- The generated C# bindings and the native binding library.
- `rbfx_csharp_add_usercode()`.
- The managed generators.

`rbfx_csharp_add_plugin()` needs the native binding target of the full build tree.
It does not work in a consumer project until the SDK has the generated bindings.
