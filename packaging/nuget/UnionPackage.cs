#:property ImportDirectoryBuildProps=false
#:property ImportDirectoryBuildTargets=false

using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

// .NET 10 file-based utility. All generated projects and packages stay under build/.
// dotnet run --file packaging/nuget/UnionPackage.cs -- create --input DIR --version VERSION
try
{
    var options = Options.Parse(args);
    if (options.Command == "create") UnionPackage.Create(options);
    else if (options.Command == "smoke") UnionPackage.Smoke(options.Required("packages"), options.Required("version"),
        options.Required("framework"), options.Values("platform"));
    else throw new ArgumentException("Expected create or smoke.");
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Error: {exception.Message}");
    return 1;
}
return 0;

static class UnionPackage
{
    static string SourcePath([CallerFilePath] string path = "") => path;
    static readonly string Repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "../.."));

    public static void Create(Options options)
    {
        var sharp = Path.GetFullPath(options.Optional("sharp-cpp-root") ?? Path.Combine(Repo, "../SharpCpp"));
        var inputs = Path.GetFullPath(options.Required("input"));
        var version = options.Required("version");
        var output = Path.GetFullPath(options.Optional("output") ?? Path.Combine(Repo, "build/package/union"));
        CreateEmptyDirectory(output);
        var platforms = Directory.GetDirectories(inputs, "managed-*")
            .Select(path => Path.GetFileName(path)["managed-".Length..]).Order(StringComparer.Ordinal).ToArray();
        if (platforms.Length == 0) throw new InvalidOperationException("No managed platform artifacts were found.");

        var dependencies = Path.Combine(output, "dependencies");
        Directory.CreateDirectory(dependencies);
        // New runs upload portable dependencies; older runs have a Linux managed package.
        var stageInputs = Directory.GetDirectories(inputs, "package-input-*")
            .SelectMany(path => Directory.Exists(Path.Combine(path, "lib"))
                ? Directory.GetDirectories(Path.Combine(path, "lib")) : []).Order(StringComparer.Ordinal).ToArray();
        string framework, analyzer;
        if (stageInputs.Length > 0)
        {
            framework = Path.GetFileName(stageInputs[0]);
            CopyDlls(stageInputs[0], dependencies);
            analyzer = Directory.GetDirectories(inputs, "package-input-*")
                .Select(path => Path.Combine(path, "analyzers/dotnet/cs/SharpCpp.Rbfx.SourceGenerator.dll"))
                .First(File.Exists);
        }
        else
        {
            var seed = Path.Combine(output, "seed");
            ExtractPackages(Directory.GetDirectories(inputs, "nuget-*")
                .SelectMany(path => Directory.GetFiles(path, "SharpCpp.Rbfx.Bindings.*.nupkg"))
                .Where(path => char.IsDigit(Path.GetFileName(path)["SharpCpp.Rbfx.Bindings.".Length])), seed);
            var lib = Directory.GetDirectories(Path.Combine(seed, "lib")).Single();
            framework = Path.GetFileName(lib);
            CopyDlls(lib, dependencies);
            analyzer = Path.Combine(seed, "analyzers/dotnet/cs/SharpCpp.Rbfx.SourceGenerator.dll");
        }

        var toolProject = Path.Combine(sharp, "src/SharpCpp.ApiUnion/SharpCpp.ApiUnion.csproj");
        Run("dotnet", ["build", toolProject, "-c", "Release", "--nologo"]);
        using var properties = JsonDocument.Parse(Run("dotnet", ["msbuild", toolProject, "-target:ResolveReferences",
            "-getProperty:TargetFramework", "-getItem:ReferencePath", "-p:Configuration=Release"], capture: true));
        var frameworkReference = properties.RootElement.GetProperty("Items").GetProperty("ReferencePath")
            .EnumerateArray().Select(item => item.GetProperty("FullPath").GetString()!)
            .Where(path => Path.GetFileName(path) == "System.Runtime.dll").Select(path => Path.GetDirectoryName(path)!).Single();
        var tool = Path.Combine(sharp, "build/bin/Release/SharpCpp.ApiUnion.dll");
        var sources = Path.Combine(output, "sources");
        IsolateProjects(sources);
        Run("dotnet", [tool, "emit", "--output", sources, "--references", frameworkReference,
            "--references", dependencies, .. platforms.Select(rid => $"{rid}={Path.Combine(inputs, "managed-" + rid, "SharpCpp.Rbfx.Bindings.dll")}")]);
        using var identity = JsonDocument.Parse(File.ReadAllText(Path.Combine(sources, "AssemblyIdentity.json")));
        var assemblyVersion = identity.RootElement.GetProperty("Version").GetString()!;
        var referenceProject = Path.Combine(sources, "UnionReference.csproj");
        Project(referenceProject, framework, [Path.Combine(sources, "Reference.g.cs")], dependencies,
            assemblyVersion, reference: true);
        Run("dotnet", ["build", referenceProject, "-c", "Release", "--nologo"]);
        var reference = Run("dotnet", ["msbuild", referenceProject, "-p:Configuration=Release", "-getProperty:TargetRefPath"], capture: true).Trim();
        var implementations = new Dictionary<string, string>();
        foreach (var rid in platforms)
        {
            var generated = Directory.GetFiles(Path.Combine(inputs, "generated-" + rid),
                "SharpCpp.Rbfx.Bindings.Aliases.g.cs", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(Path.GetDirectoryName(path)) == "rbfx").Single();
            var prepared = Path.Combine(sources, rid, "prepared");
            Run("dotnet", [tool, "prepare", "--output", prepared,
                "--stubs", Path.Combine(sources, rid, "Stubs.g.cs"), "--concrete", Path.Combine(sources, rid, "ConcreteTypes.txt"),
                "--sources", Path.GetDirectoryName(generated)!, "--sources", Path.Combine(Repo, "SharpCpp.Rbfx.Bindings")]);
            var implementationProject = Path.Combine(sources, rid, $"UnionImplementation-{rid}.csproj");
            Project(implementationProject, framework, Directory.GetFiles(prepared, "*.cs").Order(StringComparer.Ordinal),
                dependencies, assemblyVersion, analyzer: analyzer, staticRuntime: rid is "ios-arm64" or "browser-wasm",
                browserWasm: rid == "browser-wasm");
            Run("dotnet", ["build", implementationProject, "-c", "Release", "--nologo"]);
            implementations.Add(rid, Path.Combine(sources, rid, "output/SharpCpp.Rbfx.Bindings.dll"));
        }
        Run("dotnet", [tool, "validate", $"ref={reference}", .. implementations.Select(item => $"{item.Key}={item.Value}")]);
        var tools = Path.Combine(output, "tools");
        Run("dotnet", ["tool", "install", "Microsoft.DotNet.ApiCompat.Tool", "--version", "10.0.100", "--tool-path", tools]);
        foreach (var implementation in implementations.Values)
            Run(Path.Combine(tools, "apicompat"), ["-l", reference, "-r", implementation,
                "--left-assembly-references", $"{frameworkReference},{dependencies}",
                "--right-assembly-references", $"{frameworkReference},{dependencies}", "--enable-rule-cannot-change-parameter-name"]);

        var stage = Path.Combine(output, "stage");
        var packages = Path.Combine(output, "packages");
        Directory.CreateDirectory(packages);
        CopyDlls(dependencies, Path.Combine(stage, "managed/lib", framework), exclude: "SharpCpp.Rbfx.Bindings.dll");
        CopyDlls(dependencies, Path.Combine(stage, "managed/ref", framework));
        CopyDlls(Path.GetDirectoryName(analyzer)!, Path.Combine(stage, "managed/analyzers/dotnet/cs"));
        File.Copy(reference, Path.Combine(stage, "managed/ref", framework, "SharpCpp.Rbfx.Bindings.dll"), overwrite: true);
        // Bindings has no duplicate lib/ implementation: it would override the selected RID DLL.
        foreach (var (rid, implementation) in implementations)
        {
            var payload = Path.Combine(output, "runtime-" + rid);
            ExtractPackages(Directory.GetFiles(Path.Combine(inputs, "nuget-" + rid),
                "SharpCpp.Rbfx.Bindings.runtime.*.nupkg"), payload);
            var native = Path.Combine(payload, "runtimes", rid, "native");
            if (!Directory.Exists(native)) throw new InvalidOperationException($"Missing native package for {rid}.");
            CopyDirectory(native, Path.Combine(stage, "runtime", rid, "native"));
            var targets = Path.Combine(payload, "buildTransitive");
            if (Directory.Exists(targets)) CopyDirectory(targets, Path.Combine(stage, "runtime", rid, "buildTransitive"));
            var runtimeLib = Path.Combine(stage, "runtime", rid, "lib", framework);
            Directory.CreateDirectory(runtimeLib);
            File.Copy(implementation, Path.Combine(runtimeLib, Path.GetFileName(implementation)));
        }
        foreach (var rid in platforms.Where(rid => rid is not ("android-x64" or "linux-x64")))
            Pack("SharpCpp.Rbfx.Bindings.Runtime", rid, version, framework, stage, packages);
        if (platforms.Contains("android-x64") && platforms.Contains("linux-x64"))
        {
            var linuxNativeAssets = Path.Combine(stage, "runtime/linux-x64/native-assets");
            Directory.CreateDirectory(linuxNativeAssets);
            foreach (var nativeFile in Directory.GetFiles(Path.Combine(stage, "runtime/linux-x64/native")))
                File.Copy(nativeFile, Path.Combine(linuxNativeAssets, Path.GetFileName(nativeFile) + ".bin"));
            PackRuntimeGroup("unix-x64", "android-x64", "linux-x64", version, framework, stage, packages);
        }

        var runtimePackages = platforms.Select(RuntimePackageId).Distinct(StringComparer.Ordinal).ToArray();
        var dependencyProps = Path.Combine(stage, "RuntimeDependencies.props");
        new XDocument(new XElement("Project", new XElement("ItemGroup", runtimePackages.Select(packageId => new XElement("PackageReference",
            new XAttribute("Include", packageId), new XAttribute("Version", version)))))).Save(dependencyProps);
        Pack("SharpCpp.Rbfx.Bindings", platforms[0], version, framework, stage, packages, dependencyProps);
        foreach (var path in Directory.GetDirectories(inputs, "nuget-*").SelectMany(path => Directory.GetFiles(path, "*.nupkg")))
            if (new[] { "SharpCpp.Rbfx.CoreData.", "SharpCpp.Rbfx.Data.", "SharpCpp.Rbfx.EditorData." }.Any(prefix => Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal)))
                File.Copy(path, Path.Combine(packages, Path.GetFileName(path)), overwrite: true);
        // UWP uses the same Windows toolchain and a custom RID graph, so its consumer is
        // verified by the Windows UWP example job rather than this portable RID smoke build.
        Smoke(packages, version, framework, platforms.Where(rid => rid != "uap-x64").ToArray());
        Console.WriteLine($"Union packages: {packages}");
    }

    public static void Smoke(string packagePath, string version, string framework, IReadOnlyList<string> platforms)
    {
        if (platforms.Count == 0) throw new ArgumentException("smoke requires --platform.");
        var packages = Path.GetFullPath(packagePath);
        var smoke = Path.Combine(Path.GetDirectoryName(packages)!, "smoke");
        CreateEmptyDirectory(smoke);
        IsolateProjects(smoke);
        new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "union"), new XAttribute("value", packages)),
            new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json"))))).Save(Path.Combine(smoke, "NuGet.Config"));
        foreach (var rid in platforms)
        {
            var directory = Path.Combine(smoke, rid);
            Directory.CreateDirectory(directory);
            var project = Path.Combine(directory, "Consumer.csproj");
            var properties = new Dictionary<string, string> { ["TargetFramework"] = framework, ["OutputType"] = "Exe", ["RuntimeIdentifier"] = rid,
                    ["SelfContained"] = "false", ["UseAppHost"] = "false", ["ImplicitUsings"] = "enable", ["NuGetAudit"] = "false",
                    ["NoWarn"] = "CA1416", ["RestorePackagesPath"] = "../cache" };
            if (rid == "browser-wasm")
            {
                properties["WasmEnableThreads"] = "true";
                properties["WasmEnableExceptionHandling"] = "true";
            }
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                Properties(properties),
                new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "SharpCpp.Rbfx.Bindings"),
                    new XAttribute("Version", version))))).Save(project);
            File.WriteAllText(Path.Combine(directory, "Program.cs"), """
                using Urho3D.Math;
                var value = new Vector3(3, 4, 0);
                if (value.Length != 5) throw new Exception("Wrong runtime implementation");
                if (OperatingSystem.IsLinux())
                {
                    _ = Urho3D.Core.Thread.GetCurrentThreadIDAsUIntPtr();
                    try
                    {
                        _ = Urho3D.Core.Thread.GetCurrentThreadIDAsUInt32();
                        throw new Exception("Missing return-type variant did not throw");
                    }
                    catch (PlatformNotSupportedException) { }
                    try
                    {
                        _ = new Urho3D.XR.VRRigDesc();
                        throw new Exception("Missing API did not throw");
                    }
                    catch (PlatformNotSupportedException) { }
                }
                else
                {
                    try
                    {
                        _ = Urho3D.Core.Thread.GetCurrentThreadIDAsUIntPtr();
                        throw new Exception("Missing return-type variant did not throw");
                    }
                    catch (PlatformNotSupportedException) { }
                }
                Console.WriteLine("Union runtime selection and missing API stub passed");
                """);
            Run("dotnet", ["build", project, "-c", "Release", "--nologo"]);
            using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "obj/project.assets.json")));
            var target = assets.RootElement.GetProperty("targets").EnumerateObject().Single(item => item.Name.EndsWith("/" + rid, StringComparison.Ordinal)).Value;
            var main = target.GetProperty("SharpCpp.Rbfx.Bindings/" + version);
            if (!main.GetProperty("compile").TryGetProperty($"ref/{framework}/SharpCpp.Rbfx.Bindings.dll", out _))
                throw new InvalidOperationException($"{rid}: compile does not select the union reference.");
            var runtimePackageId = RuntimePackageId(rid);
            var runtime = target.GetProperty($"{runtimePackageId}/{version}");
            if (!runtime.GetProperty("runtime").TryGetProperty($"runtimes/{rid}/lib/{framework}/SharpCpp.Rbfx.Bindings.dll", out _))
                throw new InvalidOperationException($"{rid}: runtime does not select the platform implementation.");
            foreach (var otherPackageId in platforms.Select(RuntimePackageId).Distinct(StringComparer.Ordinal).Where(other => other != runtimePackageId))
                if (target.GetProperty($"{otherPackageId}/{version}").TryGetProperty("runtime", out var otherRuntime) && otherRuntime.EnumerateObject().Any())
                    throw new InvalidOperationException($"{rid}: runtime selects assets from {otherPackageId}.");
            var binary = Path.Combine(directory, "bin/Release", framework, rid);
            var selected = Path.Combine(smoke, "cache", runtimePackageId.ToLowerInvariant(), version, "runtimes", rid, "lib", framework, "SharpCpp.Rbfx.Bindings.dll");
            if (!SHA256.HashData(File.ReadAllBytes(selected)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(binary, "SharpCpp.Rbfx.Bindings.dll")))))
                throw new InvalidOperationException($"{rid}: copied runtime DLL differs from the platform package.");
            if (rid == RuntimeInformation.RuntimeIdentifier)
            {
                // The prebuilt rbfx SDK uses initial-exec TLS; reserve space before .NET loads it.
                var tunables = Environment.GetEnvironmentVariable("GLIBC_TUNABLES");
                Run("dotnet", [Path.Combine(binary, "Consumer.dll")], environment: OperatingSystem.IsLinux() ? new()
                { ["GLIBC_TUNABLES"] = string.Join(':', new[] { tunables, "glibc.rtld.optional_static_tls=32768" }.Where(value => !string.IsNullOrEmpty(value))) } : null);
            }
        }
    }

    static void Project(string path, string framework, IEnumerable<string> sources, string dependencies,
        string version, bool reference = false, string? analyzer = null, bool staticRuntime = false, bool browserWasm = false)
    {
        var items = new XElement("ItemGroup", sources.Select(source => new XElement("Compile",
            new XAttribute("Include", Path.GetRelativePath(Path.GetDirectoryName(path)!, source)))));
        foreach (var dependency in Directory.GetFiles(dependencies, "*.dll").Where(file => Path.GetFileName(file) != "SharpCpp.Rbfx.Bindings.dll"))
            items.Add(new XElement("Reference", new XAttribute("Include", Path.GetFileNameWithoutExtension(dependency)), new XElement("HintPath", dependency)));
        if (analyzer is not null) items.Add(new XElement("Analyzer", new XAttribute("Include", analyzer)));
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), Properties(new()
        {
            ["TargetFramework"] = framework, ["AssemblyName"] = "SharpCpp.Rbfx.Bindings", ["AssemblyVersion"] = version,
            ["EnableDefaultItems"] = "false", ["ProduceReferenceAssembly"] = reference.ToString().ToLowerInvariant(),
            ["AllowUnsafeBlocks"] = "true", ["DisableRuntimeMarshalling"] = "true", ["Nullable"] = "enable",
            ["ImplicitUsings"] = "enable", ["LangVersion"] = "latest", ["NoWarn"] = "CS0108;CS0109;CS0618;CS0672;CA1416",
            ["DefineConstants"] = string.Join(';', new[]
                { staticRuntime ? "SHARPCPP_STATIC_RUNTIME" : "", browserWasm ? "SHARPCPP_BROWSER_WASM" : "" }
                .Where(value => value.Length > 0)),
            ["OutputPath"] = "output/", ["AppendTargetFrameworkToOutputPath"] = "false", ["GenerateDependencyFile"] = "false"
        }), items)).Save(path);
    }

    static XElement Properties(Dictionary<string, string> values) => new("PropertyGroup", values.Select(item => new XElement(item.Key, item.Value)));
    static void Pack(string name, string rid, string version, string framework, string stage, string packages, string? dependencies = null) =>
        Run("dotnet", ["pack", Path.Combine(Repo, "packaging/nuget/packages", name, name + ".Package.csproj"),
            "-c", "Release", "--nologo", "--output", packages, "--source", packages, $"-p:PackageVersion={version}",
            $"-p:SharpCppNuGetStageRoot={stage}", $"-p:SharpCppRuntimeIdentifier={rid}", $"-p:SharpCppPackageTargetFramework={framework}",
            "-p:SharpCppUnionPackage=true", .. dependencies is null ? Array.Empty<string>() : [$"-p:SharpCppUnionDependencies={dependencies}"]]);

    static void PackRuntimeGroup(string group, string primaryRid, string additionalRid, string version, string framework,
        string stage, string packages) =>
        Run("dotnet", ["pack", Path.Combine(Repo, "packaging/nuget/packages/SharpCpp.Rbfx.Bindings.Runtime", "SharpCpp.Rbfx.Bindings.Runtime.Package.csproj"),
            "-c", "Release", "--nologo", "--output", packages, "--source", packages, $"-p:PackageVersion={version}",
            $"-p:SharpCppNuGetStageRoot={stage}", $"-p:SharpCppRuntimeIdentifier={primaryRid}",
            $"-p:SharpCppAdditionalRuntimeIdentifier={additionalRid}", $"-p:SharpCppRuntimePackageId=SharpCpp.Rbfx.Bindings.runtime.{group}",
            $"-p:SharpCppPackageTargetFramework={framework}", "-p:SharpCppUnionPackage=true"]);

    static string RuntimePackageId(string rid) => rid is "android-x64" or "linux-x64"
        ? "SharpCpp.Rbfx.Bindings.runtime.unix-x64"
        : "SharpCpp.Rbfx.Bindings.runtime." + rid;
    static void CreateEmptyDirectory(string path)
    {
        if (Directory.Exists(path)) throw new InvalidOperationException($"Use an empty output directory: {path}");
        Directory.CreateDirectory(path);
    }
    static void IsolateProjects(string path)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "Directory.Build.props"), "<Project />\n");
        File.WriteAllText(Path.Combine(path, "Directory.Build.targets"), "<Project />\n");
    }
    static void CopyDlls(string source, string destination, string? exclude = null)
    {
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.GetFiles(source, "*.dll").Where(path => Path.GetFileName(path) != exclude))
            File.Copy(path, Path.Combine(destination, Path.GetFileName(path)), overwrite: true);
    }
    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.GetFiles(source)) File.Copy(path, Path.Combine(destination, Path.GetFileName(path)));
        foreach (var path in Directory.GetDirectories(source)) CopyDirectory(path, Path.Combine(destination, Path.GetFileName(path)));
    }
    static void ExtractPackages(IEnumerable<string> packages, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var package in packages)
        {
            using var archive = ZipFile.OpenRead(package);
            foreach (var entry in archive.Entries)
            {
                var parts = entry.FullName.Split('/');
                if (entry.FullName.StartsWith('/') || parts.Contains("..") || entry.FullName.Contains('\\') || entry.FullName.Contains(':'))
                    throw new InvalidOperationException($"Unsafe package member in {package}: {entry.FullName}");
                if (entry.Name.Length == 0 || parts[0] is not ("ref" or "lib" or "analyzers" or "runtimes" or "buildTransitive")) continue;
                var path = Path.Combine(destination, Path.Combine(parts));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: true);
            }
        }
    }
    static string Run(string executable, IReadOnlyList<string> arguments, bool capture = false, Dictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = capture };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null) foreach (var (key, value) in environment) start.Environment[key] = value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {executable}.");
        var output = capture ? process.StandardOutput.ReadToEnd() : "";
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{executable} {arguments.FirstOrDefault()} failed with exit code {process.ExitCode}.");
        return output;
    }
}

sealed class Options(string command, Dictionary<string, List<string>> values)
{
    public string Command => command;
    public string Required(string name) => Optional(name) ?? throw new ArgumentException($"Missing --{name}.");
    public string? Optional(string name) => values.GetValueOrDefault(name)?.Single();
    public IReadOnlyList<string> Values(string name) => values.GetValueOrDefault(name) ?? [];
    public static Options Parse(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("Expected create or smoke.");
        var values = new Dictionary<string, List<string>>();
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Expected --option value, got {args[index]}.");
            var key = args[index][2..];
            if (key is not ("input" or "version" or "output" or "sharp-cpp-root" or "packages" or "framework" or "platform"))
                throw new ArgumentException($"Unknown option: {args[index]}.");
            if (!values.TryGetValue(key, out var list)) values.Add(key, list = []);
            list.Add(args[index + 1]);
        }
        return new Options(args[0], values);
    }
}
