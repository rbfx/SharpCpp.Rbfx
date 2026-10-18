# SharpCpp.Rbfx

C# bindings for rbfx and the .NET hosting support for native rbfx applications.

## CMake SDK

The root `CMakeLists.txt` can install a CMake SDK. The option `RBFX_INSTALL_SDK` controls it.
The default is ON when `RBFX_BUILD_HOST` is ON and this project is the top-level project.
Configure a build that you ship with `-DRBFX_HOST_DEFAULT_DOTNET_ROOT=OFF`. Then the host library has no path of the build machine.
Use one install prefix for each configuration, because the Debug and Release files have the same name.
The install rules do not change the build. Run the install step to use them.

```
cmake --build <build dir> --config <Config>
cmake --install <build dir> --config <Config> --prefix <prefix>
```

A consumer project uses the SDK like this:

```cmake
find_package(SharpCppRbfx CONFIG REQUIRED)
target_link_libraries(my_app PRIVATE SharpCpp::RbfxHost)
```

Set `CMAKE_PREFIX_PATH` to the SDK prefix and to the installed rbfx SDK (or set `Urho3D_DIR`).
Set `BUILD_SHARED_LIBS` in the consumer to the same value as the rbfx SDK. Urho3D checks it.

A consumer project wraps its own C++ headers like this. The consumer needs no SharpCpp or SharpCpp.Rbfx source tree:

```cmake
find_package(SharpCppRbfx CONFIG REQUIRED)
add_library(MyGame.Impl SHARED MyGame.cpp)
target_include_directories(MyGame.Impl PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}")
target_link_libraries(MyGame.Impl PUBLIC Urho3D)
rbfx_csharp_add_bindings(MyGame
  HEADERS MyGame.hpp
  INCLUDE_DIRECTORIES .
  IMPLEMENTATION MyGame.Impl
  NAMESPACE MyGame)
```

This example shows the integration mode (the default with one `IMPLEMENTATION` target): the generated code goes into the shared
library `MyGame.Impl`. For a static implementation library on desktop, add `SEPARATE_LIBRARY` to the call (see below).

The C++ namespace of the wrapped types must be the value of `NAMESPACE`. The generator ignores the other namespaces.
`BINDINGS_CLASS` is the name of the generated `static partial class` in `NAMESPACE`. The default is the target name with each
character that is not a letter, a digit or `_` replaced by `_`, plus `Bindings` (for example `MyGameBindings` for `MyGame`).
Use a different value when two assemblies share a namespace, or the compiler reports `CS0436`.

With one `IMPLEMENTATION` target the generated C++ code becomes a source of that target (integration mode). The target is
then the native library of the bindings: the C# code imports the entry points from the library with the `OUTPUT_NAME` of the
target (or its name), and the bindings makes no `<name>.Native` library. Set `OUTPUT_NAME` before the call. The target must be
a shared or module library, or a static or object library on a platform with static native libraries (iOS, wasm). The glue needs
C++20, so the target gets `cxx_std_20`. Add `SEPARATE_LIBRARY` to get the old behavior: a native library `LIBRARY_NAME`
(default `<target>.Native`) that links the `IMPLEMENTATION` targets. The mode is also separate without `IMPLEMENTATION`.
`LIBRARY_NAME` and `LIBRARY_TYPE` are only for the separate mode. A `RbfxUserCodeReference` item with an integration bindings target
needs the metadata `LibraryName` (the output name of the implementation target).
The integration mode needs an implementation target that uses the keyword signature of `target_link_libraries` (`PUBLIC`,
`PRIVATE`), because the function adds links to it. Otherwise use `SEPARATE_LIBRARY`. An object library works only on Emscripten.
`rbfx_csharp_add_project()` has the same `SEPARATE_LIBRARY` option and passes it to the bindings.
The package file sets the inputs of the function. They are the installed metadata, generator, headers and Clang headers.
The parse flags come from the target `Urho3D` and the compiler of the consumer. The rbfx SDK root comes from `Urho3D_DIR`.
The generated sources go to `<build>/generated` and the native library goes to `<build>/bin/<Config>`.
Set `SHARPCPP_GENERATED_ROOT` and `SHARPCPP_NATIVE_OUTPUT_ROOT` to change them.
The generator runs with `dotnet` at build time. The build needs no network and no git.

### Consume with find_package

Two prefixes give the package `SharpCppRbfx`. Both prefixes give the same targets, functions and variables.
The engine must come from an installed rbfx SDK. The two prefixes differ in how they find it.

- SDK prefix. Put the prefix of `cmake --install` on `CMAKE_PREFIX_PATH`. The package uses the installed files.
  The package calls `find_dependency(Urho3D)` itself. Set `Urho3D_DIR` or `CMAKE_PREFIX_PATH` to the rbfx SDK.
- Shim prefix. Put the folder `cmake/SharpCppRbfx` of this repository on `CMAKE_PREFIX_PATH`. The package builds this source tree
  with `add_subdirectory()` in `<build>/SharpCppRbfx`. A SharpCpp checkout must be at `SHARPCPP_ROOT` (default `../SharpCpp`).
  The shim sets `RBFX_BUILD_USERCODE`, `RBFX_BUILD_EXAMPLES` and `RBFX_INSTALL_SDK` to OFF and `RBFX_BUILD_HOST` to ON, when the consumer did not define them.
  The consumer must call `find_package(Urho3D)` before `find_package(SharpCppRbfx)`. The desktop shared engine is the tested case.
  A static engine is accepted on iOS and Emscripten only. An engine in source mode is not supported.
  The first call adds the source tree. A second call (also from a sibling directory) only sets the variables below.
  The default `SHARPCPP_NATIVE_OUTPUT_ROOT` is `<build>/SharpCppRbfx/bin` and the default `SHARPCPP_MANAGED_OUTPUT_ROOT` is
  `<build>/SharpCppRbfx/managed`. A value from the consumer wins.
  The shim build runs `dotnet build` itself for the generators (`SharpCpp.Rbfx.Generator` and `Rbfx.UserCode.Generator`), so the consumer
  needs only `CMAKE_PREFIX_PATH`. The .NET SDK (`dotnet`) must be installed. CMake stops with a message when it is not found.
  With `RBFX_CSHARP_BUILD_GENERATORS` ON (the default for the shim), the target `sharpcpp-rbfx-build-managed` runs `dotnet build` for
  `SharpCpp.Rbfx.Bindings` after the native bindings. The managed folder then holds `SharpCpp.Rbfx.Bindings`, `SharpCpp.Std`,
  `SharpCpp.Ea` and `SharpCpp.Support`. The folder is full only when the build runs this target. The targets of `rbfx_csharp_add_dotnet_project()` and
  `rbfx_csharp_add_plugin_build()` depend on it. The targets of `rbfx_csharp_add_bindings()` do not. A consumer target that reads
  `SharpCppRbfx_MANAGED_DIR` in its own custom command must call `add_dependencies(<target> sharpcpp-rbfx-build-managed)`.
  Set `RBFX_CSHARP_BUILD_GENERATORS` to OFF to use generators that you built before in `SHARPCPP_MANAGED_OUTPUT_ROOT/<Config>`.
  Then the managed folder is not filled by CMake either.

