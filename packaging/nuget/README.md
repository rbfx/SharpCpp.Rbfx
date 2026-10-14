# SharpCpp rbfx NuGet Packaging

This folder builds SDK-style NuGet packages with `dotnet pack`. It intentionally
does not use new `.nuspec` files.

## Package Set

- `SharpCpp.Rbfx.Bindings` contains the union reference assembly, `SharpCpp.Support`,
  the shared `SharpCpp.Std` and `SharpCpp.Ea` wrapper assemblies, and the rbfx
  source generator analyzer. CI makes this package depend on all supported runtime
  packages. Applications reference this one package; NuGet selects runtime assets
  for their RID. Applications must select one of the supported RIDs.
- `SharpCpp.Rbfx.Bindings.runtime.<rid>` contains the managed implementation and native runtime files for one
  runtime identifier, including `SharpCpp.Rbfx.Native` and the copied rbfx native
  dependencies from the build output directory of the RID. On desktop RIDs it also contains
  the host library (`SharpCpp.Rbfx.Host`) and the shared nethost library that it needs (`libnethost.so`, `libnethost.dylib` or `nethost.dll`).
  The nethost linkage follows `BUILD_SHARED_LIBS`. UWP does not build the host library. In CI union packages,
  Linux x64 and Android x64 share one `runtime.unix-x64` package because NuGet's
  Android x64 RID fallback includes Linux x64. Their managed assets stay in
  RID-specific folders in that package. Linux native files use a private package
  folder with `.bin` extensions and a Linux-only copy target that restores their
  `.so` names in application output, so Android tooling does not scan glibc
  libraries while packaging an Android app. Union runtime packages do not depend
  on the main package, avoiding a dependency cycle. The single-platform
  `package.sh create` mode retains its existing main-package dependency.
- `SharpCpp.Rbfx.CoreData`, `SharpCpp.Rbfx.Data`, and
  `SharpCpp.Rbfx.EditorData` contain resource folders copied from the downloaded
  rbfx SDK archive when present.

## Create Packages

```bash
./packaging/nuget/package.sh create --version 0.1.0-alpha --rid linux-x64
```

The script fetches the matching prebuilt rbfx SDK with `eng/fetch-rbfx-sdk.sh`
into `build/sdk/<rid>/` (see `eng/fetch-rbfx-sdk.sh --help`), builds the binding
project (which drives the auxiliary native CMake target through MSBuild), stages
files, and runs `dotnet pack`.
Package target-framework paths are derived from the repository `TargetFramework`
property. Use `--target-framework TFM` only when explicitly overriding it.

Use `--sdk-root PATH` to use an SDK that exists already. Use `--skip-fetch` when
`build/sdk/<rid>/` already exists. Use `--rbfx-tag TAG` to select an rbfx release.
Use `--skip-build` to package an already-built tree. The tree is `build/package/nuget/<rid>/build/bin/<Configuration>`. `--build-root` selects another tree.
Package staging, package output, and the local repository remain below
`build/package/nuget/`. Downloaded SDKs are in `build/sdk/`.

## Union packages

CI first builds the native glue and generated C# for each platform. One Linux job
then reads the public APIs of those compiled assemblies with `SharpCpp.ApiUnion`.
It emits a new reference assembly and C# stubs, and recompiles each platform's
generated and hand-written sources with those stubs. It does not rewrite input DLLs.
Missing executable APIs throw `PlatformNotSupportedException`, including missing
property accessors and interface implementations. Supported APIs keep their
original bodies. The native libraries are reused without rebuilding.

Parameter-type differences become overloads. C# cannot overload a method or property
by return type alone, so those conflicts use explicit names such as
`GetCurrentThreadIDAsUInt32`, `GetCurrentThreadIDAsUIntPtr`, `TimestampAsInt64`, and
`TimestampAsIntPtr`. `Aliases.tsv` records these names. The original platform-specific
names remain in implementations but do not appear in the union compilation contract.
The tool does not guess whether two different ABI types are interchangeable.

Enum members are combined when their underlying types and values agree.
The union rejects conflicting enum widths, base types, constant values and missing
data fields on existing types. These need an explicit generator/configuration fix rather
than a stub. OS availability attributes describe APIs absent on an entire OS;
architecture-specific absence is enforced by the runtime stub. ApiCompat checks
each rebuilt implementation against the union reference before packages are created.

To reproduce CI with downloaded artifacts from one workflow run:

```bash
gh run download RUN_ID --repo rbfx/SharpCpp.Rbfx --dir build/union-input
dotnet run --file packaging/nuget/UnionPackage.cs -- create --input build/union-input --version VERSION
```

