using System.Diagnostics.CodeAnalysis;

namespace Urho3D.Plugins;

/// <summary>
/// Names the plugin class of a managed plugin assembly. The attribute is optional.
/// Without it, the loader uses the first concrete <see cref="Plugin"/> subclass with a public constructor that takes a context.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class PluginModuleAttribute : Attribute
{
    public PluginModuleAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type pluginType)
    {
        PluginType = pluginType;
    }

    /// <summary>The plugin class. It derives from <see cref="Plugin"/> and has a public constructor that takes a context.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type PluginType { get; }

    /// <summary>The plugin name in the plugin manager. The default is the simple name of the assembly.</summary>
    public string? Name { get; init; }
}
