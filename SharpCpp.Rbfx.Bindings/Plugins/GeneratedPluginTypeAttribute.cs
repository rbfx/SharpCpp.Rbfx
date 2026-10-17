using System.Diagnostics.CodeAnalysis;

namespace Urho3D.Plugins;

/// <summary>
/// Names the plugin class of an assembly. The source generator (<c>SharpCpp.Rbfx.SourceGenerator</c>) emits it for the first
/// concrete <see cref="Plugin"/> subclass of the assembly that the generated code can see. Application code does not use it:
/// it uses <see cref="PluginModuleAttribute"/>, which has priority.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class GeneratedPluginTypeAttribute : Attribute
{
    public GeneratedPluginTypeAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type pluginType)
    {
        PluginType = pluginType;
    }

    /// <summary>The plugin class. It derives from <see cref="Plugin"/>.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public Type PluginType { get; }
}
