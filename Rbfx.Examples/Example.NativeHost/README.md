# Rbfx.NativeHost

Example of a C++ executable that hosts the .NET runtime and runs rbfx application code written in C#.

Like Example.Desktop and Example.Common, this example builds from source with the main CMake project of this repository. It is not part of `Rbfx.Examples.slnx`. It uses no NuGet packages. The local `Directory.Build.props` and `Directory.Build.targets` import the repository-root files, because MSBuild uses only the nearest file.

## Parts

- `include/RbfxNativeHost/Spinner.hpp`, `src/Spinner.cpp`: user C++ types. `rbfx_csharp_add_usercode(Rbfx.NativeHost.Wrapped ...)` wraps them as `Rbfx.NativeHost.Bindings`. At configure time it also writes the managed bindings project below `build/native/dev/generated/usercode/Rbfx.NativeHost.Wrapped/`.
- `src/main.cpp`: the executable. It includes `Urho3D/CSharp/DotNetHost.h` and uses `SharpCpp::RbfxHost` (`Urho3D::DotNetHost`) to load `Rbfx.NativeHost.Managed.dll` and to call `RbfxNativeHost.Entry.Run(int argc, char** argv)`.
- `Rbfx.NativeHost.Managed`: managed entry point. It creates an `Application`, a scene and a `Spinner`. It declares `<RbfxUserCodeReference Include="Rbfx.NativeHost.Wrapped" />`. MSBuild then builds the generated bindings project and references its assembly. The example has no hand-written bindings project.

## Build and run

```
export RBFX_SDK_ROOT=<path of the installed rbfx SDK>
dotnet build SharpCpp.Rbfx.slnx --configuration Debug
./build/bin/Debug/Rbfx.NativeHost --headless --frames 10
```

The example builds when `RBFX_BUILD_EXAMPLES` is ON. The default is ON when `RBFX_BUILD_USERCODE` and `RBFX_BUILD_HOST` are ON.

## Notes

- NuGet packaging of the host is out of scope. A C++ project consumes the host through CMake (the `SharpCpp::RbfxHost` target).
- On Linux the executable links `libUrho3D.so` at startup. The runtime loads the binding libraries later with `dlopen`, and the TLS block of Urho3D does not fit then.
- User-code bindings cannot yet pass `Context*`, `Node*` or the type information of `URHO3D_OBJECT` types. `Spinner` has no `URHO3D_OBJECT`. A small exported C function creates it on a node.
