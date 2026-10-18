# rbfx NuGet examples

All examples share one project, `Example.Common`. It has two modes (see below). The desktop
example builds from source by default. The Android, iOS, browser WebAssembly, and UWP examples
build for their own RID, so they need the SDK of that RID (`build/sdk/<rid>`, see
`eng/fetch-rbfx-sdk.sh`) and the package mode with packages that were built by `package.sh` or CI.
Requires .NET 10. The desktop example supports `linux-x64`, `osx-arm64`, and `win-x64`.
`Example.Common` owns the shared rbfx scene and node logic; each platform project
contains its entry point and platform UI or engine host. Android, iOS, browser
WebAssembly, and UWP require the matching .NET workload or Windows UWP tools.

`Example.Common` and all the apps build the native user code `Example.UserCode`,
so the cube also shows the native `RotatingComponent`. They need the native build (CMake option
`RBFX_BUILD_USERCODE`, ON by default for a top-level build). See `Example.UserCode/README.md`. `Example.Common` is one project
with two modes. The MSBuild property `RbfxBindingsPackageVersion` selects the mode:

- Source mode (the property is empty, the default): `Example.Common` references the bindings source
  projects and the generated `Example.UserCode.Bindings` assembly. It needs `CMAKE_PREFIX_PATH` (or the SDK
  of the target RID in `build/sdk/<rid>`).
- Package mode (for example `-p:RbfxBindingsPackageVersion=VERSION -p:RbfxPackageVersion=VERSION`):
  `Example.Common` references the `SharpCpp.Rbfx.Bindings` package. The engine natives come from the
  runtime package, and so does the engine binding metadata that the generator of `Example.UserCode` imports.
  Only the native library of `Example.UserCode` comes from the native build. Restore first: the build
  finds the metadata in the restored package folder. The native build does not run the engine generator.

`Example.Common` and `Rbfx.Examples.slnx` need the SDK. The first native build takes about 7 minutes.

`Example.UserCode` is native only. Its generated `Example.UserCode.Bindings.dll` is used by `Example.Common`, and so by every app.
Each app declares `<RbfxUserCodeReference Include="Example.UserCode" />` itself, because items do not flow
through a project reference.

`Example.Android`, `Example.Ios`, `Example.Wasm` and `Example.Uwp` have a `Directory.Build.props` that sets
the default `RbfxTargetRid` (`android-arm64`, `ios-arm64`, `browser-wasm`, `uap-x64`). The RID selects the
SDK, the native build tree `build/cross/<rid>` and the native items of the app. `-p:RuntimeIdentifier=<rid>`
or `-p:RbfxTargetRid=<rid>` selects another RID. Each of these apps writes its output to `bin/<Configuration>/<rid>/`
in its own folder and does not treat warnings as errors.

`dotnet build Rbfx.Examples.slnx` in the repository root builds Desktop, Android and Wasm in parallel. Each RID uses its
own build tree, so no two projects write the same file. `Rbfx.Examples.Apple.slnx` (repository root) builds the iOS app and
`Rbfx.Examples.Windows.slnx` builds the UWP app. Each RID needs its SDK in `build/sdk/<rid>`.

`Example.NativeHost` is also an exception. It is a C++ rbfx application that uses C# as a scripting layer. It builds from source with the main CMake project and is not part of the example solutions. See its README.