```cmake
find_package(Urho3D REQUIRED)
find_package(SharpCppRbfx CONFIG REQUIRED)
add_executable(my_app main.cpp)
target_link_libraries(my_app PRIVATE Urho3D SharpCpp::RbfxHost)
```

```
cmake -G Ninja -S <consumer> -B <build> -DCMAKE_PREFIX_PATH="<rbfx SDK prefix>;<SDK prefix or <repo>/cmake/SharpCppRbfx>"
```

Both prefixes set these variables:

- `SharpCppRbfx_FOUND`: TRUE.
- `SharpCppRbfx_IS_SDK`: ON for the SDK prefix, OFF for the shim prefix.
- `SharpCppRbfx_PACKAGE_ROOT`: the SDK prefix, or the root of this repository.
- `SharpCppRbfx_MANAGED_DIR`: the folder of the managed assemblies (`SharpCpp.Rbfx.Bindings`, `SharpCpp.Rbfx.NativeHost`, `SharpCpp.Support`, `SharpCpp.Std`, `SharpCpp.Ea`).
  In the shim package the value is a generator expression, `<SHARPCPP_MANAGED_OUTPUT_ROOT>/$<CONFIG>`. Use it only where CMake
  evaluates generator expressions: in the `COMMAND` of `add_custom_command()` and `add_custom_target()`, and in the `PROPERTIES` of
  the `rbfx_csharp_add_*` functions. The target `sharpcpp-rbfx-build-managed` fills the folder (see above). It exists only when `RBFX_CSHARP_BUILD_GENERATORS` is ON.
  In the SDK package the value is a plain path, `<prefix>/share/SharpCppRbfx/managed`. The SDK package leaves the variable unset
  when the SDK was built with `RBFX_CSHARP_BINDINGS_PACKAGE_VERSION`, because it then has no managed files.

The package gives the targets `SharpCpp::RbfxHost` and `SharpCpp::RbfxNative` and the functions `rbfx_csharp_add_project()`,
`rbfx_csharp_add_plugin()`, `rbfx_csharp_add_launcher()`, `rbfx_csharp_add_data()`, `rbfx_csharp_package_application()`,
`rbfx_csharp_install_application()`, `rbfx_csharp_find_core_data()`, `rbfx_csharp_add_plugin_build()`, `rbfx_csharp_add_dotnet_project()` and `rbfx_csharp_add_bindings()`.

The installed SDK has these parts:

- The host library `SharpCpp.Rbfx.Host` and the headers `Urho3D/CSharp/DotNetHost.h`, `Urho3D/CSharp/DotNetHostExport.h` and `Urho3D/CSharp/DotNetPlugin.h`.
- The imported target `SharpCpp::RbfxHost`.
- The nethost runtime library next to the host library, when the host library is shared.
  On Linux and macOS the host library finds it through its own run-time path. On Windows the
  consumer must put `nethost.dll` next to its executable.
- `share/SharpCppRbfx/cmake/FindNetHost.cmake` (hostfxr mode only). The package file uses it for a static host library.
- `share/SharpCppRbfx/cmake/RbfxCSharpDotNet.cmake` with `rbfx_csharp_add_dotnet_project()` and `rbfx_csharp_add_plugin_build()`.
- `share/SharpCppRbfx/cmake/RbfxCSharpPlugin.cmake` with `rbfx_csharp_add_plugin()`.
- `share/SharpCppRbfx/cmake/RbfxCSharpLauncher.cmake` with `rbfx_csharp_add_launcher()`.
- `share/SharpCppRbfx/cmake/RbfxCSharpData.cmake` with `rbfx_csharp_add_data()`.
- `share/SharpCppRbfx/cmake/RbfxCSharpApplication.cmake` with `rbfx_csharp_package_application()`.
- `share/SharpCppRbfx/cmake/RbfxCSharpProject.cmake` with `rbfx_csharp_add_project()`, `rbfx_csharp_install_application()` and `rbfx_csharp_find_core_data()`.
- `share/SharpCppRbfx/cmake/RbfxCSharpNewProject.cmake` and the folder `share/SharpCppRbfx/cmake/project` (see "New project").
- The native bindings library `SharpCpp.Rbfx.Native` with the imported target `SharpCpp::RbfxNative`. This part is in a source-mode build, for the shared and for the static library type.
  The install step copies the engine shared library and the other runtime files of the bindings next to it (`lib` on Linux and macOS, `bin` on Windows).
  The bindings finds them through its own run-time path.
- The binding metadata `SharpCpp.Rbfx.Bindings.json` and `SharpCpp.Rbfx.ParseUmbrella.hpp` in `share/SharpCppRbfx/metadata`.
- The package file `SharpCppRbfxConfig.cmake`.

- The managed assemblies `SharpCpp.Rbfx.Bindings`, `SharpCpp.Rbfx.NativeHost`, `SharpCpp.Support`, `SharpCpp.Std`, `SharpCpp.Ea`, `SharpCpp.Rbfx.SourceGenerator` and `SharpCpp.Support.SourceGenerator` in `share/SharpCppRbfx/managed`.
- The user-code generator in `share/SharpCppRbfx/tools/usercode`. Run it with `dotnet <prefix>/share/SharpCppRbfx/tools/usercode/Rbfx.UserCode.Generator.dll`.
  It needs a .NET runtime on the machine. The install step copies the files that the generator needs from the output folder of `dotnet build`.
- The SharpCpp headers in `include/SharpCpp` (`Marshaller.hpp` and its includes, and the `detail` marshaller headers of this repository).
  They are the input of `--sharpcpp-include` and `--rbfx-include`.
- `share/SharpCppRbfx/cmake/SharpCppGeneratedStaleness.cmake`.
- `share/SharpCppRbfx/cmake/RbfxCSharpAddBindings.cmake` with `rbfx_csharp_add_bindings()`, its templates and its helper modules
  (`ExportRbfxSdkParseFlags.cmake`, `SharpCppOutput.cmake`). The package file includes it in a source-mode build with a shared library type.
- The builtin Clang headers of the generator in `share/SharpCppRbfx/clang-resource/include`.

The managed files come from the output folder of `dotnet build SharpCpp.Rbfx.slnx` for the same configuration.
The install step does not build them. It stops with a message when a file is missing.

