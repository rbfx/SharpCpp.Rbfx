using System.Runtime.CompilerServices;

using Urho3D.Core;
using Urho3D.Math;

namespace SharpCpp.Rbfx.Bindings.Interop;

/// <summary>Typed conversion between a managed value and an engine <see cref="Variant"/>.</summary>
internal sealed class VariantConverter<T>
{
    public VariantConverter(VariantType type, Func<T, Variant> toVariant, Func<Variant, T> fromVariant)
    {
        Type = type;
        ToVariant = toVariant;
        FromVariant = fromVariant;
    }

    public VariantType Type { get; }

    /// <summary>Returns a new owning variant. The caller disposes it.</summary>
    public Func<T, Variant> ToVariant { get; }

    public Func<Variant, T> FromVariant { get; }
}

/// <summary>The managed types that can be engine attributes, and their variant types.</summary>
internal static class VariantConverters
{
    private static readonly Dictionary<Type, object> Converters = new()
    {
        [typeof(bool)] = new VariantConverter<bool>(VariantType.VarBool, static v => new Variant(v), static v => v.Bool),
        [typeof(int)] = new VariantConverter<int>(VariantType.VarInt, static v => new Variant(v), static v => v.Int),
        [typeof(long)] = new VariantConverter<long>(VariantType.VarInt64, static v => new Variant(v), static v => v.Int64),
        [typeof(float)] = new VariantConverter<float>(VariantType.VarFloat, static v => new Variant(v), static v => v.Float),
        [typeof(double)] = new VariantConverter<double>(VariantType.VarDouble, static v => new Variant(v), static v => v.Double),
        [typeof(string)] = new VariantConverter<string>(VariantType.VarString, static v => v, ReadString),
        [typeof(Vector2)] = new VariantConverter<Vector2>(VariantType.VarVector2, static v => new Variant(in v), static v => v.Vector2),
        [typeof(Vector3)] = new VariantConverter<Vector3>(VariantType.VarVector3, static v => new Variant(in v), static v => v.Vector3),
        [typeof(Vector4)] = new VariantConverter<Vector4>(VariantType.VarVector4, static v => new Variant(in v), static v => v.Vector4),
        [typeof(Quaternion)] = new VariantConverter<Quaternion>(VariantType.VarQuaternion, static v => new Variant(in v), static v => v.Quaternion),
        [typeof(Color)] = new VariantConverter<Color>(VariantType.VarColor, static v => new Variant(in v), static v => v.Color),
        [typeof(IntVector2)] = new VariantConverter<IntVector2>(VariantType.VarIntvector2, static v => new Variant(in v), static v => v.IntVector2),
        [typeof(IntVector3)] = new VariantConverter<IntVector3>(VariantType.VarIntvector3, static v => new Variant(in v), static v => v.IntVector3),
    };

    /// <summary>
    /// Finds the converter for the type. An enum with a 32-bit underlying type maps to <see cref="VariantType.VarInt"/>.
    /// <paramref name="enumNames"/> is set only when the enum values are 0 to N-1, because the engine indexes the names by value.
    /// </summary>
    public static bool TryGet(Type type, out object converter, out VariantType variantType, out string[]? enumNames)
    {
        enumNames = null;
        if (Converters.TryGetValue(type, out var found))
        {
            converter = found;
            variantType = GetVariantType(found);
            return true;
        }

        if (type.IsEnum)
        {
            var underlying = Enum.GetUnderlyingType(type);
            if (underlying == typeof(int) || underlying == typeof(uint))
            {
                converter = CreateEnumConverter(type, underlying == typeof(uint));
                variantType = VariantType.VarInt;
                enumNames = GetSequentialEnumNames(type);
                return true;
            }
        }

        converter = null!;
        variantType = VariantType.VarNone;
        return false;
    }

    private static VariantType GetVariantType(object converter) => converter switch
    {
        VariantConverter<bool> c => c.Type,
        VariantConverter<int> c => c.Type,
        VariantConverter<long> c => c.Type,
        VariantConverter<float> c => c.Type,
        VariantConverter<double> c => c.Type,
        VariantConverter<string> c => c.Type,
        VariantConverter<Vector2> c => c.Type,
        VariantConverter<Vector3> c => c.Type,
        VariantConverter<Vector4> c => c.Type,
        VariantConverter<Quaternion> c => c.Type,
        VariantConverter<Color> c => c.Type,
        VariantConverter<IntVector2> c => c.Type,
        VariantConverter<IntVector3> c => c.Type,
        _ => VariantType.VarNone,
    };

    private static string ReadString(Variant variant)
    {
        using var text = variant.String;
        return text.ToString();
    }

    // The enum converter works on boxed values, so it needs no generic instantiation over the enum type.
    private static VariantConverter<object> CreateEnumConverter(Type enumType, bool isUnsigned)
    {
        return new VariantConverter<object>(
            VariantType.VarInt,
            static value => new Variant(unchecked((int)Convert.ToInt64(value))),
            isUnsigned
                ? variant => Enum.ToObject(enumType, unchecked((uint)variant.Int))
                : variant => Enum.ToObject(enumType, variant.Int));
    }

