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
  dependencies from the shared `build/bin` output directory. The CI union packages
  contain no dependency on the main package, to avoid a dependency cycle.
  The single-platform `package.sh create` mode retains its existing main-package dependency.
- `SharpCpp.Rbfx.CoreData`, `SharpCpp.Rbfx.Data`, and
  `SharpCpp.Rbfx.EditorData` contain resource folders copied from the downloaded
  rbfx SDK archive when present.

## Create Packages

```bash
./packaging/nuget/package.sh create --version 0.1.0-alpha --rid linux-x64
```

The script downloads the matching prebuilt rbfx DLL SDK archive from
`https://github.com/rbfx/rbfx/releases/download/latest`, builds the binding
project (which drives the auxiliary native CMake target through MSBuild), stages
files, and runs `dotnet pack`.
Package target-framework paths are derived from the repository `TargetFramework`
property. Use `--target-framework TFM` only when explicitly overriding it.

Use `--skip-fetch` when `build/package/nuget/in/<sdk-name>/` already exists. Use
`--skip-build` to package an already-built `build/bin/<Configuration>` tree.
Package staging, package output, downloaded SDKs, and the local repository all
remain below `build/package/nuget/`.

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

Use an empty output directory. The default is `build/package/union`; `--output`
selects another directory. Sources, prepared compilations and packages stay there.
Inputs must include managed assemblies, generated sources, NuGet native packages,
and the managed package inputs. The older Linux managed package is also accepted
as a source of portable dependencies. Runtime packages are transitive dependencies,
so restore downloads every supported runtime package even when publishing for one RID.
The C# utility also builds consumers for every RID, verifies the selected DLLs,
and runs the Linux consumer against the native SDK and an unsupported API stub.
The Linux smoke process reserves static TLS space for the prebuilt rbfx SDK with
`GLIBC_TUNABLES=glibc.rtld.optional_static_tls=32768`.

## Runtime IDs

This pipeline packages one RID per invocation. Downloading SDK archives for other
platforms is not enough to produce their runtime packages from a Linux host: the
generated C++ glue library (`SharpCpp.Rbfx.Native`) must also be compiled and
linked for each target RID with a compatible compiler/toolchain and SDK binary
set. The generated managed assembly is portable, but the native glue is not.

To produce all runtime packages, run this same package step on CI runners for
each RID, or provide cross-compilation toolchain files that can build and link
`SharpCpp.Rbfx.Native` for those SDKs.

Known SDK archive mappings:

- `linux-x64`: `rebelfork-sdk-linux-gcc-x64-dll-latest.7z`
- `osx-x64`: `rebelfork-sdk-macos-clang-x64-dll-latest.7z`
- `osx-arm64`: `rebelfork-sdk-macos-clang-arm64-dll-latest.7z`
- `win-x64`: `rebelfork-sdk-windows-msvc-x64-dll-latest.7z`
- `uap-x64`: `rebelfork-sdk-uwp-msvc-x64-dll-latest.7z`

## Local Repository

```bash
./packaging/nuget/package.sh local-repo
```

This imports packages from `build/package/nuget/out/` into
`build/package/nuget/local-repository/` for local restore tests.
