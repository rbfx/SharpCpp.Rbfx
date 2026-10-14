# SharpCpp.Rbfx

## Responsibility

The rbfx integration layer bridges SharpCpp's generic C++/C# binding generation pipeline to the rbfx game engine. It owns the complete end-to-end build flow for generating, compiling, and deploying rbfx-specific managed bindings, native glue libraries, and user-code extensions. This directory is the composition boundary where SharpCpp's library components (Model, Metadata, Parsing, Pipeline, Rendering, Support) are instantiated into a working binding generator for a concrete C++ SDK.

## Design

### Two-Phase Generation Architecture

The integration implements a **phase-1 / phase-2** generation model:

- **Phase 1 (SharpCpp.Rbfx.Generator)**: Parses the installed rbfx SDK headers via ClangSharp, applies the bootstrap pipeline, and emits C# bindings, C++ native glue, and a `Bindings.json` metadata document. The metadata document captures the shape of all SDK types and serves as the import contract for phase 2.

- **Phase 2 (Rbfx.UserCode.Generator)**: Parses user-authored C++ headers (for example `Example.UserCode/RotatingComponent.hpp`), imports the phase-1 metadata, and generates bindings for user types that reference SDK types. This allows user types to extend SDK classes while enjoying full managed projection.

### Build-Time Code Generation

`SharpCpp.Rbfx.slnx` is the primary build graph. The `SharpCpp.Rbfx.Native`
orchestration project references both rbfx generators, then invokes the
`sharpcpp-rbfx-native` CMake target after those managed dependencies have built.
CMake runs the prebuilt generator DLLs and compiles generated C++ thunks; the
binding projects discover generated C# immediately before `CoreCompile`.

### Staleness Management

SharpCpp's `cmake/SharpCppGeneratedStaleness.cmake` helper compares each
generator DLL timestamp against its generation stamp. If the tool changed, the
corresponding generated directory is cleared before regeneration.

### Runtime Dependency Deployment

A CMake template (`CopyRbfxRuntimeDependencies.cmake.in`) uses `file(GET_RUNTIME_DEPENDENCIES)` to discover and copy rbfx SDK shared libraries (`libUrho3D.so`, transitive deps) into the binary output directory alongside the generated native library. This ensures that managed test runners and the application shell can locate native symbols at load time.

### CMake Target Topology

```
SharpCpp.Rbfx.Generator ─┐
Rbfx.UserCode.Generator ─┼─> SharpCpp.Native (MSBuild)
SharpCpp.Tool ───────────┘             |
                                      v
                              sharpcpp-native (CMake)
                               /       |        \
              SharpCppTestsE2E_Native  |  sharpcpp-generate-Example.UserCode
                                       v        |
                          sharpcpp-generate-rbfx v
                                       |  Example.UserCode
                                       v
                              SharpCppRbfx_Native
```

## Flow

### End-to-End Generation Pipeline

1. **SDK Discovery**: `RBFX_SDK_ROOT` points to an installed rbfx SDK. `find_package(Urho3D)` locates the SDK CMake config. Build flags, include directories, and compile definitions are recursively collected from the `Urho3D` CMake target into `RBFX_URHO3D_INCLUDE_DIRECTORIES`, `RBFX_URHO3D_COMPILE_DEFINITIONS`, `RBFX_URHO3D_COMPILE_OPTIONS`.

2. **Flags File Generation**: A per-config `urho3d-sdk-flags.txt` is written with `compiler_path`, `language_standard`, include directories, defines, and clang arguments. This file is consumed by both generators.

3. **Phase 1 Generation**: `SharpCpp.Rbfx.Generator.dll` is invoked with `--sdk-root`, `--flags-file`, `--generated-root`, `--project-root`, `--sdk-library`, `--log-file`. It:
   - Loads SDK parse flags
   - Discovers the host Clang toolchain (target triple, resource directory, system include paths)
   - Builds Clang arguments (standard, include dirs, defines, SIMD disable, warning suppress, toolchain flags)
   - Parses `Urho3D/Urho3DAll.h` via `ClangSharpHeaderParser`
   - Applies `BindingCompilationBootstrapPipeline` for marshaller rewrites
   - Filters functions not exported by the linked SDK library via `nm -D --demangle` symbol inspection
   - Renders C#/C++ output files via `BindingOutputRenderer`
   - Exports `SharpCpp.Rbfx.Bindings.json` metadata
   - Writes a `generate.stamp` to signal completion

