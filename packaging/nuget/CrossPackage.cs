#:property ImportDirectoryBuildProps=false
#:property ImportDirectoryBuildTargets=false

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

// .NET 10 file-based utility; toolchains are supplied by the caller, never installed implicitly.
try
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 1; i < args.Length; i += 2)
    {
        if (!args[i].StartsWith("--") || i + 1 >= args.Length) throw new ArgumentException("Expected --option VALUE.");
        options.Add(args[i][2..], args[i + 1]);
    }
    string Required(string name) => options.GetValueOrDefault(name) ?? throw new ArgumentException($"Missing --{name}.");
    switch (args.FirstOrDefault())
    {
        case "create": CrossPackage.Create(Required("rid"), Required("version"), options); break;
        case "targets": CrossPackage.Targets(Required("rid"), Path.GetFullPath(Required("stage"))); break;
        case "verify": CrossPackage.Verify(Required("rid"), Required("version"), options); break;
        default: throw new ArgumentException("Expected create, targets or verify. See packaging/nuget/README.md.");
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Error: {exception.Message}");
    return 1;
}
return 0;

static class CrossPackage
{
    static string SourcePath([CallerFilePath] string path = "") => path;
    static readonly string Repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "../.."));
    const string NdkVersion = "27.2.12479018";
    const string EmscriptenVersion = "3.1.56";

    public static void Create(string rid, string version, Dictionary<string, string> options)
    {
        var cmake = new List<string> { "-DRBFX_BUILD_USERCODE=OFF" };
        var environment = new Dictionary<string, string>();
        if (rid.StartsWith("android-", StringComparison.Ordinal))
        {
            var abi = rid switch { "android-arm" => "armeabi-v7a", "android-arm64" => "arm64-v8a", "android-x64" => "x86_64",
                _ => throw new ArgumentException($"Unsupported Android RID: {rid}") };
            var ndk = options.GetValueOrDefault("ndk-root") ?? Environment.GetEnvironmentVariable("ANDROID_NDK_HOME")
                ?? Environment.GetEnvironmentVariable("ANDROID_NDK_ROOT");
            if (ndk is null)
            {
                var sdk = Environment.GetEnvironmentVariable("ANDROID_HOME") ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
                if (sdk is not null) ndk = Path.Combine(sdk, "ndk", NdkVersion);
            }
            if (ndk is null) throw new ArgumentException($"Supply --ndk-root or install NDK {NdkVersion} under ANDROID_HOME.");
            var toolchain = Path.Combine(Path.GetFullPath(ndk), "build/cmake/android.toolchain.cmake");
            RequireFile(toolchain);
            cmake.AddRange([$"-DCMAKE_TOOLCHAIN_FILE={toolchain}", $"-DANDROID_ABI={abi}", "-DANDROID_PLATFORM=android-21", "-DANDROID_STL=c++_static", "-DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON"]);
        }
        else if (rid == "ios-arm64")
        {
            if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Building iOS packages requires macOS and Xcode.");
            var sdk = Run("xcrun", ["--sdk", "iphoneos", "--show-sdk-path"], capture: true).Trim();
            cmake.AddRange(["-DCMAKE_SYSTEM_NAME=iOS", "-DCMAKE_OSX_ARCHITECTURES=arm64", $"-DCMAKE_OSX_SYSROOT={sdk}", "-DCMAKE_OSX_DEPLOYMENT_TARGET=15.0"]);
        }
        else if (rid == "browser-wasm")
        {
            var emsdk = options.GetValueOrDefault("emsdk-root") ?? Environment.GetEnvironmentVariable("EMSDK")
                ?? throw new ArgumentException($"Supply --emsdk-root pointing to an activated Emscripten {EmscriptenVersion} SDK.");
            emsdk = Path.GetFullPath(emsdk);
            var toolchain = Path.Combine(emsdk, "upstream/emscripten/cmake/Modules/Platform/Emscripten.cmake");
            RequireFile(toolchain);
            environment["EM_CONFIG"] = Path.Combine(emsdk, ".emscripten");
            RequireFile(environment["EM_CONFIG"]);
            var emcc = Path.Combine(emsdk, "upstream/emscripten", OperatingSystem.IsWindows() ? "emcc.bat" : "emcc");
            var compilerVersion = Run(emcc, ["--version"], capture: true, environment: environment);
            if (!compilerVersion.Contains(EmscriptenVersion, StringComparison.Ordinal))
                throw new ArgumentException($"The .NET 10 native toolchain requires Emscripten {EmscriptenVersion}.");
            cmake.AddRange([$"-DCMAKE_TOOLCHAIN_FILE={toolchain}", "-DCMAKE_C_FLAGS=-fwasm-exceptions",
                "-DCMAKE_CXX_FLAGS=-fwasm-exceptions", "-DRBFX_SDK_PARSE_EXTRA_OPTIONS=-fwasm-exceptions"]);
            if (!options.ContainsKey("sdk-root"))
                options["sdk-root"] = BuildWasmSdk(toolchain, environment, options);
        }
        else throw new ArgumentException($"Unsupported cross-platform RID: {rid}");

        var arguments = new List<string> { Path.Combine(Repo, "packaging/nuget/package.sh"), "create", "--rid", rid,
            "--version", version, "--build-root", Path.GetFullPath(options.GetValueOrDefault("build-root") ?? Path.Combine(Repo, "build/cross", rid)) };
        if (options.TryGetValue("sdk-root", out var sdkRoot)) arguments.AddRange(["--sdk-root", Path.GetFullPath(sdkRoot), "--skip-fetch"]);
        foreach (var argument in cmake) arguments.AddRange(["--cmake-arg", argument]);
        Run("bash", arguments, environment: environment);
    }

    static string BuildWasmSdk(string toolchain, Dictionary<string, string> environment, Dictionary<string, string> options)
    {
        // rbfx's published Wasm archives may use a newer libc++/embind ABI than
        // the Emscripten version bundled with .NET. Build all native inputs together.
        var root = Path.GetFullPath(options.GetValueOrDefault("build-root") ?? Path.Combine(Repo, "build/cross/browser-wasm"));
        var source = Path.GetFullPath(options.GetValueOrDefault("rbfx-source") ?? Path.Combine(root, "engine-source"));
        if (!Directory.Exists(source))
            Run("git", ["clone", "--depth", "1", "--branch", "latest", "https://github.com/rbfx/rbfx.git", source]);
        RequireFile(Path.Combine(source, "CMakeLists.txt"));
        PatchWasmProcessSpawning(source);
        var build = Path.Combine(root, "engine-build");
        var sdk = Path.Combine(root, "engine-sdk");
        Run("cmake", ["-S", source, "-B", build, "-G", "Ninja", $"-DCMAKE_TOOLCHAIN_FILE={toolchain}",
            "-DCMAKE_BUILD_TYPE=Release", $"-DCMAKE_INSTALL_PREFIX={sdk}", "-DBUILD_SHARED_LIBS=OFF",
            "-DCMAKE_C_FLAGS=-fwasm-exceptions", "-DCMAKE_CXX_FLAGS=-fwasm-exceptions",
            "-DURHO3D_NO_EDITOR_PLAYER_EXE=ON", "-DURHO3D_SAMPLES=OFF", "-DURHO3D_PCH=ON"], environment: environment);
        Run("cmake", ["--build", build, "--parallel", Math.Min(Environment.ProcessorCount, 12).ToString()], environment: environment);
        Run("cmake", ["--install", build], environment: environment);
        return sdk;
    }

    static void PatchWasmProcessSpawning(string source)
    {
        // rbfx currently builds its POSIX process-spawning branch for Emscripten,
        // where posix_spawnp is unavailable. SystemRun is unsupported there.
        var path = Path.Combine(source, "Source/Urho3D/IO/FileSystem.cpp");
        RequireFile(path);
        var contents = File.ReadAllText(path);
        const string oldCondition = "#if defined(TVOS) || defined(IOS) || (defined(__ANDROID__) && __ANDROID_API__ < 28) || defined(UWP)";
        const string wasmCondition = oldCondition + " || defined(__EMSCRIPTEN__)";
        if (!contents.Contains(wasmCondition, StringComparison.Ordinal))
        {
            if (!contents.Contains(oldCondition, StringComparison.Ordinal))
                throw new InvalidOperationException("The rbfx FileSystem.cpp SystemRun platform guard changed; update the Wasm source patch.");
            File.WriteAllText(path, contents.Replace(oldCondition, wasmCondition, StringComparison.Ordinal));
        }
    }

    public static void Targets(string rid, string stage)
    {
        if (rid is not ("ios-arm64" or "browser-wasm")) return;
        var root = Path.Combine(stage, "runtime", rid);
        var libraries = Directory.GetFiles(Path.Combine(root, "native"), "*.a").Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray();
        if (libraries.Length == 0) throw new InvalidOperationException($"No static archives staged for {rid}.");
        var condition = $"'$(RuntimeIdentifier)' == '{rid}'";
        var group = new XElement("ItemGroup", new XAttribute("Condition", condition));
        var project = new XElement("Project");
        if (rid == "ios-arm64")
        {
            group.SetAttributeValue("Condition", condition + " and '$(TargetPlatformIdentifier)' == 'ios'");
            // SDK RelWithDebInfo archives otherwise carry several gigabytes of DWARF data.
            // Strip only symbols/debug sections from our staged copies, retaining linkable API.
            foreach (var library in libraries)
                Run("xcrun", ["--sdk", "iphoneos", "strip", "-S", "-x", Path.Combine(root, "native", library)]);
            foreach (var library in libraries)
            {
                var native = new XElement("NativeReference", new XAttribute("Include", $"$(MSBuildThisFileDirectory)../runtimes/{rid}/native/{library}"),
                    new XElement("Kind", "Static"), new XElement("ForceLoad", library == "libSharpCpp.Rbfx.Native.a" ? "true" : "false"),
                    new XElement("SmartLink", "true"));
                if (library == "libSharpCpp.Rbfx.Native.a")
                {
                    native.Add(new XElement("IsCxx", "true"), new XElement("LinkerFlags", "-lc++ -liconv"),
                        new XElement("Frameworks", "CoreVideo CoreAudio AudioToolbox AVFoundation CoreBluetooth CoreGraphics CoreMotion Foundation Metal OpenGLES QuartzCore UIKit"),
                        new XElement("WeakFrameworks", "GameController CoreHaptics"));
                }
                group.Add(native);
            }
        }
        else
        {
            project.Add(new XElement("PropertyGroup", new XAttribute("Condition", condition),
                new XElement("EmccExtraLDFlags", "$(EmccExtraLDFlags);--bind;-sWASM=1;-sFETCH=1;-fwasm-exceptions;-sFORCE_FILESYSTEM=1;-lidbfs.js;-sALLOW_MEMORY_GROWTH=1;--no-heap-copy;-sMIN_WEBGL_VERSION=2;-sMAX_WEBGL_VERSION=2;-sGL_ENABLE_GET_PROC_ADDRESS=1;-sFULL_ES3=1;-sDISABLE_DEPRECATED_FIND_EVENT_TARGET_BEHAVIOR=0")));
            foreach (var library in libraries)
                group.Add(new XElement("NativeFileReference", new XAttribute("Include", $"$(MSBuildThisFileDirectory)../runtimes/{rid}/native/{library}")));
            foreach (var javascript in Directory.GetFiles(Path.Combine(root, "native"), "*.js").Order(StringComparer.Ordinal))
                project.Add(new XElement("PropertyGroup", new XAttribute("Condition", condition),
                    new XElement("EmccExtraLDFlags", $"$(EmccExtraLDFlags);--js-library;\"$(MSBuildThisFileDirectory)../runtimes/{rid}/native/{Path.GetFileName(javascript)}\"")));
        }
        project.Add(group);
        var targets = Path.Combine(root, "buildTransitive");
        Directory.CreateDirectory(targets);
        new XDocument(project).Save(Path.Combine(targets, $"SharpCpp.Rbfx.Bindings.runtime.{rid}.targets"));
    }

    public static void Verify(string rid, string version, Dictionary<string, string> options)
    {
        var packages = Path.GetFullPath(options.GetValueOrDefault("packages") ?? Path.Combine(Repo, "build/package/nuget/out"));
        var output = Path.GetFullPath(options.GetValueOrDefault("output") ?? Path.Combine(Repo, "build/cross-consumer", rid));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "Directory.Build.props"), "<Project />\n");
        File.WriteAllText(Path.Combine(output, "Directory.Build.targets"), "<Project />\n");
        new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "packages"), new XAttribute("value", packages)),
            new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json"))))).Save(Path.Combine(output, "NuGet.Config"));
        var framework = rid switch { "ios-arm64" => "net10.0-ios", "browser-wasm" => "net10.0", _ when rid.StartsWith("android-") => "net10.0-android",
            _ => throw new ArgumentException($"Unsupported cross-platform RID: {rid}") };
        var properties = new XElement("PropertyGroup", new XElement("TargetFramework", framework), new XElement("RuntimeIdentifier", rid),
            new XElement("OutputType", "Exe"), new XElement("AllowUnsafeBlocks", "true"), new XElement("ImplicitUsings", "enable"),
            new XElement("Nullable", "enable"),
            new XElement("RestorePackagesPath", "cache"), new XElement("NuGetAudit", "false"));
        if (rid == "ios-arm64") properties.Add(new XElement("EnableCodeSigning", "false"), new XElement("BuildIpa", "false"), new XElement("SupportedOSPlatformVersion", "15.0"));
        if (rid == "browser-wasm") properties.Add(new XElement("WasmEnableThreads", "true"),
            new XElement("WasmEnableExceptionHandling", "true"));
        if (rid.StartsWith("android-")) properties.Add(new XElement("AndroidApplication", "true"), new XElement("ApplicationId", "org.rbfx.packagetest"),
            new XElement("SupportedOSPlatformVersion", "21.0"));
        var sdk = rid == "browser-wasm" ? "Microsoft.NET.Sdk.WebAssembly" : "Microsoft.NET.Sdk";
        new XDocument(new XElement("Project", new XAttribute("Sdk", sdk), properties,
            new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "SharpCpp.Rbfx.Bindings"), new XAttribute("Version", version)),
                new XElement("PackageReference", new XAttribute("Include", $"SharpCpp.Rbfx.Bindings.runtime.{rid}"), new XAttribute("Version", version))))).Save(Path.Combine(output, "Consumer.csproj"));
        var check = """
            if (new Urho3D.Math.Vector3(3, 4, 0).Length != 5) throw new System.Exception("Native rbfx call failed");
            using var context = new Urho3D.Core.Context();
            using var scene = new Urho3D.Scene.Scene(context);
            var child = scene.CreateChild(0u);
            if (!object.ReferenceEquals(child, scene.GetChild(0u))) throw new System.Exception("Wrapper identity lost");
            child.Remove();
            using var replacement = scene.CreateChild(0u);
            if (!child.IsClosed) throw new System.Exception("Expired native wrapper was not invalidated");
            System.Console.WriteLine("RBFX_NATIVE_PACKAGE_OK");
            """;
        File.WriteAllText(Path.Combine(output, "Program.cs"), rid.StartsWith("android-") ? $$"""
            [Android.App.Activity(MainLauncher = true, Exported = true)]
            public class MainActivity : Android.App.Activity
            {
                protected override void OnCreate(Android.OS.Bundle? state)
                {
                    base.OnCreate(state);
                    {{check}}
                }
            }
            """ : $$"""
            internal static class Program { private static void Main() { {{check}} } }
            """);
        if (rid == "ios-arm64") File.WriteAllText(Path.Combine(output, "Info.plist"), """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict><key>CFBundleIdentifier</key><string>org.rbfx.packagetest</string><key>UIDeviceFamily</key><array><integer>1</integer><integer>2</integer></array></dict></plist>
            """);
        var arguments = new List<string> { "build", Path.Combine(output, "Consumer.csproj"), "-c", "Release", "--nologo" };
        foreach (var name in new[] { "AndroidSdkDirectory", "JavaSdkDirectory" })
            if (options.TryGetValue(name, out var value)) arguments.Add($"-p:{name}={Path.GetFullPath(value)}");
        Run("dotnet", arguments);
        Console.WriteLine($"Native package consumer built: {output}");
    }

    static void RequireFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Required toolchain file is missing: {path}");
    }

    static string Run(string executable, IEnumerable<string> arguments, bool capture = false, Dictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Repo, RedirectStandardOutput = capture, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null) foreach (var (key, value) in environment) start.Environment[key] = value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {executable}.");
        var output = capture ? process.StandardOutput.ReadToEnd() : "";
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{executable} exited with code {process.ExitCode}.");
        return output;
    }
}
