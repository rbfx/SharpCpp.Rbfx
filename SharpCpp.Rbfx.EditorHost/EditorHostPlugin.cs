using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

using Urho3D.Core;
using Urho3D.IO;
using Urho3D.Plugins;

namespace SharpCpp.Rbfx.EditorHost;

/// <summary>
/// The bootstrap plugin of the editor. When the plugin is created for a plugin manager, it registers the managed plugins
/// in the folder ManagedPlugins next to this assembly. The registration does not load the plugins.
/// The engine creates a new plugin manager each time a project opens, so the native editor registers this plugin again
/// for each new plugin manager (DotNetHost::RegisterPlugin), and the registration runs again. The plugin is never loaded,
/// so it is not in the loaded plugins that the project saves.
/// </summary>
public sealed partial class EditorHostPlugin : Plugin
{
    public const string PluginsDirectoryName = "ManagedPlugins";

    // The constructor runs before the plugin manager adds this plugin, so the manager is not in an update.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The editor is a dynamic-loading host. It is not trimmed.")]
    public EditorHostPlugin(Context context)
        : base(context)
    {
        var manager = context.GetSubsystem<PluginManager>();
        if (manager is null)
        {
            Log.Error("EditorHost: the context has no PluginManager.");
            return;
        }

        var directory = Path.Combine(Path.GetDirectoryName(typeof(EditorHostPlugin).Assembly.Location) ?? string.Empty, PluginsDirectoryName);
        RegisterPlugins(manager, directory);
    }

    /// <summary>Verifies and registers each *.dll of the directory. A failed file is logged and skipped.</summary>
    [RequiresUnreferencedCode("Loading a plugin assembly from a file needs the runtime to load code that the trimmer cannot analyze.")]
    public static int RegisterPlugins(PluginManager manager, string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var count = 0;
        var files = Directory.GetFiles(directory, "*.dll");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var path in files)
        {
            try
            {
                if (!PluginRuntime.Verify(path))
                {
                    Log.Warning($"EditorHost: '{path}' is not a managed plugin assembly. It is skipped.");
                    continue;
                }

                manager.RegisterManagedPlugin(path);
                count++;
            }
            catch (Exception exception)
            {
                Log.Error($"EditorHost: cannot register '{path}': {exception.GetType().Name}: {exception.Message}");
            }
        }

        return count;
    }
}