4. **Phase 1 Native Build**: `SharpCppRbfx_Native` compiles the generated `.g.cpp` files with Unity (Jumbo) build, links against `sharpcpp-native-support` and `Urho3D`, and copies SDK runtime dependencies post-build. Package mode (`RBFX_CSHARP_BINDINGS_PACKAGE_VERSION` and `RBFX_ENGINE_METADATA_FILE` set) skips this target and the engine generator. `sharpcpp-rbfx-runtime` stays as an empty target. The user-code generators take the engine metadata from `RBFX_ENGINE_METADATA_FILE`, the file `runtimes/<rid>/metadata/SharpCpp.Rbfx.Bindings.json` of the runtime package. The folder of that file also holds `SharpCpp.Rbfx.ParseUmbrella.hpp`, which the user wrapper includes, so it is an include directory of the user library.

5. **Phase 1 Managed Build**: after the `SharpCpp.Native` dependency completes,
   `SharpCpp.Rbfx.Bindings.csproj` includes generated `.g.cs` files immediately
   before compilation.

6. **Phase 2 Generation**: `rbfx_csharp_add_usercode()` (`cmake/RbfxCSharpAddUserCode.cmake`) invokes `Rbfx.UserCode.Generator.dll` with `--sdk-root`, `--flags-file`, `--header`, `--include-directory`, `--assembly-name`, `--namespace`, `--library-name`, `--file-stem`, `--symbol-prefix`, `--generated-root`, `--import-binding-metadata` (pointing to phase 1 metadata). It:
   - Loads phase-1 binding metadata as imports
   - Parses the `HEADERS` of the call (for example `RotatingComponent.hpp` of `Example.UserCode`)
   - Applies the bootstrap pipeline with phase-1 types available as imported references
   - Renders output and exports its own metadata

7. **Phase 2 Native + Managed Build**: `rbfx_csharp_add_usercode()` writes the managed
   project (`<ASSEMBLY_NAME>.csproj`, `Directory.Build.props`, `Directory.Build.targets`,
   `<target>.props`) at configure time below `usercode/<target>/` in the build tree. CMake never
   runs `dotnet build`. A consumer csproj declares `<RbfxUserCodeReference Include="<cmake target>" />`.
   Target `RbfxResolveUserCodeReferences` in `eng/SharpCpp.Rbfx.Build.targets` then builds the
   generated project and references its dll. With `CSPROJ <path>` the user supplies the project.

8. **E2E Tests**: `dotnet test SharpCpp.slnx` runs the managed tests. Generated
   runsettings point Linux tests at the shared `build/bin/<Configuration>` native
   output directory.

## Integration

