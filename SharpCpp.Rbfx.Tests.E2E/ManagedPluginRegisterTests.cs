using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.Plugins;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

// Tests the register-only entry point of the native host (DotNetHost::RegisterPlugin).
public sealed unsafe class ManagedPluginRegisterTests
{
    private const string AttributePluginName = "E2E.AttributePlugin";

    private static string AttributePath =>
        Path.Combine(AppContext.BaseDirectory, "plugins", "AttributePlugin", "SharpCpp.Rbfx.Tests.E2E.AttributePlugin.dll");

    [Fact]
    public void Register_plugin_entry_point_registers_without_changing_the_loaded_list()
    {
        var context = new Context();
        var engine = new Engine(context);
        var manager = new PluginManager(context);
        context.RegisterSubsystem(manager);
        try
        {
            var handle = manager.DangerousGetHandle();

            // A good plugin returns 1 and writes no error. The plugin is registered, but not loaded.
            var (result, error) = CallRegisterPlugin(handle, AttributePath);
            Assert.Equal(1, result);
            Assert.Equal(string.Empty, error);
            Assert.Empty(LoadedNames(manager));
            Assert.False(manager.IsPluginLoaded(AttributePluginName));

            // A second registration of the same name fails and leaves the list unchanged.
            (result, error) = CallRegisterPlugin(handle, AttributePath);
            Assert.Equal(0, result);
            Assert.StartsWith("InvalidOperationException: ", error, StringComparison.Ordinal);
            Assert.Contains("already registered", error, StringComparison.Ordinal);
            Assert.Empty(LoadedNames(manager));

            // Bad input fails with a clear error.
            (result, error) = CallRegisterPlugin(handle, Path.Combine(AppContext.BaseDirectory, "plugins", "Missing", "Missing.Plugin.dll"));
            Assert.Equal(0, result);
            Assert.NotEqual(string.Empty, error);
            (result, error) = CallRegisterPlugin(0, AttributePath);
            Assert.Equal(0, result);
            Assert.StartsWith("ArgumentNullException: ", error, StringComparison.Ordinal);
            Assert.Empty(LoadedNames(manager));

            // The registered plugin loads when its name is in the loaded list.
            manager.SetPluginsLoaded(new Ea.Vector<Ea.String>([new Ea.String(AttributePluginName)]));
            manager.Commit();
            Assert.True(manager.IsPluginLoaded(AttributePluginName));
            Assert.Equal(new[] { AttributePluginName }, LoadedNames(manager));

            manager.SetPluginsLoaded(new Ea.Vector<Ea.String>());
            manager.Commit();
        }
        finally
        {
            context.RemoveSubsystem(PluginManager.GetTypeStatic());
            manager.Dispose();
            engine.Dispose();
            context.Dispose();
        }
    }

    private static string[] LoadedNames(PluginManager manager)
    {
        using var loaded = manager.LoadedPlugins;
        return loaded.Select(static item => item.ToString()).ToArray();
    }

    private static (int Result, string Error) CallRegisterPlugin(nint manager, string path)
    {
        var text = new StringBuilder();
        var handle = GCHandle.Alloc(text);
        try
        {
            var bytes = Encoding.UTF8.GetBytes(path + '\0');
            fixed (byte* pathBytes = bytes)
            {
                delegate* unmanaged[Cdecl]<nint, byte*, delegate* unmanaged[Cdecl]<nint, byte*, int, void>, nint, int> entry =
                    &NativeHostEntryPoints.RegisterPlugin;
                var result = entry(manager, pathBytes, &WriteError, GCHandle.ToIntPtr(handle));
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
}
