using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Urho3D.Core;

using TypeInfo = Urho3D.Core.TypeInfo;

namespace Urho3D.Plugins;

/// <summary>
/// One managed object type of a <see cref="ManagedTypeRegistryAttribute"/>. <see cref="Register"/> registers the factory
/// of the type in a context with a category and returns the type info. It is a direct generic call, so it is AOT safe.
/// </summary>
public readonly record struct ManagedTypeRegistration(
    [DynamicallyAccessedMembers(ManagedAttributeRegistrar.ManagedTypeMembers)]
    [property: DynamicallyAccessedMembers(ManagedAttributeRegistrar.ManagedTypeMembers)]
    Type Type,
    Func<Context, string, TypeInfo> Register);

/// <summary>
/// The list of the managed object types of an assembly. The source generator (SharpCpp.Rbfx.SourceGenerator) derives a
/// sealed attribute for each assembly that has managed object types, and applies it to the assembly.
/// <see cref="ManagedPlugin"/> uses this list instead of an assembly scan, so the registration needs no dynamic code.
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
/// that no other loaded plugin registered. The category of these types is the simple name of their assembly.
/// The plugin that registers a registry removes its types on unload, and then the next plugin can register it.
/// </summary>
public static class ManagedTypeRegistries
{
    private static readonly object Sync = new();
    private static readonly List<ManagedTypeRegistryAttribute> Registries = new();
    private static readonly HashSet<Assembly> Claimed = new();

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

    /// <summary>Returns true when no other plugin registers the types of the assembly. The caller must call <see cref="Release"/>.</summary>
    internal static bool TryClaim(Assembly assembly)
    {
        lock (Sync)
        {
            return Claimed.Add(assembly);
        }
    }

    internal static void Release(Assembly assembly)
    {
        lock (Sync)
        {
            Claimed.Remove(assembly);
        }
    }
}
