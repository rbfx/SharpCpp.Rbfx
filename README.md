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
The parse flags come from the target `Urho3D` and the compiler of the consumer. The rbfx SDK root comes from `Urho3D_DIR`.
The generated sources go to `<build>/generated` and the native library goes to `<build>/bin/<Config>`.
Set `SHARPCPP_GENERATED_ROOT` and `SHARPCPP_NATIVE_OUTPUT_ROOT` to change them.
The generator runs with `dotnet` at build time. The build needs no network and no git.

### Consume with find_package

Two prefixes give the package `SharpCppRbfx`. Both prefixes give the same targets, functions and variables.
The engine must come from an installed rbfx SDK. The two prefixes differ in how they find it.

- SDK prefix. Put the prefix of `cmake --install` on `CMAKE_PREFIX_PATH`. The package uses the installed files.
  The package calls `find_dependency(Urho3D)` itself. Set `Urho3D_DIR` or `CMAKE_PREFIX_PATH` to the rbfx SDK.
- Shim prefix. Put the folder `cmake/SharpCppRbfx` of this repository on `CMAKE_PREFIX_PATH`. The package builds this source tree
  with `add_subdirectory()` in `<build>/SharpCppRbfx`. A SharpCpp checkout must be at `SHARPCPP_ROOT` (default `../SharpCpp`).
  The shim sets `RBFX_BUILD_USERCODE`, `RBFX_BUILD_EXAMPLES` and `RBFX_INSTALL_SDK` to OFF and `RBFX_BUILD_HOST` to ON, when the consumer did not define them.
  The consumer must call `find_package(Urho3D)` before `find_package(SharpCppRbfx)`. The desktop shared engine is the tested case.
  A static engine is accepted on iOS and Emscripten only. An engine in source mode is not supported.
  The first call adds the source tree. A second call (also from a sibling directory) only sets the variables below.
  The default `SHARPCPP_NATIVE_OUTPUT_ROOT` is `<build>/SharpCppRbfx/bin` and the default `SHARPCPP_MANAGED_OUTPUT_ROOT` is
  `<build>/SharpCppRbfx/managed`. A value from the consumer wins.
  The shim build runs `dotnet build` itself for the generators (`SharpCpp.Rbfx.Generator` and `Rbfx.UserCode.Generator`), so the consumer
  needs only `CMAKE_PREFIX_PATH`. The .NET SDK (`dotnet`) must be installed. CMake stops with a message when it is not found.
  With `RBFX_CSHARP_BUILD_GENERATORS` ON (the default for the shim), the target `sharpcpp-rbfx-build-managed` runs `dotnet build` for
  `SharpCpp.Rbfx.Bindings` after the native wrapper. The managed folder then holds `SharpCpp.Rbfx.Bindings`, `SharpCpp.Std`,
  `SharpCpp.Ea` and `SharpCpp.Support`. The folder is full only when the build runs this target. The targets of `rbfx_csharp_add_dotnet_project()` and
  `rbfx_csharp_add_plugin()` depend on it. The targets of `rbfx_csharp_add_usercode()` do not. A consumer target that reads
  `SharpCppRbfx_MANAGED_DIR` in its own custom command must call `add_dependencies(<target> sharpcpp-rbfx-build-managed)`.
  Set `RBFX_CSHARP_BUILD_GENERATORS` to OFF to use generators that you built before in `SHARPCPP_MANAGED_OUTPUT_ROOT/<Config>`.
  Then the managed folder is not filled by CMake either.

```cmake
find_package(Urho3D REQUIRED)
find_package(SharpCppRbfx CONFIG REQUIRED)
add_executable(my_app main.cpp)
target_link_libraries(my_app PRIVATE Urho3D SharpCpp::RbfxHost)
```

```
cmake -G Ninja -S <consumer> -B <build> -DCMAKE_PREFIX_PATH="<rbfx SDK prefix>;<SDK prefix or <repo>/cmake/SharpCppRbfx>"
```

Both prefixes set these variables:

- `SharpCppRbfx_FOUND`: TRUE.
- `SharpCppRbfx_IS_SDK`: ON for the SDK prefix, OFF for the shim prefix.
- `SharpCppRbfx_PACKAGE_ROOT`: the SDK prefix, or the root of this repository.
- `SharpCppRbfx_MANAGED_DIR`: the folder of the managed assemblies (`SharpCpp.Rbfx.Bindings`, `SharpCpp.Support`, `SharpCpp.Std`, `SharpCpp.Ea`).
  In the shim package the value is a generator expression, `<SHARPCPP_MANAGED_OUTPUT_ROOT>/$<CONFIG>`. Use it only where CMake
  evaluates generator expressions: in the `COMMAND` of `add_custom_command()` and `add_custom_target()`, and in the `PROPERTIES` of
  the `rbfx_csharp_add_*` functions. The target `sharpcpp-rbfx-build-managed` fills the folder (see above). It exists only when `RBFX_CSHARP_BUILD_GENERATORS` is ON.
  In the SDK package the value is a plain path, `<prefix>/share/SharpCppRbfx/managed`. The SDK package leaves the variable unset
  when the SDK was built with `RBFX_CSHARP_BINDINGS_PACKAGE_VERSION`, because it then has no managed files.

