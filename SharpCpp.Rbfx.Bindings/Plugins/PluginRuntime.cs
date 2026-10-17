using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Urho3D.Plugins;

/// <summary>
/// Loads managed plugin assemblies and creates their plugins. Each assembly path loads once, into its own
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>. The assemblies are never unloaded.
/// </summary>
public static class PluginRuntime
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Assembly> Assemblies = new(StringComparer.Ordinal);
    private static readonly string BindingsAssemblyName = typeof(PluginRuntime).Assembly.GetName().Name!;

    /// <summary>
    /// Returns true when the file is a managed assembly that references the bindings. The file is not loaded.
    /// The plugin class is found only when the assembly loads.
    /// </summary>
    public static bool Verify(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                return false;
            }

            var metadata = peReader.GetMetadataReader();
            if (!metadata.IsAssembly)
            {
                return false;
            }

            foreach (var handle in metadata.AssemblyReferences)
            {
                if (metadata.StringComparer.Equals(metadata.GetAssemblyReference(handle).Name, BindingsAssemblyName))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return false;
        }
    }

    /// <summary>Loads the plugin assembly at the path. A second call with the same full path returns the same assembly.</summary>
    [RequiresUnreferencedCode("Loading a plugin assembly from a file needs the runtime to load code that the trimmer cannot analyze. Use the overloads that take an Assembly in trimmed and AOT applications.")]
    public static Assembly Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var fullPath = Path.GetFullPath(path);
        lock (Sync)
        {
            if (!Assemblies.TryGetValue(fullPath, out var assembly))
            {
                // Another plugin can have loaded this assembly as its dependency. Then the plugin uses that copy, so the types are shared.
                assembly = PluginLoadContext.FindLoadedFile(fullPath) ?? new PluginLoadContext(fullPath).LoadFromAssemblyPath(fullPath);
                PluginLoadContext.Remember(assembly);
                Assemblies.Add(fullPath, assembly);
            }

            return assembly;
        }
    }

    /// <summary>
    /// Returns the plugin class of the assembly: the type of <see cref="PluginModuleAttribute"/>, else the type of the
    /// <see cref="GeneratedPluginTypeAttribute"/> that the source generator emits for the first concrete <see cref="Plugin"/>
    /// subclass with a public constructor that takes a context. The method does not scan the types of the assembly.
    /// </summary>
    // The attribute constructors annotate the type argument with PublicConstructors, so the trimmer and the AOT compiler
    // keep the constructors of the plugin class (the generated trimmer roots keep them too).
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public static Type FindPluginType(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (assembly.GetCustomAttribute<PluginModuleAttribute>() is { } module)
        {
            if (!IsPluginType(KeepPublicConstructors(module.PluginType)))
            {
                throw new InvalidOperationException(
                    $"The PluginModule type '{module.PluginType}' of '{assembly.GetName().Name}' is not a concrete Plugin with a public constructor that takes a Context.");
            }

            return KeepPublicConstructors(module.PluginType);
        }

        if (assembly.GetCustomAttribute<GeneratedPluginTypeAttribute>() is { } generated && IsPluginType(KeepPublicConstructors(generated.PluginType)))
        {
            return KeepPublicConstructors(generated.PluginType);
        }

        throw new InvalidOperationException(
            $"The assembly '{assembly.GetName().Name}' has no concrete Plugin with a public constructor that takes a Context. " +
            "The assembly must reference SharpCpp.Rbfx.SourceGenerator, or name the plugin class with PluginModuleAttribute.");
    }

    /// <summary>Returns the plugin name: the name of <see cref="PluginModuleAttribute"/>, else the simple name of the assembly.</summary>
    public static string GetPluginName(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.GetCustomAttribute<PluginModuleAttribute>()?.Name ?? assembly.GetName().Name!;
    }

    // Returns an owning wrapper. The caller hands its reference over to the native side.
    internal static Plugin Create(Assembly assembly, global::Urho3D.Core.Context context)
    {
        var type = FindPluginType(assembly);
        if (typeof(ManagedPlugin).IsAssignableFrom(type) && ManagedTypeRegistries.Find(assembly) is null)
        {
            throw new InvalidOperationException(ManagedPlugin.NoRegistryMessage(assembly));
        }

        return (Plugin)Activator.CreateInstance(type, context)!;
    }

    // Both the trimmer and the analyzer report a parameter value that flows to an annotated return value as IL2068.
    // The trimmer reports other value sources (an array element, a property) with different codes than the analyzer.
    // This method gives all call sites the same value source.
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    [UnconditionalSuppressMessage("Trimming", "IL2068", Justification = "The generated trimmer roots keep the public constructors of each Plugin class.")]
    private static Type KeepPublicConstructors(Type type) => type;

    private static bool IsPluginType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type) =>
        type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters && type.IsSubclassOf(typeof(Plugin)) &&
        type.GetConstructor([typeof(global::Urho3D.Core.Context)]) is not null;
}