In a consumer project, `rbfx_csharp_add_bindings()` works with the installed SDK and needs no source tree.
The SDK installs `share/SharpCppRbfx/cmake/sdk/SharpCppRbfxUserCode.props` and `.targets`.
`find_package(SharpCppRbfx)` sets `RBFX_CSHARP_MSBUILD_PROPS`, `RBFX_CSHARP_MSBUILD_TARGETS` and `RBFX_CSHARP_MSBUILD_PROPERTIES` to these files and to the managed folder of the SDK, unless the consumer sets them before.
The props file sets the compiler options, compiles the generated sources and references `SharpCpp.Rbfx.Bindings`, `SharpCpp.Support`, `SharpCpp.Std` and `SharpCpp.Ea` from the managed folder.
An SDK that was built with the bindings package version has no managed folder. Then `rbfx_csharp_add_bindings()` stops with an error message.
In a consumer project, the target of `rbfx_csharp_add_plugin_build()` is not part of the default build. Add it with `add_dependencies()`.
The install step needs a finished build, because the generator writes the metadata files and the build links the bindings.

### Project in one call

`rbfx_csharp_add_project()` is the shortest way to make an application. It calls `rbfx_csharp_add_bindings()` (with `HEADERS`), `rbfx_csharp_add_plugin()`,
`rbfx_csharp_add_launcher()`, `rbfx_csharp_add_data()`, `rbfx_csharp_package_application()` and, on request, `rbfx_csharp_install_application()`.

```cmake
rbfx_csharp_add_project(MyApp
    [SOURCES Main.cpp] [CSHARP_DIR <dir>] [CSPROJ <csproj>]
    [HEADERS <headers...>] [INCLUDE_DIRECTORIES <dirs...>] [IMPLEMENTATION <target>] [SEPARATE_LIBRARY] [NAMESPACE <namespace>]
    [BINDINGS_TARGETS <targets...>]
    [PLUGINS <targets...>] [LINK_LIBRARIES <libraries...>] [DATA <dirs...>] [RESOURCES <files...>]
    [APPLICATION_ID <id>] [CONSOLE] [PROPERTIES <Name=Value>...]
    [INSTALL] [INSTALL_DESTINATION <dir>])
```

- A relative path is relative to `CMAKE_CURRENT_SOURCE_DIR`.
- With `HEADERS` the function calls `rbfx_csharp_add_bindings(MyApp.Bindings ...)`. The plugin `MyApp.Net` (or the name of the `CSPROJ` file) references this target.
  `INCLUDE_DIRECTORIES` is the list that you give, or the folders of `HEADERS`. `IMPLEMENTATION` is the target that implements the headers
  (by default the generated code is a source of this target; `SEPARATE_LIBRARY` makes the library `MyApp.Bindings.Native` instead and links
  the target to it). `NAMESPACE` is the C# namespace of the bindings.
- `BINDINGS_TARGETS` is a list of bindings targets that `rbfx_csharp_add_bindings()` already made (for example from other plugins). The plugin
  references them after `MyApp.Bindings`, and the packaging and the install rules use them in the same way.
- `DATA` goes to `rbfx_csharp_add_data(MyApp.Data DIRECTORIES ...)`. The data target is always made, so CoreData is also packed without `DATA`.
  On Emscripten, Android and iOS the paks are in `${CMAKE_CURRENT_BINARY_DIR}/MyApp.Data.Resources/`.
- `SOURCES`, `PLUGINS`, `LINK_LIBRARIES` and `CONSOLE` go to `rbfx_csharp_add_launcher()`. `CSHARP_DIR`, `CSPROJ` and `PROPERTIES` go to
  `rbfx_csharp_add_plugin()`. `RESOURCES` and `APPLICATION_ID` go to `rbfx_csharp_package_application()`.
- With `INSTALL` the function calls `rbfx_csharp_install_application(MyApp [DESTINATION <INSTALL_DESTINATION>])`.

`rbfx_csharp_install_application(<name> [DESTINATION <dir>])` adds the install rules of an application.
The default destination is `bin/$<CONFIG>`. If the target is not from `rbfx_csharp_package_application()`, CMake stops with an error.

- Desktop: the executable, the shared engine library, the native bindings, the shared host library with its nethost library, the shared libraries of the bindings targets and of the native plugins,
  the plugin assembly with its `.runtimeconfig.json` and `.deps.json` files, the assemblies of the bindings targets and the `SharpCpp.*.dll` files.
  In shim mode the install also has the NativeAOT head library `<plugin>.Head<suffix>` and has no nethost library.
  The installed executable has the run-time path `$ORIGIN` (`@loader_path` on macOS), so it finds the engine library in its folder.
  The function also installs each folder of the data target (the `CoreData` folder is one of them, when `rbfx_csharp_find_core_data()` finds it) in the same destination.
  It installs the `ResourceRoot.ini` of the data target there. The file maps `CoreData` and each folder of `DATA` to the installed folders.
- Android: the APK. Web: the web folder as `<destination>/<name>`. iOS: the app bundle.

`rbfx_csharp_find_core_data(<out_var>)` sets `<out_var>` to the folder `CoreData` of the rbfx SDK.
It uses `${Urho3D_PACKAGE_ROOT}/bin/CoreData`. If this folder does not exist, it looks for `bin/CoreData` in the folders of `CMAKE_PREFIX_PATH` (the host SDK).
Without a result the value is `<out_var>-NOTFOUND`.

### New project

The script `cmake/RbfxCSharpNewProject.cmake` makes a new application project from the templates in `cmake/project/`.
The script works from the source tree and from the SDK (`share/SharpCppRbfx/cmake`).

```
cmake -DNAME=<Name> -DDESTINATION=<dir> -P <path>/RbfxCSharpNewProject.cmake
```

- `NAME` starts with a letter. It has only letters, digits and underscores. The plugin is `<Name>.Net`.
- `DESTINATION` must not exist or must be empty. Otherwise the script stops with an error.
- The script copies each file of `cmake/project/` and removes the suffix `.in`. It replaces `@NAME@` in the file.
  `CSharp/Plugin.cs` becomes `CSharp/<Name>Plugin.cs`.
- The project has `CMakeLists.txt` (one call to `rbfx_csharp_add_project(<Name> DATA Data INSTALL)`), the plugin class
  `<Name>Plugin` in `CSharp/`, `Data/Readme.txt`, `ResourceRoot.ini`, `.gitignore` and `README.md`.
- The `CMakeLists.txt` of the project writes `build/ResourceRoot.ini`. This file maps `CoreData` to the folder of the
  engine SDK (`rbfx_csharp_find_core_data()`) and `Data` to the folder `Data` of the project.
  The engine uses the first `ResourceRoot.ini` above the executable.
- Configure the project with `-DCMAKE_PREFIX_PATH="<engine SDK>;<SharpCpp.Rbfx SDK>"` and build it with `cmake --build`.
  The executable is in `build/bin`.

### Plugin in one call

`rbfx_csharp_add_plugin()` makes a C# plugin project. It does not build the plugin: the packaging step of the application builds it.

```cmake
rbfx_csharp_add_plugin(MyPlugin
    [CSHARP_DIR <dir>] [CSPROJ <csproj>] [REFERENCES <bindings targets...>] [PROPERTIES <Name=Value>...])
```

