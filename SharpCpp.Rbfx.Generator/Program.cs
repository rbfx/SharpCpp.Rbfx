using SharpCpp.Toolchain;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SharpCpp.Metadata.Export;
using SharpCpp.Model.Logging;
using SharpCpp.Model.Ast;
using SharpCpp.Parsing;
using SharpCpp.Pipeline;
using SharpCpp.Rendering;

namespace SharpCpp.Rbfx.Generator;

internal static class Program
{
    private static readonly ILogger Logger = SharpCppLog.For(nameof(Program));

    private static readonly string[] ParseCompatibilityDefines =
    [
        "SDL_DISABLE_MM3DNOW_H=1",
        "SDL_DISABLE_LSX_H=1",
        "SDL_DISABLE_LASX_H=1",
        "SDL_DISABLE_IMMINTRIN_H=1",
        "SDL_DISABLE_MMINTRIN_H=1",
        "SDL_DISABLE_XMMINTRIN_H=1",
        "SDL_DISABLE_EMMINTRIN_H=1",
        "SDL_DISABLE_PMMINTRIN_H=1",
        "IMGUI_DISABLE_SSE=1"
    ];

    private static readonly string[] ParseSimdDisableArguments =
    [
        "-mno-sse",
        "-mno-sse2",
        "-mno-sse3",
        "-mno-ssse3",
        "-mno-sse4.1",
        "-mno-sse4.2",
        "-mno-avx",
        "-mno-avx2",
        "-mno-avx512f",
        "-mno-avx512bw",
        "-mno-avx512cd",
        "-mno-avx512dq",
        "-mno-avx512vl"
    ];

    // The pinned Clang builtin headers have no arm_neon.h, because the LLVM build generates it.
    private static readonly string[] ParseNeonDisableArguments =
    [
        "-U__ARM_NEON",
        "-U__ARM_NEON__",
        "-DSDL_DISABLE_ARM_NEON_H=1"
    ];

    private static readonly string[] ParseWarningDisableArguments =
    [
        "-Wno-deprecated-literal-operator",
        "-Wno-pragma-once-outside-header"
    ];

    private const string ManagedNamespace = "Urho3D";
    private const string ManagedClassName = "Bindings";
    private const string ProjectName = "SharpCpp.Rbfx.Bindings";
    private const string GeneratedFileStem = "SharpCpp.Rbfx";
    private const string EntryPointPrefix = "scrbfx";
    private static readonly AccessorPropertyPattern[] Urho3DAccessorPropertyPatterns =
    [
        new("Get", "Set", allowGetterOnly: true),
        new("Is", "Set", propertyNamePrefix: "Is", allowGetterOnly: true)
    ];

    private static readonly HashSet<string> ReservedUrho3DPropertyGetterNames = new(StringComparer.Ordinal)
    {
        "GetInstance",
        "GetHashCode",
        "GetRuntimeApi",
        "GetType",
        "GetTypeInfo",
        "GetTypeInfoStatic",
        "GetTypeName",
        "GetTypeNameStatic",
        "GetTypeStatic",
        "GetLevelsDirty",
        "GetResolveDirty"
    };

    private static readonly HashSet<string> InheritableDirectorRecordCppTypeNames = new(StringComparer.Ordinal)
    {
        "Urho3D::Object",
        "Urho3D::Application",
        "Urho3D::PluginApplication",
        "Urho3D::ApplicationState",
        "Urho3D::Serializable",
        "Urho3D::Animatable",
        "Urho3D::Component",
        "Urho3D::LogicComponent",
        "Urho3D::Drawable",
        "Urho3D::Drawable2D",
        "Urho3D::Resource",
        "Urho3D::TriggerAnimator",
        "Urho3D::RmlUIComponent",
        "Urho3D::ActionCallHandler",
        "Urho3D::EventHandler",
    };

