# Example.UserCode

Example of a native C++ component that the generator wraps for C#. This example has only native code. It is not an executable project and it has no managed project in this directory.

Like `Example.NativeHost`, this example builds from source with the main CMake project of this repository. It builds when `RBFX_BUILD_USERCODE` is ON (the default).

## Parts

- `include/RbfxExamples/RotatingComponent.hpp`, `src/RotatingComponent.cpp`: user C++ type. It rotates its node around the Y axis (default 45 degrees per second). `RotatingComponent` has no `URHO3D_OBJECT`; the private `RotatingComponentImpl` in the `.cpp` file adds it.
- `CMakeLists.txt`: calls `rbfx_csharp_add_usercode(Example.UserCode ...)`. The build produces these files:
  - `Example.UserCode.Bindings.dll`: the generated bindings assembly with the `RbfxExamples.RotatingComponent` wrapper.
  - `libExample.UserCode.Native.so` (`Example.UserCode.Native.dll` on Windows): the native library. It also exports the C function `RbfxExamples_AddRotatingComponent`, because the user-code bindings cannot pass `Node*` or `Context*` yet.

  At configure time the call also writes `Directory.Build.props`, `Directory.Build.targets`, `Example.UserCode.props` and `Example.UserCode.Bindings.csproj` below `build/native/dev/generated/usercode/Example.UserCode/`. You need no generator knowledge and no bindings project. CMake never runs `dotnet build`. With `CSPROJ <path>` you supply your own project and the call writes none.

## How managed code uses the dll

A managed project that wants the dll declares `<RbfxUserCodeReference Include="Example.UserCode" />`. MSBuild then builds the generated bindings project and references `Example.UserCode.Bindings.dll`. The project must also reference `SharpCpp.Rbfx.Bindings` as a project and not as a NuGet package, because two bindings assemblies would load.

`Rbfx.Examples/Example.Common` does this:

- `Example.Common.csproj` declares the `RbfxUserCodeReference`, references the bindings and source generator projects, and defines `RBFX_USERCODE`.
- `UserCodeSupport.cs` imports `RbfxExamples_AddRotatingComponent` with its own `LibraryImport`, because the generator does not wrap a global `extern "C"` function. The library name is a constant: `Example.UserCode.Native`, `__Internal` when `SHARPCPP_STATIC_RUNTIME` is defined (iOS), or `libExample.UserCode.Native` when `SHARPCPP_BROWSER_WASM` is defined. The generated bindings use the same names, because `rbfx_csharp_add_usercode` passes them to the generator (property `RbfxWrappedImportLibraryName` in `Example.UserCode.props`). `UserCodeSupport.AddRotatingComponent(node)` adds the component to a node. `SceneSample` calls it for its primary node.
- A target in `Example.Common.csproj` copies `libSharpCpp.Rbfx.Native`, `libUrho3D` and `libExample.UserCode.Native` to the output folder of each app that references Common. It gives an error if one file is missing.

`Rbfx.Examples/Example.Common.NuGet` compiles the same sources against the `SharpCpp.Rbfx.Bindings` NuGet package. It has no `RBFX_USERCODE` constant, and `UserCodeSupport.IsAvailable` is `false`. The Android, iOS, Wasm and UWP examples reference it.

## Build and run

The build needs `RBFX_SDK_ROOT` and the native build (about 7 minutes the first time).

```
export RBFX_SDK_ROOT=<path of the installed rbfx SDK>
dotnet build SharpCpp.Rbfx.slnx --configuration Debug
dotnet build Rbfx.Examples/Example.Desktop --configuration Debug
GLIBC_TUNABLES=glibc.rtld.optional_static_tls=32768 dotnet Rbfx.Examples/Example.Desktop/bin/Debug/net10.0/linux-x64/Example.Desktop.dll
```

The desktop example prints that `Example.UserCode` is active. On Linux the `GLIBC_TUNABLES` value reserves static TLS space for `libUrho3D.so`, which the runtime loads late with `dlopen`.