- The call makes the target `MyPlugin` (`add_custom_target`). The name of the target is the assembly name and the root namespace.
- `CSHARP_DIR` is the folder with the C# sources. The default is `${CMAKE_CURRENT_SOURCE_DIR}/CSharp`. If the folder does not exist, CMake stops with an error.
- `CSPROJ` is a project that you maintain. Do not use it together with `CSHARP_DIR`. The project imports `$(SharpCppRbfxPluginProps)`.
- `REFERENCES` are the targets from `rbfx_csharp_add_bindings()`. The plugin references the managed project of each target.
- The function writes the project files to `${CMAKE_CURRENT_BINARY_DIR}/csharp/MyPlugin/` (see "Application in one call").
- Target properties: `SHARPCPP_RBFX_PLUGIN_NAME`, `SHARPCPP_RBFX_PLUGIN_PROJECT`, `SHARPCPP_RBFX_PLUGIN_REFERENCES` and `SHARPCPP_RBFX_PLUGIN_MSBUILD_PROPERTIES`.

### Launcher in one call

`rbfx_csharp_add_launcher()` makes the native target that hosts .NET. It does not know the plugin.

```cmake
rbfx_csharp_add_launcher(MyApp
    [SOURCES <files...>] [PLUGINS <native plugin targets...>] [LINK_LIBRARIES <libraries...>] [CONSOLE])
```

- The target is an executable on desktop, a shared library on Android, an object library on the web (with `web_executable()`) and a static library on iOS.
- Without `SOURCES` the function uses the default launcher `cmake/launcher/RbfxCSharpLauncher.cpp`. It needs the target `PlayerLibrary`.
- The function links `Urho3D`, `SharpCpp::RbfxHost` and `LINK_LIBRARIES`. It calls `target_link_plugins(MyApp <PLUGINS>)`, also with an empty list for the default launcher.
- It does not define `SHARPCPP_RBFX_PLUGIN_NAME`. `rbfx_csharp_package_application()` defines it.
- Target properties: `SHARPCPP_RBFX_LAUNCHER` (`ON`), `SHARPCPP_RBFX_PLUGINS` and `SHARPCPP_RBFX_LAUNCHER_LINK_LIBRARIES`.

### Editor in one call

`rbfx_csharp_add_editor()` makes the rbfx editor with a .NET host. It is a launcher for the target `EditorLibrary`. Desktop only.

```cmake
rbfx_csharp_add_editor(MyEditor
    [SOURCES <files...>] [PLUGINS <native plugin targets...>] [LINK_LIBRARIES <libraries...>] [CONSOLE])
```

- Without `SOURCES` the function uses the default editor `cmake/launcher/RbfxCSharpEditor.cpp`. It is an `EditorApplication` that starts the .NET runtime.
- The function builds the bootstrap plugin `SharpCpp.Rbfx.EditorHost` next to the executable. It registers each managed plugin
  `ManagedPlugins/*.dll` (in the folder of the executable) for each new `PluginManager`, that is, each time a project opens.
- `PLUGINS` and `LINK_LIBRARIES` are as in `rbfx_csharp_add_launcher()`. `EditorLibrary` is always linked. `CONSOLE` builds a console program on Windows.
- If the SDK has no `EditorLibrary` target, the function prints a status message and does nothing. `eng/fetch-rbfx-sdk.sh` warns when the editor headers are missing.
  On Android, iOS and Emscripten CMake stops with an error.

### Data in one call

`rbfx_csharp_add_data()` makes the data target of an application: the resource folders.

```cmake
rbfx_csharp_add_data(MyData [DIRECTORIES <dirs...>] [NO_CORE_DATA])
```

- The call makes the target `MyData` (`add_custom_target`) in the folder of the call. A relative path is relative to `CMAKE_CURRENT_SOURCE_DIR`.
- The function adds the `CoreData` folder of the rbfx SDK (see `rbfx_csharp_find_core_data()`) as the first folder. `NO_CORE_DATA` stops this.
  If CoreData is not found, desktop skips it. On Emscripten, Android and iOS CMake stops with an error.
  Two folders with the same name are an error. A data target without a folder is an error.
- On Emscripten, Android and iOS the function packs each folder into `${CMAKE_CURRENT_BINARY_DIR}/MyData.Resources/<folder name>.pak` with `create_pak()`.
  The target depends on the paks. `create_pak()` makes custom commands, and only a target of the same folder can run them.
  So call the function in the folder where you want the paks.
- On desktop there is no pak. The function writes `${CMAKE_CURRENT_BINARY_DIR}/MyData.Install/ResourceRoot.ini`
  with one `<folder>=<folder>` line for each folder. The install rules copy the folders and this file.
- Target properties: `SHARPCPP_RBFX_DATA_DIRECTORIES` (the absolute paths of the folders, CoreData first) and `SHARPCPP_RBFX_DATA_FILES`
  (the paks, or the `ResourceRoot.ini` on desktop).

### Application in one call

`rbfx_csharp_package_application()` packages a launcher and a C# plugin as an application that hosts .NET.
`rbfx_csharp_add_launcher()`, `rbfx_csharp_add_plugin()` and `rbfx_csharp_add_data()` make the parts. The desktop platforms (hostfxr mode), Android, iOS and the web (shim mode) are supported now.

```cmake
find_package(Urho3D REQUIRED)
find_package(SharpCppRbfx CONFIG REQUIRED)
rbfx_csharp_add_bindings(MyApp.Bindings HEADERS <headers...> ...)
rbfx_csharp_add_plugin(MyApp.Net [CSHARP_DIR <dir>] REFERENCES MyApp.Bindings)
rbfx_csharp_add_launcher(MyApp [SOURCES Main.cpp] [PLUGINS <targets...>] [LINK_LIBRARIES <libraries...>])
rbfx_csharp_add_data(MyApp.Data DIRECTORIES Data)
rbfx_csharp_package_application(MyApp
    PLUGIN MyApp.Net
    [DATA MyApp.Data] [RESOURCES <files or folders...>] [APPLICATION_ID <id>] [PROPERTIES <Name=Value>...])
```

- Call `rbfx_csharp_package_application()` in the source folder of the launcher. Else CMake stops with an error. The function defers calls
  to the end of the folder (`cmake_language(DEFER DIRECTORY)`), and they must run in the folder of the launcher.
- `PLUGIN` is the target from `rbfx_csharp_add_plugin()`. The plugin name is the assembly name. The function defines
  `SHARPCPP_RBFX_PLUGIN_NAME` (the assembly name as a string) for the launcher. The plugin files
  (`<name>.dll` and `<name>.runtimeconfig.json`) are in the folder of the executable.
- `DATA` is the target from `rbfx_csharp_add_data()`. On Android, Emscripten and iOS the paks are resources of the head, and the head depends on the target.
- `RESOURCES` are more files and folders for the head. They are ignored on desktop. A generated file must come from a custom command in the folder of the launcher.
  `APPLICATION_ID` is ignored on desktop and on the web. Android and iOS use it.
