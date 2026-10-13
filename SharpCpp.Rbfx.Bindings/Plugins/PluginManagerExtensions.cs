using System.Reflection;

namespace Urho3D.Plugins;

/// <summary>
/// Registers managed plugins in the engine <see cref="PluginManager"/> without a file scan.
/// The plugin manager loads a registered plugin only when the name is in <see cref="PluginManager.SetPluginsLoaded"/>.
/// </summary>
public static class PluginManagerExtensions
{
    /// <summary>Loads the assembly at the path and registers its plugin. See <see cref="RegisterManagedPlugin(PluginManager, Assembly, string?)"/>.</summary>
    public static Plugin RegisterManagedPlugin(this PluginManager manager, string assemblyPath, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.RegisterManagedPlugin(PluginRuntime.Load(assemblyPath), name);
    }

    /// <summary>
    /// Creates the plugin of the assembly and adds it to the manager as a static plugin.
    /// The manager owns the plugin. The returned wrapper stays valid until the manager is destroyed.
    /// The plugin does not load here. Add the name to <see cref="PluginManager.SetPluginsLoaded"/> and commit.
    /// A static plugin stays in the manager after it unloads, so a name can be registered only once.
    /// </summary>
    public static Plugin RegisterManagedPlugin(this PluginManager manager, Assembly assembly, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(assembly);

        name ??= PluginRuntime.GetPluginName(assembly);
        // A duplicate name fails an engine assertion in AddStaticPlugin.
        if (IsNameUsed(manager, name))
        {
            throw new InvalidOperationException($"A plugin with the name '{name}' is already registered.");
        }

        // Do not dispose the context: GetContext returns the cached wrapper, which can belong to the caller.
        var plugin = PluginRuntime.Create(assembly, manager.GetContext());
        try
        {
            plugin.PluginName = name;
            if (!manager.AddStaticPlugin(plugin))
            {
                throw new InvalidOperationException($"The plugin manager did not accept the plugin '{name}'.");
            }
        }
        catch
        {
            plugin.Dispose();
            throw;
        }

        // The manager holds a SharedPtr now. It becomes the only native owner and disposes the wrapper on release.
        plugin.TransferOwnershipToNative();
        return plugin;
    }

    /// <summary>
    /// Registers the plugin of the assembly at the path, adds its name to the loaded plugins and commits.
    /// The plugin is loaded when the method returns, so its types can be created by name.
    /// A later call to <see cref="PluginManager.SetPluginsLoaded"/> must keep the name, else the plugin unloads.
    /// </summary>
    public static Plugin LoadManagedPlugin(this PluginManager manager, string assemblyPath, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        var plugin = manager.RegisterManagedPlugin(assemblyPath, name);
        var pluginName = plugin.PluginName.ToString();

        var loaded = new List<Ea.String>();
        using (var current = manager.LoadedPlugins)
        {
            foreach (var item in current)
            {
                loaded.Add(item.ToString());
            }
        }

        loaded.Add(pluginName);
        manager.SetPluginsLoaded(new Ea.Vector<Ea.String>(loaded));
        manager.Commit();
        if (!plugin.IsLoaded)
        {
            throw new InvalidOperationException($"The plugin manager did not load the plugin '{pluginName}'.");
        }

        return plugin;
    }

    // These queries do not load plugins. EnumerateLoadedModules lists all static plugins and the loaded dynamic plugins.
    // GetDynamicPlugin with ignoreUnloaded finds a dynamic plugin that is not loaded and creates nothing.
    // A null native result comes back as a wrapper with a null handle.
    private static bool IsNameUsed(PluginManager manager, string name)
    {
        using (var names = manager.EnumerateLoadedModules())
        {
            foreach (var used in names)
            {
                if (used.ToString() == name)
                {
                    return true;
                }
            }
        }

        return manager.GetDynamicPlugin(name, ignoreUnloaded: true) is { IsInvalid: false };
    }
}
