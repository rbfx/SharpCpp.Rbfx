using System.Reflection;

using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.Math;
using Urho3D.Plugins;
using Urho3D.Scene;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

// Tests the generated ManagedTypeRegistryAttribute against the assembly scan of ManagedPlugin.
public sealed class ManagedTypeRegistryTests
{
    private const string PluginName = "E2E.AttributePlugin";

    private static string PluginPath =>
        Path.Combine(AppContext.BaseDirectory, "plugins", "AttributePlugin", "SharpCpp.Rbfx.Tests.E2E.AttributePlugin.dll");

    [Fact]
    public void Generated_registry_lists_the_same_types_as_the_assembly_scan()
    {
        // ManagedPlugin reads the registry on the JIT runtime too.
        var context = new Context();
        var engine = new Engine(context);
        var manager = new PluginManager(context);
        context.RegisterSubsystem(manager);
        try
        {
            var plugin = (ManagedPlugin)manager.RegisterManagedPlugin(PluginPath);
            var assembly = ((object)plugin).GetType().Assembly;

            // The source generator applies a sealed registry attribute to the plugin assembly.
            var registry = ManagedTypeRegistries.Find(assembly);
            Assert.NotNull(registry);
            Assert.True(registry.GetType().IsSealed);
            Assert.Same(assembly, registry.GetType().Assembly);

            // The list has exactly the types of the scan path (ManagedPlugin.ShouldRegister), with each base type first.
            var scanned = assembly.GetTypes().Where(ShouldRegister).OrderBy(static t => t.FullName, StringComparer.Ordinal).ToArray();
            var listed = registry.Registrations.Select(static item => item.Type).ToList();
            Assert.NotEmpty(scanned);
            Assert.Equal(scanned, listed.OrderBy(static t => t.FullName, StringComparer.Ordinal));
            Assert.Contains(listed, static t => t.Name == "DerivedAttributeComponent");
            foreach (var type in listed)
            {
                if (type.BaseType is { } baseType && listed.Contains(baseType))
                {
                    Assert.True(listed.IndexOf(baseType) < listed.IndexOf(type), $"{baseType.Name} must come before {type.Name}.");
                }
            }

            // ManagedPlugin uses the registry, and registers the same types as the scan path.
            manager.SetPluginsLoaded(new Ea.Vector<Ea.String>(new Ea.String[] { PluginName }));
            manager.Commit();
            Assert.True(plugin.IsLoaded);
            var expected = scanned.Select(static t => new StringHash(t.Name)).OrderBy(static h => h.Hash).ToArray();
            Assert.Equal(expected, plugin.RegisteredTypes.OrderBy(static h => h.Hash).ToArray());
            Assert.All(expected, hash => Assert.True(context.IsReflected(hash)));

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

    [Fact]
    public void Added_registry_registers_on_load_and_unregisters_on_the_last_unload()
    {
        using var fixture = new AddedRegistryFixture();
        var first = new RegistryTestPlugin(fixture.Context);
        first.Register();
        Assert.All(fixture.ExtraTypes, hash => Assert.True(fixture.Context.IsReflected(hash)));
        Assert.Empty(first.RegisteredTypes);

        first.Unregister();
        Assert.All(fixture.ExtraTypes, hash => Assert.False(fixture.Context.IsReflected(hash)));
    }

    [Fact]
    public void Added_registry_uses_descriptors_and_scans_only_types_without_them()
    {
        using var fixture = new AddedRegistryFixture();
        var undescribed = fixture.ExtraRegistrations.Count(static item => item.Attributes is null);
        var scans = ManagedAttributeRegistrar.ReflectionScanCount;
        var plugin = new RegistryTestPlugin(fixture.Context);
        plugin.Register();
        try
        {
            Assert.Equal(scans + undescribed, ManagedAttributeRegistrar.ReflectionScanCount);
        }
        finally
        {
            plugin.Unregister();
        }
    }

    [Fact]
    public void Added_registry_stays_registered_while_another_plugin_is_loaded()
    {
        using var fixture = new AddedRegistryFixture();
        var first = new RegistryTestPlugin(fixture.Context);
        var second = new RegistryTestPlugin(fixture.Context);
        first.Register();
        second.Register();
        Assert.All(fixture.ExtraTypes, hash => Assert.True(fixture.Context.IsReflected(hash)));

        first.Unregister();
        Assert.All(fixture.ExtraTypes, hash => Assert.True(fixture.Context.IsReflected(hash)));

        second.Unregister();
        Assert.All(fixture.ExtraTypes, hash => Assert.False(fixture.Context.IsReflected(hash)));
    }

    [Fact]
    public void Added_registry_registers_again_after_a_reload()
    {
        using var fixture = new AddedRegistryFixture();
        var plugin = new RegistryTestPlugin(fixture.Context);
        plugin.Register();
        plugin.Unregister();
        Assert.All(fixture.ExtraTypes, hash => Assert.False(fixture.Context.IsReflected(hash)));

        plugin.Register();
        Assert.All(fixture.ExtraTypes, hash => Assert.True(fixture.Context.IsReflected(hash)));
        plugin.Unregister();
        Assert.All(fixture.ExtraTypes, hash => Assert.False(fixture.Context.IsReflected(hash)));
    }

    [Fact]
    public void GetManagedTypes_override_wins_over_the_registry_of_the_assembly()
    {
        using var context = new Context();
        var plugin = new RegistryTestPlugin(context) { Types = new[] { typeof(RegistryTestComponent) } };
        plugin.Register();
        Assert.Equal(new[] { new StringHash(nameof(RegistryTestComponent)) }, plugin.RegisteredTypes);
        plugin.Unregister();
        Assert.Empty(plugin.RegisteredTypes);
    }

    [Fact]
    public void Add_needs_an_assembly_with_a_generated_registry()
    {
        Assert.Null(ManagedTypeRegistries.Find(typeof(object).Assembly));
        Assert.Throws<ArgumentException>(() => ManagedTypeRegistries.Add(typeof(object).Assembly));
    }

    private static bool ShouldRegister(Type type) =>
        ManagedObjectFactory.IsFactoryType(type) && !typeof(Plugin).IsAssignableFrom(type);

    // The registry of the attribute plugin assembly is the added registry. The plugins that claim it are in the test assembly,
    // and they register nothing from their own assembly. This is a different assembly, so the claim applies.
    private sealed class AddedRegistryFixture : IDisposable
    {
        private readonly Engine _engine;
        private readonly PluginManager _manager;
        private readonly Assembly _extraAssembly;

        public AddedRegistryFixture()
        {
            Context = new Context();
            _engine = new Engine(Context);
            _manager = new PluginManager(Context);
            Context.RegisterSubsystem(_manager);

            var plugin = (ManagedPlugin)_manager.RegisterManagedPlugin(PluginPath);
            _extraAssembly = ((object)plugin).GetType().Assembly;
            var registry = ManagedTypeRegistries.Find(_extraAssembly)!;
            ExtraTypes = registry.Registrations.Select(static item => new StringHash(item.Type.Name)).ToArray();
            Assert.NotEmpty(ExtraTypes);
            Assert.True(ManagedTypeRegistries.Add(registry));
        }

        public Context Context { get; }

        public StringHash[] ExtraTypes { get; }

        public IReadOnlyList<ManagedTypeRegistration> ExtraRegistrations => ManagedTypeRegistries.Find(_extraAssembly)!.Registrations;

        public void Dispose()
        {
            ManagedTypeRegistries.Remove(_extraAssembly);
            Context.RemoveSubsystem(PluginManager.GetTypeStatic());
            _manager.Dispose();
            _engine.Dispose();
            Context.Dispose();
        }
    }
}

// Registers nothing from the test assembly, unless Types has types. Register and Unregister call the protected methods.
internal sealed partial class RegistryTestPlugin : ManagedPlugin
{
    public RegistryTestPlugin(Context context)
        : base(context)
    {
    }

    public Type[] Types { get; init; } = Array.Empty<Type>();

    // True: the plugin keeps the default of GetManagedTypes, so it registers the registry of its assembly.
    public bool UseDefaultTypes { get; init; }

    // A type for which this returns false is not registered. Null keeps the default rule.
    public Func<Type, bool>? Filter { get; init; }

    protected override bool ShouldRegister(
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(ManagedAttributeRegistrar.ManagedTypeMembers)] Type type) =>
        Filter?.Invoke(type) ?? base.ShouldRegister(type);

    public void Register() => RegisterManagedTypes();

    public void Unregister() => UnregisterManagedTypes();

    protected override IEnumerable<Type> GetManagedTypes() => UseDefaultTypes ? base.GetManagedTypes() : Types;
}

internal sealed partial class RegistryTestComponent : Component
{
    public RegistryTestComponent(Context context)
        : base(context)
    {
    }
}
