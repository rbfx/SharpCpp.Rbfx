# Example.UserCode

Example of a native C++ component that the generator wraps for C#. This example has only native code. It is not an executable project and it has no managed project in this directory.

Like `Example.NativeHost`, this example builds from source with the main CMake project of this repository. It builds when `RBFX_BUILD_USERCODE` is ON (the default).

## Parts

- `include/RbfxExamples/RotatingComponent.hpp`, `src/RotatingComponent.cpp`: user C++ type. It rotates its node around the Y axis (default 45 degrees per second). `RotatingComponent` uses `URHO3D_OBJECT`, so its managed wrapper implements `IStaticTypeInfo<RotatingComponent>` and `Node.CreateComponent<RotatingComponent>()` works. The static method `RotatingComponent::RegisterObject(Context*)` adds the object factory with `AddFactoryReflection`. The managed side calls it once.
- `CMakeLists.txt`: calls `rbfx_csharp_add_usercode(Example.UserCode ...)`. The build produces these files:
  - `Example.UserCode.Bindings.dll`: the generated bindings assembly with the `RbfxExamples.RotatingComponent` wrapper.
  - `libExample.UserCode.Native.so` (`Example.UserCode.Native.dll` on Windows): the native library. The example has no hand-written `extern "C"` function.

  At configure time the call also writes `Directory.Build.props`, `Directory.Build.targets`, `Example.UserCode.props` and `Example.UserCode.Bindings.csproj` below `build/native/dev/generated/usercode/Example.UserCode/`. You need no generator knowledge and no bindings project. CMake never runs `dotnet build`. With `CSPROJ <path>` you supply your own project and the call writes none.

## How managed code uses the dll

A managed project that wants the dll declares `<RbfxUserCodeReference Include="Example.UserCode" />`. MSBuild then builds the generated bindings project and references `Example.UserCode.Bindings.dll`. The project must reference `SharpCpp.Rbfx.Bindings` either as a project or as a NuGet package, and not both, because two bindings assemblies would load.

`Rbfx.Examples/Example.Common` does this:

- `Example.Common.csproj` declares the `RbfxUserCodeReference` and defines `RBFX_USERCODE`. In source mode (`RbfxBindingsPackageVersion` is empty) it references the bindings and source generator projects. In package mode (`RbfxBindingsPackageVersion` is set) it references the `SharpCpp.Rbfx.Bindings` package.
- `UserCodeSupport.cs` calls `RotatingComponent.RegisterObject(node.GetContext())` and then `node.CreateComponent<RotatingComponent>()`. It needs no `LibraryImport`. `SceneSample` calls `UserCodeSupport.AddRotatingComponent` for its primary node.
- Each app (Desktop, Android, iOS, Wasm, UWP) declares its own `RbfxUserCodeReference`, because items do not flow through a project reference. The repository targets then copy the native libraries to the output folder of the desktop apps (an error shows if one file is missing). For Android, iOS and Wasm they add the platform items of the native library.

## Build and run

The build needs `RBFX_SDK_ROOT` and the native build (about 7 minutes the first time).

```
export RBFX_SDK_ROOT=<path of the installed rbfx SDK>
dotnet build SharpCpp.Rbfx.slnx --configuration Debug
dotnet build Rbfx.Examples/Example.Desktop --configuration Debug
GLIBC_TUNABLES=glibc.rtld.optional_static_tls=32768 dotnet build/bin/Debug/Example.Desktop.dll
```

The desktop example prints that `Example.UserCode` is active. On Linux the `GLIBC_TUNABLES` value reserves static TLS space for `libUrho3D.so`, which the runtime loads late with `dlopen`.