- `PROPERTIES` are more MSBuild properties for the build of the plugin. They come after the properties of the plugin target.
- The function reads the bindings targets from the property `SHARPCPP_RBFX_PLUGIN_REFERENCES` of the plugin, and the native plugins from `SHARPCPP_RBFX_PLUGINS` of the launcher.
  On desktop it copies the shared library of each bindings target and the shared or module library of each native plugin to the folder of the executable.
  On Android the head packs the shared and module targets of the native plugins (in the given order) and then the shared libraries of the bindings targets.
  They are before `libMyApp.so` and without duplicates.
- It sets the target properties `SHARPCPP_RBFX_PACKAGED_PLUGIN` (the plugin target), `SHARPCPP_RBFX_PACKAGED_DATA` (the data target)
  and `SHARPCPP_RBFX_APPLICATION_OUTPUT` (the product of the application) on `MyApp`.
  The value of the last one is `<name>.apk` in the library output folder on Android, the web folder on the web and `<name>.app` on iOS.
  On desktop the value is empty.
- Without `SOURCES`, `rbfx_csharp_add_launcher()` uses the default launcher `cmake/launcher/RbfxCSharpLauncher.cpp`
  and adds `PlayerLibrary` to `LINK_LIBRARIES`. The target `PlayerLibrary` must exist. If it does not, CMake stops with an error.
  The launcher is a `PlayerApplication`. In `Setup` it sets the resource prefix paths and the orientation of mobile devices.
  It sets the engine plugins to the linked plugins and the managed plugin.
  In `Start` it calls `StartDotNetPlugin()` (header `Urho3D/CSharp/DotNetPlugin.h`). If this call fails, the launcher logs the error and calls `ErrorExit`.
  `Stop` closes the .NET host.
- The source file uses `URHO3D_DEFINE_APPLICATION_MAIN(Class)` as in any rbfx application.
- The target `MyApp.CSharp` is part of the default build. It builds the plugin with dotnet and copies the native bindings
  next to the executable. On Windows it also copies the host library and `nethost.dll`.
- On Linux the executable links with `--no-as-needed`, so Urho3D stays a start-up dependency.
- The plugin gets the MSBuild properties that it needs for source mode and for SDK mode (see `rbfx_csharp_add_plugin()`).

#### Android

The same calls, the same launcher source and the same plugin project make an APK. The Android build has no hostfxr.
A .NET Android head is the entry of the application. The native application runs as a library in the SDL activity.

- Configure with the NDK toolchain (for example `cmake/toolchains/android-arm64.cmake`), a single-configuration
  generator (Ninja), `-DBUILD_SHARED_LIBS=ON` and the Android SDKs of the engine and of SharpCpp.Rbfx on `CMAKE_PREFIX_PATH`.
  The NDK toolchain finds packages only in its sysroot. Add `-DCMAKE_FIND_ROOT_PATH_MODE_PACKAGE=BOTH`.
- `MyApp` is a shared library (`libMyApp.so`). `URHO3D_DEFINE_APPLICATION_MAIN` exports `SDL_main` from it.
- The function generates the head project in `<build>/heads/MyApp/` from the templates in `cmake/heads/android/`.
  The head is `net10.0-android`. It references the plugin project. It packs `libc++_shared.so`, `libUrho3D.so`, the bindings,
  the host library, the shared libraries in `LINK_LIBRARIES` and `libMyApp.so`. It compiles the SDL Java sources of the engine SDK.
- A file in `RESOURCES` or in the paks of `DATA` becomes an asset with its file name. A folder becomes a folder of assets.
- `APPLICATION_ID` is the package name. The default is `org.rbfx.<name in lower case>`.
- The application class of the head runs the module initializer of the bindings before the activity starts `SDL_main`.
  The initializer attaches the managed function table to the host library. `DotNetHost::Initialize` and `LoadPlugin`
  then work as on desktop. The launcher needs no Android code.
- Only native code reaches the plugin class and some `[UnmanagedCallersOnly]` methods. The IL trimmer cannot see these calls.
  The head has no `TrimmerRootAssembly` items. `SharpCpp.Rbfx.SourceGenerator` writes the exact roots (`TrimRootsGenerator`):
  the class `SharpCpp.Rbfx.Generated.TrimRoots` of each assembly has a method `Keep` with `[DynamicDependency]` attributes.
  They keep each concrete `Plugin` class with its public constructors. They also name the first such class (by documentation id) that has a public constructor that takes a `Context` in the assembly attribute
  `[assembly: GeneratedPluginType(typeof(X))]` (`PluginModule` has priority), and `PluginRuntime.FindPluginType` reads it, so it never scans
  the assembly types. An assembly without the generator needs `PluginModule`. The roots keep each static `[UnmanagedCallersOnly]` method
  without an address-of use (native code finds it by name with `DotNetHost::GetFunctionPointer`). `Keep` also keeps `Keep`
  of each referenced assembly that has one. `Program.cs` of the head keeps `Keep` of the head assembly. A method with an
  address-of use stays with the managed code that gives its address to native code. ILLink and the NativeAOT compiler both read
  the attributes.
- The target `MyApp.CSharp` builds the head with dotnet and copies the debug-signed APK to `MyApp.apk` next to `libMyApp.so`.
  The head gets the configuration of CMake (`-c <config>`) and the list of configurations (`-p:Configurations=<list>`: CMAKE_CONFIGURATION_TYPES, else CMAKE_BUILD_TYPE).
  The workload tests only Debug and Release, so the head project gives each other configuration (for example RelWithDebInfo) the Release defaults of the workload: managed symbols, trimming and Mono AOT (the default runtime is MonoVM). ReadyToRun applies only with `UseMonoRuntime=false`.
- The head build needs the `android` workload and a JDK 17 or 21. The cache variables `RBFX_CSHARP_ANDROID_SDK_DIR`
  and `RBFX_CSHARP_JAVA_SDK_DIR` (defaults: the environment variables `ANDROID_HOME` and `JAVA_HOME`) go to dotnet.
  `RBFX_CSHARP_ANDROID_JAVA_DIR` sets the folder with the SDL Java sources, if the engine SDK does not have them.

#### Web

The same calls, the same launcher source and the same plugin project make a web application (Emscripten, `browser-wasm`).
The browser has no hostfxr. A .NET WebAssembly head is the entry. The native application is static code in the runtime.

- Configure with the Emscripten toolchain, a single-configuration generator (Ninja), the static web SDK of the engine
  and the web SDK of SharpCpp.Rbfx on `CMAKE_PREFIX_PATH`. Use the emsdk version of the `wasm-tools` workload of .NET.
- `MyApp` is an object library. The sources compile with the definition `main=rbfx_application_main`, because the
  .NET runtime defines `main`. `URHO3D_DEFINE_APPLICATION_MAIN` then defines `rbfx_application_main`. The function adds
  `cmake/heads/wasm/RbfxWebMain.cpp`, which exports it as the C function `rbfx_application_start`.
  A source file of `MyApp` must not use the name `main` for other things.
