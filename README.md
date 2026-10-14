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

A consumer project wraps its own C++ headers like this. The consumer needs no SharpCpp or SharpCpp.Rbfx source tree:

```cmake
find_package(SharpCppRbfx CONFIG REQUIRED)
add_library(MyGame.Impl STATIC MyGame.cpp)
target_include_directories(MyGame.Impl PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(MyGame.Impl PUBLIC Urho3D)
rbfx_csharp_add_usercode(MyGame
  HEADERS MyGame.hpp
  INCLUDE_DIRECTORIES .
  LINK_LIBRARIES MyGame.Impl
  NAMESPACE MyGame)
```

The C++ namespace of the wrapped types must be the value of `NAMESPACE`. The generator ignores the other namespaces.
The package file sets the inputs of the function. They are the installed metadata, generator, headers and Clang headers.
The parse flags come from the target `Urho3D` and the compiler of the consumer. The rbfx SDK root comes from `Urho3D_DIR`. Set `RBFX_SDK_ROOT` to override it.
The generated sources go to `<build>/generated` and the native library goes to `<build>/bin/<Config>`.
Set `SHARPCPP_GENERATED_ROOT` and `SHARPCPP_NATIVE_OUTPUT_ROOT` to change them.
The generator runs with `dotnet` at build time. The build needs no network and no git.

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
- `share/SharpCppRbfx/cmake/RbfxCSharpAddUserCode.cmake` with `rbfx_csharp_add_usercode()`, its templates and its helper modules
  (`ExportRbfxSdkParseFlags.cmake`, `SharpCppOutput.cmake`). The package file includes it in a source-mode build with a shared library type.
- The builtin Clang headers of the generator in `share/SharpCppRbfx/clang-resource/include`.

The managed files come from the output folder of `dotnet build SharpCpp.Rbfx.slnx` for the same configuration.
The install step does not build them. It stops with a message when a file is missing.

In a consumer project, `rbfx_csharp_add_usercode()` writes the managed project files, but the SDK has no MSBuild files for them yet.
Set `RBFX_CSHARP_MSBUILD_PROPS`, `RBFX_CSHARP_MSBUILD_TARGETS` and `RBFX_CSHARP_BINDINGS_PROJECTS` before you build the written project.
In a consumer project, the target of `rbfx_csharp_add_plugin()` is not part of the default build. Add it with `add_dependencies()`.
The install step needs a finished build, because the generator writes the metadata files and the build links the wrapper.

### SDK archives

`packaging/sdk/pack-sdk.sh` installs the SDK and packs it into a 7z archive. It needs `7z` and a finished build.
Configure the build tree with `-DRBFX_HOST_DEFAULT_DOTNET_ROOT=OFF`. The script works in Git Bash on Windows too.

```
packaging/sdk/pack-sdk.sh --build-dir <tree> --config <Config> --tag <platform tag> --out <dir>
```

The archive is `<dir>/sharpcpp-rbfx-sdk-<tag>-latest.7z`. It has one root folder with the same name (example tag: `linux-gcc-x64-dll`).

`packaging/sdk/check-consumer.sh` extracts an archive (or takes an extracted folder), then configures and builds the project
`packaging/sdk-consumer` against it and the rbfx SDK. It fails when the generated wrapper source is missing.

```
packaging/sdk/check-consumer.sh --sdk-archive <7z> --rbfx-sdk <rbfx install root> --config <Config> --work <dir>
packaging/sdk/check-consumer.sh --sdk-dir <dir> --rbfx-sdk <rbfx install root> --config <Config> --work <dir>
```
A cross build does not install the engine runtime files.

CI builds the archive in the `Package` job for `linux-x64`, `osx-arm64` and `win-x64` (artifact `sdk-<rid>`).
The `SDK-Consumer` job checks it. The release jobs upload the `.7z` files to the release (`--clobber`). `uap-x64` has no archive.
