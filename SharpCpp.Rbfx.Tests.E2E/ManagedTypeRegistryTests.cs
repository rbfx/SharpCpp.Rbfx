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

            // The list has exactly the types that ManagedPlugin.ShouldRegister accepts, with each base type first.
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

            // ManagedPlugin registers the types of the registry.
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
    public void Allow_list_limits_the_types_of_the_registry_of_the_assembly()
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

// Registers nothing from the test registry, unless Types has types or UseDefaultTypes is true. Register and Unregister call the protected methods.
internal sealed partial class RegistryTestPlugin : ManagedPlugin
{
    public RegistryTestPlugin(Context context)
        : base(context)
    {
    }

    public Type[] Types { get; init; } = Array.Empty<Type>();

    // True: the plugin registers every type of the registry. Else only the types in Types.
    public bool UseDefaultTypes { get; init; }

    // A type for which this returns false is not registered. Null keeps the default rule.
    public Func<Type, bool>? Filter { get; init; }

    protected override bool ShouldRegister(
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(ManagedTypeRegistration.TypeMembers)] Type type) =>
        (!_restrict || UseDefaultTypes || type.Assembly != typeof(RegistryTestPlugin).Assembly || Array.IndexOf(Types, type) >= 0) &&
        (Filter?.Invoke(type) ?? base.ShouldRegister(type));

    // True during Register: the types of the test assembly are then limited to Types, unless UseDefaultTypes is true.
    private bool _restrict;

    public void Register()
    {
        _restrict = true;
        try
        {
            RegisterManagedTypes();
        }
        finally
        {
            _restrict = false;
        }
    }

    public void Unregister() => UnregisterManagedTypes();
}

internal sealed partial class RegistryTestComponent : Component
{
    public RegistryTestComponent(Context context)
        : base(context)
    {
    }
}