- The function generates the head project in `<build>/heads/MyApp/` from the templates in `cmake/heads/wasm/`.
  The head is `net10.0` with the SDK `Microsoft.NET.Sdk.WebAssembly`, threads and WebAssembly exceptions. It references the plugin project.
- At the end of the configure step, the function walks the link closure of `MyApp` (also `target_link_libraries` calls
  after the function) and of `SharpCpp::RbfxNative`. It writes `NativeItems.props` of the head: every static library and
  object is a `NativeFileReference`, and the flags and the `INTERFACE_LINK_OPTIONS` go to `EmccExtraLDFlags`.
  The head sets the exception mode, so the function removes `DISABLE_EXCEPTION_CATCHING` from the flags.
  A shared library in the closure is a configure error.
- The head references `SharpCpp.Rbfx.NativeHost`. The generated trimmer roots keep what only native code reaches, as on Android.
  A source build compiles the bindings with `RbfxTargetRid=browser-wasm`. Then their import names are the file names of
  `libSharpCpp.Rbfx.Native.a` and `libSharpCpp.Rbfx.Host.a`, and the runtime links the imports statically.
- A file in `RESOURCES` becomes a file in the root folder of the Emscripten file system. A folder becomes a folder with its name.
  The head build copies them next to `index.html` and writes the list `resources.json`. `main.js` loads the files
  into the file system before the engine starts.
- The target `MyApp.CSharp` runs `dotnet publish -c Release` for the head and copies the web folder to `MyApp/` in
  `CMAKE_RUNTIME_OUTPUT_DIRECTORY` (else the binary folder). The folder has `index.html`, `main.js`, `_framework` and the resources.
  The page has the canvas with the id `canvas`.
- The runtime uses threads (`SharedArrayBuffer`). The web server must send `Cross-Origin-Opener-Policy: same-origin` and
  `Cross-Origin-Embedder-Policy: require-corp`.

Why `main.js` calls the native entry, and not `Program.Main`: the threaded .NET runtime runs `Program.Main` on a worker
(the deputy thread), but SDL, WebGL and the input events of the canvas work on the browser thread. Also,
`Application::Run` calls `emscripten_set_main_loop_arg` with `simulate_infinite_loop`. Emscripten then throws a JavaScript
exception through all frames to leave the native code, and managed frames must not be on that stack. So `Program.Main`
only runs the module initializer of `SharpCpp.Rbfx.NativeHost` (the attach to the host library) and returns. Then `main.js` calls
`Module._rbfx_application_start()` on the browser thread and ignores the `'unwind'` exception. The native code calls the
managed code on the browser thread after that. `main.js` sets `jsThreadBlockingMode` to `DangerousAllowBlockingWait`, because
managed code can wait on a lock there.

#### iOS

The same calls, the same launcher source and the same plugin project make an iOS app bundle (`ios-arm64`, devices only).
An iOS app has one executable, and the .NET iOS SDK links it. So a .NET iOS head is the entry, and the native application
is static code in the executable of the app.

- Configure on macOS with the toolchain file of the engine SDK (`-DCMAKE_TOOLCHAIN_FILE=<engine SDK>/share/Urho3D/CMake/Toolchains/IOS.cmake
  -DPLATFORM=OS64 -DDEPLOYMENT_TARGET=15.0`, which sets `CMAKE_SYSTEM_NAME` to `iOS`), a single-configuration
  generator (Ninja), the static iOS SDK of the engine (`ios-clang-arm64-lib`) and the iOS SDK of SharpCpp.Rbfx on `CMAKE_PREFIX_PATH`.
  The head build needs Xcode and the `ios` workload of .NET. The simulator is not supported.
- `MyApp` is a static library (`libMyApp.a`). On iOS `URHO3D_DEFINE_APPLICATION_MAIN` defines `SDL_main`, not `main`.
  The function adds `cmake/heads/ios/RbfxIosMain.cpp`. It exports the C function `rbfx_application_start`, which calls
  `SDL_UIKitRunApp(argc, argv, SDL_main)`. The source needs no iOS code.
- The function generates the head project in `<build>/heads/MyApp/` from the templates in `cmake/heads/ios/`.
  The head is `net10.0-ios` with the RID `ios-arm64`. It references the plugin project. `SupportedOSPlatformVersion` is
  `CMAKE_OSX_DEPLOYMENT_TARGET` (default 15.0). The manifest is `cmake/heads/ios/Info.plist` (full screen, no launch storyboard).
- `Program.Main` of the head runs the module initializer of `SharpCpp.Rbfx.NativeHost` (the attach to the host library).
  Then it calls `rbfx_application_start` through `[DllImport("__Internal")]`. SDL runs `UIApplicationMain` with its own
  application delegate and calls `SDL_main` on the main thread. There is no `dlsym` and no shared library.
- At the end of the configure step, the function walks the link closure of `MyApp` (also `target_link_libraries` calls
  after the function), of `SharpCpp::RbfxNative` and of the bindings targets. It writes `NativeItems.props` of the head: every
  static library is a static `NativeReference`. The flags and the `INTERFACE_LINK_OPTIONS` go to `LinkerFlags` of `libMyApp.a`.
  An object library gives no file, because the archive of the static library that links it has its objects.
  A shared library in the closure is a configure error.
- `libMyApp.a`, the bindings, the host library and the libraries of the bindings targets have `ForceLoad`: the managed side finds their
  exports with `__Internal`. The engine libraries do not have `ForceLoad`. The engine archive has the `main` of SDL, and the
  executable has the `main` of .NET.
- The generated trimmer roots keep what only native code reaches, as on Android. A source build compiles the
  bindings with `RbfxTargetRid=ios-arm64`, so their import name is `__Internal`.
- A file in `RESOURCES` becomes a file in the root folder of the app bundle (`BundleResource`). A folder becomes a folder with its name.
- `APPLICATION_ID` is the bundle identifier. The default is `org.rbfx.<name in lower case>`.
- The target `MyApp.CSharp` builds the head with dotnet and copies the app bundle to `MyApp.app` in `CMAKE_RUNTIME_OUTPUT_DIRECTORY`
  (else the binary folder). The head gets the configuration of CMake (`-c <config>`) and the list of configurations
  (`-p:Configurations=<list>`). The head project is not verified for configurations other than Debug and Release: the iOS workload is not
  installed in the test environment, so check the properties of the workload that test `$(Configuration)`.
- The head does not sign the app by default. A device runs a signed app only. Give `EnableCodeSigning=true`, `CodesignKey`
  and `CodesignProvision` in `PROPERTIES` or in `RBFX_CSHARP_MSBUILD_PROPERTIES`.

Generated files of an iOS application `MyApp`:

