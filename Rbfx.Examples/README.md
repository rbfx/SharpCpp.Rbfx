# rbfx NuGet examples

These examples restore published packages from NuGet.org. They have no project
references to the binding generator and need no local rbfx SDK or SharpCpp checkout.
Requires .NET 10. The desktop example supports `linux-x64`, `osx-arm64`, and `win-x64`.
`Example.Common` owns the shared rbfx scene and node logic; each platform project
contains its entry point and platform UI or engine host. Android, iOS, browser
WebAssembly, and UWP require the matching .NET workload or Windows UWP tools.

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
serve the `bin/Release/net10.0/publish/wwwroot` directory over HTTP to run it. The
host must enable cross-origin isolation for the multithreaded WebAssembly runtime.
`Example.Uwp` is a small modern .NET UWP XAML app. Its custom runtime graph maps
`uap-x64` to the Windows x64 runtime while allowing NuGet to select the
UWP-specific native engine package.

`Directory.Build.props` pins the published package version for all examples.
To try another published version, edit `RbfxPackageVersion` or pass
`-p:RbfxPackageVersion=VERSION`. Select another RID with `-r osx-arm64` or `-r win-x64`.

To test packages from the GitHub `latest` release before NuGet.org lists them,
restore the project you want to test from a local feed, using the version in the
downloaded package names:

```sh
gh release download latest --repo rbfx/SharpCpp.Rbfx --pattern '*.nupkg' --dir build/examples-release-feed
dotnet restore Rbfx.Examples/Example.Desktop/Example.Desktop.csproj --source build/examples-release-feed -p:RbfxPackageVersion=VERSION
dotnet run --project Rbfx.Examples/Example.Desktop --no-restore -p:RbfxPackageVersion=VERSION
```

CI builds the desktop sample on Linux, macOS, and Windows, the Android sample for
all three published Android RIDs, and the iOS, browser-Wasm, and UWP samples on
their required build runners.

On Ubuntu, the engine needs `libopengl0 libglx0 libx11-6 libdbus-1-3 libuuid1`.
The launch profile reserves static TLS space for the Linux native SDK. When running
the built DLL directly on Linux, set it before starting the process:

```sh
GLIBC_TUNABLES=glibc.rtld.optional_static_tls=32768 dotnet Rbfx.Examples/Example.Desktop/bin/Debug/net10.0/linux-x64/Example.Desktop.dll
```