CI runs the same work in three phases on one output directory. `create` runs all phases in one process.

```bash
dotnet run --file packaging/nuget/UnionPackage.cs -- reference --input build/union-input
dotnet run --file packaging/nuget/UnionPackage.cs -- implement --input build/union-input --version VERSION --rid RID
dotnet run --file packaging/nuget/UnionPackage.cs -- pack --input build/union-input --version VERSION
```

`reference` writes the union sources, the reference assembly and `state.json`. Run `implement`
once for each RID in `state.json`. It builds the implementation, runs ApiCompat and stages the
runtime files. `pack` validates all implementations, makes the remaining packages and runs the
consumer check. CI moves the output directory between jobs as artifacts and keeps the same paths.

Use an empty output directory. The default is `build/package/union`; `--output`
selects another directory. Sources, prepared compilations and packages stay there.
Inputs must include managed assemblies, generated sources, NuGet native packages,
and the managed package inputs. The older Linux managed package is also accepted
as a source of portable dependencies. Each runtime package also carries the engine binding metadata of its RID
(`runtimes/<rid>/metadata/SharpCpp.Rbfx.Bindings.json`, with the umbrella header `SharpCpp.Rbfx.ParseUmbrella.hpp`). The user-code generator of a consumer imports it,
so a consumer does not run the engine generator. Runtime packages are transitive dependencies,
so restore downloads every supported runtime package even when publishing for one RID.
The C# utility also builds consumers for every RID, verifies the selected DLLs,
and runs the Linux consumer against the native SDK and an unsupported API stub.
CI repeats the consumer check on each native runner before releasing the packages.
The Linux smoke process reserves static TLS space for the prebuilt rbfx SDK with
`GLIBC_TUNABLES=glibc.rtld.optional_static_tls=32768`.
Linux also needs the SDK's system dependencies: OpenGL, GLX, X11, D-Bus and uuid.
On Ubuntu, CI installs `libopengl0 libglx0 libx11-6 libdbus-1-3 libuuid1` for the runtime checks.

## Publish to NuGet.org

After the union checks and all native runtime checks pass, `build.yml` publishes
the final `union-nuget` packages to NuGet.org only for builds of a `v*` tag.
Tag `v1.2.3` builds version `1.2.3`. Other builds use the next patch version of the
last `v*` tag with a suffix, for example `1.2.4-dev.gabc1234`. Master and pull request builds do not publish.
Existing versions are skipped when a workflow is rerun.

Every master or tag build uploads the packages to the rolling `latest` release.
A tag build also uploads them to the release named after the tag.