| File | Content |
| --- | --- |
| `<build>/heads/MyApp/MyApp.Head.csproj` | The head project, from `cmake/heads/ios/Head.Ios.csproj.in`. It compiles `cmake/heads/ios/Program.cs` and has the `BundleResource` items. |
| `<build>/heads/MyApp/NativeItems.props` | The `NativeReference` items and the link flags. CMake writes it at the end of the configure step. |
| `libMyApp.a` | The application library: `SOURCES` and `cmake/heads/ios/RbfxIosMain.cpp`. |
| `MyApp.app` | The app bundle, after the target `MyApp.CSharp`. |

#### Desktop shim head (NativeAOT)

With `-DRBFX_HOST_MODE=shim` on Linux, Windows or macOS, `rbfx_csharp_package_application` publishes a NativeAOT head instead of the plugin build.
The native launcher keeps `main`. There is no hostfxr and no `runtimeconfig.json`.

- The head project is `<build>/heads/MyApp/MyApp.Head.csproj`, from `cmake/heads/desktop/Head.Desktop.csproj.in` and `Program.cs`.
- `dotnet publish` compiles the head, the plugin and the bindings into one shared library. The target `MyApp.Head` copies it next to the executable as `<plugin>.Head.so`, `.dylib` or `.dll` (no `lib` prefix).
- `StartDotNetPlugin` gives that path to `DotNetHost::Initialize`. The host loads the library and calls the export `sharpcpp_rbfx_head_attach`, which calls `NativeHostBootstrap.Attach()`.
- `Program.cs` has the partial hooks `OnManagedStartup` (before the attach) and `OnManagedShutdown` (in `sharpcpp_rbfx_head_detach`).
- The head finds `SharpCpp.Rbfx.Host` and `SharpCpp.Rbfx.Native` in the folder of the executable with the default NativeAOT probing.
- `eng/run-aot-test.sh --rid linux-x64` builds and runs the test project `SharpCpp.Rbfx.Tests.Aot` headless.
- The head has no `TrimmerRootAssembly` items. The NativeAOT compiler roots a whole assembly for each such item (it reads only `RootMode="EntryPoint"`, as no root). The generated trimmer roots (`SharpCpp.Rbfx.Generated.TrimRoots`, see the Android head) keep what only native code reaches. `Program.cs` keeps them with `[DynamicDependency]` on `sharpcpp_rbfx_head_attach`.

The generated registry is the only path for managed types and attributes. There is no reflection scan:

- In the source tree, `Directory.Build.targets` adds `SharpCpp.Rbfx.SourceGenerator` and `SharpCpp.Support.SourceGenerator` as analyzers to each project with a `ProjectReference` to `SharpCpp.Rbfx.Bindings`. A project that lists a generator keeps it, and the rule adds the other one. `SharpCpp.Rbfx.Bindings`, the head templates and `cmake/sdk/SharpCppRbfxPlugin.props` list the generators themselves. The target `RbfxCheckSourceGenerators` runs before `CoreCompile` and stops the build with error SCPPSG002 if a project that uses `SharpCpp.Rbfx.Bindings` has no generator. A project that has no generator on purpose sets the MSBuild property `SharpCppRbfxSkipGeneratorCheck` to `true`. The rule and the check skip such a project.
- `SharpCpp.Support.SourceGenerator` is required in each plugin and head project with a class that overrides a virtual method of a native type. It emits `IsManagedOverride` for the class. Such a class, and each type that contains it, must be `partial` (error SCPPSG001).
- The RTTI overrides that `SharpCpp.Rbfx.SourceGenerator` emits are not visible to the other generator. The RTTI generator lists their slots in `Object.IsGeneratedManagedOverride`, which `Object.IsManagedOverride` reads.
- `SharpCpp.Rbfx.SourceGenerator` is required in each plugin and head project. It adds a sealed `ManagedTypeRegistryAttribute` to each assembly with managed object types, and to each assembly with a `ManagedPlugin` (the list can be empty).
- The attribute lists each concrete managed type with a direct generic call to its factory registration.
- `ManagedPlugin` uses the registry of its assembly on every runtime (JIT with hostfxr or the editor, and NativeAOT), so both behave the same. `ShouldRegister` can still reject a type of the registry. There is no `GetManagedTypes`: the registry is the list.
- An assembly without the attribute (the generator is not in the project) is an error. `RegisterManagedPlugin` and `LoadManagedPlugin` throw an `InvalidOperationException` that tells you to reference the generator. A plugin that the host creates in another way logs the same error in `Load` and registers no type, because a managed exception in a native `Load` ends the process.
- The registry also lists the `[SerializeField]` members of each type as attribute descriptors (getter, setter and default delegates). The registry path finds and reads the members without reflection.
- A private or protected member is reached with `[UnsafeAccessor]` methods that the generator writes into the registry file (static extern accessors; a field uses `UnsafeAccessorKind.Field`, a property uses its `get_`/`set_` methods; a generic type uses the generic form, .NET 9 and later). A private setter of a public property uses an accessor too.
- The default value of an attribute comes from the declaration: `[SerializeField] float Speed = 2.5f;`. The generator reads the initializer (or `default(T)` without one) and emits it as a `static () => ...` delegate, so the registry path needs no temporary instance.
- If a listed base type is not registered (`ShouldRegister` returns false, or the registration fails), the derived type is an error at run time and is skipped, because its list has no base members.
- A type can derive from a managed type of another assembly that the generated registry of that assembly lists (for example plugin B has `Boss : PluginA.Enemy`). That type is concrete, not generic and accessible, has a public constructor with a `Context` parameter, and is not a `Plugin`. The generator stops the member list at the nearest base type that the registry lists. A base type that the registry does not list (for example an abstract type) is no stop: the generator reads its members, and a `[SerializeField]` member in it is an error. The registry of B lists only the members of `Boss` and the members between `Boss` and `Enemy`. At run time the plugin of A must be loaded first, because the engine reflection of `Enemy` gives the base attributes to `Boss`. Without it, `Boss` is not registered and the plugin logs an error that names the base type. Plugins that reference each other share one copy of the referenced plugin assembly (an assembly with a type registry or a `PluginModule`); the other dependencies stay private to each plugin, so the types of A are the same in A and in B.
  - An unsigned plugin assembly is matched by its simple name. A nested or manual-RTTI foreign base type that its registry lists is matched at run time by its type name.
- The generator reports an error for each member or type that it cannot describe. The build fails, and the message tells how to fix it:
  - RBFXSG003: a managed type that the registry cannot name, because it or a containing type is private or protected. Make it internal or public.
  - RBFXSG004: a `[SerializeField]` member that it cannot list. The cases are a readonly or init-only member, a property without a getter and a setter, an indexer, an attribute on a backing field, an unsupported value type, a private enum type, a private or protected member of a private or protected declaring type, a private or protected virtual member (`UnsafeAccessor` cannot call it), a private or protected field of a generic type whose type uses a type parameter (the runtime does not find it through `UnsafeAccessor`), a private or protected property of a generic type (the runtime does not find the `get_` and `set_` accessors of a generic type; use a field, or make the property public or internal), a private or protected member of a generic type with constraints, a member of a base type in another assembly that has no generated registry or whose base type its registry does not list (see below), and a default that the generator cannot emit.
  - A default is not emittable when a constructor assigns the member, when the initializer needs the instance, a lambda, an object or collection initializer, an extension method, a ref or out argument, or a private or protected symbol, or when the member is a virtual, abstract or partial property or not an auto-property. Give a literal, a constant or a static initializer, and do not assign the member in a constructor.
