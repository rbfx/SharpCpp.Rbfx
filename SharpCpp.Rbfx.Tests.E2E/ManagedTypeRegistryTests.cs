using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.Math;
using Urho3D.Plugins;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

// Tests the generated ManagedTypeRegistryAttribute against the assembly scan of ManagedPlugin.
public sealed class ManagedTypeRegistryTests
{
    private const string PluginName = "E2E.AttributePlugin";

    private static string PluginPath =>
        Path.Combine(AppContext.BaseDirectory, "plugins", "AttributePlugin", "SharpCpp.Rbfx.Tests.E2E.AttributePlugin.dll");

    [Fact]
    public void Generated_registry_registers_the_same_types_as_the_assembly_scan()
    {
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
    public void Add_needs_an_assembly_with_a_generated_registry()
    {
        Assert.Null(ManagedTypeRegistries.Find(typeof(object).Assembly));
        Assert.Throws<ArgumentException>(() => ManagedTypeRegistries.Add(typeof(object).Assembly));
    }

    private static bool ShouldRegister(Type type) =>
        ManagedObjectFactory.IsFactoryType(type) && !typeof(Plugin).IsAssignableFrom(type);
}
