# SharpCpp.Rbfx agent guide

This repository generates C# bindings for the rbfx game engine with SharpCpp. It is an
external SharpCpp consumer: it uses only SharpCpp's public projects, native headers
(`include/SharpCpp`) and CMake modules (`cmake/`).

## Layout

- `SharpCpp.Rbfx.Generator/` - phase-1 generator for the installed rbfx SDK headers.
- `Rbfx.UserCode.Generator/` - the phase-2 user-code generator.
- `Rbfx.Host/` - native library that hosts the .NET runtime (`SharpCpp::RbfxHost`). Public headers are `Urho3D/CSharp/DotNetHost.h` and `DotNetHostExport.h` (`namespace Urho3D`). `DotNetHost::LoadPlugin(PluginManager*, path, error)` loads `SharpCpp.Rbfx.Bindings.dll` into the default load context and calls `NativeHostEntryPoints.LoadPlugin`. Call it after `Engine::Initialize`. `cmake/FindNetHost.cmake` finds nethost. It detects a musl RID on Linux, and `NETHOST_RID` overrides the RID. The nethost linkage follows `BUILD_SHARED_LIBS` (`NETHOST_PREFER_SHARED`). A shared link uses `libnethost.so`, `libnethost.dylib` or `nethost.dll`, and the host build copies it next to the host library. A static link uses `libnethost.a`. MSVC always links the import library `nethost.lib` (the static `libnethost.lib` uses /MT and does not link with /MD). The top-level `CMakeLists.txt` sets `BUILD_SHARED_LIBS` from `RBFX_NATIVE_LIBRARY_TYPE`. The Android toolchains use `c++_shared` unless the command line sets `-DBUILD_SHARED_LIBS=OFF`. `-DRBFX_ANDROID_STL=c++_shared` or `c++_static` overrides both.
- CMake SDK: `RBFX_INSTALL_SDK` (default ON when `RBFX_BUILD_HOST` is ON and the project is top-level) adds install rules. `Rbfx.Host/CMakeLists.txt` installs the host target (export `SharpCppRbfxTargets`, imported name `SharpCpp::RbfxHost` through `EXPORT_NAME`), the two public headers and the shared nethost library. The root `CMakeLists.txt` writes `SharpCppRbfxConfig.cmake` (from `cmake/SharpCppRbfxConfig.cmake.in`; no version file) and installs `FindNetHost.cmake` and `RbfxCSharpDotNet.cmake` to `share/SharpCppRbfx/cmake`. The Config finds `NetHost` only for a static host. `SharpCpp.Rbfx.Bindings/CMakeLists.txt` adds the wrapper `SharpCppRbfx_Native` to the same export set (`SharpCpp::RbfxNative`) in a source-mode build with a shared library type. It installs the engine runtime files next to the wrapper through `CopyRbfxRuntimeDependencies.cmake.in` (`destination_dir`) and the metadata to `share/SharpCppRbfx/metadata`. The root `CMakeLists.txt` also installs the managed assemblies (`SharpCpp.Rbfx.Bindings`, `SharpCpp.Support`, `SharpCpp.Std`, `SharpCpp.Ea`, `SharpCpp.Rbfx.SourceGenerator`) from `SHARPCPP_MANAGED_OUTPUT_ROOT/<Config>` to `share/SharpCppRbfx/managed`. The Config sets `SharpCppRbfx_MANAGED_DIR` only when this payload is installed. The shim package `cmake/SharpCppRbfx/share/SharpCppRbfx/SharpCppRbfxConfig.cmake` builds this source tree for a consumer with `find_package(SharpCppRbfx)` and sets the same variables. The default output roots are `<repo>/build/bin` for a top-level build and `<build>/SharpCppRbfx/bin` and `<build>/SharpCppRbfx/managed` otherwise. `cmake/RbfxInstallUserCodeGenerator.cmake` (an install script) copies the user-code generator and its dependencies, selected from `Rbfx.UserCode.Generator.deps.json`, to `share/SharpCppRbfx/tools/usercode`. The install step needs no dotnet and does not build: run `dotnet build` first. It installs the `include/SharpCpp` headers of SharpCpp and of this repository (merged), `SharpCppGeneratedStaleness.cmake`, `SharpCppOutput.cmake`, `ExportRbfxSdkParseFlags.cmake`, `RbfxCSharpAddUserCode.cmake` with `cmake/templates`, and a copy of the builtin Clang headers (`share/SharpCppRbfx/clang-resource/include`, the link target of `SHARPCPP_CLANG_RESOURCE_DIRECTORY/include`). In a source-mode build with a shared library type (`SHARPCPP_RBFX_SDK_HAS_USERCODE`), the Config sets the inputs of `rbfx_csharp_add_usercode()` to the installed files, writes the SDK flags file from the `Urho3D` target of the consumer, creates `SharpCpp::NativeSupport` and includes the module. The module takes `SHARPCPP_RBFX_SHARPCPP_INCLUDE_DIRECTORY`, `SHARPCPP_RBFX_RBFX_INCLUDE_DIRECTORY` and `SHARPCPP_RBFX_STALENESS_SCRIPT`. The root `CMakeLists.txt` sets them to the source-tree files. The module and `rbfx_csharp_add_plugin()` depend on `sharpcpp-generate-rbfx` and `sharpcpp-rbfx-runtime` only when these targets exist. The SDK has no MSBuild files for the written managed project yet.
- `Rbfx.Examples/Example.UserCode/` - example: native C++ component (`RotatingComponent`), static `TextTools::RepeatText`, and CMake. The files are directly in the directory. No managed project.
- `Rbfx.Examples/Example.Common/` - shared demo with the native user code `Example.UserCode`. One project with two modes. Source mode (default) references the bindings source projects. Package mode (`-p:RbfxBindingsPackageVersion=VERSION`) references the `SharpCpp.Rbfx.Bindings` package. `Example.Desktop`, `Example.Android`, `Example.Ios`, `Example.Wasm` and `Example.Uwp` use it.
- `Rbfx.Examples/Example.NativeHost/` - example: C++ rbfx `Application` that hosts .NET. `DotNetHost::LoadPlugin` loads a C# `ManagedPlugin`, and C++ creates its `LogicComponent` by name with `CreateComponent("Spinner")`. The managed entry point of `LoadPlugin` is `Urho3D.Plugins.NativeHostEntryPoints` in `SharpCpp.Rbfx.Bindings`.
- `SharpCpp.Rbfx.Bindings/` - generated rbfx wrapper assembly and hand-written runtime glue.
- `SharpCpp.Ea/`, `SharpCpp.Rbfx.SourceGenerator/` - EASTL containers and Roslyn helpers.
- `SharpCpp.Rbfx.Tests.E2E/` - round-trip tests against the generated rbfx wrappers.
- `include/SharpCpp/detail/Rbfx*.hpp` - rbfx marshaller configuration headers.
- `native/SharpCpp.Rbfx.Native/` - MSBuild project that drives the CMake native build.
- `packaging/nuget/` - NuGet packaging for the generated bindings.

