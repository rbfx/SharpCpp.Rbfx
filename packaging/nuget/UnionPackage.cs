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
// CI runs the same work in phases that share one output directory:
//   reference --input DIR
//   implement --input DIR --version VERSION --rid RID (once for each RID, in parallel jobs)
//   pack --input DIR --version VERSION
try
{
    var options = Options.Parse(args);
    switch (options.Command)
    {
        case "create": UnionPackage.Create(options); break;
        case "reference": UnionPackage.Reference(options); break;
        case "implement": UnionPackage.Implement(options); break;
        case "pack": UnionPackage.Package(options); break;
        case "smoke": UnionPackage.Smoke(options.Required("packages"), options.Required("version"),
            options.Required("framework"), options.Values("platform")); break;
        default: throw new ArgumentException(Options.Usage);
    }
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
    static readonly string DeterministicProps = Path.GetFullPath(Path.Combine(Repo, "eng/SharpCpp.Rbfx.Deterministic.props"));

    // The build of a shipped assembly maps the directories of this machine to fixed names. The generated projects
    // ignore the Directory.Build.props of the repository, so each build gets the props file and the roots as arguments.
    // The roots are the output directory of the union build, the repository and the SharpCpp checkout.
    static string[] PathMapArguments = [];
    static Layout CreateLayout(Options options)
    {
        var layout = new Layout(options);
        UseBuildRoot(layout.Output, layout.Sharp);
        return layout;
    }
    static void UseBuildRoot(string buildRoot, string sharpRoot) => PathMapArguments =
    [
        "-p:RbfxPackaging=true", $"-p:CustomAfterMicrosoftCommonProps={DeterministicProps}",
        $"-p:SharpCppBuildRoot={buildRoot}", $"-p:SharpCppRbfxRoot={Repo}", $"-p:SharpCppRoot={sharpRoot}"
    ];

    // All phases use the same paths. A CI job continues from the output directory of an earlier job,
    // so the generated projects and the build outputs are the same as in one create run.
    sealed class Layout(Options options)
    {
        public string Sharp { get; } = Path.GetFullPath(options.Optional("sharp-cpp-root") ?? Path.Combine(Repo, "../SharpCpp"));
        public string Inputs { get; } = Path.GetFullPath(options.Required("input"));
        public string Output { get; } = Path.GetFullPath(options.Optional("output") ?? Path.Combine(Repo, "build/package/union"));
        public string Dependencies => Path.Combine(Output, "dependencies");
        public string Sources => Path.Combine(Output, "sources");
        public string Stage => Path.Combine(Output, "stage");
        public string Packages => Path.Combine(Output, "packages");
        public string StateFile => Path.Combine(Output, "state.json");
        public string Implementation(string rid) => Path.Combine(Sources, rid, "output/SharpCpp.Rbfx.Bindings.dll");
        public string RuntimeLib(string rid, string framework) => Path.Combine(Stage, "runtime", rid, "lib", framework);
    }

    sealed record Tool(string Assembly, string FrameworkReference);

    // The reference phase writes this record to state.json. The later phases read it.
    sealed record State(string Framework, string AssemblyVersion, string Analyzer, string Reference, string[] Platforms);

    public static void Create(Options options)
    {
        var layout = CreateLayout(options);
        var version = options.Required("version");
        var tool = BuildTool(layout.Sharp);
        var state = CreateReference(layout, tool);
        var built = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        // Each platform builds in its own folder, so the builds run in parallel.
        Parallel.ForEach(state.Platforms, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) },
            rid => built[rid] = BuildImplementation(layout, tool, state, rid));
        var implementations = built.OrderBy(item => item.Key, StringComparer.Ordinal).ToDictionary(item => item.Key, item => item.Value);
        Validate(tool, state, implementations);
        var apicompat = InstallApiCompat(layout);
        foreach (var implementation in implementations.Values) CheckCompatibility(apicompat, tool, state, layout, implementation);
        foreach (var (rid, implementation) in implementations) StageRuntime(layout, state, rid, implementation);
        foreach (var rid in state.Platforms) PackRuntime(layout, state, version, rid);
        PackUnion(layout, state, version);
    }

    // Phase 1: the union API, the reference assembly and state.json.
    public static void Reference(Options options)
    {
        var layout = CreateLayout(options);
        CreateReference(layout, BuildTool(layout.Sharp));
    }

    // Phase 2: the implementation, ApiCompat and the runtime package of one RID.
    public static void Implement(Options options)
    {
        var layout = CreateLayout(options);
        var version = options.Required("version");
        var rid = options.Required("rid");
        var state = LoadState(layout);
        if (!state.Platforms.Contains(rid)) throw new ArgumentException($"{rid} is not a platform of the union reference.");
        var tool = BuildTool(layout.Sharp);
        IsolateProjects(layout.Sources);
        var implementation = BuildImplementation(layout, tool, state, rid);
        CheckCompatibility(InstallApiCompat(layout), tool, state, layout, implementation);
        StageRuntime(layout, state, rid, implementation);
        PackRuntime(layout, state, version, rid);
    }

    // Phase 3: cross-platform validation, the remaining packages and the consumer smoke test.
    public static void Package(Options options)
    {
        var layout = CreateLayout(options);
        var version = options.Required("version");
        var state = LoadState(layout);
        var implementations = state.Platforms.ToDictionary(rid => rid, rid =>
            File.Exists(layout.Implementation(rid)) && File.Exists(Path.Combine(layout.RuntimeLib(rid, state.Framework), "SharpCpp.Rbfx.Bindings.dll"))
                ? layout.Implementation(rid)
                : throw new InvalidOperationException($"Missing implementation of {rid}. Run implement for each platform."));
        Validate(BuildTool(layout.Sharp), state, implementations);
        PackUnion(layout, state, version);
    }

    static Tool BuildTool(string sharp)
    {
        var toolProject = Path.Combine(sharp, "src/SharpCpp.ApiUnion/SharpCpp.ApiUnion.csproj");
        Run("dotnet", ["build", toolProject, "-c", "Release", "--nologo"]);
        using var properties = JsonDocument.Parse(Run("dotnet", ["msbuild", toolProject, "-target:ResolveReferences",
            "-getProperty:TargetFramework", "-getItem:ReferencePath", "-p:Configuration=Release"], capture: true));
        var frameworkReference = properties.RootElement.GetProperty("Items").GetProperty("ReferencePath")
            .EnumerateArray().Select(item => item.GetProperty("FullPath").GetString()!)
            .Where(path => Path.GetFileName(path) == "System.Runtime.dll").Select(path => Path.GetDirectoryName(path)!).Single();
        return new Tool(Path.Combine(sharp, "build/bin/Release/SharpCpp.ApiUnion.dll"), frameworkReference);
    }

    static State CreateReference(Layout layout, Tool tool)
    {
        var inputs = layout.Inputs;
        var output = layout.Output;
        CreateEmptyDirectory(output);
        var platforms = Directory.GetDirectories(inputs, "managed-*")
            .Select(path => Path.GetFileName(path)["managed-".Length..]).Order(StringComparer.Ordinal).ToArray();
        if (platforms.Length == 0) throw new InvalidOperationException("No managed platform artifacts were found.");

        var dependencies = layout.Dependencies;
        Directory.CreateDirectory(dependencies);
        // New runs upload portable dependencies; older runs have a Linux managed package.
        var stageInputs = Directory.GetDirectories(inputs, "package-input-*")
            .SelectMany(path => Directory.Exists(Path.Combine(path, "lib"))
                ? Directory.GetDirectories(Path.Combine(path, "lib")) : []).Order(StringComparer.Ordinal).ToArray();
        string framework, analyzerSource;
        if (stageInputs.Length > 0)
        {
            framework = Path.GetFileName(stageInputs[0]);
            CopyDlls(stageInputs[0], dependencies);
            analyzerSource = Directory.GetDirectories(inputs, "package-input-*")
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
            analyzerSource = Path.Combine(seed, "analyzers/dotnet/cs/SharpCpp.Rbfx.SourceGenerator.dll");
        }
        // The later phases read the analyzer from the output directory, not from the inputs.
        var analyzer = Path.Combine(output, "analyzer", Path.GetFileName(analyzerSource));
        CopyDlls(Path.GetDirectoryName(analyzerSource)!, Path.GetDirectoryName(analyzer)!);

        var sources = layout.Sources;
        IsolateProjects(sources);
        Run("dotnet", [tool.Assembly, "emit", "--output", sources, "--references", tool.FrameworkReference,
            "--references", dependencies, .. platforms.Select(rid => $"{rid}={Path.Combine(inputs, "managed-" + rid, "SharpCpp.Rbfx.Bindings.dll")}")]);
        using var identity = JsonDocument.Parse(File.ReadAllText(Path.Combine(sources, "AssemblyIdentity.json")));
        var assemblyVersion = identity.RootElement.GetProperty("Version").GetString()!;
        var referenceProject = Path.Combine(sources, "UnionReference.csproj");
        Project(referenceProject, framework, [Path.Combine(sources, "Reference.g.cs")], dependencies,
            assemblyVersion, reference: true);
        Run("dotnet", ["build", referenceProject, "-c", "Release", "--nologo", .. PathMapArguments]);
        var built = Run("dotnet", ["msbuild", referenceProject, "-p:Configuration=Release", "-getProperty:TargetRefPath"], capture: true).Trim();
        // A copy outside obj/ lets the later phases use the reference without the reference build tree.
        var reference = Path.Combine(output, "reference", Path.GetFileName(built));
        Directory.CreateDirectory(Path.GetDirectoryName(reference)!);
        File.Copy(built, reference);
        var state = new State(framework, assemblyVersion, analyzer, reference, platforms);
        SaveState(layout, state);
        return state;
    }

    static string BuildImplementation(Layout layout, Tool tool, State state, string rid)
    {
        var sources = layout.Sources;
        var generated = Directory.GetFiles(Path.Combine(layout.Inputs, "generated-" + rid),
            "SharpCpp.Rbfx.Bindings.Aliases.g.cs", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(Path.GetDirectoryName(path)) == "rbfx").Single();
        var prepared = Path.Combine(sources, rid, "prepared");
        Run("dotnet", [tool.Assembly, "prepare", "--output", prepared,
            "--stubs", Path.Combine(sources, rid, "Stubs.g.cs"), "--concrete", Path.Combine(sources, rid, "ConcreteTypes.txt"),
            "--sources", Path.GetDirectoryName(generated)!, "--sources", Path.Combine(Repo, "SharpCpp.Rbfx.Bindings")]);
        var implementationProject = Path.Combine(sources, rid, $"UnionImplementation-{rid}.csproj");
        Project(implementationProject, state.Framework, Directory.GetFiles(prepared, "*.cs").Order(StringComparer.Ordinal),
            layout.Dependencies, state.AssemblyVersion, analyzer: state.Analyzer, staticRuntime: rid is "ios-arm64" or "browser-wasm",
            browserWasm: rid == "browser-wasm");
        Run("dotnet", ["build", implementationProject, "-c", "Release", "--nologo", .. PathMapArguments]);
        return layout.Implementation(rid);
    }

    static void Validate(Tool tool, State state, IReadOnlyDictionary<string, string> implementations) =>
        Run("dotnet", [tool.Assembly, "validate", $"ref={state.Reference}", .. implementations.Select(item => $"{item.Key}={item.Value}")]);

    static string InstallApiCompat(Layout layout)
    {
        var tools = Path.Combine(layout.Output, "tools");
        Run("dotnet", ["tool", "install", "Microsoft.DotNet.ApiCompat.Tool", "--version", "10.0.100", "--tool-path", tools]);
        return Path.Combine(tools, "apicompat");
    }

    static void CheckCompatibility(string apicompat, Tool tool, State state, Layout layout, string implementation) =>
        Run(apicompat, ["-l", state.Reference, "-r", implementation,
            "--left-assembly-references", $"{tool.FrameworkReference},{layout.Dependencies}",
            "--right-assembly-references", $"{tool.FrameworkReference},{layout.Dependencies}", "--enable-rule-cannot-change-parameter-name"]);

    static void StageRuntime(Layout layout, State state, string rid, string implementation)
    {
        var payload = Path.Combine(layout.Output, "runtime-" + rid);
        ExtractPackages(Directory.GetFiles(Path.Combine(layout.Inputs, "nuget-" + rid),
            "SharpCpp.Rbfx.Bindings.runtime.*.nupkg"), payload);
        var native = Path.Combine(payload, "runtimes", rid, "native");
        if (!Directory.Exists(native)) throw new InvalidOperationException($"Missing native package for {rid}.");
        CopyDirectory(native, Path.Combine(layout.Stage, "runtime", rid, "native"));
        // The runtime package of one RID carries the engine binding metadata that the user-code generator of a consumer reads,
        // and the umbrella header that the wrapper of the user code includes.
        var metadata = Path.Combine(payload, "runtimes", rid, "metadata");
        foreach (var name in new[] { "SharpCpp.Rbfx.Bindings.json", "SharpCpp.Rbfx.ParseUmbrella.hpp" })
            if (!File.Exists(Path.Combine(metadata, name))) throw new InvalidOperationException($"Missing {name} in the native package for {rid}.");
        CopyDirectory(metadata, Path.Combine(layout.Stage, "runtime", rid, "metadata"));
        var targets = Path.Combine(payload, "buildTransitive");
        if (Directory.Exists(targets)) CopyDirectory(targets, Path.Combine(layout.Stage, "runtime", rid, "buildTransitive"));
        var runtimeLib = layout.RuntimeLib(rid, state.Framework);
        Directory.CreateDirectory(runtimeLib);
        File.Copy(implementation, Path.Combine(runtimeLib, Path.GetFileName(implementation)));
    }

    // android-x64 and linux-x64 share one runtime package. PackUnion packs that group.
    static void PackRuntime(Layout layout, State state, string version, string rid)
    {
        if (rid is "android-x64" or "linux-x64") return;
        Directory.CreateDirectory(layout.Packages);
        Pack("SharpCpp.Rbfx.Bindings.Runtime", rid, version, state.Framework, layout.Stage, layout.Packages);
    }

    static void PackUnion(Layout layout, State state, string version)
    {
        var (stage, packages, framework, platforms) = (layout.Stage, layout.Packages, state.Framework, state.Platforms);
        Directory.CreateDirectory(packages);
        // Bindings has no duplicate lib/ implementation: it would override the selected RID DLL.
        CopyDlls(layout.Dependencies, Path.Combine(stage, "managed/lib", framework), exclude: "SharpCpp.Rbfx.Bindings.dll");
        CopyDlls(layout.Dependencies, Path.Combine(stage, "managed/ref", framework));
        CopyDlls(Path.GetDirectoryName(state.Analyzer)!, Path.Combine(stage, "managed/analyzers/dotnet/cs"));
        File.Copy(state.Reference, Path.Combine(stage, "managed/ref", framework, "SharpCpp.Rbfx.Bindings.dll"), overwrite: true);
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
        foreach (var path in Directory.GetDirectories(layout.Inputs, "nuget-*").SelectMany(path => Directory.GetFiles(path, "*.nupkg")))
            if (new[] { "SharpCpp.Rbfx.CoreData.", "SharpCpp.Rbfx.Data.", "SharpCpp.Rbfx.EditorData." }.Any(prefix => Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal)))
                File.Copy(path, Path.Combine(packages, Path.GetFileName(path)), overwrite: true);
        // UWP uses the same Windows toolchain and a custom RID graph, so its consumer is
        // verified by the Windows UWP example job rather than this portable RID smoke build.
        Smoke(packages, version, framework, platforms.Where(rid => rid != "uap-x64").ToArray());
        Console.WriteLine($"Union packages: {packages}");
    }

    // Paths in state.json are relative to the output directory.
    static void SaveState(Layout layout, State state)
    {
        using var stream = File.Create(layout.StateFile);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("Framework", state.Framework);
        writer.WriteString("AssemblyVersion", state.AssemblyVersion);
        writer.WriteString("Analyzer", Path.GetRelativePath(layout.Output, state.Analyzer));
        writer.WriteString("Reference", Path.GetRelativePath(layout.Output, state.Reference));
        writer.WriteStartArray("Platforms");
        foreach (var rid in state.Platforms) writer.WriteStringValue(rid);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    static State LoadState(Layout layout)
    {
        if (!File.Exists(layout.StateFile)) throw new InvalidOperationException($"Missing {layout.StateFile}. Run reference first.");
        using var json = JsonDocument.Parse(File.ReadAllText(layout.StateFile));
        var root = json.RootElement;
        string Value(string name) => root.GetProperty(name).GetString()!;
        return new State(Value("Framework"), Value("AssemblyVersion"), Path.Combine(layout.Output, Value("Analyzer")),
            Path.Combine(layout.Output, Value("Reference")), root.GetProperty("Platforms").EnumerateArray().Select(item => item.GetString()!).ToArray());
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
            "-p:SharpCppUnionPackage=true", .. PathMapArguments, .. dependencies is null ? Array.Empty<string>() : [$"-p:SharpCppUnionDependencies={dependencies}"]]);

    static void PackRuntimeGroup(string group, string primaryRid, string additionalRid, string version, string framework,
        string stage, string packages) =>
        Run("dotnet", ["pack", Path.Combine(Repo, "packaging/nuget/packages/SharpCpp.Rbfx.Bindings.Runtime", "SharpCpp.Rbfx.Bindings.Runtime.Package.csproj"),
            "-c", "Release", "--nologo", "--output", packages, "--source", packages, $"-p:PackageVersion={version}",
            $"-p:SharpCppNuGetStageRoot={stage}", $"-p:SharpCppRuntimeIdentifier={primaryRid}",
            $"-p:SharpCppAdditionalRuntimeIdentifier={additionalRid}", $"-p:SharpCppRuntimePackageId=SharpCpp.Rbfx.Bindings.runtime.{group}",
            $"-p:SharpCppPackageTargetFramework={framework}", "-p:SharpCppUnionPackage=true", .. PathMapArguments]);

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
    public const string Usage = "Expected create, reference, implement, pack or smoke.";
    public string Command => command;
    public string Required(string name) => Optional(name) ?? throw new ArgumentException($"Missing --{name}.");
    public string? Optional(string name) => values.GetValueOrDefault(name)?.Single();
    public IReadOnlyList<string> Values(string name) => values.GetValueOrDefault(name) ?? [];
    public static Options Parse(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException(Usage);
        var values = new Dictionary<string, List<string>>();
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Expected --option value, got {args[index]}.");
            var key = args[index][2..];
            if (key is not ("input" or "version" or "output" or "sharp-cpp-root" or "packages" or "framework" or "platform" or "rid"))
                throw new ArgumentException($"Unknown option: {args[index]}.");
            if (!values.TryGetValue(key, out var list)) values.Add(key, list = []);
            list.Add(args[index + 1]);
        }
        return new Options(args[0], values);
    }
}