    public static int Main(string[] args)
    {
        var options = GeneratorOptions.Parse(args);
        var fileWriter = options.LogFilePath is not null
            ? new StreamWriter(options.LogFilePath, append: false) { AutoFlush = true }
            : null;
        SharpCppLog.Configure(minLevel: LogLevel.Information, fileWriter: fileWriter);

        try
        {
            Logger.LogInformation("=== SharpCpp rbfx generator started ===");
            var mainSw = Stopwatch.StartNew();
            var sdkRoot = Path.GetFullPath(options.SdkRoot);
            var includeRoot = Path.Combine(sdkRoot, "include");
            var urhoIncludeRoot = Path.Combine(includeRoot, "Urho3D");
            if (!Directory.Exists(urhoIncludeRoot))
            {
                throw new InvalidOperationException($"rbfx SDK include directory was not found at '{urhoIncludeRoot}'.");
            }

            var sharpCppIncludeRoot = Path.GetFullPath(options.SharpCppIncludeRoot);
            var rbfxIncludeRoot = Path.GetFullPath(options.RbfxIncludeRoot);
            var marshallerConfigurationFiles = new List<string>
            {
                Path.Combine(sharpCppIncludeRoot, "SharpCpp", "Marshaller.hpp"),
                Path.Combine(rbfxIncludeRoot, "SharpCpp", "detail", "RbfxValueTypeMarshallers.hpp")
            };

            Logger.LogInformation("rbfx SDK root: {SdkRoot}", sdkRoot);
            Logger.LogInformation("SDK flags file: {FlagsFilePath}", options.FlagsFilePath);
            Logger.LogInformation("Generated output root: {GeneratedRoot}", options.GeneratedRoot);

            var sdkFlags = SdkParseFlags.Load(options.FlagsFilePath);
            var submodules = RbfxBindingSubmodules.Detect(sdkFlags);
            if (submodules.HasSystemUi)
            {
                marshallerConfigurationFiles.Add(Path.Combine(rbfxIncludeRoot, "SharpCpp", "detail", "RbfxImGuiValueMarshallers.hpp"));
            }

            Logger.LogInformation("RmlUI bindings: {State}", submodules.HasRmlUi ? "enabled" : "disabled");
            Logger.LogInformation("Dear ImGui bindings: {State}", submodules.HasSystemUi ? "enabled" : "disabled");

            var clangArguments = BuildClangArguments(
                [sharpCppIncludeRoot, rbfxIncludeRoot], includeRoot, sdkFlags, out var targetTriple);
            var parseUmbrellaHeaderPath = submodules.WriteParseUmbrella(options.GeneratedRoot, urhoIncludeRoot);

            Logger.LogInformation("Parsing umbrella header '{Header}' with owned declaration root '{Root}'.", parseUmbrellaHeaderPath, urhoIncludeRoot);

            var parseSw = Stopwatch.StartNew();
            var parser = new ClangSharpHeaderParser();
            var compilation = parser.Parse(
                new ClangSharpParseRequest(
                    ProjectName,
                     ManagedNamespace,
                     ManagedClassName,
                     sdkFlags.NativeLibraryName,
                     parseUmbrellaHeaderPath,
                     Path.GetFileName(parseUmbrellaHeaderPath),
                     clangArguments,
                     marshallerConfigurationFiles,
                     EntryPointPrefix,
                     OwnedFileRoots: submodules.BuildOwnedFileRoots(includeRoot),
                     OwnedFileRootIncludePrefixes: submodules.BuildOwnedFileRootIncludePrefixes(includeRoot),
                     DirectorRecordCppTypeNames: InheritableDirectorRecordCppTypeNames));
            foreach (var headerPath in submodules.EnabledHeaderPaths)
            {
                if (!compilation.HeaderFiles.Contains(headerPath, StringComparer.Ordinal))
                {
                    compilation.HeaderFiles.Add(headerPath);
                }
            }
            Logger.LogInformation("Parse complete: {Records} records, {Functions} functions.", compilation.Records.Count, compilation.Functions.Count);
            Logger.LogInformation("Parse completed in {Duration}", FormatDuration(parseSw.Elapsed));

            foreach (var marshallerHeader in marshallerConfigurationFiles)
            {
                compilation.NativeSupportHeaders.Add(marshallerHeader.Replace('\\', '/'));
            }

            compilation.NamespaceMappings = BuildNamespaceMappings(compilation, submodules);

            Logger.LogInformation("Applying pipeline passes...");
            var pipelineSw = Stopwatch.StartNew();
            var taggedPointerRangeCount = 0;
            var explicitManagedNameCount = 0;
            BindingCompilationBootstrapPipeline.Apply(
                compilation,
                marshallerConfigurationFiles,
                clangArguments,
                configureBeforeValidation: c =>
                {
                    taggedPointerRangeCount = RbfxPointerRangesPass.Apply(c);
                    explicitManagedNameCount = RbfxManagedNamesPass.Apply(c);
                });
            Logger.LogInformation("Applied {Count} explicit rbfx managed member name(s).", explicitManagedNameCount);
            Logger.LogInformation("Tagged {Count} rbfx pointer range(s).", taggedPointerRangeCount);
            Logger.LogInformation("Pipeline complete: {Retained}/{Total} records retained.", compilation.Records.Count(r => !r.IsIgnored()), compilation.Records.Count);
            Logger.LogInformation("Pipeline completed in {Duration}", FormatDuration(pipelineSw.Elapsed));

            var silentExclusionCount = RbfxSilentExclusionPass.Apply(compilation);
            Logger.LogInformation("Silently excluded {Count} rbfx non-public implementation declaration(s).", silentExclusionCount);

            var unsupportedApiCount = RbfxUnsupportedApisPass.Apply(
                compilation, targetTriple?.Contains("emscripten", StringComparison.OrdinalIgnoreCase) == true);
            Logger.LogInformation("Suppressed {Count} rbfx API(s) with known unsupported linkage or surface constraints.", unsupportedApiCount);

            var sdkFilterSw = Stopwatch.StartNew();
            ApplySdkExportFilter(compilation, options.SdkLibraryPath, sdkFlags.SymbolToolPath);
            Logger.LogInformation("SDK filter complete.");
            Logger.LogInformation("SDK filter completed in {Duration}", FormatDuration(sdkFilterSw.Elapsed));

            if (submodules.HasRmlUi)
            {
                var rmlCount = RbfxRmlUiSurfacePass.Apply(compilation);
                Logger.LogInformation("Applied RmlUI surface shaping to {Count} declaration(s).", rmlCount);
            }

            if (submodules.HasSystemUi)
            {
                var imguiCount = RbfxImGuiSurfacePass.Apply(compilation);
                Logger.LogInformation("Applied Dear ImGui surface shaping to {Count} declaration(s).", imguiCount);
            }

            RbfxFlagSetLoweringPass.Apply(compilation);
            ApplyPeSdkExportFilter(compilation, options.SdkLibraryPath);
            var managedProjection = RbfxManagedNamespaceProjectionPass.Apply(compilation);
            Logger.LogInformation("Managed namespace projection complete: {NamespaceCount} namespace node(s).", managedProjection.Namespaces.Count);

            var macroSurfaceCount = RbfxNamespaceMacroSurfacePass.Apply(compilation, includeRoot);
            Logger.LogInformation("Applied rbfx macro surface shaping to {Count} header class(es).", macroSurfaceCount);

            var propertyCount = GenerateAccessorPropertiesPass.Apply(
                compilation,
                Urho3DAccessorPropertyPatterns,
                recordFilter: static record => record.CppQualifiedName.StartsWith("Urho3D::", StringComparison.Ordinal),
                getterFilter: function => !ReservedUrho3DPropertyGetterNames.Contains(function.ManagedName));
            Logger.LogInformation("Promoted {Count} rbfx accessor propert(ies).", propertyCount);

            Logger.LogInformation("Rendering...");
            var renderSw = Stopwatch.StartNew();
            var renderedBindings = BindingOutputRenderer.Render(compilation, GeneratedFileStem, DeriveGroupStem);
            Logger.LogInformation("Rendering complete.");
            Logger.LogInformation("Render completed in {Duration}", FormatDuration(renderSw.Elapsed));
            var metadataDocument = BindingMetadataExporter.Export(
                compilation,
                new BindingMetadataExportOptions(
                    PackageId: ProjectName,
                    Phase2ClosureHash: "rbfx-bootstrap-phase2",
                    ClangArgsHash: "rbfx-bootstrap-clang"));

            var exportSw = Stopwatch.StartNew();
            Directory.CreateDirectory(options.GeneratedRoot);
            renderedBindings.WriteToDirectory(options.GeneratedRoot);
            File.WriteAllText(
                Path.Combine(options.GeneratedRoot, $"{GeneratedFileStem}.Bindings.json"),
                BindingMetadataSerializer.Serialize(metadataDocument));
            Logger.LogInformation("Metadata export + file write completed in {Duration}", FormatDuration(exportSw.Elapsed));

            Logger.LogInformation("Generated rbfx bindings for {Retained} retained record(s) from {Total} ingested record declaration(s).", compilation.Records.Count(record => !record.IsIgnored()), compilation.Records.Count);

            var warnedRecords = compilation.Records.Where(record => record.GetWarnings().Count > 0).ToArray();
            foreach (var record in warnedRecords)
            {
                foreach (var warning in record.GetWarnings())
                {
                    Logger.LogWarning("Record '{CppQualifiedName}': {Warning}", record.CppQualifiedName, warning);
                }
            }

            Logger.LogInformation("Total generation completed in {Duration}", FormatDuration(mainSw.Elapsed));
            return 0;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Unhandled exception in rbfx generator");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static string FormatDuration(TimeSpan elapsed) => $"{elapsed.TotalSeconds:F1}s";

    private static IReadOnlyList<string> BuildClangArguments(
        IReadOnlyList<string> projectIncludeRoots,
        string includeRoot,
        SdkParseFlags sdkFlags,
        out string? targetTriple)
    {
        var arguments = new List<string>
        {
            "-xc++",
            $"-std={sdkFlags.LanguageStandard}"
        };

        arguments.AddRange(ParseWarningDisableArguments.Where(argument => !arguments.Contains(argument, StringComparer.Ordinal)));

        if (!sdkFlags.IncludeDirectories.Contains(includeRoot, StringComparer.Ordinal))
        {
            arguments.Add($"-I{includeRoot}");
        }

        foreach (var includeDirectory in sdkFlags.IncludeDirectories)
        {
            arguments.Add($"-I{includeDirectory}");
        }

        // SharpCpp and rbfx marshaller include roots let configuration headers resolve
        // <SharpCpp/Marshaller.hpp> and <SharpCpp/detail/Rbfx*.hpp>.
        foreach (var projectInclude in projectIncludeRoots)
        {
            if (!arguments.Contains($"-I{projectInclude}", StringComparer.Ordinal))
            {
                arguments.Add($"-I{projectInclude}");
            }
        }

        foreach (var define in sdkFlags.Defines)
        {
            arguments.Add($"-D{define}");
        }

        foreach (var define in ParseCompatibilityDefines)
        {
            var argument = $"-D{define}";
            if (!arguments.Contains(argument, StringComparer.Ordinal))
            {
                arguments.Add(argument);
            }
        }

        arguments.AddRange(sdkFlags.AdditionalArguments);

        var toolchainInfo = CompilerToolchainDiscovery.Discover(
            sdkFlags.CompilerPath,
            targetTripleOverride: sdkFlags.TargetTriple,
            sysroot: sdkFlags.Sysroot,
            clangResourceDirectoryOverride: Environment.GetEnvironmentVariable("SHARPCPP_RBFX_CLANG_RESOURCE_DIRECTORY"));
        targetTriple = toolchainInfo.TargetTriple;
        if (IsX86Target(toolchainInfo.TargetTriple))
            arguments.AddRange(ParseSimdDisableArguments);
        else if (IsArmTarget(toolchainInfo.TargetTriple))
            arguments.AddRange(ParseNeonDisableArguments);

        arguments.AddRange(toolchainInfo.GetClangArguments(arguments, sdkFlags.Sysroot));

        return arguments.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool IsArmTarget(string? targetTriple)
    {
        return targetTriple is not null &&
            (targetTriple.StartsWith("arm", StringComparison.OrdinalIgnoreCase) ||
             targetTriple.StartsWith("aarch64", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsX86Target(string? targetTriple)
    {
        if (string.IsNullOrEmpty(targetTriple))
            return true;

        foreach (var prefix in new[] { "x86_64", "amd64", "i386", "i486", "i586", "i686" })
        {
            if (targetTriple.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void ApplySdkExportFilter(BindingCompilation compilation, string? sdkLibraryPath, string? symbolToolPath)
    {
        if (string.IsNullOrWhiteSpace(sdkLibraryPath) || !File.Exists(sdkLibraryPath))
        {
            return;
        }

        // The filter for a Windows library runs after the passes that clear an ignore reason.
        if (IsPortableExecutable(sdkLibraryPath))
        {
            return;
        }

        var exportedSymbols = LoadExportedSymbols(sdkLibraryPath, symbolToolPath);
        if (exportedSymbols.Count == 0)
        {
            return;
        }

        var removedCount = 0;
        foreach (var function in compilation.Functions.Where(static function => (function.Parent is null || function.IsStatic) && !function.IsIgnored()))
        {
            if (!function.CppQualifiedName.Contains("::", StringComparison.Ordinal))
            {
                continue;
            }

            if (function.GetMetadata<bool>(MetadataKeys.HasNativeDefinition))
            {
                continue;
            }

            if (exportedSymbols.Any(symbol => symbol.Contains($"{function.CppQualifiedName}(", StringComparison.Ordinal)))
            {
                continue;
            }

            function.SetIgnoreReason($"function '{function.CppQualifiedName}' is not exported by the linked rbfx SDK library");
            removedCount++;
        }

        if (removedCount > 0)
        {
            Logger.LogInformation("Ignored {Count} function(s) that are not exported by '{LibraryPath}'.", removedCount, sdkLibraryPath);
        }
    }

    private static bool IsPortableExecutable(string sdkLibraryPath)
    {
        try
        {
            return SdkPeExportTable.IsPortableExecutable(sdkLibraryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // nm cannot read a Windows DLL on a Windows host, and a DLL exports only the declarations
    // with URHO3D_API. The filter reads the export table in process and has a wider scope.
    private static void ApplyPeSdkExportFilter(BindingCompilation compilation, string? sdkLibraryPath)
    {
        if (string.IsNullOrWhiteSpace(sdkLibraryPath) || !File.Exists(sdkLibraryPath) || !IsPortableExecutable(sdkLibraryPath))
        {
            return;
        }

        SdkPeExportTable exports;
        try
        {
            exports = SdkPeExportTable.Load(sdkLibraryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            Logger.LogWarning("Could not read the exported symbols of '{LibraryPath}': {Error}", sdkLibraryPath, exception.Message);
            return;
        }

        if (exports.QualifiedNameCount == 0)
        {
            Logger.LogWarning("Could not read the exported symbols of '{LibraryPath}': the export table has no C++ name.", sdkLibraryPath);
            return;
        }

        Logger.LogInformation(
            "Read {ExportCount} export(s) from '{LibraryPath}': {NameCount} C++ name(s), {VirtualTableCount} virtual table(s).",
            exports.ExportCount,
            sdkLibraryPath,
            exports.QualifiedNameCount,
            exports.VirtualTableCount);

        var result = SdkPeExportFilter.Apply(compilation, exports);
        if (result.Total > 0)
        {
            Logger.LogInformation(
                "Ignored {FreeFunctions} free function(s), {StaticMethods} static method(s), {InstanceMethods} instance method(s), {Constructors} constructor(s) and {StaticFields} static field(s) that are not exported by '{LibraryPath}'. " +
                "Removed the constructors ({RecordConstructors}), the clone and the destroy thunk of {DestructorRecords} record(s) with a destructor dependency and {VirtualTableRecords} record(s) with a virtual table dependency that is not exported. " +
                "Ignored {ByValueFunctions} function(s) that pass such a record by value.",
                result.FreeFunctions,
                result.StaticMethods,
                result.InstanceMethods,
                result.Constructors,
                result.StaticFields,
                sdkLibraryPath,
                result.RecordConstructors,
                result.DestructorRecords,
                result.VirtualTableRecords,
                result.ByValueFunctions);
        }
    }

    private static IReadOnlyList<string> LoadExportedSymbols(string sdkLibraryPath, string? symbolToolPath)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = symbolToolPath ?? "nm",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            // A Mach-O library has no dynamic symbol table, so nm rejects -D for it.
            startInfo.ArgumentList.Add(OperatingSystem.IsMacOS() || sdkLibraryPath.EndsWith(".a", StringComparison.OrdinalIgnoreCase)
                ? "--extern-only" : "-D");
            startInfo.ArgumentList.Add("--defined-only");
            startInfo.ArgumentList.Add("--demangle");
            startInfo.ArgumentList.Add(sdkLibraryPath);

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return Array.Empty<string>();
            }

            process.StandardInput.Close();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
            {
                process.Kill(entireProcessTree: true);
                Logger.LogWarning("Could not read the exported symbols of '{LibraryPath}': nm timed out.", sdkLibraryPath);
                return Array.Empty<string>();
            }

            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                Logger.LogWarning("Could not read the exported symbols of '{LibraryPath}': {Error}", sdkLibraryPath, error.Trim());
                return Array.Empty<string>();
            }

            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch (Exception exception)
        {
            Logger.LogWarning("Could not read the exported symbols of '{LibraryPath}': {Error}", sdkLibraryPath, exception.Message);
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyDictionary<string, string> BuildNamespaceMappings(BindingCompilation compilation, RbfxBindingSubmodules submodules)
    {
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Urho3D"] = "Urho3D"
        };

        if (submodules.HasRmlUi)
        {
            mappings["Rml"] = "Rml";
        }

        if (submodules.HasSystemUi)
        {
            mappings["ImGui"] = "ImGuiNet";
        }

        foreach (var cppQualifiedName in compilation.Records.Select(static record => record.CppQualifiedName)
                     .Concat(compilation.Enums.Select(static @enum => @enum.CppQualifiedName)))
        {
            var lastSeparator = cppQualifiedName.LastIndexOf("::", StringComparison.Ordinal);
            if (lastSeparator < 0)
            {
                continue;
            }

            var cppNamespace = cppQualifiedName[..lastSeparator];
            if (cppNamespace.StartsWith("Urho3D::", StringComparison.Ordinal) && !mappings.ContainsKey(cppNamespace))
            {
                mappings[cppNamespace] = "Urho3D";
            }
        }

        return mappings;
    }

    private static string DeriveGroupStem(string? headerPath)
    {
        if (string.IsNullOrWhiteSpace(headerPath))
        {
            return $"{GeneratedFileStem}.Bindings";
        }

        var normalizedHeaderPath = headerPath.Replace('\\', '/');
        if (normalizedHeaderPath.StartsWith("Urho3D/", StringComparison.Ordinal))
        {
            normalizedHeaderPath = normalizedHeaderPath["Urho3D/".Length..];
        }

        if (normalizedHeaderPath.StartsWith("RmlUi/", StringComparison.Ordinal))
        {
            normalizedHeaderPath = $"ThirdParty/{normalizedHeaderPath}";
        }
        else if (normalizedHeaderPath.StartsWith("ImGui/", StringComparison.Ordinal))
        {
            normalizedHeaderPath = $"ThirdParty/{normalizedHeaderPath}";
        }

        var pathWithoutExtension = Path.ChangeExtension(normalizedHeaderPath, null)!.Replace('\\', '/');
        var pathParts = pathWithoutExtension.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathParts.Length == 0)
        {
            return $"{GeneratedFileStem}.Bindings";
        }

        if (pathParts.Length >= 3 && string.Equals(pathParts[0], "ThirdParty", StringComparison.Ordinal))
        {
            return $"ThirdParty/{pathParts[1]}/{GeneratedFileStem}.{string.Join('.', pathParts.Skip(2))}";
        }

        if (pathParts.Length == 1)
        {
            return $"{GeneratedFileStem}.{pathParts[0]}";
        }

        return $"{pathParts[0]}/{GeneratedFileStem}.{string.Join('.', pathParts.Skip(1))}";
    }
}