The package gives the targets `SharpCpp::RbfxHost` and `SharpCpp::RbfxNative` and the functions `rbfx_csharp_add_plugin()`,
`rbfx_csharp_add_dotnet_project()` and `rbfx_csharp_add_usercode()`.

The installed SDK has these parts:

- The host library `SharpCpp.Rbfx.Host` and the headers `Urho3D/CSharp/DotNetHost.h` and `Urho3D/CSharp/DotNetHostExport.h`.
- The imported target `SharpCpp::RbfxHost`.
- The nethost runtime library next to the host library, when the host library is shared.
  On Linux and macOS the host library finds it through its own run-time path. On Windows the
  consumer must put `nethost.dll` next to its executable.
- `share/SharpCppRbfx/cmake/FindNetHost.cmake` (hostfxr mode only). The package file uses it for a static host library.
- `share/SharpCppRbfx/cmake/RbfxCSharpDotNet.cmake` with `rbfx_csharp_add_dotnet_project()` and `rbfx_csharp_add_plugin()`.
- The native binding wrapper `SharpCpp.Rbfx.Native` with the imported target `SharpCpp::RbfxNative`. This part is in a source-mode build, for the shared and for the static library type.
  The install step copies the engine shared library and the other runtime files of the wrapper next to it (`lib` on Linux and macOS, `bin` on Windows).
  The wrapper finds them through its own run-time path.
- The binding metadata `SharpCpp.Rbfx.Bindings.json` and `SharpCpp.Rbfx.ParseUmbrella.hpp` in `share/SharpCppRbfx/metadata`.
- The package file `SharpCppRbfxConfig.cmake`.

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

### DotNetHost modes

`Urho3D::DotNetHost` has the same public API in two modes. The cache variable `RBFX_HOST_MODE` selects the mode.

- `hostfxr` is the default on desktop. `DotNetHost` loads hostfxr and starts the .NET runtime. This mode needs nethost.
- `shim` is the default on Android, iOS and Emscripten. `DotNetHost` never starts a runtime. The .NET head starts the runtime.
  The managed side pushes a function table (`SharpCppRbfxManagedHost`, header `Urho3D/CSharp/DotNetHostShim.h`) into the native
  host library through `sharpcpp_rbfx_attach_managed_host`. The shim code is in `SharpCpp.Rbfx.Host` on every platform
  (source `Rbfx.Host/src/DotNetHostShim.cpp`), not in `SharpCpp.Rbfx.Native`. The NuGet runtime packages do not pack the host library.
  `SharpCpp::RbfxHost` is a real library of the type `RBFX_NATIVE_LIBRARY_TYPE` (shared on Android, static on iOS and Emscripten).
  It has no link dependency and the interface definition `URHO3D_DOTNETHOST_SHIM`. A static library also has `URHO3D_DOTNETHOST_STATIC`.
  `DotNetHost::Initialize` refers to the attach export, so the linker keeps it in a static program. No whole-archive link is needed.
  `DotNetHost::Initialize` returns false and gives an error text while no table is attached.
  `RBFX_INSTALL_SDK` works in both modes. The shim SDK has `SharpCpp::RbfxHost` (the host library and the headers `DotNetHost.h`,
  `DotNetHostExport.h` and `DotNetHostShim.h`) and `SharpCpp::RbfxNative` (the wrapper). It has no `FindNetHost.cmake`.
  A native application links `SharpCpp::RbfxHost`. A .NET head loads `SharpCpp.Rbfx.Host` next to `SharpCpp.Rbfx.Native`.
  `SharpCppRbfxConfig.cmake` sets `SharpCppRbfx_HOST_MODE` and `SharpCppRbfx_LIBRARY_TYPE`. It stops when the target `Urho3D` has another library type than the SDK.
  `DotNetHost::GetFunctionPointer` accepts only an empty `delegateTypeName` (an `[UnmanagedCallersOnly]` method).
  The bindings attach the table automatically when the `SharpCpp.Rbfx.Bindings` assembly is first used, so the head code does not call it.

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
