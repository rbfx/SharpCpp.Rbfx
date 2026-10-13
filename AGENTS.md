# SharpCpp.Rbfx agent guide

This repository generates C# bindings for the rbfx game engine with SharpCpp. It is an
external SharpCpp consumer: it uses only SharpCpp's public projects, native headers
(`include/SharpCpp`) and CMake modules (`cmake/`).

## Layout

- `SharpCpp.Rbfx.Generator/` - phase-1 generator for the installed rbfx SDK headers.
- `Rbfx.UserCode.Generator/` - the phase-2 user-code generator.
- `Rbfx.Host/` - native library that hosts the .NET runtime (`SharpCpp::RbfxHost`). Public headers are `Urho3D/CSharp/DotNetHost.h` and `DotNetHostExport.h` (`namespace Urho3D`). `DotNetHost::LoadPlugin(PluginManager*, path, error)` loads `SharpCpp.Rbfx.Bindings.dll` into the default load context and calls `NativeHostEntryPoints.LoadPlugin`. Call it after `Engine::Initialize`. `cmake/FindNetHost.cmake` finds nethost. It detects a musl RID on Linux, and `NETHOST_RID` overrides the RID. The nethost linkage follows `BUILD_SHARED_LIBS` (`NETHOST_PREFER_SHARED`). A shared link uses `libnethost.so`, `libnethost.dylib` or `nethost.dll`, and the host build copies it next to the host library. A static link uses `libnethost.a`. MSVC always links the import library `nethost.lib` (the static `libnethost.lib` uses /MT and does not link with /MD). The top-level `CMakeLists.txt` sets `BUILD_SHARED_LIBS` from `RBFX_NATIVE_LIBRARY_TYPE`. The Android toolchains use `c++_shared` unless the command line sets `-DBUILD_SHARED_LIBS=OFF`. `-DRBFX_ANDROID_STL=c++_shared` or `c++_static` overrides both.
- `Rbfx.Examples/Example.UserCode/` - example: native C++ component (`RotatingComponent`) and CMake. No managed project.
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
- `RbfxSdkRoot` (MSBuild) / `RBFX_SDK_ROOT` (CMake or environment): installed
  shared-library rbfx SDK; defaults to `../../rbfx-sdk` when present.

## Wrap user C++ code

`rbfx_csharp_add_usercode(<target> HEADERS ... [SOURCES INCLUDE_DIRECTORIES LINK_LIBRARIES NAMESPACE
ASSEMBLY_NAME LIBRARY_NAME SYMBOL_PREFIX GENERATED_DIRECTORY LIBRARY_TYPE CSPROJ])` is in
`cmake/RbfxCSharpAddUsercode.cmake`.

- The function builds the native library and runs the phase-2 generator.
- At configure time it writes `Directory.Build.props`, `Directory.Build.targets`, `<target>.props`
  and `<ASSEMBLY_NAME>.csproj` into the build tree, below `usercode/<target>/`.
- The user needs no generator knowledge and no bindings csproj.
- CMake never runs `dotnet build`. It would deadlock with the native build lock.
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

`eng/SharpCpp.Rbfx.NativeItems.targets` is the one mapping from a RID to the platform items for native libraries (Android `AndroidNativeLibraryNoJniPreload` for every `*.so`, iOS `NativeReference`, Wasm `NativeFileReference` and `--js-library` flags). Its input is the `RbfxNativeRuntimeFile` items with the metadata `RbfxNativeRid` and `RbfxNativeKind`. It maps only the items of the RID of the consumer (`RbfxTargetRid`, else `RuntimeIdentifier`) and is idempotent. `eng/SharpCpp.Rbfx.Consumer.targets` imports it. `packaging/nuget/CrossPackage.cs targets` lists the staged files as `RbfxNativeRuntimeFile` and copies the shared file to `buildTransitive/SharpCpp.Rbfx.NativeItems.<rid>.targets` of the runtime package.

## Run the examples

- `Example.Desktop`, `Example.Common` and the mobile and UWP apps need the native build of `Example.UserCode` (about 7 minutes the first time). Source mode needs `RBFX_SDK_ROOT`. Package mode (`-p:RbfxBindingsPackageVersion=VERSION -p:RbfxPackageVersion=VERSION`) restores the packages of that version, and the engine natives come from the runtime package.
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
SDK is current. Set `RBFX_SDK_ROOT="$(eng/fetch-rbfx-sdk.sh --rid linux-x64 --print-root)"` before
CMake or MSBuild. `--print-cache-key` prints the CI cache key. `browser-wasm` comes from the
SharpCpp.Rbfx release `latest`. See `--help` for all options.

Set the MSBuild property `RbfxBindingsPackageVersion` to build the user code against the built
packages (from package.sh or CI, in a local feed). It adds `-DRBFX_CSHARP_BINDINGS_PACKAGE_VERSION=<version>` and `-DRBFX_BUILD_HOST=OFF` to
`RbfxNativeConfigureArguments`. The generated bindings project then references the package
`SharpCpp.Rbfx.Bindings`. Unset means source build.

Coherent local commits after validation are authorized; publishing requires explicit
task authorization.
