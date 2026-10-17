using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Scene;

using TypeInfo = Urho3D.Core.TypeInfo;

namespace Urho3D.Plugins;

/// <summary>
/// The base class for managed plugins. On load it registers the managed object types of the plugin assembly:
/// a factory for each type and an engine attribute for each <see cref="Urho3D.Scene.SerializeFieldAttribute"/> member.
/// On unload it removes the reflections of these types in reverse order.
/// The source generator (SharpCpp.Rbfx.SourceGenerator) writes a <see cref="ManagedTypeRegistryAttribute"/> into the plugin assembly.
/// On every runtime the plugin registers the types and the attributes from this registry, with no assembly scan.
/// An assembly without a registry (the generator did not run) logs a warning and uses the reflection scan, which is not AOT safe.
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

    /// <summary>Has no effect. The generated registry is always preferred, also on a JIT runtime. A later step removes it.</summary>
    internal static bool ForceRegistry { get; set; }

    /// <summary>
    /// The hashes of the type names that this plugin registered from its own assembly, in registration order.
    /// The types of the added registries are shared between the plugins and are not in the list.
    /// </summary>
    public IReadOnlyList<StringHash> RegisteredTypes => _registeredTypes;

    /// <summary>The reflection category of the registered types. The default is the simple name of the plugin assembly.</summary>
    protected virtual string FactoryCategory => ((object)this).GetType().Assembly.GetName().Name ?? string.Empty;

    /// <summary>Registers the managed types. A type that fails to register is logged and skipped.</summary>
    protected override void Load() => RegisterManagedTypes();

    /// <summary>Removes the reflections of the registered types.</summary>
    protected override void Unload() => UnregisterManagedTypes();

    /// <summary>
    /// Returns the candidate types. The default value lets the plugin use the generated registry of its assembly (or, without a registry,
    /// the types of the assembly). An override is an explicit list that wins: the plugin registers only these types.
    /// A listed type that is in the registry uses the registry. Other listed types use reflection, and need dynamic code.
    /// </summary>
    protected virtual IEnumerable<Type> GetManagedTypes() => new AssemblyTypes(((object)this).GetType().Assembly);

    // The marker of the default list. RegisterManagedTypes tests the type of the list, so that it needs no reflection to find an override.
    private sealed class AssemblyTypes : IEnumerable<Type>
    {
        private readonly Assembly _assembly;

        public AssemblyTypes(Assembly assembly) => _assembly = assembly;

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The plugin assembly is a trimmer root, so GetTypes returns all of its types.")]
        public IEnumerator<Type> GetEnumerator()
        {
            try
            {
                return ((IEnumerable<Type>)_assembly.GetTypes()).GetEnumerator();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.OfType<Type>().GetEnumerator();
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Returns true to register the type. The default accepts every factory type that is not a plugin.</summary>
    protected virtual bool ShouldRegister([DynamicallyAccessedMembers(ManagedAttributeRegistrar.ManagedTypeMembers)] Type type) =>
        ManagedObjectFactory.IsFactoryType(type) && !typeof(Plugin).IsAssignableFrom(type);

    /// <summary>
    /// Registers the factories and the attributes. A second call does nothing until the types are unregistered.
    /// The plugin reads the generated <see cref="ManagedTypeRegistryAttribute"/> of its assembly on every runtime. A
    /// <see cref="GetManagedTypes"/> override wins: only the listed types are registered, from the registry when it has them.
    /// An assembly without a registry logs one warning, and a JIT runtime then scans the assembly with <see cref="GetManagedTypes"/>.
    /// A runtime without dynamic code (NativeAOT) and without a registry registers no type.
    /// Then the types of the registries in <see cref="ManagedTypeRegistries"/> are added. The first plugin that claims a registry
    /// registers its types, and the last plugin that claims it removes them.
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

        var registry = ManagedTypeRegistries.Find(assembly);
        var candidates = GetManagedTypes();
        if (registry is not null && candidates is AssemblyTypes)
        {
            RegisterFromRegistry(context, registry, category);
        }
        else if (registry is null && !RuntimeFeature.IsDynamicCodeSupported)
        {
            ManagedAttributeAccessor.LogWarning(
                $"The plugin assembly {assembly.GetName().Name} has no managed type registry, and the scan needs dynamic code. Its managed types are not registered.");
        }
        else
        {
            if (registry is null)
            {
                ManagedAttributeAccessor.LogWarning(
                    $"The assembly {assembly.GetName().Name} has no managed type registry; the source generator did not run; reflection scan is used and is not AOT safe.");
            }

            RegisterListed(context, candidates, registry, category);
        }

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

    // The explicit list of a GetManagedTypes override, or the scan of an assembly without a registry.
    // A listed type that is in the registry uses the registry. The others use reflection.
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "The plugin assembly is a trimmer root, so all of its types keep their members.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "The plugin assembly is a trimmer root, so all of its types keep their members.")]
    private void RegisterListed(Context context, IEnumerable<Type> candidates, ManagedTypeRegistryAttribute? registry, string category)
    {
        var selected = candidates.Where(candidate => ShouldRegister(candidate)).ToHashSet();
        var covered = new HashSet<Type>();
        if (registry is not null)
        {
            covered.UnionWith(registry.Registrations.Select(static item => item.Type));
            RegisterFromRegistry(context, registry, category, _registeredTypes, selected);
        }

        // A base type registers before its derived types, so that the derived types can copy its attributes.
        foreach (var type in selected.Where(type => !covered.Contains(type)).OrderBy(GetDepth).ThenBy(static t => t.FullName, StringComparer.Ordinal))
        {
            if (!RuntimeFeature.IsDynamicCodeSupported)
            {
                ManagedAttributeAccessor.LogWarning($"The type {type.FullName} is not in the managed type registry, and the reflection scan needs dynamic code. It is not registered.");
                continue;
            }

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

    internal void RegisterFromRegistry(Context context, ManagedTypeRegistryAttribute registry, string category) =>
        RegisterFromRegistry(context, registry, category, _registeredTypes);

    // Only: when it is not null, the registry types that are not in it are not registered.
    private void RegisterFromRegistry(
        Context context, ManagedTypeRegistryAttribute registry, string category, List<StringHash> registered, HashSet<Type>? only = null)
    {
        // The generated list is in base-before-derived order. The sort keeps the order of the scan path for any list.
        var registrations = registry.Registrations;
        var listed = new HashSet<Type>(registrations.Select(static item => item.Type));
        var skipped = new HashSet<Type>();
        foreach (var item in registrations)
        {
            if (!ShouldRegister(item.Type) || (only is not null && !only.Contains(item.Type)))
            {
                skipped.Add(item.Type);
            }
        }

        var items = registrations.Where(item => !skipped.Contains(item.Type)).OrderBy(static item => GetDepth(item.Type))
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
                skipped.Add(item.Type);
                continue;
            }

            // The generated list of a type leaves out the members of its nearest listed base type. When that base type is not
            // registered, the list is incomplete, and the reflection scan finds the members.
            var complete = item.Attributes is not null && !HasSkippedListedBase(item.Type, listed, skipped);
            RegisterAttributes(context, item.Type, typeInfo, registered, complete ? item.Attributes : null, complete ? item.CreateDefaults : null);
        }
    }

    private static bool HasSkippedListedBase(Type type, HashSet<Type> listed, HashSet<Type> skipped)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (listed.Contains(current))
            {
                return skipped.Contains(current);
            }
        }

        return false;
    }

    private void RegisterAttributes(Context context, [DynamicallyAccessedMembers(ManagedAttributeRegistrar.ManagedTypeMembers)] Type type, TypeInfo typeInfo)
        => RegisterAttributes(context, type, typeInfo, _registeredTypes);

    private static void RegisterAttributes(
        Context context,
        [DynamicallyAccessedMembers(ManagedAttributeRegistrar.ManagedTypeMembers)] Type type,
        TypeInfo typeInfo,
        List<StringHash> registered,
        ManagedAttributeDescriptor[]? descriptors = null,
        Func<Context, Serializable>? createDefaults = null)
    {
        try
        {
            registered.Add(typeInfo.GetType());
            var reflection = context.GetReflection(typeInfo.GetType());
            ManagedAttributeRegistrar.Register(context, reflection, typeInfo, type, descriptors, createDefaults);
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
