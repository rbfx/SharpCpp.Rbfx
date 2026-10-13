using System.Reflection;

using SharpCpp.Rbfx.Bindings.Interop;
using SharpCpp.Support;

using Urho3D.Core;
using Urho3D.Scene;

using TypeInfo = Urho3D.Core.TypeInfo;

namespace Urho3D.Plugins;

/// <summary>Adds the <see cref="SerializeFieldAttribute"/> members of a managed type to its engine reflection.</summary>
internal static class ManagedAttributeRegistrar
{
    private const BindingFlags DeclaredInstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly Assembly BindingsAssembly = typeof(ManagedAttributeRegistrar).Assembly;

    private static readonly MethodInfo CreateBindingMethod =
        typeof(ManagedAttributeRegistrar).GetMethod(nameof(CreateBinding), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Copies the attributes of the nearest reflected base type, then adds the marked members of the type and of its
    /// managed base types that have no reflection. A temporary instance gives the default values.
    /// </summary>
    public static void Register(Context context, ObjectReflection reflection, TypeInfo typeInfo, Type type)
    {
        CopyBaseAttributes(context, reflection, typeInfo);

        var members = CollectMembers(context, type);
        if (members.Count == 0)
        {
            return;
        }

        if (!typeof(Serializable).IsAssignableFrom(type))
        {
            ManagedAttributeAccessor.LogWarning($"The type {type.FullName} has [SerializeField] members but is not a Serializable. The members are skipped.");
            return;
        }

        Serializable? defaults = null;
        try
        {
            defaults = (Serializable)Activator.CreateInstance(type, context)!;
        }
        catch (Exception exception)
        {
            ManagedAttributeAccessor.LogWarning(
                $"Cannot create a temporary {type.FullName} for attribute default values. The variant defaults are used: {exception.GetBaseException().Message}");
        }

        try
        {
            foreach (var (member, attribute) in members)
            {
                try
                {
                    AddAttribute(reflection, type, member, attribute, defaults);
                }
                catch (Exception exception)
                {
                    ManagedAttributeAccessor.LogWarning($"Cannot register the attribute {type.FullName}.{member.Name}: {exception.GetBaseException().Message}");
                }
            }
        }
        finally
        {
            // The temporary instance has the only reference. Its native object is deleted here.
            defaults?.Dispose();
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

    // Members of the type, and of managed base types up to the first base type with a reflection.
    // The attributes of that base type come from CopyAttributesFrom.
    private static List<(MemberInfo Member, SerializeFieldAttribute Attribute)> CollectMembers(Context context, Type type)
    {
        var result = new List<(MemberInfo, SerializeFieldAttribute)>();
        var chain = new List<Type>();
        for (var current = type; current is not null && current.Assembly != BindingsAssembly; current = current.BaseType)
        {
            if (current != type && IsReflected(context, current))
            {
                break;
            }

            chain.Add(current);
        }

        // Base members first, as in the engine order of attributes.
        chain.Reverse();
        foreach (var current in chain)
        {
            foreach (var field in current.GetFields(DeclaredInstanceMembers))
            {
                if (field.GetCustomAttribute<SerializeFieldAttribute>() is not { } attribute || field.IsDefined(typeof(NonSerializedAttribute), inherit: false))
                {
                    continue;
                }

                if (field.IsInitOnly || field.IsLiteral)
                {
                    ManagedAttributeAccessor.LogWarning($"The attribute {current.FullName}.{field.Name} is read-only and is skipped.");
                    continue;
                }

                result.Add((field, attribute));
            }

            foreach (var property in current.GetProperties(DeclaredInstanceMembers))
            {
                if (property.GetCustomAttribute<SerializeFieldAttribute>() is not { } attribute)
                {
                    continue;
                }

                if (property.GetMethod is null || property.SetMethod is null || property.GetIndexParameters().Length != 0)
                {
                    ManagedAttributeAccessor.LogWarning(
                        $"The attribute {current.FullName}.{property.Name} must be a property with a getter and a setter. It is skipped.");
                    continue;
                }

                result.Add((property, attribute));
            }
        }

        return result;
    }

    private static bool IsReflected(Context context, Type type)
    {
        var method = type.GetMethod("GetTypeInfoStatic", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly, Type.EmptyTypes);
        return method?.Invoke(null, null) is TypeInfo { IsInvalid: false } typeInfo && context.IsReflected(typeInfo.GetType());
    }

    private static void AddAttribute(ObjectReflection reflection, Type type, MemberInfo member, SerializeFieldAttribute attribute, Serializable? defaults)
    {
        var valueType = member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;
        if (!VariantConverters.TryGet(valueType, out var converter, out var variantType, out var enumNames))
        {
            ManagedAttributeAccessor.LogWarning(
                $"The attribute {type.FullName}.{member.Name} has the unsupported type {valueType.FullName}. It is skipped.");
            return;
        }

        var binding = (ManagedAttributeBinding)CreateBindingMethod.MakeGenericMethod(type, valueType).Invoke(null, [member, converter])!;
        var name = attribute.Name ?? member.Name;
        using var defaultValue = (defaults is not null ? binding.ReadValue(defaults) : null) ?? new Variant(variantType);
        using var accessor = ManagedAttributeAccessor.Create(binding);
        if (!accessor.AddTo(reflection, variantType, name, enumNames, defaultValue, attribute.Mode))
        {
            ManagedAttributeAccessor.LogWarning($"The reflection of {type.FullName} did not accept the attribute '{name}'.");
        }
    }

    private static ManagedAttributeBinding CreateBinding<TOwner, TValue>(MemberInfo member, object converter)
        where TOwner : Serializable =>
        ManagedAttributeBinding<TOwner, TValue>.Create(member, (VariantConverter<TValue>)converter);
}
