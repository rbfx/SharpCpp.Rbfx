using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.Math;
using Urho3D.Plugins;
using Urho3D.Scene;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed class ManagedPluginLoadingTests
{
    private const string PluginName = "E2E.TestPlugin";

    private static string PluginPath =>
        Path.Combine(AppContext.BaseDirectory, "plugins", "TestPlugin", "SharpCpp.Rbfx.Tests.E2E.TestPlugin.dll");

    [Fact]
    public void Plugin_assembly_is_verified_without_loading()
    {
        Assert.True(PluginRuntime.Verify(PluginPath));
        Assert.False(PluginRuntime.Verify(typeof(object).Assembly.Location));
        Assert.False(PluginRuntime.Verify(Path.Combine(AppContext.BaseDirectory, "xunit.runner.json")));
    }

    [Fact]
    public void Managed_plugin_from_other_directory_runs_through_plugin_manager()
    {
        // TestPluginState is static, and the plugin assembly is cached for each path.
        // Only this test registers the plugin, so the counters start at zero.
        var assembly = RunPluginLifecycle();

        // The plugin manager owns the plugin, so the plugin dies with the manager.
        // The release is synchronous on this thread, so no GC is necessary.
        Assert.Equal(1, GetState<int>(assembly, "Disposed"));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Assembly RunPluginLifecycle()
    {
        using var context = new Context();
        using var engine = new Engine(context);
        using var manager = new PluginManager(context);
        context.RegisterSubsystem(manager);
        using var scene = new Scene(context);
        using var node = scene.CreateChild("holder")!;

        var plugin = manager.RegisterManagedPlugin(PluginPath);
        var assembly = ((object)plugin).GetType().Assembly;
        var events = GetState<List<string>>(assembly, "Events");
        var componentType = new StringHash("TestPluginComponent");

        Assert.NotEqual(Path.GetDirectoryName(typeof(ManagedPluginLoadingTests).Assembly.Location), Path.GetDirectoryName(assembly.Location));
        Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(assembly));
        Assert.Same(typeof(Plugin).Assembly, ((object)plugin).GetType().BaseType!.Assembly);
        Assert.Equal(PluginName, plugin.PluginName.ToString());
        Assert.False(plugin.IsLoaded);
        // The SharedPtr of the plugin manager is the only native reference.
        Assert.Equal(1, plugin.Refs());
        AssertNotCreated(node, componentType);

        manager.SetPluginsLoaded(new Ea.Vector<Ea.String>(new Ea.String[] { PluginName }));
        manager.Commit();
        Assert.True(plugin.IsLoaded);
        Assert.Equal(new[] { "Load" }, events);

        var created = node.CreateComponent(componentType);
        Assert.False(created.IsInvalid);
        Assert.Equal("TestPluginComponent", ((object)created).GetType().Name);
        Assert.Same(assembly, ((object)created).GetType().Assembly);
        node.RemoveAllComponents();

        manager.StartApplication();
        manager.Commit();
        Assert.True(plugin.IsStarted);

        manager.StopApplication();
        manager.Commit();
        Assert.False(plugin.IsStarted);

        manager.SetPluginsLoaded(new Ea.Vector<Ea.String>());
        manager.Commit();
        Assert.False(plugin.IsLoaded);
        Assert.Equal(new[] { "Load", "Start(False)", "Stop", "Unload" }, events);
        AssertNotCreated(node, componentType);

        Assert.Equal(1, plugin.Refs());
        Assert.Throws<InvalidOperationException>(() => manager.RegisterManagedPlugin(PluginPath));
        Assert.Equal(0, GetState<int>(assembly, "Disposed"));

        // Release the last native reference to the plugin manager.
        context.RemoveSubsystem(PluginManager.GetTypeStatic());
        return assembly;
    }

    private static void AssertNotCreated(Node node, StringHash type)
    {
        var created = node.CreateComponent(type);
        Assert.True(created is null || created.IsInvalid);
    }

    private static T GetState<T>(Assembly assembly, string property) =>
        (T)assembly.GetType("SharpCpp.Rbfx.Tests.E2E.TestPlugin.TestPluginState", throwOnError: true)!
            .GetProperty(property, BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
}
