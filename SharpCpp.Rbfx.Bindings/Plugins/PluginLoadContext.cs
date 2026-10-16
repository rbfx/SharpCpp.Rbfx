using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace Urho3D.Plugins;

// Load context of one managed plugin assembly. It is not collectible, because hot reload is not supported.
// Assemblies that the host can resolve come from the default context. Thus the plugin and the host share one copy of
// the bindings and of the wrapper types. Only the other dependencies load from the directory of the plugin.
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string assemblyPath)
        : base($"Plugin:{Path.GetFileNameWithoutExtension(assemblyPath)}", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "This context loads plugin assemblies from files. Only PluginRuntime.Load creates it, and that method requires unreferenced code.")]
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (TryLoadFromHost(assemblyName) is { } shared)
        {
            return shared;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
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
