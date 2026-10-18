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

    public string WriteParseUmbrella(string generatedRoot, RbfxEngineHeaderLayout layout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generatedRoot);
        ArgumentNullException.ThrowIfNull(layout);
        Directory.CreateDirectory(generatedRoot);

        var path = Path.Combine(generatedRoot, "SharpCpp.Rbfx.ParseUmbrella.hpp");
        var lines = new List<string>
        {
            "#pragma once",
        };
        if (layout.UrhoRoots.Any(root => File.Exists(Path.Combine(root, "Urho3D.h"))))
            lines.Add("#include <Urho3D/Urho3D.h>");
        if (layout.UrhoRoots.Any(root => File.Exists(Path.Combine(root, "Urho3DAll.h"))))
            lines.Add("#include <Urho3D/Urho3DAll.h>");
        else
        {
            // Mobile/web SDKs omit the optional monolithic header. Installed subdirectories
            // contain only the engine modules enabled by that SDK's build.
            lines.AddRange(layout.EnumerateUrhoHeaders(".h", ".hpp")
                .Select(header => header.RelativePath)
                .Where(path => path.Contains('/') && path is not ("Core/Main.h" or "Core/PE.h"))
                .Distinct(StringComparer.Ordinal)
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

    public IReadOnlyList<string> BuildOwnedFileRoots(RbfxEngineHeaderLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var roots = new List<string>(layout.UrhoRoots);

        if (HasRmlUi && layout.RmlUiRoot is not null)
            roots.Add(layout.RmlUiRoot);

        if (HasRmlUi && layout.RmlUiConfigRoot is not null)
            roots.Add(layout.RmlUiConfigRoot);

        if (HasSystemUi && layout.ImGuiRoot is not null)
            roots.Add(layout.ImGuiRoot);

        return roots.Where(Directory.Exists).Distinct(StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyDictionary<string, string> BuildOwnedFileRootIncludePrefixes(RbfxEngineHeaderLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);

        if (layout.UrhoIncludePrefix is not null)
        {
            foreach (var root in layout.UrhoRoots)
                prefixes[root] = layout.UrhoIncludePrefix;
        }

        if (HasRmlUi && layout.RmlUiRoot is not null)
            prefixes[layout.RmlUiRoot] = RbfxEngineHeaderLayout.RmlUiRootName;

        if (HasRmlUi && layout.RmlUiConfigRoot is not null)
            prefixes[layout.RmlUiConfigRoot] = RbfxEngineHeaderLayout.RmlUiConfigIncludePrefix;

        if (HasSystemUi && layout.ImGuiRoot is not null)
            prefixes[layout.ImGuiRoot] = RbfxEngineHeaderLayout.ImGuiRootName;

        return prefixes;
    }
}