## Inputs

- `SharpCppRoot` (MSBuild) / `SHARPCPP_ROOT` (CMake): SharpCpp checkout; defaults to the
  sibling `../SharpCpp`.
- `RbfxSdkRoot` (MSBuild) / `CMAKE_PREFIX_PATH` (CMake or environment): installed
  shared-library rbfx SDK. CMake finds the engine only with `find_package(Urho3D)` through
  `CMAKE_PREFIX_PATH`, and derives the internal SDK root `RBFX_ENGINE_ROOT` from `Urho3D_DIR`.
  MSBuild takes `RbfxSdkRoot` (default: the environment variable `CMAKE_PREFIX_PATH`, then
  `build/sdk/<rid>`, then `../../rbfx-sdk`) and passes it to CMake as `CMAKE_PREFIX_PATH`.
  An engine target from source mode is not supported yet.

## Wrap user C++ code

`rbfx_csharp_add_usercode(<target> HEADERS ... [INCLUDE_DIRECTORIES LINK_LIBRARIES NAMESPACE
ASSEMBLY_NAME LIBRARY_NAME SYMBOL_PREFIX GENERATED_DIRECTORY LIBRARY_TYPE CSPROJ])` is in
`cmake/RbfxCSharpAddUserCode.cmake`.

- The function creates only the bindings target: the generator target `sharpcpp-generate-<target>`, the native library `<target>` with the generated wrappers, and the managed project files. It has no `SOURCES`. The implementation of the headers is a separate target. The caller passes it in `LINK_LIBRARIES` (linked PUBLIC). `Example.UserCode.Impl` is the example. It is a static library (an object library when the native libraries are static).
- At configure time it writes `Directory.Build.props`, `Directory.Build.targets`, `<target>.props`
  and `<ASSEMBLY_NAME>.csproj` into the build tree, below `usercode/<target>/`.
