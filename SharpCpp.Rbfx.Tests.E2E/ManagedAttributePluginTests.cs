using System.Reflection;

using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.Math;
using Urho3D.Plugins;
using Urho3D.Resource;
using Urho3D.Scene;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed class ManagedAttributePluginTests
{
    private const string PluginName = "E2E.AttributePlugin";

    private static readonly StringHash ComponentType = new("AttributeComponent");
    private static readonly StringHash DerivedType = new("DerivedAttributeComponent");

    private static string PluginPath =>
        Path.Combine(AppContext.BaseDirectory, "plugins", "AttributePlugin", "SharpCpp.Rbfx.Tests.E2E.AttributePlugin.dll");

    private static Type AccessorType =>
        typeof(ManagedPlugin).Assembly.GetType("SharpCpp.Rbfx.Bindings.Interop.ManagedAttributeAccessor", throwOnError: true)!;

    private static int LiveAccessors =>
        (int)AccessorType.GetProperty("LiveAccessorCount", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    [Fact]
    public void Serialize_fields_are_engine_attributes_with_defaults()
    {
        using var host = PluginHost.Load();
        Assert.True(host.Context.IsReflected(ComponentType));
        Assert.True(host.Context.IsReflected(DerivedType));

        var component = host.Node.CreateComponent(ComponentType);
        Assert.False(component.IsInvalid);
        Assert.Equal("AttributeComponent", ((object)component).GetType().Name);

        Assert.Equal(2.5f, ReadAttribute(component, "Speed", static v => v.Float));
        Assert.Equal(7, ReadAttribute(component, "Count", static v => v.Int));
        Assert.Equal("hello", ReadAttribute(component, "Label", ReadString));
        Assert.Equal(new Vector3(1, 2, 3), ReadAttribute(component, "Offset", static v => v.Vector3));
        Assert.Equal(1, ReadAttribute(component, "Mood", static v => v.Int));
        Assert.Equal(42, ReadAttribute(component, "Secret Value", static v => v.Int));
        Assert.True(ReadAttribute(component, "Flag", static v => v.Bool));

        // The engine default values come from the generated descriptors.
        Assert.Equal(2.5f, ReadDefault(component, "Speed", static v => v.Float));
        Assert.Equal("hello", ReadDefault(component, "Label", ReadString));
        Assert.Equal(42, ReadDefault(component, "Secret Value", static v => v.Int));

        // Unmarked and NonSerialized members are not attributes. The private field is not exposed by its own name.
        Assert.Equal(VariantType.VarNone, ReadAttribute(component, "Skipped", static v => v.Type));
        Assert.Equal(VariantType.VarNone, ReadAttribute(component, "NotMarked", static v => v.Type));
        Assert.Equal(VariantType.VarNone, ReadAttribute(component, "_secret", static v => v.Type));

        // The derived type copies the base attributes and adds its own.
        var derived = host.Node.CreateComponent(DerivedType);
        Assert.Equal(2.5f, ReadAttribute(derived, "Speed", static v => v.Float));
        Assert.Equal(42, ReadAttribute(derived, "Secret Value", static v => v.Int));
        Assert.Equal(0.5, ReadAttribute(derived, "Extra", static v => v.Double));
        Assert.Equal(VariantType.VarNone, ReadAttribute(component, "Extra", static v => v.Type));

        host.Node.RemoveAllComponents();
    }

    [Fact]
    public void Set_attribute_writes_managed_members_including_private_field()
    {
        using var host = PluginHost.Load();
        var component = host.Node.CreateComponent(ComponentType);

        Assert.True(WriteAttribute(component, "Speed", new Variant(9.5f)));
        Assert.True(WriteAttribute(component, "Count", new Variant(-3)));
        Assert.True(WriteAttribute(component, "Label", "world"));
        Assert.True(WriteAttribute(component, "Offset", new Variant(new Vector3(4, 5, 6))));
        Assert.True(WriteAttribute(component, "Mood", new Variant(2)));
        Assert.True(WriteAttribute(component, "Secret Value", new Variant(11)));
        Assert.True(WriteAttribute(component, "Flag", new Variant(false)));

        Assert.Equal(9.5f, GetMember<float>(component, "Speed"));
        Assert.Equal(-3, GetMember<int>(component, "Count"));
        Assert.Equal("world", GetMember<string>(component, "Label"));
        Assert.Equal(new Vector3(4, 5, 6), GetMember<Vector3>(component, "Offset"));
        Assert.Equal("Angry", GetMember<object>(component, "Mood").ToString());
        Assert.Equal(11, GetMember<int>(component, "Secret"));
        Assert.False(GetMember<bool>(component, "Flag"));

        // The getter reads the managed member back through the engine.
        Assert.Equal(11, ReadAttribute(component, "Secret Value", static v => v.Int));
        Assert.Equal("world", ReadAttribute(component, "Label", ReadString));

        host.Node.RemoveAllComponents();
    }

    [Fact]
    public void Attributes_persist_through_xml_save_and_load()
    {
        using var host = PluginHost.Load();
        var component = host.Node.CreateComponent(ComponentType);
        WriteAttribute(component, "Speed", new Variant(9.5f));
        WriteAttribute(component, "Label", "saved");
        WriteAttribute(component, "Offset", new Variant(new Vector3(4, 5, 6)));
        WriteAttribute(component, "Mood", new Variant(2));
        WriteAttribute(component, "Secret Value", new Variant(11));

        string text;
        using (var xml = new XMLFile(host.Context))
        {
            using var rootName = new Ea.String("node");
            var root = xml.CreateRoot(rootName);
            Assert.True(host.Node.SaveXML(root));
            using var indentation = new Ea.String("\t");
            using var written = xml.ToString(indentation);
            text = written.ToString();
        }

        // The enum attribute is saved by its name.
        Assert.Contains("Angry", text, StringComparison.Ordinal);
        Assert.Contains("saved", text, StringComparison.Ordinal);

        using var otherScene = new Scene(host.Context);
        using var other = otherScene.CreateChild("copy")!;
        using (var xml = new XMLFile(host.Context))
        {
            using var source = new Ea.String(text);
            Assert.True(xml.FromString(source));
            using var rootName = new Ea.String("node");
            Assert.True(other.LoadXML(xml.GetRoot(rootName)));
        }

        var loaded = other.GetComponent(ComponentType);
        Assert.False(loaded.IsInvalid);
        Assert.NotSame(component, loaded);
        Assert.Equal(9.5f, GetMember<float>(loaded, "Speed"));
        Assert.Equal("saved", GetMember<string>(loaded, "Label"));
        Assert.Equal(new Vector3(4, 5, 6), GetMember<Vector3>(loaded, "Offset"));
        Assert.Equal("Angry", GetMember<object>(loaded, "Mood").ToString());
        Assert.Equal(11, GetMember<int>(loaded, "Secret"));
        // A value that equals the default is not saved and keeps the default.
        Assert.Equal(7, GetMember<int>(loaded, "Count"));

        other.RemoveAllComponents();
        host.Node.RemoveAllComponents();
    }

    [Fact]
    public void Unload_removes_reflections_and_releases_accessors()
    {
        var before = LiveAccessors;
        var host = PluginHost.Load();
        try
        {
            // AttributeComponent has 7 attributes. The derived type shares them and adds one.
            Assert.Equal(before + 8, LiveAccessors);
            Assert.Equal(new[] { "Load(2)" }, host.Events.TakeLast(1));

            var component = host.Node.CreateComponent(ComponentType);
            Assert.False(component.IsInvalid);
            host.Node.RemoveAllComponents();

            host.Unload();
            Assert.Equal("Unload", host.Events[^1]);
            Assert.False(host.Context.IsReflected(ComponentType));
            Assert.False(host.Context.IsReflected(DerivedType));
            var created = host.Node.CreateComponent(ComponentType);
            Assert.True(created is null || created.IsInvalid);
            Assert.Equal(before, LiveAccessors);
        }
        finally
        {
            host.Dispose();
        }
    }

    [Fact]
    public void Plugin_loads_again_after_unload()
    {
        var before = LiveAccessors;
        var host = PluginHost.Load();
        try
        {
            Assert.Equal(before + 8, LiveAccessors);

            host.Unload();
            Assert.False(host.Context.IsReflected(ComponentType));
            Assert.Equal(before, LiveAccessors);

            host.LoadAgain();
            Assert.True(host.Context.IsReflected(ComponentType));
            Assert.True(host.Context.IsReflected(DerivedType));
            Assert.Equal(before + 8, LiveAccessors);

            var component = host.Node.CreateComponent(ComponentType);
            Assert.False(component.IsInvalid);
            Assert.Equal(2.5f, ReadAttribute(component, "Speed", static v => v.Float));
            host.Node.RemoveAllComponents();
        }
        finally
        {
            host.Dispose();
        }
    }

    [Fact]
    public void Accessor_wrapper_and_attribute_info_balance_references()
    {
        var before = LiveAccessors;
        var field = typeof(AccessorProbe).GetField("Value")!;
        var bindingsAssembly = typeof(ManagedPlugin).Assembly;
        var tryGet = bindingsAssembly.GetType("SharpCpp.Rbfx.Bindings.Interop.VariantConverters", throwOnError: true)!
            .GetMethod("TryGet", BindingFlags.Public | BindingFlags.Static)!;
        var arguments = new object?[] { typeof(int), null, null, null };
        Assert.True((bool)tryGet.Invoke(null, arguments)!);
        var binding = bindingsAssembly.GetType("SharpCpp.Rbfx.Bindings.Interop.ManagedAttributeBinding`2", throwOnError: true)!
            .MakeGenericType(typeof(AccessorProbe), typeof(int))
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [field, arguments[1]])!;

        using var context = new Context();
        using var probe = new AccessorProbe(context);
        var accessor = (AttributeAccessor)AccessorType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [binding])!;
        Assert.Equal(before + 1, LiveAccessors);
        Assert.Equal(1, accessor.Refs());

        var probeType = context.RegisterFactory(typeof(AccessorProbe), "E2E");
        var reflection = context.GetReflection(probeType.GetType());
        using (var defaultValue = new Variant(0))
        {
            var addTo = AccessorType.GetMethod("AddTo", BindingFlags.Public | BindingFlags.Instance)!;
            Assert.True((bool)addTo.Invoke(accessor, [reflection, VariantType.VarInt, "Value", null, defaultValue, AttributeMode.AmDefault])!);
        }

        // The reflection holds the second reference.
        Assert.Equal(2, accessor.Refs());

        // The wrapper calls the native accessor, which calls the managed binding.
        using (var value = new Variant(5))
        {
            accessor.Set(probe, value);
        }

        Assert.Equal(5, probe.Value);
        using (var read = new Variant())
        {
            accessor.Get(probe, read);
            Assert.Equal(5, read.Int);
        }

        // The engine finds the attribute by name.
        Assert.True(WriteAttribute(probe, "Value", new Variant(8)));
        Assert.Equal(8, probe.Value);
        Assert.Equal(8, ReadAttribute(probe, "Value", static v => v.Int));

        context.RemoveReflection(probeType.GetType());
        Assert.Equal(1, accessor.Refs());
        accessor.Dispose();
        Assert.Equal(before, LiveAccessors);
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

    private static bool WriteAttribute(Serializable serializable, string name, Variant value)
    {
        using (value)
        {
            using var text = new Ea.String(name);
            return serializable.SetAttribute(text, value);
        }
    }

    private static string ReadString(Variant value)
    {
        using var text = value.String;
        return text.ToString();
    }

    private static T GetMember<T>(object instance, string name)
    {
        var type = instance.GetType();
        var value = type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance)
            ?? type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(instance);
        return (T)value!;
    }

    // Loads the attribute plugin through a plugin manager. Dispose unloads the plugin and releases the manager.
    private sealed class PluginHost : IDisposable
    {
        private readonly Engine _engine;
        private readonly PluginManager _manager;
        private readonly Scene _scene;
        private bool _loaded;

        private PluginHost()
        {
            Context = new Context();
            _engine = new Engine(Context);
            _manager = new PluginManager(Context);
            Context.RegisterSubsystem(_manager);
            _scene = new Scene(Context);
            Node = _scene.CreateChild("holder")!;
        }

        public Context Context { get; }

        public Node Node { get; }

        public List<string> Events { get; private set; } = new();

        public static PluginHost Load()
        {
            var host = new PluginHost();
            var plugin = host._manager.RegisterManagedPlugin(PluginPath);
            host.Events = (List<string>)((object)plugin).GetType().Assembly
                .GetType("SharpCpp.Rbfx.Tests.E2E.AttributePlugin.AttributePluginState", throwOnError: true)!
                .GetProperty("Events", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
            host._manager.SetPluginsLoaded(new Ea.Vector<Ea.String>(new Ea.String[] { PluginName }));
            host._manager.Commit();
            Assert.True(plugin.IsLoaded);
            host._loaded = true;
            return host;
        }

        public void LoadAgain()
        {
            _manager.SetPluginsLoaded(new Ea.Vector<Ea.String>(new Ea.String[] { PluginName }));
            _manager.Commit();
            _loaded = true;
        }

        public void Unload()
        {
            if (!_loaded)
            {
                return;
            }

            _manager.SetPluginsLoaded(new Ea.Vector<Ea.String>());
            _manager.Commit();
            _loaded = false;
        }

        public void Dispose()
        {
            Node.RemoveAllComponents();
            Unload();
            Node.Dispose();
            _scene.Dispose();
            Context.RemoveSubsystem(PluginManager.GetTypeStatic());
            _manager.Dispose();
            _engine.Dispose();
            Context.Dispose();
        }
    }
}

public partial class AccessorProbe : Component
{
    public int Value;

    public AccessorProbe(Context context)
        : base(context)
    {
    }
}
