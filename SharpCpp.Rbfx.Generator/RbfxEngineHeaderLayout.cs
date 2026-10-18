namespace SharpCpp.Rbfx.Generator;

/// <summary>
/// Describes where the engine headers live. An installed SDK has one include tree.
/// A source tree has separate roots for Urho3D, RmlUi and ImGui headers.
/// </summary>
internal sealed record RbfxEngineHeaderLayout(
    IReadOnlyList<string> UrhoRoots,
    string? UrhoIncludePrefix,
    string? RmlUiRoot,
    string? ImGuiRoot,
    string? SdkIncludeRoot,
    string? RmlUiConfigRoot = null)
{
    public const string UrhoRootName = "Urho3D";
    public const string RmlUiRootName = "RmlUi";
    public const string ImGuiRootName = "ImGui";

    /// <summary>
    /// The RmlUi configuration headers. An installed SDK has them in the RmlUi include tree. A source tree keeps them
    /// outside it, and the root gets the include prefix <see cref="RmlUiConfigIncludePrefix"/>.
    /// </summary>
    public const string RmlUiConfigRootName = "RmlUiConfig";
    public const string RmlUiConfigIncludePrefix = "RmlUi/Config";

    /// <summary>
    /// The absolute paths of the Urho3D headers that the engine target compiles. It is null in SDK mode, where the
    /// installed include tree is the list. A source tree also has the headers of the disabled modules, so the header
    /// scans use only these files.
    /// </summary>
    public IReadOnlyList<string>? UrhoHeaderList { get; init; }

    /// <summary>
    /// Returns the Urho3D headers as the path relative to the matching Urho3D root and the full path. The result has
    /// the files with one of the extensions only. The headers in the CSharp directory are not part of the result.
    /// </summary>
    public IEnumerable<(string RelativePath, string FullPath)> EnumerateUrhoHeaders(params string[] extensions)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var existingRoots = UrhoRoots.Where(Directory.Exists).ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<(string Root, string Path)> files;
        if (UrhoHeaderList is null)
        {
            files = existingRoots.SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.Ordinal)
                .Select(path => (root, path)));
        }
        else
        {
            files = UrhoHeaderList.Select(path => (Root: FindRoot(existingRoots, path, comparison), Path: path))
                .Where(static item => item.Root is not null)
                .Select(static item => (item.Root!, item.Path));
        }

        foreach (var (root, path) in files)
        {
            if (!extensions.Contains(Path.GetExtension(path), StringComparer.Ordinal))
                continue;
            var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relativePath.StartsWith("CSharp/", StringComparison.Ordinal) || !seen.Add(relativePath))
                continue;
            yield return (relativePath, path);
        }
    }

    private static string? FindRoot(IReadOnlyList<string> roots, string path, StringComparison comparison)
    {
        string? best = null;
        foreach (var root in roots)
        {
            var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (path.StartsWith(prefix, comparison) && (best is null || root.Length > best.Length))
                best = root;
        }

        return best;
    }

    public static RbfxEngineHeaderLayout FromSdkRoot(string sdkRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sdkRoot);
        var fullSdkRoot = Path.GetFullPath(sdkRoot);
        var includeRoot = Path.Combine(fullSdkRoot, "include");
        var urhoIncludeRoot = Path.Combine(includeRoot, UrhoRootName);
        if (!Directory.Exists(urhoIncludeRoot))
        {
            throw new InvalidOperationException($"rbfx SDK include directory was not found at '{urhoIncludeRoot}'.");
        }

        return new RbfxEngineHeaderLayout(
            [urhoIncludeRoot],
            UrhoIncludePrefix: null,
            RmlUiRoot: Path.Combine(includeRoot, RmlUiRootName),
            ImGuiRoot: Path.Combine(includeRoot, ImGuiRootName),
            SdkIncludeRoot: includeRoot);
    }

    public static RbfxEngineHeaderLayout FromHeaderRoots(
        IEnumerable<KeyValuePair<string, string>> roots,
        string? headerListPath = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var urhoRoots = new List<string>();
        string? rmlUiRoot = null;
        string? imGuiRoot = null;
        string? rmlUiConfigRoot = null;
        foreach (var (name, directory) in roots)
        {
            var path = Path.GetFullPath(directory);
            switch (name)
            {
                case UrhoRootName:
                    urhoRoots.Add(path);
                    break;
                case RmlUiRootName:
                    rmlUiRoot = path;
                    break;
                case ImGuiRootName:
                    imGuiRoot = path;
                    break;
                case RmlUiConfigRootName:
                    rmlUiConfigRoot = path;
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown engine header root name '{name}'. Expected '{UrhoRootName}', '{RmlUiRootName}', '{RmlUiConfigRootName}' or '{ImGuiRootName}'.");
            }
        }

        urhoRoots = urhoRoots.Distinct(StringComparer.Ordinal).ToList();
        if (!urhoRoots.Any(Directory.Exists))
        {
            throw new InvalidOperationException(
                $"At least one existing '{UrhoRootName}' engine header root is required.");
        }

        return new RbfxEngineHeaderLayout(
            urhoRoots.Where(Directory.Exists).ToArray(),
            UrhoIncludePrefix: UrhoRootName,
            RmlUiRoot: rmlUiRoot,
            ImGuiRoot: imGuiRoot,
            SdkIncludeRoot: null,
            RmlUiConfigRoot: rmlUiConfigRoot)
        {
            UrhoHeaderList = headerListPath is null ? null : ReadHeaderList(headerListPath),
        };
    }

    private static string[] ReadHeaderList(string headerListPath) =>
        File.ReadAllLines(headerListPath)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
