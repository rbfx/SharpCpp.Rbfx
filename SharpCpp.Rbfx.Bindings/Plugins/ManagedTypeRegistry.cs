using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Scene;

using TypeInfo = Urho3D.Core.TypeInfo;

namespace Urho3D.Plugins;

/// <summary>
/// One managed object type of a <see cref="ManagedTypeRegistryAttribute"/>. <see cref="Register"/> registers the factory
/// of the type in a context with a category and returns the type info. It is a direct generic call, so it is AOT safe.
/// <see cref="Attributes"/> is the complete list of the engine attributes of the type, without the attributes of its nearest listed base type.
/// A descriptor has the default value of its member: the generator reads it from the declaration initializer, so the plugin makes no
/// temporary instance.
/// <see cref="Category"/> is the reflection category from <see cref="FactoryReflectionAttribute"/>. When it is null, the registration
/// uses the category of the plugin.
/// </summary>
public readonly record struct ManagedTypeRegistration(
    [DynamicallyAccessedMembers(ManagedTypeRegistration.TypeMembers)]
    [property: DynamicallyAccessedMembers(ManagedTypeRegistration.TypeMembers)]
    Type Type,
    Func<Context, string, TypeInfo> Register,
    ManagedAttributeDescriptor[] Attributes,
    string? Category = null)
{
    /// <summary>
    /// The members of a registered type that the trimmer and the AOT compiler keep. The overrides and the constructor are called from
    /// the engine, and nothing in managed code calls them. A type of a head assembly is not a trimmer root, so the type needs this annotation.
    /// </summary>
    internal const DynamicallyAccessedMemberTypes TypeMembers =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces;
}

/// <summary>
/// The list of the managed object types of an assembly: the types with <see cref="FactoryReflectionAttribute"/>.
/// The source generator (SharpCpp.Rbfx.SourceGenerator) derives a sealed attribute for each assembly that has
/// such types or a <see cref="ManagedPlugin"/>, and applies it to the assembly.
/// <see cref="ManagedPlugin"/> uses this list as the only source of the managed types, so the registration needs no dynamic code.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public abstract class ManagedTypeRegistryAttribute : Attribute
{
    /// <summary>The managed object types of the assembly. A base type comes before its derived types.</summary>
    public abstract IReadOnlyList<ManagedTypeRegistration> Registrations { get; }
}

/// <summary>
/// The managed type registries of other assemblies, for example the application assembly of a head.
/// On load, a <see cref="ManagedPlugin"/> registers the types of its own assembly, then the types of each added registry
/// that no other loaded plugin registered. The category of these types is the simple name of their assembly,
/// unless <see cref="FactoryReflectionAttribute.Category"/> of the type sets another category.
/// The first plugin that claims a registry registers its types. The last plugin that claims it removes them on unload.
/// </summary>
public static class ManagedTypeRegistries
{
    private static readonly object Sync = new();
    private static readonly List<ManagedTypeRegistryAttribute> Registries = new();
    private static readonly Dictionary<Assembly, ClaimState> Claims = new();

    /// <summary>Returns the generated registry of the assembly, or null when the assembly has none.</summary>
    public static ManagedTypeRegistryAttribute? Find(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.GetCustomAttribute<ManagedTypeRegistryAttribute>();
    }

    /// <summary>Adds the generated registry of the assembly. Returns false when the registry of the assembly is already added.</summary>
    /// <exception cref="ArgumentException">The assembly has no generated registry.</exception>
    public static bool Add(Assembly assembly)
    {
        var registry = Find(assembly) ??
            throw new ArgumentException($"The assembly '{assembly.GetName().Name}' has no ManagedTypeRegistryAttribute. Reference SharpCpp.Rbfx.SourceGenerator in its project.", nameof(assembly));
        return Add(registry);
    }

    /// <summary>Adds a registry. Returns false when a registry of the same assembly is already added.</summary>
    public static bool Add(ManagedTypeRegistryAttribute registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var assembly = registry.GetType().Assembly;
        lock (Sync)
        {
            if (Registries.Exists(item => item.GetType().Assembly == assembly))
            {
                return false;
            }

            Registries.Add(registry);
            return true;
        }
    }

    /// <summary>The added registries, in the order of the calls to Add.</summary>
    internal static ManagedTypeRegistryAttribute[] Snapshot()
    {
        lock (Sync)
        {
            return Registries.ToArray();
        }
    }

    /// <summary>
    /// Counts a claim of the registry of the assembly. Returns true for the first claim: the caller registers the types and gives
    /// them to <see cref="SetTypes"/>. The caller must call <see cref="Release"/>.
    /// </summary>
    internal static bool Claim(Assembly assembly)
    {
        lock (Sync)
        {
            if (Claims.TryGetValue(assembly, out var claim))
            {
                claim.Count++;
                return false;
            }

            Claims.Add(assembly, new ClaimState { Count = 1 });
            return true;
        }
    }

    /// <summary>Stores the registered types of a claimed registry, so that the last plugin can remove them.</summary>
    internal static void SetTypes(Assembly assembly, List<StringHash> types)
    {
        lock (Sync)
        {
            if (Claims.TryGetValue(assembly, out var claim))
            {
                claim.Types = types;
            }
        }
    }

    /// <summary>Removes a claim. Returns the registered types after the last claim, and null while other plugins still claim the registry.</summary>
    internal static List<StringHash>? Release(Assembly assembly)
    {
        lock (Sync)
        {
            if (!Claims.TryGetValue(assembly, out var claim) || --claim.Count > 0)
            {
                return null;
            }

            Claims.Remove(assembly);
            return claim.Types;
        }
    }

    /// <summary>Removes an added registry. Tests only.</summary>
    internal static void Remove(Assembly assembly)
    {
        lock (Sync)
        {
            Registries.RemoveAll(item => item.GetType().Assembly == assembly);
            Claims.Remove(assembly);
        }
    }

    private sealed class ClaimState
    {
        public int Count;
        public List<StringHash> Types = new();
    }
}