- Call `ManagedTypeRegistries.Add(typeof(MyType).Assembly)` in `OnManagedStartup` to register the types of another assembly, for example the head assembly.
- The first plugin that loads registers these types with the assembly name as category. The last plugin that unloads removes them. This applies to both runtimes.

Customize the head with `HEAD_SOURCES`:

- `rbfx_csharp_package_application(MyApp PLUGIN MyPlugin HEAD_SOURCES Head/MyHead.cs)` adds C# files to the head assembly. Each file becomes a `<Compile>` item of the head project. A relative path is relative to the current source folder.
- The head project has the SharpCpp.Rbfx source generator. A managed type in a `HEAD_SOURCES` file gets a factory, and the head assembly gets its own type registry.
- Every head declares `Program` as `partial` with the hooks `static partial void OnManagedStartup();` and `static partial void OnManagedShutdown();`. A hook without a body compiles to nothing. One `HEAD_SOURCES` file compiles on every platform.
- On desktop, `OnManagedStartup` runs in `sharpcpp_rbfx_head_attach` before `NativeHostBootstrap.Attach()`. `OnManagedShutdown` runs in `sharpcpp_rbfx_head_detach`.
- On iOS and web, `OnManagedStartup` runs in `Program.Main` after the native host module initializer and before the native start. On Android it runs in `RbfxApplication.OnCreate`. These three heads declare `OnManagedShutdown` but never call it. On Android, `Program.cs` of the head template declares the hooks and `RbfxApplication` calls `Program.RunStartup()`.
- The hooks run before the engine `Context` exists. A hook must not touch engine objects. Use it to register types and to set options.

```csharp
namespace Rbfx.Head;

internal static partial class Program
{
    static partial void OnManagedStartup() => Urho3D.Plugins.ManagedTypeRegistries.Add(typeof(Program).Assembly);
}
```

The iOS, web and Android heads are not built in CI. The hostfxr mode on desktop has no head, so it ignores `HEAD_SOURCES`.

### DotNetHost modes

`Urho3D::DotNetHost` has the same public API in two modes. The cache variable `RBFX_HOST_MODE` selects the mode.
The API uses the types of the engine: paths are UTF-8 `ea::string` (like in `FileSystem`), names are `ea::string_view`, and errors are `ea::string&`.
The host library links the `Urho3D` target in both modes.

- `hostfxr` is the default on desktop. `DotNetHost` loads hostfxr and starts the .NET runtime. This mode needs nethost.
- `shim` is the default on Android, iOS and Emscripten. `DotNetHost` never starts a runtime. The .NET head starts the runtime.
  The managed side pushes a function table (`SharpCppRbfxManagedHost`, header `Urho3D/CSharp/DotNetHostShim.h`) into the native
  host library through `sharpcpp_rbfx_attach_managed_host`. The managed side of this attach is `SharpCpp.Rbfx.NativeHost` (see below). The shim code is in `SharpCpp.Rbfx.Host` on every platform
  (source `Rbfx.Host/src/DotNetHostShim.cpp`), not in `SharpCpp.Rbfx.Native`. The NuGet runtime packages do not pack the host library.
  `SharpCpp::RbfxHost` is a real library of the type `RBFX_NATIVE_LIBRARY_TYPE` (shared on Android, static on iOS and Emscripten).
  It links `Urho3D` PUBLIC, because `ea::string` needs the engine allocator. It has the interface definition `URHO3D_DOTNETHOST_SHIM`. A static library also has `URHO3D_DOTNETHOST_STATIC`.
  `DotNetHost::Initialize` refers to the attach export, so the linker keeps it in a static program. No whole-archive link is needed.
  `DotNetHost::Initialize` returns false and gives an error text while no table is attached.
  `RBFX_INSTALL_SDK` works in both modes. The shim SDK has `SharpCpp::RbfxHost` (the host library and the headers `DotNetHost.h`,
  `DotNetHostExport.h`, `DotNetHostShim.h` and `DotNetPlugin.h`) and `SharpCpp::RbfxNative` (the bindings). It has no `FindNetHost.cmake`.
  A native application links `SharpCpp::RbfxHost`. A .NET head loads `SharpCpp.Rbfx.Host` next to `SharpCpp.Rbfx.Native`.
  `SharpCppRbfxConfig.cmake` sets `SharpCppRbfx_HOST_MODE` and `SharpCppRbfx_LIBRARY_TYPE`. It stops when the target `Urho3D` has another library type than the SDK.
  `DotNetHost::GetFunctionPointer` accepts only an empty `delegateTypeName` (an `[UnmanagedCallersOnly]` method).
  The module initializer of `SharpCpp.Rbfx.NativeHost` attaches the table. Native hosting on Android, iOS and web lives in
  `SharpCpp.Rbfx.NativeHost`; only heads reference it. The head touches the assembly before the native entry with
  `RuntimeHelpers.RunModuleConstructor(typeof(Urho3D.Plugins.NativeHostModule).Module.ModuleHandle)`. `SharpCpp.Rbfx.Bindings`
  does not reference it, and the NuGet packages do not contain it, so a NuGet-only app has no host symbols to link.

### SDK archives

`packaging/sdk/pack-sdk.sh` installs the SDK and packs it into a 7z archive. It needs `7z` and a finished build.
Configure the build tree with `-DRBFX_HOST_DEFAULT_DOTNET_ROOT=OFF`. The script works in Git Bash on Windows too.

```
packaging/sdk/pack-sdk.sh --build-dir <tree> --config <Config> --tag <platform tag> --out <dir>
```

The archive is `<dir>/sharpcpp-rbfx-sdk-<tag>-latest.7z`. It has one root folder with the same name (example tag: `linux-gcc-x64-dll`).

`packaging/sdk/check-consumer.sh` extracts an archive (or takes an extracted folder), then configures and builds the project
`packaging/sdk-consumer` against it and the rbfx SDK. It fails when the generated bindings source is missing.

```
packaging/sdk/check-consumer.sh --sdk-archive <7z> --rbfx-sdk <rbfx install root> --config <Config> --work <dir>
packaging/sdk/check-consumer.sh --sdk-dir <dir> --rbfx-sdk <rbfx install root> --config <Config> --work <dir>
```
A cross build does not install the engine runtime files.

CI builds the archive in the `Package` job for `linux-x64`, `osx-arm64` and `win-x64` (artifact `sdk-<rid>`).
The `SDK-Consumer` job checks it. The release jobs upload the `.7z` files to the release (`--clobber`). `uap-x64` has no archive.
