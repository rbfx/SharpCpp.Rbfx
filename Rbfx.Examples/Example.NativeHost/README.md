# Rbfx.NativeHost

Example of a plain C++ rbfx application that uses C# as a scripting layer. The application starts the .NET runtime, loads a C# component and adds it to a node.

Like Example.Desktop and Example.Common, this example builds from source with the main CMake project of this repository. It is not part of `Rbfx.Examples.slnx`. It uses no NuGet packages. The local `Directory.Build.props` and `Directory.Build.targets` import the repository-root files, because MSBuild uses only the nearest file.

## Parts

- `src/main.cpp`: `NativeHostApp` derives from `Urho3D::Application`. `Start()` initializes `Urho3D::DotNetHost` (`SharpCpp::RbfxHost`), creates a scene with a box node and calls the managed function `RbfxNativeHost.Scripting.AttachSpinner(Node*, float)`. After the requested number of frames the application reads the yaw of the node and exits. The exit code is not 0 when the yaw is 0.
- `Rbfx.NativeHost.Managed/Scripting.cs`: the `[UnmanagedCallersOnly]` function that the C++ code calls. It creates a `Spinner` and adds it to the node.
- `Rbfx.NativeHost.Managed/Spinner.cs`: a C# `LogicComponent` that rotates its node around the Y axis in `Update`.

## Build and run

```
export RBFX_SDK_ROOT=<path of the installed rbfx SDK>
dotnet build SharpCpp.Rbfx.slnx --configuration Debug
./build/bin/Debug/Rbfx.NativeHost --headless --frames 10
```

The example builds when `RBFX_BUILD_EXAMPLES` is ON. The default is ON when `RBFX_BUILD_USERCODE` and `RBFX_BUILD_HOST` are ON.

## Notes

- NuGet packaging of the host is out of scope. A C++ project consumes the host through CMake (the `SharpCpp::RbfxHost` target).
- On Linux the executable links `libUrho3D.so` at startup. The runtime loads native libraries later with `dlopen`, and the TLS block of Urho3D does not fit then.
