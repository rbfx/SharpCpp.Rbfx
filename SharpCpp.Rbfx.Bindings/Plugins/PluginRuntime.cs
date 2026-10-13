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
    public static Assembly Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var fullPath = Path.GetFullPath(path);
        lock (Sync)
        {
            if (!Assemblies.TryGetValue(fullPath, out var assembly))
            {
                assembly = new PluginLoadContext(fullPath).LoadFromAssemblyPath(fullPath);
                Assemblies.Add(fullPath, assembly);
            }

            return assembly;
        }
    }

    /// <summary>
    /// Returns the plugin class of the assembly: the type of <see cref="PluginModuleAttribute"/>, else the first
    /// concrete <see cref="Plugin"/> subclass with a public constructor that takes a context.
    /// </summary>
    public static Type FindPluginType(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (assembly.GetCustomAttribute<PluginModuleAttribute>() is { } module)
        {
            if (!IsPluginType(module.PluginType))
            {
                throw new InvalidOperationException(
                    $"The PluginModule type '{module.PluginType}' of '{assembly.GetName().Name}' is not a concrete Plugin with a public constructor that takes a Context.");
            }

            return module.PluginType;
        }

        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types;
        }

        foreach (var type in types)
        {
            if (type is not null && IsPluginType(type))
            {
                return type;
            }
        }

        throw new InvalidOperationException($"The assembly '{assembly.GetName().Name}' has no concrete Plugin with a public constructor that takes a Context.");
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
        return (Plugin)Activator.CreateInstance(type, context)!;
    }

    private static bool IsPluginType(Type type) =>
        type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters && type.IsSubclassOf(typeof(Plugin)) &&
        type.GetConstructor([typeof(global::Urho3D.Core.Context)]) is not null;
}
