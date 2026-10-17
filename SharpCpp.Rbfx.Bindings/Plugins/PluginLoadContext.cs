using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace Urho3D.Plugins;

// Load context of one managed plugin assembly. It is not collectible, because hot reload is not supported.
// Assemblies that the host can resolve come from the default context. Thus the plugin and the host share one copy of
// the bindings and of the wrapper types. Only the other dependencies load from the directory of the plugin.
// An assembly that another plugin context already loaded is shared by its simple name. Thus a type of plugin B can derive
// from a type of plugin A, and both plugins see one copy of the type of A.
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly Dictionary<string, Assembly> LoadedByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string assemblyPath)
        : base($"Plugin:{Path.GetFileNameWithoutExtension(assemblyPath)}", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "This context loads plugin assemblies from files. Only PluginRuntime.Load creates it, and that method requires unreferenced code.")]
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // The copy of another plugin comes first: all plugins must use the same copy of a type.
        if (FindLoaded(assemblyName) is { } loaded)
        {
            return loaded;
        }

        if (TryLoadFromHost(assemblyName) is { } shared)
        {
            return shared;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path is null)
        {
            return null;
        }

        var assembly = LoadFromAssemblyPath(path);
        Remember(assembly);
        return assembly;
    }

    /// <summary>Records the plugin assembly that a context loaded, for the plugins that reference it. The first assembly of a name stays.</summary>
    internal static void Remember(Assembly assembly)
    {
        if (assembly.GetName().Name is { } name)
        {
            lock (LoadedByName)
            {
                LoadedByName.TryAdd(name, assembly);
            }
        }
    }

    /// <summary>Returns the assembly of this file name that a plugin context loaded already as a dependency of another plugin.</summary>
    internal static Assembly? FindLoadedFile(string assemblyPath)
    {
        var name = AssemblyName.GetAssemblyName(assemblyPath).Name;
        if (name is null)
        {
            return null;
        }

        // A dependency of another plugin comes from the host first (see TryLoadFromHost), so this plugin uses the same copy.
        foreach (var loaded in Default.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return loaded;
            }
        }

        lock (LoadedByName)
        {
            return LoadedByName.GetValueOrDefault(name);
        }
    }

    // An assembly of the same name that another plugin context loaded, when its version is the requested version or newer.
    private static Assembly? FindLoaded(AssemblyName requested)
    {
        if (requested.Name is null)
        {
            return null;
        }

        lock (LoadedByName)
        {
            return LoadedByName.TryGetValue(requested.Name, out var loaded) &&
                   (requested.Version is null || loaded.GetName().Version >= requested.Version)
                ? loaded
                : null;
        }
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? 0 : LoadUnmanagedDllFromPath(path);
    }

    private static Assembly? TryLoadFromHost(AssemblyName assemblyName)
    {
        foreach (var loaded in Default.Assemblies)
        {
            if (AssemblyName.ReferenceMatchesDefinition(assemblyName, loaded.GetName()))
            {
                return loaded;
            }
        }

        try
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (FileLoadException)
        {
            return null;
        }
    }
}