The job uses [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
with `NuGet/login@v1` and job-scoped `id-token: write` permission. The policy belongs
to NuGet user `rokups` for repository `rbfx/SharpCpp.Rbfx` and workflow `build.yml`.
No long-lived API key or username variable is required.
The policy's environment is empty, so `Publish-NuGet` has no `environment` setting.

## Runtime IDs

Desktop packages use `package.sh create`. Cross-platform packages use the C#
utility and must be built with the matching native toolchain. CI builds and tests
these RIDs:

- Android: `android-arm`, `android-arm64`, `android-x64` (Android NDK 27.2.12479018)
- iOS: `ios-arm64` (macOS with Xcode)
- Browser WebAssembly: `browser-wasm` (Emscripten 3.1.56, matching .NET 10)
- UWP: `uap-x64` (WindowsStore CMake target, Windows SDK 10.0.26100.0)

For local Android and Wasm package builds, pass `--ndk-root` or `--emsdk-root`:

```bash
dotnet run --file packaging/nuget/CrossPackage.cs -- create --rid android-arm64 --version 0.1.0-local.1 --ndk-root build/toolchains/android-sdk/ndk/27.2.12479018
dotnet run --file packaging/nuget/CrossPackage.cs -- create --rid browser-wasm --version 0.1.0-local.1 --emsdk-root build/toolchains/emsdk
dotnet run --file packaging/nuget/CrossPackage.cs -- verify --rid browser-wasm --version 0.1.0-local.1
```

Android and iOS use the matching published SDK archives fetched by `eng/fetch-rbfx-sdk.sh`.
The Wasm package uses Emscripten 3.1.56, matching .NET 10. It first looks for an
exact SDK asset in this repository's rolling `latest` release, named
`rbfx-web-wasm-sdk-emscripten-3.1.56-rbfx-<12-character-commit>.7z`. The filename
is the cache key: it records the Emscripten version and rbfx commit, so the archive
needs no embedded manifest or additional SDK validation. If the exact asset is
absent, CI builds rbfx from its `latest` tag with the matching Emscripten SDK and
uses the result for packaging. `eng/fetch-rbfx-sdk.sh` downloads the asset.

To build and upload the cache on demand, run the **Update Wasm SDK Cache**
workflow, or build the archive locally:

```bash
dotnet run --file packaging/nuget/CrossPackage.cs -- build-wasm-sdk --emsdk-root build/toolchains/emsdk
```

The update workflow removes older assets with the `rbfx-web-wasm-sdk-` prefix
before uploading, so the rolling release retains one Wasm SDK archive. Wasm
consumers require the `wasm-tools` workload and must select .NET's multithreaded
runtime and WebAssembly exception handling. iOS parsing can use the platform compiler's
builtin Clang headers with `-DRBFX_SDK_PARSE_USE_PLATFORM_COMPILER_INCLUDES=ON`;
the cross-platform package workflow enables this for iOS.
Set these properties in the app project (or `Directory.Build.props`) so they
are present during restore and runtime-pack selection:

```xml
<PropertyGroup>
  <WasmEnableThreads>true</WasmEnableThreads>
  <WasmEnableExceptionHandling>true</WasmEnableExceptionHandling>
</PropertyGroup>
```
Android consumer verification accepts `--AndroidSdkDirectory` and
`--JavaSdkDirectory`.

iOS packages must be built on macOS. iOS and Wasm native glue use static libraries;
Android uses shared `.so` files and includes `libc++_shared.so` from the selected NDK. The Android toolchains use `c++_shared` unless the command line sets `-DBUILD_SHARED_LIBS=OFF`. `-DRBFX_ANDROID_STL=c++_shared` or `c++_static` overrides both.
Its build targets defer rbfx native libraries until the binding calls them, because
`libUrho3D.so`'s JNI startup expects SDL's Java activity. The Android x64 target also
sets Tracy's `TRACY_NO_INVARIANT_CHECK` environment variable so Android emulators
without invariant TSC can load the upstream SDK library.
The Android, iOS and Wasm runtime packages share one RID to item mapping with the source build:
`eng/SharpCpp.Rbfx.NativeItems.targets`. `CrossPackage.cs targets` lists the staged files as
`RbfxNativeRuntimeFile` items with the metadata `RbfxNativeRid` and `RbfxNativeKind` in
`buildTransitive/SharpCpp.Rbfx.Bindings.runtime.<rid>.targets`. It also copies the shared file to
`buildTransitive/SharpCpp.Rbfx.NativeItems.<rid>.targets` and imports that copy. The mapping uses only
the items of the RID of the consumer (Android needs no RID) and is idempotent, so a union consumer maps each file once.
Android ships every staged `*.so`, so `libc++_shared.so` reaches the application with no extra rule.
The iOS package strips debug information from staged copies of its archives to
keep the package smaller; source SDK files are left untouched. Platform-specific
consumer projects verify the NuGet linking metadata in CI. Android and Wasm
consumers can also be generated locally with the utility's `verify` command.

Known SDK archive mappings (kept in `eng/fetch-rbfx-sdk.sh`):

- `linux-x64`: `rebelfork-sdk-linux-gcc-x64-dll-latest.7z`
- `osx-x64`: `rebelfork-sdk-macos-clang-x64-dll-latest.7z`
- `osx-arm64`: `rebelfork-sdk-macos-clang-arm64-dll-latest.7z`
- `win-x64`: `rebelfork-sdk-windows-msvc-x64-dll-latest.7z`
- `uap-x64`: `rebelfork-sdk-uwp-msvc-x64-dll-latest.7z`
- `android-arm`: `rebelfork-sdk-android-clang-arm-dll-latest.7z`
- `android-arm64`: `rebelfork-sdk-android-clang-arm64-dll-latest.7z`
- `android-x64`: `rebelfork-sdk-android-clang-x64-dll-latest.7z`
- `ios-arm64`: `rebelfork-sdk-ios-clang-arm64-lib-latest.7z`
- `browser-wasm`: `rbfx-web-wasm-sdk-emscripten-<version>-rbfx-<short-commit>.7z`

## Local Repository

```bash
./packaging/nuget/package.sh local-repo
```

This imports the packages of one RID (option `--rid`, default is the host RID) and the runtime packages of every RID from `build/package/nuget/<rid>/out/` into
`build/package/nuget/local-repository/` for local restore tests.