    private static string[]? GetSequentialEnumNames(Type type)
    {
        var values = Enum.GetValuesAsUnderlyingType(type);
        var names = Enum.GetNames(type);
        var result = new string[names.Length];
        var seen = new bool[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            var value = Convert.ToInt64(values.GetValue(i));
            if (value < 0 || value >= names.Length || seen[value])
            {
                return null;
            }

            seen[value] = true;
            result[value] = names[i];
        }

        return result;
    }
}

/// <summary>Binds one field or property of <typeparamref name="TOwner"/> to an engine attribute.</summary>
internal sealed class ManagedAttributeBinding<TOwner, TValue> : ManagedAttributeBinding
    where TOwner : global::Urho3D.Scene.Serializable
{
    /// <summary>
    /// Creates a typed getter and setter for the field or property. It compiles them when the runtime supports dynamic code,
    /// else it uses reflection.
    /// </summary>
    public static ManagedAttributeBinding<TOwner, TValue> Create(System.Reflection.MemberInfo member, VariantConverter<TValue> converter)
    {
        if (member is not (System.Reflection.FieldInfo or System.Reflection.PropertyInfo))
        {
            throw new ArgumentException($"The member '{member.Name}' is not a field or a property.", nameof(member));
        }

        Func<TOwner, TValue> getter;
        Action<TOwner, TValue> setter;
        if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            var owner = System.Linq.Expressions.Expression.Parameter(typeof(TOwner), "owner");
            var value = System.Linq.Expressions.Expression.Parameter(typeof(TValue), "value");
            System.Linq.Expressions.Expression instance = member.DeclaringType == typeof(TOwner)
                ? owner
                : System.Linq.Expressions.Expression.Convert(owner, member.DeclaringType!);
            System.Linq.Expressions.MemberExpression access = member switch
            {
                System.Reflection.FieldInfo field => System.Linq.Expressions.Expression.Field(instance, field),
                _ => System.Linq.Expressions.Expression.Property(instance, (System.Reflection.PropertyInfo)member),
            };

            System.Linq.Expressions.Expression read = access.Type == typeof(TValue)
                ? access
                : System.Linq.Expressions.Expression.Convert(access, typeof(TValue));
            System.Linq.Expressions.Expression write = access.Type == typeof(TValue)
                ? value
                : System.Linq.Expressions.Expression.Convert(value, access.Type);
            getter = System.Linq.Expressions.Expression.Lambda<Func<TOwner, TValue>>(read, owner).Compile();
            setter = System.Linq.Expressions.Expression.Lambda<Action<TOwner, TValue>>(
                System.Linq.Expressions.Expression.Assign(access, write), owner, value).Compile();
        }
        else if (member is System.Reflection.FieldInfo field)
        {
            getter = owner => (TValue)field.GetValue(owner)!;
            setter = (owner, value) => field.SetValue(owner, value);
        }
        else
        {
            var property = (System.Reflection.PropertyInfo)member;
            getter = owner => (TValue)property.GetValue(owner)!;
            setter = (owner, value) => property.SetValue(owner, value);
        }

        return new ManagedAttributeBinding<TOwner, TValue>(getter, setter, converter);
    }

    private readonly Func<TOwner, TValue> _getter;
    private readonly Action<TOwner, TValue> _setter;
    private readonly VariantConverter<TValue> _converter;

    public ManagedAttributeBinding(Func<TOwner, TValue> getter, Action<TOwner, TValue> setter, VariantConverter<TValue> converter)
    {
        _getter = getter;
        _setter = setter;
        _converter = converter;
    }

    /// <summary>Returns a new owning variant with the member value of the instance.</summary>
    public Variant Read(TOwner owner) => _converter.ToVariant(_getter(owner));

    public override Variant? ReadValue(global::Urho3D.Scene.Serializable owner) => owner is TOwner typed ? Read(typed) : null;

    public override void Get(nint serializable, nint dest)
    {
        if (!TryResolve(serializable, out var owner))
        {
            return;
        }

        using var value = Read(owner);
        ManagedAttributeAccessor.AssignVariant(dest, value);
    }

    public override void Set(nint serializable, nint src)
    {
        if (src == 0 || !TryResolve(serializable, out var owner))
        {
            return;
        }

        // The engine can pass a variant that already has a managed wrapper, for example the argument of
        // Serializable.SetAttribute. Only a wrapper that this call creates is disposed.
        if (Variant.s_instanceCache.TryGetExisting(src, ownsHandle: false, out var existing) && existing is Variant cached)
        {
            _setter(owner, _converter.FromVariant(cached));
            return;
        }

        using var variant = Variant.CreateManagedWrapper(src, ownsHandle: false);
        _setter(owner, _converter.FromVariant(variant));
    }

    // The managed instance is in the instance cache while the native object lives. No new wrapper is created.
    private static bool TryResolve(nint serializable, out TOwner owner)
    {
        if (serializable != 0 &&
            global::Urho3D.Interop.RefCountedInstanceCache.Shared.TryGetExisting(serializable, ownsHandle: false, out var instance) &&
            instance is TOwner typed)
        {
            owner = typed;
            return true;
        }

        owner = null!;
        return false;
    }
}
