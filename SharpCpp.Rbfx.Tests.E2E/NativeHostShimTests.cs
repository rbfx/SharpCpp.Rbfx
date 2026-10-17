using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Urho3D.Plugins;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed unsafe class NativeHostShimTests
{
    private static string TestAssemblyPath => typeof(NativeHostShimTests).Assembly.Location;

    private static string ExportsTypeName => typeof(ShimExports).FullName!;

    [Fact]
    public void Resolve_returns_the_function_pointer_of_an_unmanaged_callers_only_method()
    {
        var pointer = NativeHostShim.Resolve(TestAssemblyPath, ExportsTypeName, nameof(ShimExports.Add), null, out var error);

        Assert.NotEqual(0, pointer);
        Assert.Equal(string.Empty, error);
        Assert.Equal(7, ((delegate* unmanaged<int, int, int>)pointer)(3, 4));

        // An empty delegateTypeName is the same as none. The assembly name is the file stem, so a bare name works.
        var again = NativeHostShim.Resolve("SharpCpp.Rbfx.Tests.E2E", ExportsTypeName + ", SharpCpp.Rbfx.Tests.E2E", nameof(ShimExports.Add), string.Empty, out error);
        Assert.Equal(pointer, again);
    }

    [Fact]
    public void Resolve_reports_a_missing_type()
    {
        var pointer = NativeHostShim.Resolve(TestAssemblyPath, "No.Such.Type", nameof(ShimExports.Add), null, out var error);

        Assert.Equal(0, pointer);
        Assert.Contains("No.Such.Type", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_reports_a_missing_method()
    {
        var pointer = NativeHostShim.Resolve(TestAssemblyPath, ExportsTypeName, "NoSuchMethod", null, out var error);

        Assert.Equal(0, pointer);
        Assert.Contains("NoSuchMethod", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_rejects_a_method_without_unmanaged_callers_only()
    {
        var pointer = NativeHostShim.Resolve(TestAssemblyPath, ExportsTypeName, nameof(ShimExports.Plain), null, out var error);

        Assert.Equal(0, pointer);
        Assert.Contains("UnmanagedCallersOnly", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_rejects_a_delegate_type_name()
    {
        var pointer = NativeHostShim.Resolve(TestAssemblyPath, ExportsTypeName, nameof(ShimExports.Add), "Some.Delegate", out var error);

        Assert.Equal(0, pointer);
        Assert.Equal("delegateTypeName is not supported by the DotNetHost shim.", error);
    }

    [Fact]
    public void Resolve_reports_an_assembly_that_does_not_exist()
    {
        var pointer = NativeHostShim.Resolve("/nowhere/No.Such.Assembly.dll", ExportsTypeName, nameof(ShimExports.Add), null, out var error);

        Assert.Equal(0, pointer);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Attach_on_a_desktop_platform_returns_without_a_change()
    {
        Assert.False(OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsBrowser());
        Assert.False(NativeHostShim.IsAttached);

        NativeHostShim.Attach();

        Assert.False(NativeHostShim.IsAttached);
    }

    [Fact]
    public void Bootstrap_attach_has_no_operating_system_guard_and_is_safe_to_call_twice()
    {
        var first = NativeHostBootstrap.Attach();
        var second = NativeHostBootstrap.Attach();

        Assert.Equal(NativeHostShim.IsAttached, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Load_managed_plugin_accepts_a_loaded_assembly()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "plugins", "AttributePlugin", "SharpCpp.Rbfx.Tests.E2E.AttributePlugin.dll");
        var assembly = PluginRuntime.Load(path);
        var context = new Urho3D.Core.Context();
        var engine = new Urho3D.Engine.Engine(context);
        var manager = new PluginManager(context);
        context.RegisterSubsystem(manager);
        try
        {
            Assert.Throws<ArgumentNullException>(() => manager.LoadManagedPlugin((System.Reflection.Assembly)null!));

            var plugin = manager.LoadManagedPlugin(assembly);

            Assert.True(plugin.IsLoaded);
            Assert.Equal("E2E.AttributePlugin", plugin.PluginName.ToString());
        }
        finally
        {
            manager.SetPluginsLoaded(new Ea.Vector<Ea.String>());
            manager.Commit();
            context.RemoveSubsystem(PluginManager.GetTypeStatic());
            manager.Dispose();
            engine.Dispose();
            context.Dispose();
        }
    }

    private static class ShimExports
    {
        [UnmanagedCallersOnly]
        internal static int Add(int left, int right) => left + right;

        internal static int Plain(int value) => value;
    }
}
