namespace SharpCpp.Rbfx.Generator;

internal sealed record RbfxBindingSubmodules(bool HasRmlUi, bool HasSystemUi)
{
    public static readonly string[] RmlUiHeaderPaths =
    [
        "RmlUi/Core.h",
        "RmlUi/Debugger.h"
    ];

    public static readonly string[] ImGuiHeaderPaths =
    [
        "ImGui/imgui.h",
        "Urho3D/SystemUI/ImGui.h"
    ];

    public IEnumerable<string> EnabledHeaderPaths
    {
        get
        {
            if (HasRmlUi)
            {
                foreach (var headerPath in RmlUiHeaderPaths)
                    yield return headerPath;
            }

            if (HasSystemUi)
            {
                foreach (var headerPath in ImGuiHeaderPaths)
                    yield return headerPath;
            }
        }
    }

    public static RbfxBindingSubmodules Detect(SdkParseFlags sdkFlags)
    {
        ArgumentNullException.ThrowIfNull(sdkFlags);
        return new RbfxBindingSubmodules(
            sdkFlags.HasDefine("URHO3D_RMLUI", "1"),
            sdkFlags.HasDefine("URHO3D_SYSTEMUI", "1"));
    }

    public string WriteParseUmbrella(string generatedRoot, string urhoIncludeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generatedRoot);
        Directory.CreateDirectory(generatedRoot);

        var path = Path.Combine(generatedRoot, "SharpCpp.Rbfx.ParseUmbrella.hpp");
        var lines = new List<string>
        {
            "#pragma once",
        };
        if (File.Exists(Path.Combine(urhoIncludeRoot, "Urho3D.h")))
            lines.Add("#include <Urho3D/Urho3D.h>");
        if (File.Exists(Path.Combine(urhoIncludeRoot, "Urho3DAll.h")))
            lines.Add("#include <Urho3D/Urho3DAll.h>");
        else
        {
            // Mobile/web SDKs omit the optional monolithic header. Installed subdirectories
            // contain only the engine modules enabled by that SDK's build.
            lines.AddRange(Directory.GetFiles(urhoIncludeRoot, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetExtension(path) is ".h" or ".hpp")
                .Select(path => Path.GetRelativePath(urhoIncludeRoot, path).Replace('\\', '/'))
                .Where(path => path.Contains('/') && path is not ("Core/Main.h" or "Core/PE.h"))
                .Order(StringComparer.Ordinal).Select(path => $"#include <Urho3D/{path}>"));
            lines.Add("#include <Urho3D/DebugNew.h>");
        }
        lines.AddRange([
            "",
            "template void Urho3D::Object::SubscribeToEvent<Urho3D::EventHandler::HandlerFunction>(Urho3D::StringHash, Urho3D::EventHandler::HandlerFunction);",
            "template void Urho3D::Object::SubscribeToEvent<Urho3D::EventHandler::HandlerFunction>(Urho3D::Object*, Urho3D::StringHash, Urho3D::EventHandler::HandlerFunction);"
        ]);
        lines.AddRange(EnabledHeaderPaths.Select(static header => $"#include <{header}>"));
        File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        return path;
    }

    public IReadOnlyList<string> BuildOwnedFileRoots(string includeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(includeRoot);
        var roots = new List<string> { Path.Combine(includeRoot, "Urho3D") };

        if (HasRmlUi)
            roots.Add(Path.Combine(includeRoot, "RmlUi"));

        if (HasSystemUi)
            roots.Add(Path.Combine(includeRoot, "ImGui"));

        return roots.Where(Directory.Exists).Distinct(StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyDictionary<string, string> BuildOwnedFileRootIncludePrefixes(string includeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(includeRoot);
        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);

        if (HasRmlUi)
            prefixes[Path.Combine(includeRoot, "RmlUi")] = "RmlUi";

        if (HasSystemUi)
            prefixes[Path.Combine(includeRoot, "ImGui")] = "ImGui";

        return prefixes;
    }
}