- The user needs no generator knowledge and no bindings csproj.
- One driver builds the native tree. MSBuild is the driver when it builds `SharpCpp.Rbfx.slnx`: target `RbfxBuildNative` of `native/SharpCpp.Rbfx.Native` runs `BuildNative.cmake` under a lock and configures with `-DRBFX_CSHARP_BUILD_MANAGED=OFF`. CMake is the driver when it builds the tree. Then CMake runs `dotnet build` only through `rbfx_csharp_add_dotnet_project()` and `rbfx_csharp_add_plugin()` (`cmake/RbfxCSharpDotNet.cmake`). These pass `-p:RbfxNativeBuildDriver=cmake -p:RbfxNativeBuildDir=<CMake binary dir>`, so `RbfxBuildNative` does not run in the nested build. A nested MSBuild that runs `RbfxBuildNative` would wait on the lock or start a second build of the same tree. The option `RBFX_CSHARP_BUILD_MANAGED` (default ON) turns the managed targets off. Do not set the driver rule aside.
- A consumer tree (add_subdirectory or the find_package shim) builds the generators itself: the option `RBFX_CSHARP_BUILD_GENERATORS` (default ON when the project is not top-level, OFF otherwise; it needs `RBFX_CSHARP_BUILD_MANAGED`) adds the targets `sharpcpp-rbfx-build-generator` and `sharpcpp-rbfx-build-usercode-generator`. They run `dotnet build` into `SHARPCPP_MANAGED_OUTPUT_ROOT/<Config>` with the intermediate files in `<build>/SharpCppRbfx/dotnet`. `sharpcpp-generate-rbfx` and the user-code generate targets depend on them. The target `sharpcpp-rbfx-build-managed` builds `SharpCpp.Rbfx.Bindings` (and its references Std, Ea, Support) after the native wrapper, so `SharpCppRbfx_MANAGED_DIR` holds them. In a consumer tree the root sets `RBFX_CSHARP_MSBUILD_PROPERTIES` (`SharpCppManagedOutputRoot`, `SharpCppNativeOutputRoot`, `SharpCppBuildRoot`). `rbfx_csharp_add_dotnet_project()` passes them as `-p:` and the written user-code `Directory.Build.props` sets them, so every dotnet build uses the same roots. The shim copies the variables that `rbfx_csharp_add_usercode()` reads (the global property `SHARPCPP_RBFX_USERCODE_INPUT_VARIABLES` in `cmake/RbfxCSharpAddUserCode.cmake`, which the function checks too) to the consumer scope. MSBuild sets `RBFX_CSHARP_BUILD_MANAGED=OFF`, so the MSBuild path is unchanged. A CMake-driven build needs the generators in `build/bin/<Config>` (`SharpCpp.Rbfx.Generator.dll`, `Rbfx.UserCode.Generator.dll`) first. Run `dotnet build SharpCpp.Rbfx.slnx` once. Do not use the tree `build/native/linux-gcc-x64-dll` for both drivers: `BuildNative.cmake` configures it again with `RBFX_CSHARP_BUILD_MANAGED=OFF`. Use another binary directory for a CMake-driven build, or configure with `-DRBFX_CSHARP_BUILD_MANAGED=ON` again.
- CMake presets follow the rbfx scheme `<platform>-<compiler>-<arch>-<dll|lib>`. Build presets add `-debug` or `-release`. Host presets (`linux-gcc-x64-dll`, `macos-clang-arm64-dll`, `windows-msvc-x64-dll`) have a host condition and the binary directory `build/native/<preset>`. Cross presets use `build/cross/<preset>`. `eng/SharpCpp.Rbfx.Build.props` holds the one map from RID to preset (property `RbfxNativePreset`): `linux-x64` to `linux-gcc-x64-dll`, `osx-arm64` to `macos-clang-arm64-dll`, `win-x64` to `windows-msvc-x64-dll`, `android-arm`, `android-arm64` and `android-x64` to `android-clang-<arch>-dll`, `ios-arm64` to `ios-clang-arm64-lib`, `browser-wasm` to `web-emscripten-wasm-lib`, `uap-x64` to `uwp-msvc-x64-dll`. `-p:RbfxNativePreset=<name>` overrides the map. The toolchain files in `cmake/toolchains` keep their RID names.
- `CSPROJ <path>`: the user supplies the project and the function writes none. `<target>.props` stays available to import.
- Opt-in hooks: a user csproj can import `<target>.props`. Cache variables are
  `RBFX_CSHARP_BINDINGS_PROJECTS`, `RBFX_CSHARP_BINDINGS_PACKAGE_VERSION`,
  `RBFX_CSHARP_MSBUILD_PROPS` and `RBFX_CSHARP_MSBUILD_TARGETS`. Target properties are
  `SHARPCPP_RBFX_MANAGED_PROJECT`, `SHARPCPP_RBFX_MANAGED_PROPS` and `SHARPCPP_RBFX_MANAGED_ASSEMBLY`.
