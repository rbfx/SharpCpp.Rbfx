namespace SharpCpp.Rbfx.Generator;

internal sealed class GeneratorOptions
{
    public string? SdkRoot { get; init; }

    public IReadOnlyList<KeyValuePair<string, string>> EngineHeaderRoots { get; init; } = [];

    public string? EngineHeaderListPath { get; init; }

    public required string FlagsFilePath { get; init; }

    public required string GeneratedRoot { get; init; }

    public required string SharpCppIncludeRoot { get; init; }

    public required string RbfxIncludeRoot { get; init; }

    public string? SdkLibraryPath { get; init; }

    public string? LogFilePath { get; init; }

    public RbfxEngineHeaderLayout CreateHeaderLayout() =>
        SdkRoot is not null
            ? RbfxEngineHeaderLayout.FromSdkRoot(SdkRoot)
            : RbfxEngineHeaderLayout.FromHeaderRoots(EngineHeaderRoots, EngineHeaderListPath);

    public static GeneratorOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args.Any(static argument => argument is "--help" or "-h"))
        {
            PrintUsage();
            Environment.Exit(0);
        }

        var sdkRoot = GetOptionalPathOption(args, "--sdk-root", isDirectory: true);
        var engineHeaderRoots = GetEngineHeaderRoots(args);
        if ((sdkRoot is null) == (engineHeaderRoots.Count == 0))
        {
            throw new ArgumentException("Specify exactly one of '--sdk-root' or '--engine-header-root'.");
        }

        var engineHeaderListPath = GetOptionalPathOption(args, "--engine-header-list", isDirectory: false);
        if (engineHeaderListPath is not null && sdkRoot is not null)
        {
            throw new ArgumentException("'--engine-header-list' is valid only with '--engine-header-root'.");
        }

        var flagsFilePath = GetRequiredPathOption(args, "--flags-file", isDirectory: false);
        var generatedRoot = GetRequiredPathOption(args, "--generated-root", isDirectory: false, mustExist: false);
        var sharpCppIncludeRoot = GetRequiredPathOption(args, "--sharpcpp-include", isDirectory: true);
        var rbfxIncludeRoot = GetRequiredPathOption(args, "--rbfx-include", isDirectory: true);
        var sdkLibraryPath = GetOptionalPathOption(args, "--sdk-library", isDirectory: false);
        var logFilePath = GetOptionalPathOption(args, "--log-file", isDirectory: false, mustExist: false);

        ValidateKnownOptions(args, new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["--sdk-root"] = true,
            ["--engine-header-root"] = true,
            ["--engine-header-list"] = true,
            ["--flags-file"] = true,
            ["--generated-root"] = true,
            ["--sharpcpp-include"] = true,
            ["--rbfx-include"] = true,
            ["--sdk-library"] = true,
            ["--log-file"] = true
        });

        return new GeneratorOptions
        {
            SdkRoot = sdkRoot,
            EngineHeaderRoots = engineHeaderRoots,
            EngineHeaderListPath = engineHeaderListPath,
            FlagsFilePath = flagsFilePath,
            GeneratedRoot = generatedRoot,
            SharpCppIncludeRoot = sharpCppIncludeRoot,
            RbfxIncludeRoot = rbfxIncludeRoot,
            SdkLibraryPath = sdkLibraryPath,
            LogFilePath = logFilePath
        };
    }

    public static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: dotnet SharpCpp.Rbfx.Generator.dll (--sdk-root <path> | --engine-header-root <Name>=<dir> [--engine-header-root <Name>=<dir> ...] [--engine-header-list <file>]) --flags-file <path> --generated-root <path> --sharpcpp-include <path> --rbfx-include <path> [--sdk-library <path>] [--log-file <path>]");
        Console.WriteLine(
            "Engine header root names: Urho3D (repeatable, at least one), RmlUi, RmlUiConfig, ImGui.");
        Console.WriteLine(
            "--engine-header-list: a file with one absolute header path per line. With engine header roots, the generator scans only these Urho3D headers (the umbrella fallback and the namespace macro pass), not the whole root. Use it to skip the headers of disabled engine modules.");
    }

    private static void ValidateKnownOptions(IReadOnlyList<string> args, IReadOnlyDictionary<string, bool> knownOptions)
    {
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (!knownOptions.TryGetValue(argument, out var requiresValue))
            {
                throw new ArgumentException($"Unknown option '{argument}'.");
            }

            if (!requiresValue)
            {
                continue;
            }

            if (index == args.Count - 1 || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Missing value for option '{argument}'.");
            }

            index++;
        }
    }

    private static List<KeyValuePair<string, string>> GetEngineHeaderRoots(IReadOnlyList<string> args)
    {
        var roots = new List<KeyValuePair<string, string>>();
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (!string.Equals(args[index], "--engine-header-root", StringComparison.Ordinal))
            {
                continue;
            }

            var value = args[index + 1];
            var separator = value.IndexOf('=');
            if (separator <= 0 || separator == value.Length - 1)
            {
                throw new ArgumentException($"Invalid value '{value}' for option '--engine-header-root'. Expected <Name>=<dir>.");
            }

            roots.Add(new KeyValuePair<string, string>(value[..separator], value[(separator + 1)..]));
            index++;
        }

        return roots;
    }

    private static string GetRequiredPathOption(IReadOnlyList<string> args, string optionName, bool isDirectory, bool mustExist = true)
    {
        var value = GetOptionalPathOption(args, optionName, isDirectory, mustExist);
        return value ?? throw new ArgumentException($"Missing required option '{optionName}'.");
    }

    private static string? GetOptionalPathOption(IReadOnlyList<string> args, string optionName, bool isDirectory, bool mustExist = true)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (!string.Equals(args[index], optionName, StringComparison.Ordinal))
            {
                continue;
            }

            var path = Path.GetFullPath(args[index + 1]);
            if (!mustExist)
            {
                return path;
            }

            var exists = isDirectory ? Directory.Exists(path) : File.Exists(path);
            if (!exists)
            {
                throw new ArgumentException($"The path for '{optionName}' does not exist: '{path}'.");
            }

            return path;
        }

        return null;
    }
}
