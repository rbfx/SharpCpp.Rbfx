# Rbfx.NativeHost

Example of a C++ executable that hosts the .NET runtime and runs rbfx application code written in C#.

## Parts

- `include/RbfxNativeHost/Spinner.hpp`, `src/Spinner.cpp`: user C++ types. `rbfx_csharp_add_usercode()` wraps them as `Rbfx.NativeHost.Bindings`.
- `src/main.cpp`: the executable. It uses `SharpCpp::RbfxHost` (`DotNetHost`) to load `Rbfx.NativeHost.Managed.dll` and to call `RbfxNativeHost.Entry.Run(int argc, char** argv)`.
- `Rbfx.NativeHost.Bindings`: managed project that compiles the generated bindings of the wrapped types.
- `Rbfx.NativeHost.Managed`: managed entry point. It creates an `Application`, a scene and a `Spinner`.

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
