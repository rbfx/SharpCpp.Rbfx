using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

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

    private static readonly StringHash AttributeType = new("AttributeComponent");
    private static readonly StringHash RetryType = new("RetryPluginComponent");

    private static string AttributePath =>
        Path.Combine(AppContext.BaseDirectory, "plugins", "AttributePlugin", "SharpCpp.Rbfx.Tests.E2E.AttributePlugin.dll");

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
        var exception = Assert.Throws<InvalidOperationException>(() => LoadRegistered(host.Manager, registered, _ => throw injected));
        Assert.Contains(RetryPluginName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("did not load", exception.Message, StringComparison.Ordinal);
        Assert.Same(injected, exception.InnerException);
        AssertRestored(host, registered, other);

        // A commit that does not load the plugin gives the same clear failure.
        exception = Assert.Throws<InvalidOperationException>(() => LoadRegistered(host.Manager, registered, static _ => { }));
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

    private static void AssertRestored(Host host, Plugin failed, Plugin other)
    {
        Assert.Equal(new[] { AttributePluginName }, host.LoadedNames());
        Assert.False(failed.IsLoaded);
        Assert.False(host.Manager.IsPluginLoaded(RetryPluginName));
        host.AssertNotCreatable(RetryType);
        Assert.True(other.IsLoaded);
        host.AssertCreatable(AttributeType);
    }

    // The private step of LoadManagedPlugin after the registration. The commit parameter injects a failure.
    private static Plugin LoadRegistered(PluginManager manager, Plugin plugin, Action<PluginManager> commit)
    {
        var method = typeof(PluginManagerExtensions).GetMethod("LoadRegistered", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            return (Plugin)method.Invoke(null, [manager, plugin, plugin.PluginName.ToString(), commit])!;
        }
        catch (TargetInvocationException exception)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException!).Throw();
            throw;
        }
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
                delegate* unmanaged[Cdecl]<nint, byte*, int, void> callback = useCallback ? &WriteError : null;
                var entry = (delegate* unmanaged[Cdecl]<nint, byte*, delegate* unmanaged[Cdecl]<nint, byte*, int, void>, nint, int>)
                    (delegate* unmanaged[Cdecl]<nint, byte*, delegate* unmanaged[Cdecl]<nint, byte*, int, void>, nint, int>)&NativeHostEntryPoints.LoadPlugin;
                var result = entry(manager, pathBytes, callback, GCHandle.ToIntPtr(handle));
                return (result, text.ToString());
            }
        }
        finally
        {
            handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
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