| Consumer | What it depends on | Mechanism |
|---|---|---|
| `SharpCpp.Rbfx.Generator` | `SharpCpp.Model`, `SharpCpp.Metadata`, `SharpCpp.Parsing`, `SharpCpp.Pipeline`, `SharpCpp.Rendering` | .NET project references |
| `SharpCpp.Rbfx.Bindings` | `SharpCpp.Support`, `SharpCpp.Native`, generated `*.g.cs` | .NET project references + late generated-source inclusion |
| `SharpCppRbfx_Native` | `SharpCpp::NativeSupport`, `Urho3D` SDK target, generated `*.g.cpp` | CMake `target_link_libraries` + Unity build |
| `Rbfx.UserCode.Generator` | Same pipeline libraries as rbfx generator | .NET project references |
| `Example.UserCode.Bindings` (generated by `rbfx_csharp_add_usercode`, csproj in the build tree) | `SharpCpp.Support`, `SharpCpp.Rbfx.Bindings`, `SharpCpp.Native`, generated `*.g.cs` | .NET project references + late generated-source inclusion |
| `Example.UserCode` (native bindings target, `Example.UserCode.Native` library) | `SharpCpp::NativeSupport`, `Urho3D`, `Example.UserCode.Impl` (user `.cpp` files), generated `.g.cpp` | CMake `target_link_libraries` + Unity build |
| `Example.Common` (source mode) | `SharpCpp.Rbfx.Bindings`, `Example.UserCode.Bindings.dll` | `RbfxUserCodeReference` + project reference |
| `Example.Common` (package mode, `RbfxBindingsPackageVersion` set) | `SharpCpp.Rbfx.Bindings` NuGet package, `Example.UserCode.Bindings.dll` | `RbfxUserCodeReference` + package reference |
| `Rbfx.NativeHost.Managed` | `SharpCpp.Rbfx.Bindings`, `SharpCpp.Rbfx.SourceGenerator` | .NET project references |
| `SharpCpp.Rbfx.Tests.E2E` | `SharpCpp.Rbfx.Bindings`, xUnit, .NET Test SDK | .NET project + package references |
| `rbfx/CMakeLists.txt` | `rbfx-sdk` installed at `RBFX_SDK_ROOT` | `find_package(Urho3D CONFIG)` |

### Directory Map

| Path | Role |
|---|---|
| `SharpCpp.Rbfx.Generator/` | Phase-1 SDK binding generator (ClangSharp-based, pipeline-enabled) |
| `SharpCpp.Rbfx.Bindings/` | Phase-1 managed assembly container (generated C# consumed via wildcard) |
| `SharpCpp.Rbfx.Tests.E2E/` | End-to-end xUnit tests exercising generated SDK wrappers |
| `Rbfx.UserCode.Generator/` | Phase-2 user-code generator |
| `cmake/` | `RbfxCSharpAddUserCode.cmake` (`rbfx_csharp_add_usercode()`), `RbfxCSharpDotNet.cmake`, `FindNetHost.cmake`, `SharpCppRbfxConfig.cmake.in` (package config of the installed CMake SDK, option `RBFX_INSTALL_SDK`), `ExportRbfxSdkParseFlags.cmake`, `templates/` |
| `eng/` | Shared MSBuild targets: `RbfxUserCodeReference` handling, `NativeItems.targets` (the one RID to native item mapping, also packed into the Android, iOS and Wasm runtime packages by `CrossPackage.cs targets`) |
| `Rbfx.Host/` | Native .NET hosting library (nethost/hostfxr), `SharpCpp::RbfxHost`, header `Urho3D/CSharp/DotNetHost.h`. `DotNetHost::LoadPlugin` loads a managed plugin through `Urho3D.Plugins.NativeHostEntryPoints.LoadPlugin` (`SharpCpp.Rbfx.Bindings/Plugins/NativeHostEntryPoints.cs`). The nethost linkage follows `BUILD_SHARED_LIBS` (`NETHOST_PREFER_SHARED`). A shared link copies `libnethost.so`, `libnethost.dylib` or `nethost.dll` next to the host library. MSVC always links `nethost.lib`. |
| `Rbfx.Examples/Example.UserCode/` | Example: native `RotatingComponent`, `TextTools`, implementation target `Example.UserCode.Impl` + CMake; no managed project |
| `Rbfx.Examples/Example.Common/` | Shared demo (`UserCodeSupport.cs`, `DemoApplication.cs`, `DemoDocument.cs`, `SceneSample.cs`); one project, two modes by `RbfxBindingsPackageVersion` (empty: source projects, set: bindings package); uses `Example.UserCode.Bindings.dll` |
| `Rbfx.Examples/Example.NativeHost/` | Example: plain C++ rbfx `Application` hosts .NET, loads a C# `ManagedPlugin` with `DotNetHost::LoadPlugin` and creates the C# `LogicComponent` (`Spinner`) by name |
| `CheckGeneratedStaleness.cmake` | Generator staleness checker (timestamp-based invalidation) |
| `CopyRbfxRuntimeDependencies.cmake.in` | Runtime library deployment script template |
