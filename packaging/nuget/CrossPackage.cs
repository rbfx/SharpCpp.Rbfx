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
        case "build-wasm-sdk": CrossPackage.BuildWasmSdkCommand(options); break;
        case "help":
        case "--help":
        case "-h":
            Console.WriteLine("Usage: CrossPackage.cs <create|targets|verify|build-wasm-sdk> [--option VALUE ...]");
            break;
        default: throw new ArgumentException("Expected create, targets, verify or build-wasm-sdk. See packaging/nuget/README.md.");
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
    const string RbfxRepository = "https://github.com/rbfx/rbfx.git";
    const string RbfxLatestRef = "refs/tags/latest";
    const string WasmSdkAssetPrefix = "rbfx-web-wasm-sdk-";

    public static void Create(string rid, string version, Dictionary<string, string> options)
    {
        var cmake = new List<string> { "-DRBFX_BUILD_USERCODE=OFF", "-DRBFX_BUILD_EXAMPLES=OFF", "-DRBFX_BUILD_HOST=OFF" };
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
            ndk = Path.GetFullPath(ndk);
            environment["ANDROID_NDK_HOME"] = ndk;
            environment["ANDROID_NDK_ROOT"] = ndk;
            var toolchain = Path.Combine(ndk, "build/cmake/android.toolchain.cmake");
            RequireFile(toolchain);
            cmake.AddRange([$"-DCMAKE_TOOLCHAIN_FILE={toolchain}", $"-DANDROID_ABI={abi}", "-DANDROID_PLATFORM=android-21", "-DANDROID_STL=c++_static", "-DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON"]);
        }
        else if (rid == "ios-arm64")
        {
            if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Building iOS packages requires macOS and Xcode.");
            var sdk = Run("xcrun", ["--sdk", "iphoneos", "--show-sdk-path"], capture: true).Trim();
            cmake.AddRange(["-DCMAKE_SYSTEM_NAME=iOS", "-DCMAKE_OSX_ARCHITECTURES=arm64", $"-DCMAKE_OSX_SYSROOT={sdk}", "-DCMAKE_OSX_DEPLOYMENT_TARGET=15.0",
                "-DRBFX_SDK_PARSE_USE_PLATFORM_COMPILER_INCLUDES=ON"]);
        }
        else if (rid == "browser-wasm")
        {
            var emsdk = GetEmsdkRoot(options, environment);
            ValidateEmsdk(emsdk, environment);
            string wasmSdkRoot;
            if (options.TryGetValue("sdk-root", out var explicitSdkRoot))
                wasmSdkRoot = Path.GetFullPath(explicitSdkRoot);
            else wasmSdkRoot = ResolveWasmSdk(emsdk, options, environment);
            var toolchain = Path.Combine(emsdk, "upstream/emscripten/cmake/Modules/Platform/Emscripten.cmake");
            cmake.AddRange([$"-DCMAKE_TOOLCHAIN_FILE={toolchain}", "-DCMAKE_C_FLAGS=-fwasm-exceptions",
                "-DCMAKE_CXX_FLAGS=-fwasm-exceptions", "-DRBFX_SDK_PARSE_EXTRA_OPTIONS=-fwasm-exceptions"]);
            options["sdk-root"] = wasmSdkRoot;
        }
        else throw new ArgumentException($"Unsupported cross-platform RID: {rid}");

        var arguments = new List<string> { Path.Combine(Repo, "packaging/nuget/package.sh"), "create", "--rid", rid,
            "--version", version, "--build-root", Path.GetFullPath(options.GetValueOrDefault("build-root") ?? Path.Combine(Repo, "build/cross", rid)) };
        if (options.TryGetValue("sdk-root", out var sdkRoot)) arguments.AddRange(["--sdk-root", Path.GetFullPath(sdkRoot), "--skip-fetch"]);
        foreach (var argument in cmake) arguments.AddRange(["--cmake-arg", argument]);
        Run("bash", arguments, environment: environment);
    }

    public static void BuildWasmSdkCommand(Dictionary<string, string> options)
    {
        var environment = new Dictionary<string, string>();
        var emsdk = GetEmsdkRoot(options, environment);
        ValidateEmsdk(emsdk, environment);
        var buildRoot = Path.GetFullPath(options.GetValueOrDefault("build-root") ?? Path.Combine(Repo, "build/cross/browser-wasm-sdk"));
        var (sdkRoot, commit) = BuildWasmSdkFromSource(emsdk, buildRoot, environment);
        var assetName = WasmSdkAssetName(EmscriptenVersion, commit);
        var archive = Path.GetFullPath(options.GetValueOrDefault("output") ?? Path.Combine(Repo, "build/package/nuget/wasm-sdk", assetName));
        CreateWasmSdkArchive(sdkRoot, archive);
        Console.WriteLine($"Built rbfx Wasm SDK for rbfx {commit} with Emscripten {EmscriptenVersion}: {archive}");
    }

    static string GetEmsdkRoot(Dictionary<string, string> options, Dictionary<string, string> environment)
    {
        var emsdk = options.GetValueOrDefault("emsdk-root") ?? Environment.GetEnvironmentVariable("EMSDK")
            ?? throw new ArgumentException($"Supply --emsdk-root pointing to an activated Emscripten {EmscriptenVersion} SDK.");
        emsdk = Path.GetFullPath(emsdk);
        environment["EMSDK"] = emsdk;
        environment["EM_CONFIG"] = Path.Combine(emsdk, ".emscripten");
        RequireFile(environment["EM_CONFIG"]);

        var searchPaths = new List<string>
        {
            Path.Combine(emsdk, "upstream/emscripten"),
            Path.Combine(emsdk, "upstream/bin")
        };
        var nodeRoot = Path.Combine(emsdk, "node");
        if (Directory.Exists(nodeRoot))
            foreach (var nodeDirectory in Directory.GetDirectories(nodeRoot).Order(StringComparer.Ordinal))
            {
                searchPaths.Add(nodeDirectory);
                searchPaths.Add(Path.Combine(nodeDirectory, "bin"));
            }
        var pathEntries = searchPaths.Where(Directory.Exists).ToList();
        if (Environment.GetEnvironmentVariable("PATH") is { Length: > 0 } path) pathEntries.Add(path);
        environment["PATH"] = string.Join(Path.PathSeparator, pathEntries);
        return emsdk;
    }

    static void ValidateEmsdk(string emsdk, Dictionary<string, string> environment)
    {
        var toolchain = Path.Combine(emsdk, "upstream/emscripten/cmake/Modules/Platform/Emscripten.cmake");
        RequireFile(toolchain);
        var emcc = Path.Combine(emsdk, "upstream/emscripten", OperatingSystem.IsWindows() ? "emcc.bat" : "emcc");
        var compilerVersion = Run(emcc, ["--version"], capture: true, environment: environment);
        if (!compilerVersion.Contains(EmscriptenVersion, StringComparison.Ordinal))
            throw new ArgumentException($"The .NET 10 Wasm workload requires Emscripten {EmscriptenVersion}; found '{compilerVersion.Trim()}'.");
    }

    static string ResolveWasmSdk(string emsdk, Dictionary<string, string> options, Dictionary<string, string> environment)
    {
        var buildRoot = Path.GetFullPath(options.GetValueOrDefault("build-root") ?? Path.Combine(Repo, "build/cross/browser-wasm"));
        var expectedAsset = WasmSdkAssetName(EmscriptenVersion, ResolveRbfxLatestCommit());
        try
        {
            // eng/fetch-rbfx-sdk.sh is the single source of truth for SDK downloads. It logs to stderr.
            var output = Run("bash", [Path.Combine(Repo, "eng/fetch-rbfx-sdk.sh"), "--rid", "browser-wasm", "--print-root"], capture: true);
            var root = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                Console.WriteLine("eng/fetch-rbfx-sdk.sh did not print an SDK root; building the rbfx Wasm SDK locally.");
            else
            {
                // Accept only the SDK asset that matches the Emscripten version and the current rbfx latest commit.
                var asset = ReadSdkStampAsset(root);
                if (asset == expectedAsset)
                {
                    Console.WriteLine($"Using rbfx Wasm SDK asset {asset}.");
                    return Path.GetFullPath(root);
                }
                Console.WriteLine($"Fetched Wasm SDK asset '{asset ?? "unknown"}' is not '{expectedAsset}'; building it locally.");
            }
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine($"Could not fetch the rbfx Wasm SDK ({exception.Message}); building it locally.");
        }

        var (sdkRoot, _) = BuildWasmSdkFromSource(emsdk, Path.Combine(buildRoot, "wasm-sdk-build"), environment);
        return sdkRoot;
    }

    static string? ReadSdkStampAsset(string sdkRoot)
    {
        var stamp = Path.Combine(sdkRoot, ".rbfx-sdk.json");
        if (!File.Exists(stamp)) return null;
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(stamp));
        return document.RootElement.TryGetProperty("asset", out var asset) ? asset.GetString() : null;
    }

    static string ResolveRbfxLatestCommit()
    {
        var output = Run("git", ["ls-remote", RbfxRepository, RbfxLatestRef, $"{RbfxLatestRef}^{{}}"], capture: true);
        var entries = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2)
            .ToArray();
        var commit = entries.FirstOrDefault(parts => parts[1] == $"{RbfxLatestRef}^{{}}")?.FirstOrDefault()
            ?? entries.FirstOrDefault(parts => parts[1] == RbfxLatestRef)?.FirstOrDefault();
        if (commit is null || commit.Length != 40 || !commit.All(Uri.IsHexDigit))
            throw new InvalidOperationException($"Could not resolve rbfx's {RbfxLatestRef} commit.");
        return commit.ToLowerInvariant();
    }

    static string WasmSdkAssetName(string emscriptenVersion, string rbfxCommit) =>
        $"{WasmSdkAssetPrefix}emscripten-{emscriptenVersion}-rbfx-{rbfxCommit[..12]}.7z";

    static (string SdkRoot, string RbfxCommit) BuildWasmSdkFromSource(string emsdk, string buildRoot,
        Dictionary<string, string> environment)
    {
        var source = Path.Combine(buildRoot, "engine-source");
        var build = Path.Combine(buildRoot, "engine-build");
        var sdk = Path.Combine(buildRoot, "engine-sdk");

        if (Directory.Exists(buildRoot)) Directory.Delete(buildRoot, recursive: true);
        Directory.CreateDirectory(buildRoot);
        Run("git", ["clone", "--depth", "1", "--branch", "latest", RbfxRepository, source]);
        var actualCommit = Run("git", ["-C", source, "rev-parse", "HEAD"], capture: true).Trim();
        PatchWasmProcessSpawning(source);

        var toolchain = Path.Combine(emsdk, "upstream/emscripten/cmake/Modules/Platform/Emscripten.cmake");
        Run("cmake", ["-S", source, "-B", build, "-G", "Ninja", $"-DCMAKE_TOOLCHAIN_FILE={toolchain}",
            "-DCMAKE_BUILD_TYPE=Release", $"-DCMAKE_INSTALL_PREFIX={sdk}", "-DBUILD_SHARED_LIBS=OFF",
            "-DCMAKE_C_FLAGS=-fwasm-exceptions", "-DCMAKE_CXX_FLAGS=-fwasm-exceptions",
            "-DURHO3D_NO_EDITOR_PLAYER_EXE=ON", "-DURHO3D_SAMPLES=OFF", "-DURHO3D_PCH=ON"], environment: environment);
        Run("cmake", ["--build", build, "--parallel", Math.Min(Environment.ProcessorCount, 12).ToString()], environment: environment);
        Run("cmake", ["--install", build], environment: environment);
        return (sdk, actualCommit.ToLowerInvariant());
    }

    static void PatchWasmProcessSpawning(string source)
    {
        var path = Path.Combine(source, "Source/Urho3D/IO/FileSystem.cpp");
        RequireFile(path);
        var contents = File.ReadAllText(path);
        const string oldCondition = "#if defined(TVOS) || defined(IOS) || (defined(__ANDROID__) && __ANDROID_API__ < 28) || defined(UWP)";
        const string wasmCondition = oldCondition + " || defined(__EMSCRIPTEN__)";
        if (contents.Contains(wasmCondition, StringComparison.Ordinal)) return;
        if (!contents.Contains(oldCondition, StringComparison.Ordinal))
            throw new InvalidOperationException("The rbfx FileSystem.cpp SystemRun platform guard changed; update the Wasm source patch.");
        File.WriteAllText(path, contents.Replace(oldCondition, wasmCondition, StringComparison.Ordinal));
    }

    static void CreateWasmSdkArchive(string sdkRoot, string archive)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        if (File.Exists(archive)) File.Delete(archive);
        Run("7z", ["a", "-t7z", "-mx=5", "-y", archive, "."], workingDirectory: sdkRoot);
    }

    public static void Targets(string rid, string stage)
    {
        if (rid.StartsWith("android-", StringComparison.Ordinal))
        {
            var androidRoot = Path.Combine(stage, "runtime", rid);
            var nativeDirectory = Path.Combine(androidRoot, "native");
            var androidLibraries = Directory.GetFiles(nativeDirectory, "*.so")
                .Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray();
            if (androidLibraries.Length == 0) throw new InvalidOperationException($"No shared libraries staged for {rid}.");

            var androidProject = new XElement("Project");
            var androidGroup = new XElement("ItemGroup", new XAttribute("Condition", "'$(AndroidApplication)' == 'true'"));
            foreach (var library in androidLibraries)
                androidGroup.Add(new XElement("AndroidNativeLibraryNoJniPreload",
                    new XAttribute("Include", $"$(MSBuildThisFileDirectory)../runtimes/{rid}/native/{library}")));
            if (rid == "android-x64")
                androidGroup.Add(new XElement("AndroidEnvironment",
                    new XAttribute("Include", "$(MSBuildThisFileDirectory)SharpCpp.Rbfx.Bindings.runtime.android-x64.env")));
            androidProject.Add(androidGroup);

            var androidTargets = Path.Combine(androidRoot, "buildTransitive");
            Directory.CreateDirectory(androidTargets);
            new XDocument(androidProject).Save(Path.Combine(androidTargets, $"SharpCpp.Rbfx.Bindings.runtime.{rid}.targets"));
            if (rid == "android-x64")
                File.WriteAllText(Path.Combine(androidTargets, "SharpCpp.Rbfx.Bindings.runtime.android-x64.env"),
                    "TRACY_NO_INVARIANT_CHECK=1\n");
            return;
        }

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
                new XElement("PackageReference", new XAttribute("Include", UnionRuntimePackageId(rid)), new XAttribute("Version", version))))).Save(Path.Combine(output, "Consumer.csproj"));
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

    static string UnionRuntimePackageId(string rid) => rid is "android-x64" or "linux-x64"
        ? "SharpCpp.Rbfx.Bindings.runtime.unix-x64"
        : "SharpCpp.Rbfx.Bindings.runtime." + rid;

    static string Run(string executable, IEnumerable<string> arguments, bool capture = false,
        Dictionary<string, string>? environment = null, string? workingDirectory = null)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory ?? Repo, RedirectStandardOutput = capture, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null) foreach (var (key, value) in environment) start.Environment[key] = value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {executable}.");
        var output = capture ? process.StandardOutput.ReadToEnd() : "";
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{executable} exited with code {process.ExitCode}.");
        return output;
    }
}
