# Rbfx.NativeHost

Example of a plain C++ rbfx application that uses C# as a scripting layer. The application starts the .NET runtime, loads a C# plugin and creates the C# component of the plugin by name.

Like Example.Desktop and Example.Common, this example builds from source with the main CMake project of this repository. It is not part of `Rbfx.Examples.slnx`. It uses no NuGet packages. The local `Directory.Build.props` and `Directory.Build.targets` import the repository-root files, because MSBuild uses only the nearest file.

## Parts

- `src/main.cpp`: `NativeHostApp` derives from `Urho3D::Application`. `Start()` initializes `Urho3D::DotNetHost` (`SharpCpp::RbfxHost`) and calls `DotNetHost::LoadPlugin` with the plugin manager of the engine and `Rbfx.NativeHost.Managed.dll`. Then it creates a scene with a box node, calls `CreateComponent("Spinner")` and sets the `Speed` attribute to 90. After the requested number of frames the application reads the yaw of the node and exits. The exit code is not 0 when the yaw is 0.
- `Rbfx.NativeHost.Managed/NativeHostPlugin.cs`: the plugin of the assembly. It derives from `ManagedPlugin`, which registers `Spinner` and its `[SerializeField]` attributes when the plugin loads.
- `Rbfx.NativeHost.Managed/Spinner.cs`: a C# `LogicComponent` that rotates its node around the Y axis in `Update`. `Speed` is a `[SerializeField]` property, so C++ sets it through the attribute API.

## Call order

1. `DotNetHost::Initialize` starts the runtime from `Rbfx.NativeHost.Managed.runtimeconfig.json`.
2. `Engine::Initialize` creates the `PluginManager`. `Application::Start()` runs after it.
3. `DotNetHost::LoadPlugin` loads `SharpCpp.Rbfx.Bindings.dll` (next to the runtime config) and calls its entry point `Urho3D.Plugins.NativeHostEntryPoints.LoadPlugin`. The entry point installs the script runtime, registers the plugin, adds its name to the loaded plugins and commits.
4. C++ creates the C# types by name.

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
