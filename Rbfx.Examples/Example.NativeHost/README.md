# Rbfx.NativeHost

Example of a plain C++ rbfx application that uses C# as a scripting layer. The application starts the .NET runtime, loads a C# plugin and creates the C# component of the plugin by name.

Like Example.Desktop and Example.Common, this example builds from source with the main CMake project of this repository. It is not part of `Rbfx.Examples.slnx`. It uses no NuGet packages. The local `Directory.Build.props` and `Directory.Build.targets` import the repository-root files, because MSBuild uses only the nearest file.

## Parts

- `main.cpp`: `NativeHostApp` derives from `Urho3D::Application`. `Start()` initializes `Urho3D::DotNetHost` (`SharpCpp::RbfxHost`) and calls `DotNetHost::LoadPlugin` with the plugin manager of the engine and `Rbfx.NativeHost.Net.dll`. Then it creates a scene with a box node, calls `CreateComponent("Spinner")` and sets the `Speed` attribute to 90. The application runs until you close the window.
- `Rbfx.NativeHost.Net/NativeHostPlugin.cs`: the plugin of the assembly. It derives from `ManagedPlugin`, which registers `Spinner` and its `[SerializeField]` attributes when the plugin loads.
- `Rbfx.NativeHost.Net/Spinner.cs`: a C# `LogicComponent` that rotates its node around the Y axis in `Update`. `Speed` is a `[SerializeField]` property, so C++ sets it through the attribute API.

## Call order

1. `DotNetHost::Initialize` starts the runtime from `Rbfx.NativeHost.Net.runtimeconfig.json`.
2. `Engine::Initialize` creates the `PluginManager`. `Application::Start()` runs after it.
3. `DotNetHost::LoadPlugin` loads `SharpCpp.Rbfx.Bindings.dll` (next to the runtime config) and calls its entry point `Urho3D.Plugins.NativeHostEntryPoints.LoadPlugin`. The entry point installs the script runtime, registers the plugin, adds its name to the loaded plugins and commits.
4. C++ creates the C# types by name.

## Build and run

Build with CMake. The target `Rbfx.NativeHost.Net` runs `dotnet build` for the plugin project and puts the plugin next to the executable. The call is `rbfx_csharp_add_plugin_build` (`cmake/RbfxCSharpDotNet.cmake`). The target `sharpcpp-rbfx-native` (part of the default build) includes it.

```
export CMAKE_PREFIX_PATH=<path of the installed rbfx SDK>
dotnet build SharpCpp.Rbfx.slnx --configuration Debug
cmake -S . -B <build directory> -G "Ninja Multi-Config"
cmake --build <build directory> --config Debug
./build/bin/Debug/Rbfx.NativeHost
```

- The `dotnet build` of the solution first builds the generators. CMake needs them in `build/bin/<Config>` and does not build them.
- The nested `dotnet build` of CMake sets the MSBuild property `RbfxNativeBuildDriver=cmake`. MSBuild then does not start a native build, because CMake is already the driver.
- `dotnet build SharpCpp.Rbfx.slnx` alone also builds the plugin. In that case MSBuild is the driver and it configures `build/native/linux-gcc-x64-dll` with `-DRBFX_CSHARP_BUILD_MANAGED=OFF`. Use another build directory for the CMake build.
- On Linux, set `GLIBC_TUNABLES=glibc.rtld.optional_static_tls=32768` for a run with a window.

The example builds when `RBFX_BUILD_EXAMPLES` is ON. The default is ON when `RBFX_BUILD_USERCODE` and `RBFX_BUILD_HOST` are ON.

## Notes

- NuGet packaging of the host is out of scope. A C++ project consumes the host through CMake (the `SharpCpp::RbfxHost` target).
- On Linux the executable links `libUrho3D.so` at startup. The runtime loads native libraries later with `dlopen`, and the TLS block of Urho3D does not fit then.
