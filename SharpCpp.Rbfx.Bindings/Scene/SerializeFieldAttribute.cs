namespace Urho3D.Scene;

/// <summary>
/// Marks a field or a property of a managed <see cref="Serializable"/> as an engine attribute.
/// <see cref="Urho3D.Plugins.ManagedPlugin"/> registers the marked members when the plugin loads.
/// Public and private members are supported. A field with <see cref="NonSerializedAttribute"/> is skipped.
/// A property must have a getter and a setter.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class SerializeFieldAttribute : Attribute
{
    /// <summary>The attribute name in the engine. When null, the member name is used.</summary>
    public string? Name { get; set; }

    /// <summary>The attribute mode: serialization, editor visibility and other flags.</summary>
    public global::Urho3D.Core.AttributeMode Mode { get; set; } = global::Urho3D.Core.AttributeMode.AmDefault;
}
