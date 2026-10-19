namespace Urho3D.Plugins;

/// <summary>
/// Marks a managed <see cref="Urho3D.Core.Object"/> type for registration in the engine.
/// <see cref="ManagedPlugin"/> registers a factory and an engine reflection for each marked type when the plugin loads.
/// A type without this attribute is not registered. Native code cannot create it by name.
/// The type must be a concrete, non-generic class that derives from <see cref="Urho3D.Core.Object"/> and not from <see cref="Plugin"/>.
/// It must be partial, and it must have a public constructor with one <see cref="Urho3D.Core.Context"/> parameter
/// or a manual <c>IManagedObjectFactory&lt;T&gt;</c>.
/// The source generator (SharpCpp.Rbfx.SourceGenerator) reads the attribute at compile time. It reports an error for a wrong use.
/// The attribute stays in the metadata, so that a derived type in another assembly can see that the base type is registered.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class FactoryReflectionAttribute : Attribute
{
    /// <summary>
    /// The reflection category of the type in the engine. When null, the type uses the category of the plugin
    /// (<c>ManagedPlugin.DefaultCategory</c>). An empty or white-space string is an error (RBFXSG006).
    /// </summary>
    public string? Category { get; set; }
}