- A duplicate `ASSEMBLY_NAME` is a configure error.

A consumer csproj declares `<RbfxUserCodeReference Include="<cmake target>" />`.
`eng/SharpCpp.Rbfx.Build.targets` then adds a reference to the native project. Target
`RbfxResolveUserCodeReferences` restores and builds the generated project and references its dll.
So the wrapped user code gives a dll that other modules use.

`eng/SharpCpp.Rbfx.NativeItems.targets` is the one mapping from a RID to the platform items for native libraries (Android `AndroidNativeLibraryNoJniPreload` for every `*.so`, iOS `NativeReference`, Wasm `NativeFileReference` and `--js-library` flags). Its input is the `RbfxNativeRuntimeFile` items with the metadata `RbfxNativeRid` and `RbfxNativeKind`. It maps only the items of the RID of the consumer (`RbfxTargetRid`, else `RuntimeIdentifier`) and is idempotent. `eng/SharpCpp.Rbfx.Build.targets` imports it. `packaging/nuget/CrossPackage.cs targets` lists the staged files as `RbfxNativeRuntimeFile` and copies the shared file to `buildTransitive/SharpCpp.Rbfx.NativeItems.<rid>.targets` of the runtime package.

## Run the examples

- `Example.Desktop`, `Example.Common` and the mobile and UWP apps need the native build of `Example.UserCode` (about 7 minutes the first time). Source mode needs `CMAKE_PREFIX_PATH` (the installed rbfx SDK). Package mode (`-p:RbfxBindingsPackageVersion=VERSION -p:RbfxPackageVersion=VERSION`) restores the packages of that version, and the engine natives come from the runtime package.
- `Rbfx.Examples.slnx` also needs the SDK now.
- A Linux desktop run needs `GLIBC_TUNABLES=glibc.rtld.optional_static_tls=32768`.

Do not use absolute paths. Missing generator capabilities are fixed in SharpCpp, not
worked around here.

## Build and verify

```sh
dotnet build SharpCpp.Rbfx.slnx --configuration Debug -maxcpucount
dotnet test SharpCpp.Rbfx.Tests.E2E --configuration Debug --no-build --no-restore
```

Fetch a prebuilt rbfx SDK with `eng/fetch-rbfx-sdk.sh --rid <rid>` (default: the host RID).
It needs `curl`, `jq` and `7z`. It extracts the SDK into `build/sdk/<rid>` and does nothing if the
SDK is current. Set `CMAKE_PREFIX_PATH="$(eng/fetch-rbfx-sdk.sh --rid linux-x64 --print-root)"` before
CMake or MSBuild. `--print-cache-key` prints the CI cache key. `browser-wasm` comes from the
SharpCpp.Rbfx release `latest`. See `--help` for all options.

Set the MSBuild property `RbfxBindingsPackageVersion` to build the user code against the built
packages (from package.sh or CI, in a local feed). It adds `-DRBFX_CSHARP_BINDINGS_PACKAGE_VERSION=<version>` and `-DRBFX_BUILD_HOST=OFF` to
`RbfxNativeConfigureArguments`. The generated bindings project then references the package
`SharpCpp.Rbfx.Bindings`. The native build neither runs the engine generator nor compiles `SharpCppRbfx_Native`. The user-code generator reads the engine
binding metadata from the runtime package (`runtimes/<rid>/metadata/SharpCpp.Rbfx.Bindings.json`, with the umbrella header `SharpCpp.Rbfx.ParseUmbrella.hpp` that the user wrapper includes). The consumer project finds the file in the restored package
folder (`eng/SharpCpp.Rbfx.Build.targets`, target `RbfxSetProjectReferenceProperties`) and passes it through the native project to `-DRBFX_ENGINE_METADATA_FILE`.
Restore the consumer first. Unset means source build.

Coherent local commits after validation are authorized; publishing requires explicit
task authorization.