`Example.DesktopAot` is a desktop NativeAOT application. It is a CMake project and is not part of the example solutions, because
its C# plugin project needs the properties that CMake passes. It shows the shim host mode: a native launcher starts the
engine, and a NativeAOT head (the C# runtime is compiled ahead of time, so no .NET runtime is installed) loads the C# plugin
`DesktopAot`. The plugin creates a scene with the managed `Spinner` component (a `LogicComponent` with the `[SerializeField]`
float `Speed`). `CMakeLists.txt` uses `rbfx_csharp_add_plugin`, `rbfx_csharp_add_launcher`, `rbfx_csharp_package_application`
and `rbfx_csharp_install_application`. NativeAOT needs `-DRBFX_HOST_MODE=shim`, the SDK of the RID (`build/sdk/<rid>`) and
the native toolchain of the .NET ILCompiler (a C and C++ compiler and a linker). With a window
the example runs until you close it. With `--headless` it stops after three updates. Build and run it (Linux x64) from the
repository root:

```sh
cd Rbfx.Examples/Example.DesktopAot
cmake --preset linux-gcc-x64-dll -B /tmp/desktop-aot \
  "-DCMAKE_PREFIX_PATH=$PWD/../../build/sdk/linux-x64;$PWD/../../cmake/SharpCppRbfx" \
  -DRBFX_HOST_MODE=shim -DRBFX_BUILD_HOST=ON
cmake --build /tmp/desktop-aot --config Debug --target ExampleDesktopAot.CSharp
/tmp/desktop-aot/bin/Debug/ExampleDesktopAot --headless
```

The log shows `DesktopAot: loaded`, the updates and `DesktopAot: spinner turns at 90 degrees per second`.
`SharpCpp.Rbfx.Tests.Aot` (run by `eng/run-aot-test.sh --rid linux-x64`) is the test of the same setup.

From the SharpCpp.Rbfx repository root:

```sh
dotnet build Rbfx.Examples/Example.Desktop
dotnet run --project Rbfx.Examples/Example.Desktop
```

The window displays a rotating cube using models, materials and shaders copied
from the `SharpCpp.Rbfx.Data` and `SharpCpp.Rbfx.CoreData` NuGet packages. Close the
window to exit.

`Example.Android` uses a native rbfx scene behind an Android interface. Tap **Add a
node** to create a node, set its position, and display its distance from the origin.
Build its APK with:

```sh
dotnet build Rbfx.Examples/Example.Android -c Release
```

It defaults to `android-arm64`; pass `-r android-x64` for an x64 emulator. Deploy
the generated signed APK with `adb install -r PATH_TO_APK`. Android projects use
an Activity that calls the static `Program.Main` entry function.

`Example.Ios` creates an rbfx scene and reports a node's position in a UIKit view.
`Example.Wasm` creates the same native scene through the browser WebAssembly runtime;
serve the `bin/Release/browser-wasm/publish/wwwroot` directory over HTTP to run it. The
host must enable cross-origin isolation for the multithreaded WebAssembly runtime.
`Example.Uwp` is a small modern .NET UWP XAML app. Its custom runtime graph maps
`uap-x64` to the Windows x64 runtime while allowing NuGet to select the
UWP-specific native engine package.

`Directory.Build.props` imports the repository props and sets the default `RbfxPackageVersion` for the
examples. The desktop and the data packages (`SharpCpp.Rbfx.CoreData`, `SharpCpp.Rbfx.Data`) use it.
Select another RID with `-r osx-arm64` or `-r win-x64`.

To run the examples in package mode, use packages that were built by `package.sh` or by CI, in a local
feed. Use the version in the package names for both properties:

```sh
dotnet restore Rbfx.Examples/Example.Desktop/Example.Desktop.csproj --source PATH_TO_FEED -p:RbfxBindingsPackageVersion=VERSION -p:RbfxPackageVersion=VERSION
dotnet run --project Rbfx.Examples/Example.Desktop --no-restore -p:RbfxBindingsPackageVersion=VERSION -p:RbfxPackageVersion=VERSION
```

CI builds the desktop sample on Linux, macOS, and Windows, the Android sample for
all three Android RIDs, and the iOS, browser-Wasm, and UWP samples on
their required build runners.

On Ubuntu, the engine needs `libopengl0 libglx0 libx11-6 libdbus-1-3 libuuid1`.
The launch profile reserves static TLS space for the Linux native SDK. When running
the built DLL directly on Linux, set it before starting the process:

```sh
GLIBC_TUNABLES=glibc.rtld.optional_static_tls=32768 dotnet build/bin/Debug/Example.Desktop.dll
```
