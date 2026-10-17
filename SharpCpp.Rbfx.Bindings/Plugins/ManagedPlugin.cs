using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Math;

using TypeInfo = Urho3D.Core.TypeInfo;

namespace Urho3D.Plugins;

/// <summary>
/// The base class for managed plugins. On load it registers the managed object types of the plugin assembly:
/// a factory for each type and an engine attribute for each <see cref="Urho3D.Scene.SerializeFieldAttribute"/> member.
/// On unload it removes the reflections of these types in reverse order.
/// The source generator (SharpCpp.Rbfx.SourceGenerator) writes a <see cref="ManagedTypeRegistryAttribute"/> into the plugin assembly.
/// The registry is the only source of the types and the attributes. There is no assembly scan and no reflection on members.
/// An assembly without a registry (the generator is not in the plugin project) logs an error and registers no type.
/// The plugin manager extensions refuse to register such a plugin and throw an exception with the same message.
/// A type is registered when it is a concrete <see cref="Object"/> with a public constructor that takes a
/// <see cref="Context"/>, and when it is partial, so that the source generator implements
/// <see cref="IManagedObjectFactory{T}"/>. Override <see cref="Load"/> and <see cref="Unload"/> to add more work,
/// and call the base methods.
/// </summary>
public abstract partial class ManagedPlugin : Plugin
{
    private readonly List<StringHash> _registeredTypes = new();
    private readonly List<Assembly> _claimedRegistries = new();

    protected ManagedPlugin(Context context)
        : base(context)
    {
    }

    /// <summary>
    /// The hashes of the type names that this plugin registered from its own assembly, in registration order.
    /// The types of the added registries are shared between the plugins and are not in the list.
    /// </summary>
    public IReadOnlyList<StringHash> RegisteredTypes => _registeredTypes;

    /// <summary>The reflection category of the registered types. The default is the simple name of the plugin assembly.</summary>
    protected virtual string FactoryCategory => ((object)this).GetType().Assembly.GetName().Name ?? string.Empty;

    /// <summary>Returns the message of the error for an assembly without a registry.</summary>
    internal static string NoRegistryMessage(Assembly assembly) =>
        $"The plugin assembly {assembly.GetName().Name} has no managed type registry. The registry is the only way to register managed types. " +
        "Reference SharpCpp.Rbfx.SourceGenerator as an analyzer in the project of the plugin.";

    /// <summary>Registers the managed types. A type that fails to register is logged and skipped.</summary>
    protected override void Load() => RegisterManagedTypes();

    /// <summary>Removes the reflections of the registered types.</summary>
    protected override void Unload() => UnregisterManagedTypes();

    /// <summary>
    /// Returns true to register the type of the registry. The default accepts every factory type that is not a plugin.
    /// A type that this method rejects is not registered, and its derived types of the registry are not registered either.
    /// </summary>
    protected virtual bool ShouldRegister([DynamicallyAccessedMembers(ManagedTypeRegistration.TypeMembers)] Type type) =>
        ManagedObjectFactory.IsFactoryType(type) && !typeof(Plugin).IsAssignableFrom(type);

    /// <summary>
    /// Registers the factories and the attributes. A second call does nothing until the types are unregistered.
    /// The plugin reads the generated <see cref="ManagedTypeRegistryAttribute"/> of its assembly on every runtime.
    /// An assembly without a registry logs an error, and registers no type: a native plugin load cannot fail with an exception.
    /// Then the types of the registries in <see cref="ManagedTypeRegistries"/> are added. The first plugin that claims a registry
    /// registers its types, and the last plugin that claims it removes them.
    /// </summary>
    protected void RegisterManagedTypes()
    {
        if (_registeredTypes.Count > 0)
        {
            return;
        }

        // GetContext returns a borrowed wrapper. The Context property returns an owning wrapper, and the engine caches
        // it in the native context. That wrapper keeps the context alive, so the engine never shuts down.
        var context = GetContext();
        var category = FactoryCategory;
        var assembly = ((object)this).GetType().Assembly;

        var registry = ManagedTypeRegistries.Find(assembly);
        if (registry is null)
        {
            ManagedAttributeAccessor.LogError(NoRegistryMessage(assembly));
            return;
        }

        RegisterFromRegistry(context, registry, category);

        foreach (var extra in ManagedTypeRegistries.Snapshot())
        {
            var extraAssembly = extra.GetType().Assembly;
            if (extraAssembly == assembly || _claimedRegistries.Contains(extraAssembly))
            {
                continue;
            }

            _claimedRegistries.Add(extraAssembly);
            if (ManagedTypeRegistries.Claim(extraAssembly))
            {
                // The claim gets the list first, so that Release returns every type registered before a failure.
                var types = new List<StringHash>();
                ManagedTypeRegistries.SetTypes(extraAssembly, types);
                RegisterFromRegistry(context, extra, extraAssembly.GetName().Name ?? string.Empty, types);
            }
        }
    }

