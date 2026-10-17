using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.Math;
using Urho3D.Plugins;
using Urho3D.Scene;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed unsafe class ManagedPluginLoadFailureTests
{
    private const string AttributePluginName = "E2E.AttributePlugin";
    private const string RetryPluginName = "E2E.RetryPlugin";
    private const string BossPluginName = "E2E.BossPlugin";

    private static readonly StringHash AttributeType = new("AttributeComponent");
    private static readonly StringHash RetryType = new("RetryPluginComponent");
    private static readonly StringHash BossType = new("AttributeBoss");
    private static readonly StringHash ChiefType = new("ChiefBoss");
    private static readonly StringHash MiddleBossType = new("MiddleBoss");
    private static readonly StringHash GenericBossType = new("GenericBoss");

    private static string AttributePath =>
        Path.Combine(AppContext.BaseDirectory, "plugins", "AttributePlugin", "SharpCpp.Rbfx.Tests.E2E.AttributePlugin.dll");

    private static string BossPath =>
        Path.Combine(AppContext.BaseDirectory, "plugins", "BossPlugin", "SharpCpp.Rbfx.Tests.E2E.BossPlugin.dll");

    private static string ChiefPath =>
        Path.Combine(AppContext.BaseDirectory, "plugins", "ChiefPlugin", "SharpCpp.Rbfx.Tests.E2E.ChiefPlugin.dll");

    private static string RetryPath =>
        Path.Combine(AppContext.BaseDirectory, "plugins", "RetryPlugin", "SharpCpp.Rbfx.Tests.E2E.RetryPlugin.dll");

    [Fact]
    public void Load_managed_plugin_loads_and_creates_components_by_name()
    {
        using var host = new Host();
        var plugin = host.Manager.LoadManagedPlugin(AttributePath);

        Assert.True(plugin.IsLoaded);
        Assert.Equal(AttributePluginName, plugin.PluginName.ToString());
        Assert.Equal(new[] { AttributePluginName }, host.LoadedNames());
        Assert.True(host.Manager.IsPluginLoaded(AttributePluginName));
        host.AssertCreatable(AttributeType);

        host.Unload();
        Assert.False(plugin.IsLoaded);
        host.AssertNotCreatable(AttributeType);
    }

    [Fact]
    public void Type_with_a_base_type_of_another_plugin_copies_the_base_attributes()
    {
        using var host = new Host();
        host.Manager.LoadManagedPlugin(AttributePath);
        var boss = host.Manager.LoadManagedPlugin(BossPath);
        Assert.True(boss.IsLoaded);

        // The plugin B shares the assembly of the plugin A, so both use one copy of the type of A.
        var shared = PluginRuntime.Load(AttributePath);
        Assert.IsType<PluginLoadContext>(AssemblyLoadContext.GetLoadContext(shared));
        var bossType = PluginRuntime.Load(BossPath).GetTypes().Single(static type => type.Name == "AttributeBoss");
        Assert.Same(shared, bossType.BaseType!.Assembly);

        // The registry of the plugin B lists only "Rage". The others come from the reflection of the base type of plugin A.
        var component = host.Create(BossType);
        Assert.Equal(4, host.ReadInt(component, "Rage"));
        Assert.Equal(7, host.ReadInt(component, "Count"));
        Assert.Equal(42, host.ReadInt(component, "Secret Value"));
        Assert.Equal(2.5f, host.ReadFloat(component, "Speed"));
        Assert.True(host.WriteInt(component, "Secret Value", 11));
        Assert.Equal(11, host.ReadInt(component, "Secret Value"));

        // The defaults of the inherited attributes are the initializers of the base type, also after a write.
        Assert.Equal(42, host.ReadDefaultInt(component, "Secret Value"));
        Assert.Equal(7, host.ReadDefaultInt(component, "Count"));
        Assert.Equal(4, host.ReadDefaultInt(component, "Rage"));
        host.Unload();
    }

    [Fact]
    public void Type_with_an_unlisted_abstract_type_of_another_plugin_between_it_and_the_base_copies_the_base_attributes()
    {
        using var host = new Host();
        host.Manager.LoadManagedPlugin(AttributePath);
        host.Manager.LoadManagedPlugin(BossPath);

        var component = host.Create(MiddleBossType);
        Assert.Equal(3, host.ReadInt(component, "Fury"));
        Assert.Equal(7, host.ReadInt(component, "Count"));
        Assert.Equal(42, host.ReadInt(component, "Secret Value"));
        host.Unload();
    }

    [Fact]
    public void Type_with_a_generic_type_between_it_and_the_base_of_another_plugin_copies_the_base_attributes()
    {
        using var host = new Host();
        host.Manager.LoadManagedPlugin(AttributePath);
        host.Manager.LoadManagedPlugin(BossPath);

        var component = host.Create(GenericBossType);
        Assert.Equal(6, host.ReadInt(component, "Power"));
        Assert.Equal(5, host.ReadInt(component, "Level"));
        Assert.Equal(7, host.ReadInt(component, "Count"));
        Assert.Equal(42, host.ReadInt(component, "Secret Value"));
        host.Unload();
    }

    [Fact]
    public void Types_with_a_type_between_them_and_the_base_of_a_plugin_that_is_not_loaded_are_not_registered()
    {
        var messages = new List<string>();
        ManagedAttributeAccessor.MessageObserver = messages.Add;
        try
        {
            using var host = new Host();
            var boss = host.Manager.LoadManagedPlugin(BossPath);

            Assert.True(boss.IsLoaded);
            host.AssertNotCreatable(MiddleBossType);
            host.AssertNotCreatable(GenericBossType);
            host.Unload();
        }
        finally
        {
            ManagedAttributeAccessor.MessageObserver = null;
        }

        AssertBaseError(messages, "MiddleBoss", "AttributeComponent");
        AssertBaseError(messages, "GenericBoss", "AttributeComponent");
    }

    [Fact]
    public void Type_with_a_base_type_of_a_plugin_that_is_not_loaded_is_not_registered()
    {
        var messages = new List<string>();
        ManagedAttributeAccessor.MessageObserver = messages.Add;
        try
        {
            using var host = new Host();
            var boss = host.Manager.LoadManagedPlugin(BossPath);

            // The plugin loads and logs an error. The derived type is not registered, because it would lack the base attributes.
            Assert.True(boss.IsLoaded);
            host.AssertNotCreatable(BossType);
            host.Unload();
        }
        finally
        {
            ManagedAttributeAccessor.MessageObserver = null;
        }

        AssertBaseError(messages, "AttributeBoss", "AttributeComponent");
    }

    [Fact]
    public void Type_of_a_third_plugin_copies_the_attributes_of_both_base_plugins()
    {
        using var host = new Host();
        host.Manager.LoadManagedPlugin(AttributePath);
        host.Manager.LoadManagedPlugin(BossPath);
        host.Manager.LoadManagedPlugin(ChiefPath);

        // ChiefBoss derives from AttributeBoss (plugin B), which derives from AttributeComponent (plugin A).
        var component = host.Create(ChiefType);
        Assert.Equal(9, host.ReadInt(component, "Rank"));
        Assert.Equal(4, host.ReadInt(component, "Rage"));
        Assert.Equal(7, host.ReadInt(component, "Count"));
        Assert.Equal(42, host.ReadInt(component, "Secret Value"));
        Assert.Equal(2.5f, host.ReadFloat(component, "Speed"));
        host.Unload();
    }

    [Fact]
    public void Type_of_a_third_plugin_is_not_registered_when_the_middle_plugin_is_not_loaded()
    {
        var messages = new List<string>();
        ManagedAttributeAccessor.MessageObserver = messages.Add;
        try
        {
            using var host = new Host();
            host.Manager.LoadManagedPlugin(AttributePath);
            var chief = host.Manager.LoadManagedPlugin(ChiefPath);

            Assert.True(chief.IsLoaded);
            host.AssertNotCreatable(ChiefType);
            host.Unload();
        }
        finally
        {
            ManagedAttributeAccessor.MessageObserver = null;
        }

        AssertBaseError(messages, "ChiefBoss", "AttributeBoss");
    }

    [Fact]
    public void Plugins_that_derive_from_each_other_load_again_after_the_base_plugin_unloads()
    {
        using var host = new Host();
        host.Manager.LoadManagedPlugin(AttributePath);
        host.Manager.LoadManagedPlugin(BossPath);
        host.AssertCreatable(BossType);

        // Unload A while B is loaded, load A again, then load B again.
        host.SetLoaded(BossPluginName);
        host.AssertNotCreatable(AttributeType);
        host.SetLoaded(BossPluginName, AttributePluginName);
        host.AssertCreatable(AttributeType);
        host.SetLoaded(AttributePluginName);
        host.AssertNotCreatable(BossType);
        host.SetLoaded(AttributePluginName, BossPluginName);

        var component = host.Create(BossType);
        Assert.Equal(4, host.ReadInt(component, "Rage"));
        Assert.Equal(7, host.ReadInt(component, "Count"));
        Assert.Equal(42, host.ReadInt(component, "Secret Value"));
        host.Unload();
    }

    private static void AssertBaseError(List<string> messages, string typeName, string baseName)
    {
        Assert.Contains(
            messages,
            message => message.Contains("Cannot register the managed type", StringComparison.Ordinal) &&
                       message.Contains("." + typeName + ":", StringComparison.Ordinal) &&
                       message.Contains("base type", StringComparison.Ordinal) &&
                       message.Contains("." + baseName + " ", StringComparison.Ordinal) &&
                       message.Contains("Load the plugin of that assembly first", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_managed_plugin_with_missing_file_throws_clear_exception()
    {
        using var host = new Host();
        var path = Path.Combine(AppContext.BaseDirectory, "plugins", "Missing", "Missing.Plugin.dll");

        var exception = Assert.Throws<FileNotFoundException>(() => host.Manager.LoadManagedPlugin(path));
        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Missing.Plugin.dll", exception.Message, StringComparison.Ordinal);
        Assert.Empty(host.LoadedNames());
        Assert.Throws<ArgumentException>(() => host.Manager.LoadManagedPlugin(string.Empty));
    }

    [Fact]
    public void Failed_load_restores_the_loaded_list_and_allows_a_retry()
    {
        using var host = new Host();
        var other = host.Manager.LoadManagedPlugin(AttributePath);
        Assert.Equal(new[] { AttributePluginName }, host.LoadedNames());

        // A managed exception in a plugin callback ends the process, so the test injects the failure in the commit step.
        var registered = host.Manager.RegisterManagedPlugin(RetryPath);
        var injected = new IOException("injected commit failure");
        var exception = Assert.Throws<InvalidOperationException>(() => PluginManagerExtensions.LoadRegistered(host.Manager, registered, RetryPluginName, CommitFailingOnce(injected)));
        Assert.Contains(RetryPluginName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("did not load", exception.Message, StringComparison.Ordinal);
        Assert.Same(injected, exception.InnerException);
        AssertRestored(host, registered, other);

        // A commit that does not load the plugin gives the same clear failure.
        exception = Assert.Throws<InvalidOperationException>(() => PluginManagerExtensions.LoadRegistered(host.Manager, registered, RetryPluginName, static _ => { }));
        Assert.Contains(RetryPluginName, exception.Message, StringComparison.Ordinal);
        AssertRestored(host, registered, other);

        // The plugin manager does not load the failed plugin on a later commit.
        host.Manager.Commit();
        AssertRestored(host, registered, other);

        // A retry with the same name reuses the registered plugin.
        var retried = host.Manager.LoadManagedPlugin(RetryPath);
        Assert.True(retried.IsLoaded);
        Assert.True(other.IsLoaded);
        Assert.Equal(new[] { AttributePluginName, RetryPluginName }, host.LoadedNames());
        host.AssertCreatable(RetryType);
        host.AssertCreatable(AttributeType);

        // The same name with another assembly stays an error. The list does not change.
        Assert.Throws<InvalidOperationException>(() => host.Manager.LoadManagedPlugin(AttributePath, RetryPluginName));
        Assert.Equal(new[] { AttributePluginName, RetryPluginName }, host.LoadedNames());

        host.Unload();
    }

    [Fact]
    public void Failed_restore_throws_both_failures_and_leaves_a_manager_that_can_recover()
    {
        using var host = new Host();
        var other = host.Manager.LoadManagedPlugin(AttributePath);
        var registered = host.Manager.RegisterManagedPlugin(RetryPath);

        // The commit fails in the load and again in the restore.
        var loadFailure = new IOException("injected commit failure");
        var restoreFailure = new TimeoutException("injected restore failure");
        var calls = 0;
        var exception = Assert.Throws<InvalidOperationException>(() => PluginManagerExtensions.LoadRegistered(
            host.Manager, registered, RetryPluginName, _ => throw (++calls == 1 ? loadFailure : restoreFailure)));
        Assert.Equal(2, calls);
        Assert.Contains(RetryPluginName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("restore", exception.Message, StringComparison.Ordinal);
        var aggregate = Assert.IsType<AggregateException>(exception.InnerException);
        Assert.Equal(new Exception[] { loadFailure, restoreFailure }, aggregate.InnerExceptions);

        // The injected failures commit nothing, so a real commit brings the manager back to the previous state.
        host.Manager.SetPluginsLoaded(new Ea.Vector<Ea.String>([new Ea.String(AttributePluginName)]));
        host.Manager.Commit();
        AssertRestored(host, registered, other);

        host.Unload();
    }

    [Fact]
    public void Native_host_entry_point_writes_errors_through_the_callback()
    {
        using var host = new Host();
        var manager = host.Manager.DangerousGetHandle();

        // A missing file.
        var missing = Path.Combine(AppContext.BaseDirectory, "plugins", "Missing", "Missing.Plugin.dll");
        var (result, error) = CallLoadPlugin(manager, missing);
        Assert.Equal(0, result);
        Assert.StartsWith("FileNotFoundException: ", error, StringComparison.Ordinal);
        Assert.Contains("Missing.Plugin.dll", error, StringComparison.Ordinal);

        // A file that is not an assembly.
        (result, error) = CallLoadPlugin(manager, Path.Combine(AppContext.BaseDirectory, "xunit.runner.json"));
        Assert.Equal(0, result);
        Assert.StartsWith("BadImageFormatException: ", error, StringComparison.Ordinal);

        // An empty path and a null plugin manager.
        (result, error) = CallLoadPlugin(manager, string.Empty);
        Assert.Equal(0, result);
        Assert.StartsWith("ArgumentException: ", error, StringComparison.Ordinal);
        Assert.Contains("path is empty", error, StringComparison.Ordinal);

        (result, error) = CallLoadPlugin(0, AttributePath);
        Assert.Equal(0, result);
        Assert.StartsWith("ArgumentNullException: ", error, StringComparison.Ordinal);
        Assert.Empty(host.LoadedNames());

        // The same call without a callback only returns the failure code.
        Assert.Equal(0, CallLoadPlugin(manager, missing, useCallback: false).Result);

        // A good plugin returns 1 and writes no error.
        (result, error) = CallLoadPlugin(manager, AttributePath);
        Assert.Equal(1, result);
        Assert.Equal(string.Empty, error);
        Assert.Equal(new[] { AttributePluginName }, host.LoadedNames());
        host.AssertCreatable(AttributeType);

        host.Unload();
    }

    // The first commit (the load) fails. The next commit (the restore) is a real commit.
    private static Action<PluginManager> CommitFailingOnce(Exception failure)
    {
        var failed = false;
        return manager =>
        {
            if (!failed)
            {
                failed = true;
                throw failure;
            }

            manager.Commit();
        };
    }

    private static void AssertRestored(Host host, Plugin failed, Plugin other)
    {
        Assert.Equal(new[] { AttributePluginName }, host.LoadedNames());
        Assert.False(failed.IsLoaded);
        Assert.False(host.Manager.IsPluginLoaded(RetryPluginName));
        host.AssertNotCreatable(RetryType);
        Assert.True(other.IsLoaded);
        host.AssertCreatable(AttributeType);
    }

    private static (int Result, string Error) CallLoadPlugin(nint manager, string path, bool useCallback = true)
    {
        var text = new StringBuilder();
        var handle = GCHandle.Alloc(text);
        try
        {
            var bytes = Encoding.UTF8.GetBytes(path + '\0');
            fixed (byte* pathBytes = bytes)
            {
                delegate* unmanaged<nint, byte*, int, void> callback = useCallback ? &WriteError : null;
                delegate* unmanaged<nint, byte*, delegate* unmanaged<nint, byte*, int, void>, nint, int> entry =
                    &NativeHostEntryPoints.LoadPlugin;
                var result = entry(manager, pathBytes, callback, GCHandle.ToIntPtr(handle));
                return (result, text.ToString());
            }
        }
        finally
        {
            handle.Free();
        }
    }

    [UnmanagedCallersOnly]
    private static void WriteError(nint state, byte* message, int length)
    {
        var text = (StringBuilder)GCHandle.FromIntPtr(state).Target!;
        text.Append(Encoding.UTF8.GetString(message, length));
    }

    // A plugin manager in its own context. Dispose releases the plugins and the engine objects.
    private sealed class Host : IDisposable
    {
        private readonly Context _context;
        private readonly Engine _engine;
        private readonly Scene _scene;
        private readonly Node _node;

        public Host()
        {
            _context = new Context();
            _engine = new Engine(_context);
            Manager = new PluginManager(_context);
            _context.RegisterSubsystem(Manager);
            _scene = new Scene(_context);
            _node = _scene.CreateChild("holder")!;
        }

        public PluginManager Manager { get; }

        public string[] LoadedNames()
        {
            using var loaded = Manager.LoadedPlugins;
            return loaded.Select(static item => item.ToString()).ToArray();
        }

        public Component Create(StringHash type)
        {
            var created = _node.CreateComponent(type);
            Assert.False(created is null || created.IsInvalid);
            return created!;
        }

        public int ReadInt(Component component, string name)
        {
            using var text = new Ea.String(name);
            using var value = component.GetAttribute(text);
            return value.Int;
        }

        public int ReadDefaultInt(Component component, string name)
        {
            using var text = new Ea.String(name);
            using var value = component.GetAttributeDefault(text);
            return value.Int;
        }

        public float ReadFloat(Component component, string name)
        {
            using var text = new Ea.String(name);
            using var value = component.GetAttribute(text);
            return value.Float;
        }

        public bool WriteInt(Component component, string name, int number)
        {
            using var text = new Ea.String(name);
            using var value = new Variant(number);
            return component.SetAttribute(text, value);
        }

        public void AssertCreatable(StringHash type)
        {
            var created = _node.CreateComponent(type);
            Assert.False(created is null || created.IsInvalid);
            _node.RemoveAllComponents();
        }

        public void AssertNotCreatable(StringHash type)
        {
            var created = _node.CreateComponent(type);
            Assert.True(created is null || created.IsInvalid);
        }

        public void SetLoaded(params string[] names)
        {
            var items = names.Select(static item => new Ea.String(item)).ToList();
            try
            {
                using var vector = new Ea.Vector<Ea.String>(items);
                Manager.SetPluginsLoaded(vector);
                Manager.Commit();
            }
            finally
            {
                foreach (var item in items)
                {
                    item.Dispose();
                }
            }
        }

        public void Unload()
        {
            _node.RemoveAllComponents();
            Manager.SetPluginsLoaded(new Ea.Vector<Ea.String>());
            Manager.Commit();
        }

        public void Dispose()
        {
            _node.RemoveAllComponents();
            Unload();
            _node.Dispose();
            _scene.Dispose();
            _context.RemoveSubsystem(PluginManager.GetTypeStatic());
            Manager.Dispose();
            _engine.Dispose();
            _context.Dispose();
        }
    }
}
