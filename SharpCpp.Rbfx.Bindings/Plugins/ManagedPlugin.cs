using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Math;

using TypeInfo = Urho3D.Core.TypeInfo;

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
    private readonly List<Assembly> _claimedRegistries = new();

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
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The plugin assembly is a trimmer root, so GetTypes returns all of its types.")]
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
    protected virtual bool ShouldRegister([DynamicallyAccessedMembers(ManagedAttributeRegistrar.ManagedTypeMembers)] Type type) =>
        ManagedObjectFactory.IsFactoryType(type) && !typeof(Plugin).IsAssignableFrom(type);

    /// <summary>
    /// Registers the factories and the attributes. A second call does nothing until the types are unregistered.
    /// When the plugin assembly has a generated <see cref="ManagedTypeRegistryAttribute"/>, its list gives the candidate
    /// types and the registration needs no dynamic code. Without it, <see cref="GetManagedTypes"/> scans the assembly (JIT only).
    /// Then the types of the registries in <see cref="ManagedTypeRegistries"/> that no other plugin registered are added.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "The plugin assembly is a trimmer root, so all of its types keep their members.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "The plugin assembly is a trimmer root, so all of its types keep their members.")]
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

        if (ManagedTypeRegistries.Find(assembly) is { } registry)
        {
            RegisterFromRegistry(context, registry, category);
        }
        else if (RuntimeFeature.IsDynamicCodeSupported)
        {
            // A base type registers before its derived types, so that the derived types can copy its attributes.
            var types = GetManagedTypes().Where(candidate => ShouldRegister(candidate)).OrderBy(GetDepth).ThenBy(static t => t.FullName, StringComparer.Ordinal);
            foreach (var type in types)
            {
                TypeInfo typeInfo;
                try
                {
                    typeInfo = context.RegisterFactory(type, category);
                }
                catch (Exception exception)
                {
                    LogRegistrationFailure(type, exception);
                    continue;
                }

                RegisterAttributes(context, type, typeInfo);
            }
        }
        else
        {
            ManagedAttributeAccessor.LogWarning(
                $"The plugin assembly {assembly.GetName().Name} has no ManagedTypeRegistryAttribute, and the scan needs dynamic code. Its managed types are not registered.");
        }

        foreach (var extra in ManagedTypeRegistries.Snapshot())
        {
            var extraAssembly = extra.GetType().Assembly;
            if (extraAssembly == assembly || !ManagedTypeRegistries.TryClaim(extraAssembly))
            {
                continue;
            }

            _claimedRegistries.Add(extraAssembly);
            RegisterFromRegistry(context, extra, extraAssembly.GetName().Name ?? string.Empty);
        }
    }

    private void RegisterFromRegistry(Context context, ManagedTypeRegistryAttribute registry, string category)
    {
        // The generated list is in base-before-derived order. The sort keeps the order of the scan path for any list.
        var items = registry.Registrations.Where(item => ShouldRegister(item.Type)).OrderBy(static item => GetDepth(item.Type))
            .ThenBy(static item => item.Type.FullName, StringComparer.Ordinal);
        foreach (var item in items)
        {
            TypeInfo typeInfo;
            try
            {
                typeInfo = item.Register(context, category);
            }
            catch (Exception exception)
            {
                LogRegistrationFailure(item.Type, exception);
                continue;
            }

            RegisterAttributes(context, item.Type, typeInfo);
        }
    }

    private void RegisterAttributes(Context context, [DynamicallyAccessedMembers(ManagedAttributeRegistrar.ManagedTypeMembers)] Type type, TypeInfo typeInfo)
    {
        try
        {
            _registeredTypes.Add(typeInfo.GetType());
            var reflection = context.GetReflection(typeInfo.GetType());
            ManagedAttributeRegistrar.Register(context, reflection, typeInfo, type);
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

        foreach (var assembly in _claimedRegistries)
        {
            ManagedTypeRegistries.Release(assembly);
        }

        _claimedRegistries.Clear();
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
