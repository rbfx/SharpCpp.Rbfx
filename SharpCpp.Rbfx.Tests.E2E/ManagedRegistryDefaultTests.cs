using System.Reflection;

using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.Math;
using Urho3D.Plugins;
using Urho3D.Scene;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

// ManagedPlugin uses the generated registry of its assembly on every runtime, also on the JIT runtime of the tests.
// The registry is the only path. An assembly without a registry fails to register.
public sealed class ManagedRegistryDefaultTests
{
    private static string PluginPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "plugins", name, $"SharpCpp.Rbfx.Tests.E2E.{name}.dll");

    [Fact]
    public void JIT_plugin_with_a_registry_registers_types_and_attributes()
    {
        var messages = new List<string>();
        ManagedAttributeAccessor.MessageObserver = messages.Add;
        using var host = new PluginHost("RegistryPlugin");
        try
        {
            var plugin = host.Load();
            Assert.NotNull(ManagedTypeRegistries.Find(((object)plugin).GetType().Assembly));
            Assert.Equal(2, plugin.RegisteredTypes.Count);
            Assert.True(host.Context.IsReflected(new StringHash("RegistryFixtureComponent")));
            Assert.True(host.Context.IsReflected(new StringHash("RegistryFixtureDerivedComponent")));

            // The attributes are registered and have the defaults of the declarations.
            var derived = host.Create("RegistryFixtureDerivedComponent");
            Assert.Equal(3.5f, ReadAttribute(derived, "Speed", static v => v.Float));
            Assert.Equal(11, ReadAttribute(derived, "Count", static v => v.Int));
            Assert.Equal("registry", ReadAttribute(derived, "Label", ReadString));
            Assert.Equal(0.25, ReadAttribute(derived, "Extra", static v => v.Double));
            Assert.Equal(11, ReadDefault(derived, "Count", static v => v.Int));
            Assert.Empty(messages);
        }
        finally
        {
            ManagedAttributeAccessor.MessageObserver = null;
        }
    }

    [Fact]
    public void Plugin_without_a_registry_fails_to_register_with_a_clear_message()
    {
        var context = new Context();
        var engine = new Engine(context);
        var manager = new PluginManager(context);
        context.RegisterSubsystem(manager);
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => manager.RegisterManagedPlugin(PluginPath("NoRegistryPlugin")));
            Assert.Contains("no managed type registry", exception.Message, StringComparison.Ordinal);
            Assert.Contains("SharpCpp.Rbfx.Tests.E2E.NoRegistryPlugin", exception.Message, StringComparison.Ordinal);
            Assert.Contains("SharpCpp.Rbfx.SourceGenerator", exception.Message, StringComparison.Ordinal);
            Assert.False(context.IsReflected(new StringHash("NoRegistryComponent")));
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
    public void ShouldRegister_filter_limits_the_registered_types_of_the_registry()
    {
        var messages = new List<string>();
        ManagedAttributeAccessor.MessageObserver = messages.Add;
        using var context = new Context();
        var plugin = new RegistryTestPlugin(context) { Types = new[] { typeof(DescriptorProbeComponent) } };
        try
        {
            // The registry of the test assembly lists many more types. The filter accepts one.
            Assert.True(ManagedTypeRegistries.Find(typeof(RegistryTestPlugin).Assembly)!.Registrations.Count > 1);
            plugin.Register();
            Assert.Equal(new[] { new StringHash(nameof(DescriptorProbeComponent)) }, plugin.RegisteredTypes);
            Assert.True(context.IsReflected(new StringHash(nameof(DescriptorProbeComponent))));
            Assert.False(context.IsReflected(new StringHash(nameof(RegistryTestComponent))));
            Assert.Empty(messages);
        }
        finally
        {
            plugin.Unregister();
            ManagedAttributeAccessor.MessageObserver = null;
        }
    }

    private static T ReadAttribute<T>(Serializable serializable, string name, Func<Variant, T> read)
    {
        using var text = new Ea.String(name);
        using var value = serializable.GetAttribute(text);
        return read(value);
    }

    private static T ReadDefault<T>(Serializable serializable, string name, Func<Variant, T> read)
    {
        using var text = new Ea.String(name);
        using var value = serializable.GetAttributeDefault(text);
        return read(value);
    }

    private static string ReadString(Variant value)
    {
        using var text = value.String;
        return text.ToString();
    }

    // Loads a fixture plugin through a plugin manager (the hostfxr path). Dispose unloads it.
    private sealed class PluginHost : IDisposable
    {
        private readonly string _pluginName;
        private readonly Engine _engine;
        private readonly PluginManager _manager;
        private readonly Scene _scene;
        private readonly Node _node;
        private bool _loaded;

        public PluginHost(string fixture)
        {
            _pluginName = "E2E." + fixture;
            Fixture = fixture;
            Context = new Context();
            _engine = new Engine(Context);
            _manager = new PluginManager(Context);
            Context.RegisterSubsystem(_manager);
            _scene = new Scene(Context);
            _node = _scene.CreateChild("holder")!;
        }

        public string Fixture { get; }

        public Context Context { get; }

        public ManagedPlugin Load()
        {
            var plugin = (ManagedPlugin)_manager.RegisterManagedPlugin(PluginPath(Fixture));
            _manager.SetPluginsLoaded(new Ea.Vector<Ea.String>(new Ea.String[] { _pluginName }));
            _manager.Commit();
            Assert.True(plugin.IsLoaded);
            _loaded = true;
            return plugin;
        }

        public Serializable Create(string typeName) => _node.CreateComponent(new StringHash(typeName));

        public void Dispose()
        {
            _node.RemoveAllComponents();
            if (_loaded)
            {
                _manager.SetPluginsLoaded(new Ea.Vector<Ea.String>());
                _manager.Commit();
            }

            _node.Dispose();
            _scene.Dispose();
            Context.RemoveSubsystem(PluginManager.GetTypeStatic());
            _manager.Dispose();
            _engine.Dispose();
            Context.Dispose();
        }
    }
}
