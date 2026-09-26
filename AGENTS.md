# SharpCpp.Rbfx agent guide

This repository generates C# bindings for the rbfx game engine with SharpCpp. It is an
external SharpCpp consumer: it uses only SharpCpp's public projects, native headers
(`include/SharpCpp`) and CMake modules (`cmake/`).

## Layout

- `SharpCpp.Rbfx.Generator/` - phase-1 generator for the installed rbfx SDK headers.
- `Rbfx.UserCode/` - phase-2 user-code generator, sample user code and bindings.
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

Do not use absolute paths. Missing generator capabilities are fixed in SharpCpp, not
worked around here.

## Build and verify

```sh
dotnet build SharpCpp.Rbfx.slnx --configuration Debug -maxcpucount
dotnet test SharpCpp.Rbfx.Tests.E2E --configuration Debug --no-build --no-restore
```

Coherent local commits after validation are authorized; publishing requires explicit
task authorization.
