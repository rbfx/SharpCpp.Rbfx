# Example.UserCode

Example of a managed executable that uses a native C++ component through bindings that the generator writes.

Like `Example.NativeHost`, this example builds from source with the main CMake project of this repository. It is not part of `Rbfx.Examples.slnx`. It uses no NuGet packages. The local `Directory.Build.props` and `Directory.Build.targets` import the repository-root files, because MSBuild uses only the nearest file.

## Parts

- `include/RbfxExamples/RotatingComponent.hpp`, `src/RotatingComponent.cpp`: user C++ type. It rotates its node around the Y axis (default 45 degrees per second). `rbfx_csharp_add_usercode(Example.UserCode ...)` wraps it as `Example.UserCode.Bindings`. At configure time it also writes the managed bindings project below `build/native/dev/generated/usercode/Example.UserCode/`.
- `Example.UserCode.csproj`, `Program.cs`: managed executable. It declares `<RbfxUserCodeReference Include="Example.UserCode" />`, so MSBuild builds the generated bindings project and references its assembly. It compiles the sources of `../Example.Common` (`SceneSample`) as part of the project, and it references `SharpCpp.Rbfx.Bindings` as a project. It must not reference the bindings NuGet package, because two bindings assemblies would load.
- `Program.cs` calls the exported C function `RbfxExamples_AddRotatingComponent` in `Example.UserCode.Native`, because the user-code bindings cannot pass `Node*` or `Context*` yet. `RotatingComponent` has no `URHO3D_OBJECT`; the private `RotatingComponentImpl` in the `.cpp` file adds it.

## Build and run

```
export RBFX_SDK_ROOT=<path of the installed rbfx SDK>
dotnet build SharpCpp.Rbfx.slnx --configuration Debug
GLIBC_TUNABLES=glibc.rtld.optional_static_tls=32768 ./build/bin/Debug/Example.UserCode --headless --frames 10
```

On Linux the executable needs the `GLIBC_TUNABLES` value. It reserves static TLS space for `libUrho3D.so`, which the runtime loads late with `dlopen`. The launch profile in `Properties/launchSettings.json` sets it.

The example builds when `RBFX_BUILD_USERCODE` is ON (the default). Options: `--headless` runs without a window, and `--frames N` exits after N frames.
