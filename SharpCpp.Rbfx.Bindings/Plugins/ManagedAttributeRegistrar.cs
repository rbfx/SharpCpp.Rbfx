using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Scene;

using TypeInfo = Urho3D.Core.TypeInfo;

namespace Urho3D.Plugins;

/// <summary>Adds the engine attributes of a managed type, from the descriptors of the generated registry, to its engine reflection.</summary>
internal static class ManagedAttributeRegistrar
{
    /// <summary>
    /// Copies the attributes of the nearest reflected base type, then adds the attributes of the descriptors.
    /// <paramref name="descriptors"/> is the complete list of the attributes of the type that the base type does not have.
    /// A descriptor has its own default value, so the type needs no temporary instance.
    /// </summary>
    public static void Register(Context context, ObjectReflection reflection, TypeInfo typeInfo, Type type, ManagedAttributeDescriptor[] descriptors)
    {
        CopyBaseAttributes(context, reflection, typeInfo);
        if (descriptors.Length == 0)
        {
            return;
        }

        if (!typeof(Serializable).IsAssignableFrom(type))
        {
            ManagedAttributeAccessor.LogWarning($"The type {type.FullName} has [SerializeField] members but is not a Serializable. The members are skipped.");
            return;
        }

        foreach (var descriptor in descriptors)
        {
            try
            {
                AddAttribute(reflection, type, descriptor);
            }
            catch (Exception exception)
            {
                ManagedAttributeAccessor.LogWarning($"Cannot register the attribute {type.FullName}.{descriptor.MemberName}: {exception.GetBaseException().Message}");
            }
        }
    }

    private static void CopyBaseAttributes(Context context, ObjectReflection reflection, TypeInfo typeInfo)
    {
        // TypeInfo and reflection wrappers from these getters are borrowed and can be shared, so they are not disposed.
        for (var baseInfo = typeInfo.BaseTypeInfo; baseInfo is { IsInvalid: false }; baseInfo = baseInfo.BaseTypeInfo)
        {
            var baseReflection = context.GetReflection(baseInfo.GetType());
            if (baseReflection is { IsInvalid: false })
            {
                reflection.CopyAttributesFrom(baseReflection);
                return;
            }
        }
    }

    private static void AddAttribute(ObjectReflection reflection, Type type, ManagedAttributeDescriptor descriptor)
    {
        if (!VariantConverters.TryGet(descriptor.ValueType, out var converter, out var variantType, out var enumNames))
        {
            ManagedAttributeAccessor.LogWarning(
                $"The attribute {type.FullName}.{descriptor.MemberName} has the unsupported type {descriptor.ValueType.FullName}. It is skipped.");
            return;
        }

        var binding = descriptor.CreateBinding(converter);
        using var defaultValue = descriptor.CreateDefault(converter);
        using var accessor = ManagedAttributeAccessor.Create(binding);
        if (!accessor.AddTo(reflection, variantType, descriptor.Name, enumNames, defaultValue, descriptor.Mode))
        {
            ManagedAttributeAccessor.LogWarning($"The reflection of {type.FullName} did not accept the attribute '{descriptor.Name}'.");
        }
    }
}
