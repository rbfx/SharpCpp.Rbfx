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
    /// <remarks>
    /// On failure the method restores the previous loaded plugins and throws <see cref="InvalidOperationException"/>.
    /// The engine has no API to remove a static plugin, so the failed plugin stays registered in the manager, but
    /// it is not in the loaded plugins and the engine does not load it again. A later call with the same assembly
    /// and name reuses that registered plugin and tries to load it again. A call with the same name and another
    /// assembly fails, because the name is used.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A second call for an already loaded plugin throws "already registered".</exception>
    public static Plugin LoadManagedPlugin(this PluginManager manager, string assemblyPath, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"The plugin assembly '{assemblyPath}' does not exist.", assemblyPath);
        }

        var assembly = PluginRuntime.Load(assemblyPath);
        var pluginName = name ?? PluginRuntime.GetPluginName(assembly);
        var plugin = FindFailedPlugin(manager, assembly, pluginName) ?? manager.RegisterManagedPlugin(assembly, pluginName);

        return LoadRegistered(manager, plugin, pluginName, static m => m.Commit());
    }

    // The commit parameter lets a test inject a failure in the load and in the restore. A managed exception in a plugin
    // callback ends the process. The method is internal for the E2E tests.
    internal static Plugin LoadRegistered(PluginManager manager, Plugin plugin, string pluginName, Action<PluginManager> commit)
    {
        var previous = GetLoadedNames(manager);
        var requested = new List<string>(previous) { pluginName };
        Exception? failure = null;
        try
        {
            SetLoadedNames(manager, requested);
            commit(manager);
            if (plugin.IsLoaded)
            {
                return plugin;
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        var restoreFailure = RestoreLoadedNames(manager, previous, commit);
        if (restoreFailure is not null)
        {
            // The manager can keep the failed plugin in its list, so the caller must see both errors.
            failure ??= new InvalidOperationException("The commit did not load the plugin.");
            throw new InvalidOperationException(
                $"The plugin manager did not load the plugin '{pluginName}', and the restore of the loaded plugins failed. " +
                "The loaded plugins are not known.", new AggregateException(failure, restoreFailure));
        }

        throw new InvalidOperationException(
            $"The plugin manager did not load the plugin '{pluginName}'. The plugin stays registered, but not loaded.", failure);
    }

    // A plugin that failed to load stays registered and is not loaded. A retry reuses it, because it cannot be removed.
    private static Plugin? FindFailedPlugin(PluginManager manager, Assembly assembly, string name)
    {
        if (!IsNameUsed(manager, name))
        {
            return null;
        }

        using var key = new Ea.String(name);
        var existing = manager.GetPlugin(key, true);
        if (existing is { IsInvalid: false, IsLoaded: false } && ((object)existing).GetType() == PluginRuntime.FindPluginType(assembly))
        {
            return existing;
        }

        return null;
    }

    private static List<string> GetLoadedNames(PluginManager manager)
    {
        var loaded = new List<string>();
        using var current = manager.LoadedPlugins;
        foreach (var item in current)
        {
            loaded.Add(item.ToString());
        }

        return loaded;
    }

    private static void SetLoadedNames(PluginManager manager, List<string> names)
    {
        // The native vector copies the items on push, so the managed strings and the vector are disposed after the call.
        var items = names.Select(static item => new Ea.String(item)).ToList();
        try
        {
            using var vector = new Ea.Vector<Ea.String>(items);
            manager.SetPluginsLoaded(vector);
        }
        finally
        {
            foreach (var item in items)
            {
                item.Dispose();
            }
        }
    }

    // Returns the restore failure, or null when the restore succeeds.
    private static Exception? RestoreLoadedNames(PluginManager manager, List<string> previous, Action<PluginManager> commit)
    {
        try
        {
            SetLoadedNames(manager, previous);
            commit(manager);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    // These queries do not load plugins. EnumerateLoadedModules lists all static plugins and the loaded dynamic plugins.
    // GetDynamicPlugin with ignoreUnloaded finds a dynamic plugin that is not loaded and creates nothing.
    // A null native result is a null reference.
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
