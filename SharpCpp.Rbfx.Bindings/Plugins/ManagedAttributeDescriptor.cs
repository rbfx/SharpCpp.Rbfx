using System.ComponentModel;

using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Scene;

namespace Urho3D.Plugins;

/// <summary>
/// One engine attribute of a managed type, as the reflection scan of <see cref="ManagedPlugin"/> finds it from a
/// <see cref="SerializeFieldAttribute"/> member. The source generator emits descriptors into the
/// <see cref="ManagedTypeRegistryAttribute"/>, so that a runtime without dynamic code needs no reflection to find the member
/// or to read and write its value. The getter and the setter are plain delegates.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ManagedAttributeDescriptor
{
    private readonly Func<object, ManagedAttributeBinding> _createBinding;

    private ManagedAttributeDescriptor(
        string name, string memberName, Type valueType, AttributeMode mode, Func<object, ManagedAttributeBinding> createBinding)
    {
        Name = name;
        MemberName = memberName;
        ValueType = valueType;
        Mode = mode;
        _createBinding = createBinding;
    }

    /// <summary>The attribute name in the engine: <see cref="SerializeFieldAttribute.Name"/>, or the member name when it is null.</summary>
    public string Name { get; }

    /// <summary>The name of the field or property.</summary>
    public string MemberName { get; }

    /// <summary>The declared type of the member. For an enum, this is the enum type.</summary>
    public Type ValueType { get; }

    /// <summary>The attribute mode: <see cref="SerializeFieldAttribute.Mode"/>.</summary>
    public AttributeMode Mode { get; }

    /// <summary>
    /// Creates a descriptor. <typeparamref name="TValue"/> is the value type of the binding: the member type, or
    /// <see cref="object"/> for an enum. The delegates take the owner as a <see cref="Serializable"/>, and cast it to the declaring type.
    /// </summary>
    /// <param name="name">The engine attribute name.</param>
    /// <param name="memberName">The name of the field or property.</param>
    /// <param name="valueType">The declared type of the member. It selects the variant converter.</param>
    /// <param name="mode">The attribute mode.</param>
    /// <param name="getter">Reads the member of the owner.</param>
    /// <param name="setter">Writes the member of the owner.</param>
    public static ManagedAttributeDescriptor Create<TValue>(
        string name,
        string memberName,
        Type valueType,
        AttributeMode mode,
        Func<Serializable, TValue> getter,
        Action<Serializable, TValue> setter)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(memberName);
        ArgumentNullException.ThrowIfNull(valueType);
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);
        return new ManagedAttributeDescriptor(
            name,
            memberName,
            valueType,
            mode,
            converter => new ManagedAttributeBinding<Serializable, TValue>(getter, setter, (VariantConverter<TValue>)converter));
    }

    /// <summary>Creates the binding with the variant converter of <see cref="ValueType"/>.</summary>
    internal ManagedAttributeBinding CreateBinding(object converter) => _createBinding(converter);
}
