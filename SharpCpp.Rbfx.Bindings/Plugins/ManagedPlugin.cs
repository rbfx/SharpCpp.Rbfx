using System.Reflection;

using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Math;

namespace Urho3D.Plugins;

/// <summary>
/// The base class for managed plugins. On load it registers the managed object types of the plugin assembly:
/// a factory for each type and an engine attribute for each <see cref="Urho3D.Scene.SerializeFieldAttribute"/> member.
/// On unload it removes the reflections of these types in reverse order.
/// A type is registered when it is a concrete <see cref="Object"/> with a public constructor that takes a
/// <see cref="Context"/>, and when it is partial, so that the source generator implements
/// <see cref="IManagedObjectFactory{T}"/>. Override <see cref="Load"/> and <see cref="Unload"/> to add more work,
/// and call the base methods.
/// </summary>
public abstract partial class ManagedPlugin : Plugin
{
    private readonly List<StringHash> _registeredTypes = new();

    protected ManagedPlugin(Context context)
        : base(context)
    {
    }

    /// <summary>The hashes of the type names that this plugin registered, in registration order.</summary>
    public IReadOnlyList<StringHash> RegisteredTypes => _registeredTypes;

    /// <summary>The reflection category of the registered types. The default is the simple name of the plugin assembly.</summary>
    protected virtual string FactoryCategory => ((object)this).GetType().Assembly.GetName().Name ?? string.Empty;

    /// <summary>Registers the managed types. A type that fails to register is logged and skipped.</summary>
    protected override void Load() => RegisterManagedTypes();

    /// <summary>Removes the reflections of the registered types.</summary>
    protected override void Unload() => UnregisterManagedTypes();

    /// <summary>Returns the candidate types. The default is all types of the plugin assembly.</summary>
    protected virtual IEnumerable<Type> GetManagedTypes()
    {
        try
        {
            return ((object)this).GetType().Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    /// <summary>Returns true to register the type. The default accepts every factory type that is not a plugin.</summary>
    protected virtual bool ShouldRegister(Type type) =>
        ManagedObjectFactory.IsFactoryType(type) && !typeof(Plugin).IsAssignableFrom(type);

    /// <summary>Registers the factories and the attributes. A second call does nothing until the types are unregistered.</summary>
    protected void RegisterManagedTypes()
    {
        if (_registeredTypes.Count > 0)
        {
            return;
        }

        // Do not dispose the context: the property returns a shared wrapper.
        var context = Context;
        var category = FactoryCategory;

        // A base type registers before its derived types, so that the derived types can copy its attributes.
        var types = GetManagedTypes().Where(ShouldRegister).OrderBy(GetDepth).ThenBy(static t => t.FullName, StringComparer.Ordinal);
        foreach (var type in types)
        {
            try
            {
                var typeInfo = context.RegisterFactory(type, category);
                _registeredTypes.Add(typeInfo.GetType());
                var reflection = context.GetReflection(typeInfo.GetType());
                ManagedAttributeRegistrar.Register(context, reflection, typeInfo, type);
            }
            catch (Exception exception)
            {
                ManagedAttributeAccessor.LogWarning($"Cannot register the managed type {type.FullName}: {exception.GetBaseException().Message}");
            }
        }
    }

    /// <summary>Removes the reflections of the registered types in reverse order.</summary>
    protected void UnregisterManagedTypes()
    {
        var context = Context;
        for (var i = _registeredTypes.Count - 1; i >= 0; i--)
        {
            try
            {
                context.RemoveReflection(_registeredTypes[i]);
            }
            catch (Exception exception)
            {
                ManagedAttributeAccessor.LogWarning($"Cannot remove a managed type reflection: {exception.GetBaseException().Message}");
            }
        }

        _registeredTypes.Clear();
    }

    private static int GetDepth(Type type)
    {
        var depth = 0;
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            depth++;
        }

        return depth;
    }
}