    internal void RegisterFromRegistry(Context context, ManagedTypeRegistryAttribute registry, string category) =>
        RegisterFromRegistry(context, registry, category, _registeredTypes);

    private void RegisterFromRegistry(Context context, ManagedTypeRegistryAttribute registry, string category, List<StringHash> registered)
    {
        // The generated list is in base-before-derived order. The sort keeps this order for any list.
        var registrations = registry.Registrations;
        var listed = new HashSet<Type>(registrations.Select(static item => item.Type));
        var skipped = new HashSet<Type>();
        foreach (var item in registrations)
        {
            if (!ShouldRegister(item.Type))
            {
                skipped.Add(item.Type);
            }
        }

        var items = registrations.Where(item => !skipped.Contains(item.Type)).OrderBy(static item => GetDepth(item.Type))
            .ThenBy(static item => item.Type.FullName, StringComparer.Ordinal);
        foreach (var item in items)
        {
            // The generated attributes of a type leave out the attributes of its nearest listed base type. When that base type is not
            // registered, the list of the type is incomplete, so the type is an error and is not registered.
            if (GetSkippedListedBase(item.Type, listed, skipped) is { } skippedBase)
            {
                ManagedAttributeAccessor.LogError(
                    $"Cannot register the managed type {item.Type.FullName}: its base type {skippedBase.FullName} of the registry is not registered.");
                skipped.Add(item.Type);
                continue;
            }

            TypeInfo typeInfo;
            try
            {
                typeInfo = item.Register(context, category);
            }
            catch (Exception exception)
            {
                LogRegistrationFailure(item.Type, exception);
                skipped.Add(item.Type);
                continue;
            }

            RegisterAttributes(context, item.Type, typeInfo, registered, item.Attributes);
        }
    }

    private static Type? GetSkippedListedBase(Type type, HashSet<Type> listed, HashSet<Type> skipped)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (listed.Contains(current))
            {
                return skipped.Contains(current) ? current : null;
            }
        }

        return null;
    }

    private static void RegisterAttributes(Context context, Type type, TypeInfo typeInfo, List<StringHash> registered, ManagedAttributeDescriptor[] descriptors)
    {
        try
        {
            registered.Add(typeInfo.GetType());
            var reflection = context.GetReflection(typeInfo.GetType());
            ManagedAttributeRegistrar.Register(context, reflection, typeInfo, type, descriptors);
        }
        catch (Exception exception)
        {
            LogRegistrationFailure(type, exception);
        }
    }

    private static void LogRegistrationFailure(Type type, Exception exception) =>
        ManagedAttributeAccessor.LogWarning($"Cannot register the managed type {type.FullName}: {exception.GetBaseException().Message}");

    /// <summary>Removes the reflections of the registered types in reverse order.</summary>
    protected void UnregisterManagedTypes()
    {
        var context = GetContext();
        RemoveReflections(context, _registeredTypes);
        _registeredTypes.Clear();

        // The last plugin that claims an added registry removes its types.
        foreach (var assembly in _claimedRegistries)
        {
            if (ManagedTypeRegistries.Release(assembly) is { } types)
            {
                RemoveReflections(context, types);
            }
        }

        _claimedRegistries.Clear();
    }

    private static void RemoveReflections(Context context, List<StringHash> types)
    {
        for (var i = types.Count - 1; i >= 0; i--)
        {
            try
            {
                context.RemoveReflection(types[i]);
            }
            catch (Exception exception)
            {
                ManagedAttributeAccessor.LogWarning($"Cannot remove a managed type reflection: {exception.GetBaseException().Message}");
            }
        }
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
