using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

using SharpCpp.Rbfx.Bindings.Interop;
using SharpCpp.Support;

using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Scene;

using TypeInfo = Urho3D.Core.TypeInfo;

namespace Urho3D.Plugins;

/// <summary>Adds the <see cref="SerializeFieldAttribute"/> members of a managed type to its engine reflection.</summary>
internal static class ManagedAttributeRegistrar
{
    internal const DynamicallyAccessedMemberTypes ManagedTypeMembers =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces |
        DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties;

    private const BindingFlags DeclaredInstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly Assembly BindingsAssembly = typeof(ManagedAttributeRegistrar).Assembly;

    private static readonly MethodInfo CreateBindingMethod =
        typeof(ManagedAttributeRegistrar).GetMethod(nameof(CreateBinding), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static int _reflectionScanCount;
    private static int _activatorCreateCount;

    /// <summary>The number of member scans by reflection (<c>CollectMembers</c>). It is incremented once for each scan, and read by tests.</summary>
    internal static int ReflectionScanCount => Volatile.Read(ref _reflectionScanCount);

    /// <summary>The number of temporary instances made by <see cref="Activator"/>. It is incremented once for each instance, and read by tests.</summary>
    internal static int ActivatorCreateCount => Volatile.Read(ref _activatorCreateCount);

    /// <summary>
    /// Copies the attributes of the nearest reflected base type, then adds the marked members of the type and of its
    /// managed base types that have no reflection. A temporary instance gives the default values.
    /// When <paramref name="descriptors"/> is not null, it is the complete list of the attributes of the type (the generated registry).
    /// The type is not scanned for members then. When it is null, the members come from reflection.
    /// A descriptor has its own default value, so a descriptor list needs no temporary instance.
    /// </summary>
    public static void Register(
        Context context,
        ObjectReflection reflection,
        TypeInfo typeInfo,
        [DynamicallyAccessedMembers(ManagedTypeMembers)] Type type,
        ManagedAttributeDescriptor[]? descriptors = null)
    {
        CopyBaseAttributes(context, reflection, typeInfo);

        List<(MemberInfo Member, SerializeFieldAttribute Attribute)>? members = null;
        if (descriptors is null)
        {
            Interlocked.Increment(ref _reflectionScanCount);
            members = CollectMembers(context, type);
        }

        if ((members?.Count ?? descriptors!.Length) == 0)
        {
            return;
        }

        if (!typeof(Serializable).IsAssignableFrom(type))
        {
            ManagedAttributeAccessor.LogWarning($"The type {type.FullName} has [SerializeField] members but is not a Serializable. The members are skipped.");
            return;
        }

        Serializable? defaults = null;
        if (descriptors is null)
        {
            try
            {
                Interlocked.Increment(ref _activatorCreateCount);
                defaults = (Serializable)Activator.CreateInstance(type, context)!;
            }
            catch (Exception exception)
            {
                ManagedAttributeAccessor.LogWarning(
                    $"Cannot create a temporary {type.FullName} for attribute default values. The variant defaults are used: {exception.GetBaseException().Message}");
            }
        }

        try
        {
            if (descriptors is not null)
            {
                foreach (var descriptor in descriptors)
                {
                    try
                    {
                        AddAttribute(
                            reflection, type, descriptor.MemberName, descriptor.ValueType, descriptor.Name, descriptor.Mode, descriptor.CreateBinding, defaults,
                            descriptor.CreateDefault);
                    }
                    catch (Exception exception)
                    {
                        ManagedAttributeAccessor.LogWarning($"Cannot register the attribute {type.FullName}.{descriptor.MemberName}: {exception.GetBaseException().Message}");
                    }
                }
            }
            else
            {
                foreach (var (member, attribute) in members!)
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
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "The base types come from the annotated type. The annotation keeps the same members of the base types.")]
    private static List<(MemberInfo Member, SerializeFieldAttribute Attribute)> CollectMembers(Context context, [DynamicallyAccessedMembers(ManagedTypeMembers)] Type type)
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

    private static bool IsReflected(Context context, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type)
    {
        var method = type.GetMethod("GetTypeInfoStatic", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly, Type.EmptyTypes);
        return method?.Invoke(null, null) is TypeInfo { IsInvalid: false } typeInfo && context.IsReflected(typeInfo.GetType());
    }

    private static void AddAttribute(ObjectReflection reflection, Type type, MemberInfo member, SerializeFieldAttribute attribute, Serializable? defaults)
    {
        var valueType = member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;
        AddAttribute(
            reflection,
            type,
            member.Name,
            valueType,
            attribute.Name ?? member.Name,
            attribute.Mode,
            converter => CreateBindingFor(type, valueType, member, converter),
            defaults,
            createDefault: null);
    }

    private static void AddAttribute(
        ObjectReflection reflection,
        Type type,
        string memberName,
        Type valueType,
        string name,
        AttributeMode mode,
        Func<object, ManagedAttributeBinding> createBinding,
        Serializable? defaults,
        Func<object, Variant?>? createDefault)
    {
        if (!VariantConverters.TryGet(valueType, out var converter, out var variantType, out var enumNames))
        {
            ManagedAttributeAccessor.LogWarning(
                $"The attribute {type.FullName}.{memberName} has the unsupported type {valueType.FullName}. It is skipped.");
            return;
        }

        var binding = createBinding(converter);
        using var defaultValue = createDefault?.Invoke(converter) ?? (defaults is not null ? binding.ReadValue(defaults) : null) ?? new Variant(variantType);
        using var accessor = ManagedAttributeAccessor.Create(binding);
        if (!accessor.AddTo(reflection, variantType, name, enumNames, defaultValue, mode))
        {
            ManagedAttributeAccessor.LogWarning($"The reflection of {type.FullName} did not accept the attribute '{name}'.");
        }
    }

    // Enum values are boxed by the enum converter, so every enum maps to the object binding.
    // The known value types use direct generic calls, which AOT compilers can see.
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "This fallback runs only when dynamic code is supported. The table above covers every known value type for AOT, which throws instead.")]
    private static ManagedAttributeBinding CreateBindingFor(Type type, Type valueType, MemberInfo member, object converter)
    {
        var bindingType = valueType.IsEnum ? typeof(object) : valueType;
        if (bindingType == typeof(bool)) return CreateBinding<Serializable, bool>(member, converter);
        if (bindingType == typeof(int)) return CreateBinding<Serializable, int>(member, converter);
        if (bindingType == typeof(long)) return CreateBinding<Serializable, long>(member, converter);
        if (bindingType == typeof(float)) return CreateBinding<Serializable, float>(member, converter);
        if (bindingType == typeof(double)) return CreateBinding<Serializable, double>(member, converter);
        if (bindingType == typeof(string)) return CreateBinding<Serializable, string>(member, converter);
        if (bindingType == typeof(Vector2)) return CreateBinding<Serializable, Vector2>(member, converter);
        if (bindingType == typeof(Vector3)) return CreateBinding<Serializable, Vector3>(member, converter);
        if (bindingType == typeof(Vector4)) return CreateBinding<Serializable, Vector4>(member, converter);
        if (bindingType == typeof(Quaternion)) return CreateBinding<Serializable, Quaternion>(member, converter);
        if (bindingType == typeof(Color)) return CreateBinding<Serializable, Color>(member, converter);
        if (bindingType == typeof(IntVector2)) return CreateBinding<Serializable, IntVector2>(member, converter);
        if (bindingType == typeof(IntVector3)) return CreateBinding<Serializable, IntVector3>(member, converter);
        if (bindingType == typeof(object)) return CreateBinding<Serializable, object>(member, converter);

        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            throw new NotSupportedException(
                $"The attribute value type {valueType.FullName} of {type.FullName}.{member.Name} needs dynamic code, which this runtime does not support.");
        }

        return (ManagedAttributeBinding)CreateBindingMethod.MakeGenericMethod(type, bindingType).Invoke(null, [member, converter])!;
    }

    private static ManagedAttributeBinding CreateBinding<TOwner, TValue>(MemberInfo member, object converter)
        where TOwner : Serializable =>
        ManagedAttributeBinding<TOwner, TValue>.Create(member, (VariantConverter<TValue>)converter);
}
