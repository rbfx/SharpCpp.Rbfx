namespace Rbfx.UserCode.Generator;

internal sealed class GeneratorOptions
{
    public required string SdkRoot { get; init; }

    public required string FlagsFilePath { get; init; }

    public required string HeaderPath { get; init; }

    public IReadOnlyList<string> IncludeDirectories { get; init; } = Array.Empty<string>();

    public required string AssemblyName { get; init; }

    public required string ManagedNamespace { get; init; }

    public string BindingsClassName { get; init; } = "Bindings";

    public required string LibraryName { get; init; }

    public required string FileStem { get; init; }

    public required string SymbolPrefix { get; init; }

    public required string SharpCppIncludeRoot { get; init; }

    public required string RbfxIncludeRoot { get; init; }

    public required string GeneratedRoot { get; init; }

    public IReadOnlyList<string> ImportedBindingMetadataFiles { get; init; } = Array.Empty<string>();

    public string? LogFilePath { get; init; }

    public static GeneratorOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args.Any(static argument => argument is "--help" or "-h"))
        {
            PrintUsage();
            Environment.Exit(0);
        }

        var sdkRoot = GetRequiredOption(args, "--sdk-root");
        var flagsFilePath = GetRequiredOption(args, "--flags-file");
        var headerPath = GetRequiredOption(args, "--header");
        var includeDirectories = GetRepeatedPathOption(args, "--include-directory");
        var assemblyName = GetRequiredValueOption(args, "--assembly-name");
        var managedNamespace = GetRequiredValueOption(args, "--namespace");
        var bindingsClassName = GetOptionalValueOption(args, "--bindings-class") ?? "Bindings";
        var libraryName = GetRequiredValueOption(args, "--library-name");
        var fileStem = GetRequiredValueOption(args, "--file-stem");
        var symbolPrefix = GetRequiredValueOption(args, "--symbol-prefix");
        var sharpCppIncludeRoot = GetRequiredOption(args, "--sharpcpp-include");
        var rbfxIncludeRoot = GetRequiredOption(args, "--rbfx-include");
        var generatedRoot = GetRequiredOption(args, "--generated-root");
        var importedBindingMetadataFiles = GetRepeatedPathOption(args, "--import-binding-metadata");
        var logFilePath = GetOptionalPathOption(args, "--log-file");

        ValidatePath(sdkRoot, isDirectory: true, "--sdk-root");
        ValidatePath(flagsFilePath, isDirectory: false, "--flags-file");
        ValidatePath(headerPath, isDirectory: false, "--header");
        ValidatePath(sharpCppIncludeRoot, isDirectory: true, "--sharpcpp-include");
        ValidatePath(rbfxIncludeRoot, isDirectory: true, "--rbfx-include");

        foreach (var includeDirectory in includeDirectories)
        {
            ValidatePath(includeDirectory, isDirectory: true, "--include-directory");
        }

        foreach (var metadataFile in importedBindingMetadataFiles)
        {
            ValidatePath(metadataFile, isDirectory: false, "--import-binding-metadata");
        }

        return new GeneratorOptions
        {
            SdkRoot = sdkRoot,
            FlagsFilePath = flagsFilePath,
            HeaderPath = headerPath,
            IncludeDirectories = includeDirectories,
            AssemblyName = assemblyName,
            ManagedNamespace = managedNamespace,
            BindingsClassName = bindingsClassName,
            LibraryName = libraryName,
            FileStem = fileStem,
            SymbolPrefix = symbolPrefix,
            SharpCppIncludeRoot = sharpCppIncludeRoot,
            RbfxIncludeRoot = rbfxIncludeRoot,
            GeneratedRoot = generatedRoot,
            ImportedBindingMetadataFiles = importedBindingMetadataFiles,
            LogFilePath = logFilePath
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            Usage:
              dotnet Rbfx.UserCode.Generator.dll --sdk-root <path> --flags-file <path> --header <path> [--include-directory <path> ...] --assembly-name <name> --namespace <ns> [--bindings-class <name>] --library-name <name> --file-stem <stem> --symbol-prefix <prefix> --sharpcpp-include <path> --rbfx-include <path> --generated-root <path> [--import-binding-metadata <path> ...] [--log-file <path>]
            """);
    }

    private static string GetRequiredOption(IReadOnlyList<string> args, string optionName)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.Ordinal))
            {
                return Path.GetFullPath(args[index + 1]);
            }
        }

        throw new ArgumentException($"Missing required option '{optionName}'.");
    }

    private static string GetRequiredValueOption(IReadOnlyList<string> args, string optionName)
    {
        return GetOptionalValueOption(args, optionName)
            ?? throw new ArgumentException($"Missing required option '{optionName}'.");
    }

    private static string? GetOptionalValueOption(IReadOnlyList<string> args, string optionName)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static string? GetOptionalPathOption(IReadOnlyList<string> args, string optionName)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.Ordinal))
            {
                return Path.GetFullPath(args[index + 1]);
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetRepeatedPathOption(IReadOnlyList<string> args, string optionName)
    {
        var values = new List<string>();

        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.Ordinal))
            {
                values.Add(Path.GetFullPath(args[index + 1]));
                index++;
            }
        }

        return values;
    }

    private static void ValidatePath(string path, bool isDirectory, string optionName)
    {
        var exists = isDirectory ? Directory.Exists(path) : File.Exists(path);
        if (!exists)
        {
            throw new ArgumentException($"The path for '{optionName}' does not exist: '{path}'.");
        }
    }
}
