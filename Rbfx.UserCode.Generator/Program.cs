using SharpCpp.Toolchain;
using Microsoft.Extensions.Logging;
using SharpCpp.Metadata.Export;
using SharpCpp.Metadata.Import;
using SharpCpp.Model.Ast;
using SharpCpp.Model.Logging;
using SharpCpp.Parsing;
using SharpCpp.Pipeline;
using SharpCpp.Rendering;

namespace Rbfx.UserCode.Generator;

internal static class Program
{
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

    private static readonly ILogger Logger = SharpCppLog.For(nameof(Program));

    public static int Main(string[] args)
    {
        var options = GeneratorOptions.Parse(args);
        var fileWriter = options.LogFilePath is not null
            ? new StreamWriter(options.LogFilePath, append: false) { AutoFlush = true }
            : null;
        SharpCppLog.Configure(minLevel: LogLevel.Information, fileWriter: fileWriter);

        try
        {
            var sdkRoot = options.SdkRoot is null ? null : Path.GetFullPath(options.SdkRoot);
            var includeRoot = sdkRoot is null ? null : Path.Combine(sdkRoot, "include");
            var sharpCppIncludeRoot = Path.GetFullPath(options.SharpCppIncludeRoot);
            var rbfxIncludeRoot = Path.GetFullPath(options.RbfxIncludeRoot);
            var userIncludeRoots = options.IncludeDirectories.Select(Path.GetFullPath).ToArray();
            var headerPath = Path.GetFullPath(options.HeaderPath);
            if (!File.Exists(headerPath))
            {
                throw new InvalidOperationException($"User code header was not found at '{headerPath}'.");
            }

            var headerIncludePath = GetHeaderIncludePath(headerPath, userIncludeRoots);

            var sdkFlags = SdkParseFlags.Load(options.FlagsFilePath);
            var marshallerHeaderPaths = new List<string>
            {
                Path.Combine(sharpCppIncludeRoot, "SharpCpp", "Marshaller.hpp"),
                Path.Combine(rbfxIncludeRoot, "SharpCpp", "detail", "RbfxRefCountedMarshallers.hpp"),
                Path.Combine(rbfxIncludeRoot, "SharpCpp", "detail", "RbfxValueTypeMarshallers.hpp"),
                Path.Combine(rbfxIncludeRoot, "SharpCpp", "detail", "RbfxSignalMarshallers.hpp")
            };
            if (sdkFlags.HasDefine("URHO3D_SYSTEMUI", "1"))
            {
                marshallerHeaderPaths.Add(Path.Combine(rbfxIncludeRoot, "SharpCpp", "detail", "RbfxImGuiValueMarshallers.hpp"));
            }
            if (sdkFlags.HasDefine("URHO3D_RMLUI", "1"))
            {
                marshallerHeaderPaths.Add(Path.Combine(rbfxIncludeRoot, "SharpCpp", "detail", "RbfxRmlMathMarshallers.hpp"));
            }

            var marshallerConfigurationFiles = marshallerHeaderPaths.Select(Path.GetFullPath).ToArray();

            Logger.LogInformation("rbfx SDK root: {SdkRoot}", sdkRoot ?? "(flags file only)");
            Logger.LogInformation("Flags file: {FlagsFilePath}", options.FlagsFilePath);
            Logger.LogInformation("User code header: {HeaderPath}", headerPath);
            Logger.LogInformation("Generated output root: {GeneratedRoot}", options.GeneratedRoot);

            var importedBindings = BindingMetadataImportCatalog.Load(options.ImportedBindingMetadataFiles);
            foreach (var metadataFile in options.ImportedBindingMetadataFiles)
            {
                Logger.LogInformation("Imported binding metadata: '{MetadataFile}'", metadataFile);
            }

            var clangArguments = BuildClangArguments([sharpCppIncludeRoot, rbfxIncludeRoot], includeRoot, userIncludeRoots, sdkFlags);
            var parser = new ClangSharpHeaderParser();
            var compilation = parser.Parse(
                new ClangSharpParseRequest(
                    options.AssemblyName,
                    options.ManagedNamespace,
                    options.BindingsClassName,
                    options.LibraryName,
                    headerPath,
                    headerIncludePath,
                    clangArguments,
                    marshallerConfigurationFiles,
                    options.SymbolPrefix,
                    OwnedFileRoots: userIncludeRoots,
                    ImportedBindingMetadata: importedBindings));

            // The root C++ namespace of the wrapped headers has the same name as the managed namespace.
            compilation.NamespaceMappings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Urho3D"] = "Urho3D",
                [options.ManagedNamespace] = options.ManagedNamespace
            };
            foreach (var marshallerHeader in marshallerConfigurationFiles)
            {
                compilation.NativeSupportHeaders.Add(marshallerHeader.Replace('\\', '/'));
            }

            BindingCompilationBootstrapPipeline.Apply(compilation, marshallerConfigurationFiles, clangArguments);

            var renderedBindings = BindingOutputRenderer.Render(compilation, options.FileStem);
            var metadataDocument = BindingMetadataExporter.Export(
                compilation,
                new BindingMetadataExportOptions(
                    PackageId: options.AssemblyName,
                    Phase2ClosureHash: $"{options.FileStem}-phase2",
                    ClangArgsHash: "rbfx-usercode-clang",
                    Imports: importedBindings.Imports));

            Directory.CreateDirectory(options.GeneratedRoot);
            renderedBindings.WriteToDirectory(options.GeneratedRoot);
            File.WriteAllText(
                Path.Combine(options.GeneratedRoot, $"{options.FileStem}.Bindings.json"),
                BindingMetadataSerializer.Serialize(metadataDocument));

            Logger.LogInformation("Generated user-code bindings for {Retained} retained local record(s).", compilation.Records.Count(record => !record.IsIgnored() && !record.GetMetadata<bool>(MetadataKeys.SuppressEmission)));
            return 0;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Unhandled exception in user-code generator");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static string GetHeaderIncludePath(string headerPath, IReadOnlyList<string> includeRoots)
    {
        foreach (var includeRoot in includeRoots)
        {
            var relativePath = Path.GetRelativePath(includeRoot, headerPath);
            var escapes = relativePath == ".."
                || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relativePath.StartsWith("../", StringComparison.Ordinal);
            if (!escapes && !Path.IsPathRooted(relativePath))
            {
                return relativePath.Replace('\\', '/');
            }
        }

        throw new InvalidOperationException($"Header '{headerPath}' is not inside any --include-directory.");
    }

    private static IReadOnlyList<string> BuildClangArguments(IReadOnlyList<string> projectIncludeRoots, string? sdkIncludeRoot, IReadOnlyList<string> userIncludeRoots, SdkParseFlags sdkFlags)
    {
        var arguments = new List<string>
        {
            "-xc++",
            $"-std={sdkFlags.LanguageStandard}",
        };

        foreach (var userIncludeRoot in userIncludeRoots)
        {
            arguments.Add($"-I{userIncludeRoot}");
        }

        arguments.AddRange(ParseWarningDisableArguments.Where(argument => !arguments.Contains(argument, StringComparer.Ordinal)));

        if (sdkIncludeRoot is not null && !sdkFlags.IncludeDirectories.Contains(sdkIncludeRoot, StringComparer.Ordinal))
        {
            arguments.Add($"-I{sdkIncludeRoot}");
        }

        foreach (var includeDirectory in sdkFlags.IncludeDirectories)
        {
            arguments.Add($"-I{includeDirectory}");
        }

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
            clangResourceDirectoryOverride: Environment.GetEnvironmentVariable("SHARPCPP_RBFX_CLANG_RESOURCE_DIRECTORY"));
        if (IsX86Target(toolchainInfo.TargetTriple))
            arguments.AddRange(ParseSimdDisableArguments);
        else if (IsArmTarget(toolchainInfo.TargetTriple))
            arguments.AddRange(ParseNeonDisableArguments);

        arguments.AddRange(toolchainInfo.GetClangArguments(arguments));

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
}
