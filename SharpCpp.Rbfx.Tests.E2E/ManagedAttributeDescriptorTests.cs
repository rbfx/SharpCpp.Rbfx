using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Plugins;
using Urho3D.Scene;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

// Tests the attribute descriptors of a ManagedTypeRegistration, the only source of the attributes of ManagedPlugin.
public sealed class ManagedAttributeDescriptorTests
{
    [Fact]
    public void Descriptors_register_attributes_and_the_member_scan_is_skipped()
    {
        using var context = new Context();
        var reads = 0;
        var writes = 0;
        var speed = ManagedAttributeDescriptor.Create<float>(
            "Fast Speed",
            nameof(DescriptorProbeComponent.Speed),
            typeof(float),
            AttributeMode.AmDefault,
            o =>
            {
                reads++;
                return ((DescriptorProbeComponent)o).Speed;
            },
            (o, v) =>
            {
                writes++;
                ((DescriptorProbeComponent)o).Speed = v;
            },
            static () => 2.5f);
        var mood = ManagedAttributeDescriptor.Create<object>(
            "Mood",
            nameof(DescriptorProbeComponent.Mood),
            typeof(DescriptorMood),
            AttributeMode.AmDefault,
            static o => ((DescriptorProbeComponent)o).Mood,
            static (o, v) => ((DescriptorProbeComponent)o).Mood = (DescriptorMood)v,
            static () => (object)DescriptorMood.Happy);
        Assert.Equal("Fast Speed", speed.Name);
        Assert.Equal(nameof(DescriptorProbeComponent.Speed), speed.MemberName);
        Assert.Equal(typeof(float), speed.ValueType);

        var registry = new TestRegistry(
            new ManagedTypeRegistration(
                typeof(DescriptorProbeComponent),
                static (ctx, category) => ManagedObjectFactory.RegisterFactoryAndGetTypeInfo<DescriptorProbeComponent>(ctx, category),
                [speed, mood]));
        var plugin = new RegistryTestPlugin(context);
        plugin.RegisterFromRegistry(context, registry, "E2E");
        try
        {
            using var probe = new DescriptorProbeComponent(context);

            // The default values come from the descriptors. The getter reads the attribute.
            Assert.Equal(2.5f, ReadAttribute(probe, "Fast Speed", static v => v.Float));
            Assert.Equal(2.5f, ReadDefault(probe, "Fast Speed", static v => v.Float));
            Assert.Equal(1, ReadAttribute(probe, "Mood", static v => v.Int));
            Assert.Equal(1, ReadDefault(probe, "Mood", static v => v.Int));
            Assert.True(reads > 0);

            // The set and get round trip uses the delegates.
            Assert.True(WriteAttribute(probe, "Fast Speed", new Variant(7.5f)));
            Assert.True(WriteAttribute(probe, "Mood", new Variant(2)));
            Assert.Equal(1, writes);
            Assert.Equal(7.5f, probe.Speed);
            Assert.Equal(DescriptorMood.Angry, probe.Mood);
            Assert.Equal(7.5f, ReadAttribute(probe, "Fast Speed", static v => v.Float));
            Assert.Equal(2, ReadAttribute(probe, "Mood", static v => v.Int));

            // The descriptors list neither Speed under its own name nor Hidden.
            Assert.Equal(VariantType.VarNone, ReadAttribute(probe, "Speed", static v => v.Type));
            Assert.Equal(VariantType.VarNone, ReadAttribute(probe, "Hidden", static v => v.Type));
            Assert.False(WriteAttribute(probe, "Hidden", new Variant(5)));
            Assert.Equal(3, probe.Hidden);
        }
        finally
        {
            plugin.Unregister();
        }
    }

    [Fact]
    public void Empty_descriptor_array_registers_no_attributes()
    {
        using var context = new Context();
        var registry = new TestRegistry(
            new ManagedTypeRegistration(
                typeof(DescriptorProbeComponent),
                static (ctx, category) => ManagedObjectFactory.RegisterFactoryAndGetTypeInfo<DescriptorProbeComponent>(ctx, category),
                []));
        var plugin = new RegistryTestPlugin(context);
        plugin.RegisterFromRegistry(context, registry, "E2E");
        try
        {
            using var probe = new DescriptorProbeComponent(context);
            Assert.Equal(VariantType.VarNone, ReadAttribute(probe, "Speed", static v => v.Type));
            Assert.Equal(VariantType.VarNone, ReadAttribute(probe, "Hidden", static v => v.Type));
        }
        finally
        {
            plugin.Unregister();
        }
    }

    [Fact]
    public void Generated_registration_of_a_described_type_needs_no_member_scan_and_no_Activator()
    {
        using var context = new Context();
        var generated = ManagedTypeRegistries.Find(typeof(DescriptorProbeComponent).Assembly)!.Registrations
            .Single(static item => item.Type == typeof(DescriptorProbeComponent));
        Assert.NotNull(generated.Attributes);

        var plugin = new RegistryTestPlugin(context);
        plugin.RegisterFromRegistry(context, new TestRegistry(generated), "E2E");
        try
        {
            using var probe = new DescriptorProbeComponent(context);
            Assert.Equal(2.5f, ReadDefault(probe, "Speed", static v => v.Float));
            Assert.Equal(3, ReadDefault(probe, "Hidden", static v => v.Int));
            Assert.Equal(1, ReadDefault(probe, "Mood", static v => v.Int));
        }
        finally
        {
            plugin.Unregister();
        }
    }

    [Fact]
    public void Descriptor_default_is_used_and_no_temporary_instance_is_made()
    {
        using var context = new Context();
        var defaultCalls = 0;
        var descriptors = new[]
        {
            ManagedAttributeDescriptor.Create<float>(
                "Speed",
                nameof(GuardedDefaultsComponent.Speed),
                typeof(float),
                AttributeMode.AmDefault,
                static o => ((GuardedDefaultsComponent)o).Speed,
                static (o, v) => ((GuardedDefaultsComponent)o).Speed = v,
                () =>
                {
                    defaultCalls++;
                    return 8.5f;
                }),
        };
        var registry = new TestRegistry(
            new ManagedTypeRegistration(
                typeof(GuardedDefaultsComponent),
                static (ctx, category) => ManagedObjectFactory.RegisterFactoryAndGetTypeInfo<GuardedDefaultsComponent>(ctx, category),
                descriptors));
        GuardedDefaultsComponent.Blocked = true;
        var plugin = new RegistryTestPlugin(context);
        plugin.RegisterFromRegistry(context, registry, "E2E");
        try
        {
            Assert.Equal(1, defaultCalls);
            using var probe = GuardedDefaultsComponent.CreateAllowed(context);
            Assert.Equal(8.5f, ReadDefault(probe, "Speed", static v => v.Float));
            Assert.Equal(4.5f, ReadAttribute(probe, "Speed", static v => v.Float));
        }
        finally
        {
            plugin.Unregister();
            GuardedDefaultsComponent.Blocked = false;
        }
    }

    [Fact]
    public void Generated_defaults_of_a_type_with_a_throwing_constructor_need_no_instance()
    {
        using var context = new Context();
        var generated = ManagedTypeRegistries.Find(typeof(GuardedDefaultsComponent).Assembly)!.Registrations
            .Single(static item => item.Type == typeof(GuardedDefaultsComponent));
        GuardedDefaultsComponent.Blocked = true;
        var plugin = new RegistryTestPlugin(context);
        plugin.RegisterFromRegistry(context, new TestRegistry(generated), "E2E");
        try
        {
            using var probe = GuardedDefaultsComponent.CreateAllowed(context);
            Assert.Equal(4.5f, ReadDefault(probe, "Speed", static v => v.Float));
        }
        finally
        {
            plugin.Unregister();
            GuardedDefaultsComponent.Blocked = false;
        }
    }

    [Fact]
    public void Descriptors_of_a_derived_type_are_complete_when_the_base_type_is_registered()
    {
        using var context = new Context();
        var plugin = new RegistryTestPlugin(context);
        plugin.RegisterFromRegistry(context, BaseDerivedRegistry(), "E2E");
        try
        {
            using var derived = new DescriptorDerivedComponent(context);
            Assert.Equal(11, ReadAttribute(derived, "BaseValue", static v => v.Int));
            Assert.Equal(22, ReadAttribute(derived, "OwnValue", static v => v.Int));
        }
        finally
        {
            plugin.Unregister();
        }
    }

    [Fact]
    public void Derived_type_is_an_error_when_its_listed_base_type_is_not_registered()
    {
        using var context = new Context();
        var errors = new List<string>();
        ManagedAttributeAccessor.MessageObserver = errors.Add;
        var plugin = new RegistryTestPlugin(context) { Filter = static type => type != typeof(DescriptorBaseComponent) };
        plugin.RegisterFromRegistry(context, BaseDerivedRegistry(), "E2E");
        try
        {
            // The generated list has no base members, so the derived type is incomplete without its base type.
            Assert.False(context.IsReflected(new StringHash(nameof(DescriptorBaseComponent))));
            Assert.False(context.IsReflected(new StringHash(nameof(DescriptorDerivedComponent))));
            Assert.Contains(errors, static message => message.Contains(nameof(DescriptorDerivedComponent), StringComparison.Ordinal) &&
                                                      message.Contains(nameof(DescriptorBaseComponent), StringComparison.Ordinal));
        }
        finally
        {
            ManagedAttributeAccessor.MessageObserver = null;
            plugin.Unregister();
        }
    }

    [Fact]
    public void Registry_of_the_test_assembly_describes_every_type()
    {
        // The generator describes every type of the test assembly.
        var registrations = ManagedTypeRegistries.Find(typeof(DescriptorProbeComponent).Assembly)!.Registrations;
        Assert.NotEmpty(registrations);

        using var context = new Context();
        var plugin = new RegistryTestPlugin(context) { UseDefaultTypes = true };
        try
        {
            plugin.Register();
            Assert.NotEmpty(plugin.RegisteredTypes);
        }
        finally
        {
            plugin.Unregister();
        }
    }

    private static TestRegistry BaseDerivedRegistry() => new(
        new ManagedTypeRegistration(
            typeof(DescriptorBaseComponent),
            static (ctx, category) => ManagedObjectFactory.RegisterFactoryAndGetTypeInfo<DescriptorBaseComponent>(ctx, category),
            [ManagedAttributeDescriptor.Create<int>("BaseValue", nameof(DescriptorBaseComponent.BaseValue), typeof(int), AttributeMode.AmDefault,
                static o => ((DescriptorBaseComponent)o).BaseValue, static (o, v) => ((DescriptorBaseComponent)o).BaseValue = v, static () => 11)]),
        new ManagedTypeRegistration(
            typeof(DescriptorDerivedComponent),
            static (ctx, category) => ManagedObjectFactory.RegisterFactoryAndGetTypeInfo<DescriptorDerivedComponent>(ctx, category),
            [ManagedAttributeDescriptor.Create<int>("OwnValue", nameof(DescriptorDerivedComponent.OwnValue), typeof(int), AttributeMode.AmDefault,
                static o => ((DescriptorDerivedComponent)o).OwnValue, static (o, v) => ((DescriptorDerivedComponent)o).OwnValue = v, static () => 22)]));

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

    private sealed class TestRegistry : ManagedTypeRegistryAttribute
    {
        private readonly ManagedTypeRegistration[] _registrations;

        public TestRegistry(params ManagedTypeRegistration[] registrations) => _registrations = registrations;

        public override IReadOnlyList<ManagedTypeRegistration> Registrations => _registrations;
    }
}

internal enum DescriptorMood
{
    Calm,
    Happy,
    Angry,
}

// The probe type has the members Speed, Mood and Hidden. The tests give descriptors for some of them only.
internal sealed partial class DescriptorProbeComponent : Component
{
    [SerializeField]
    public float Speed = 2.5f;

    [SerializeField]
    public DescriptorMood Mood = DescriptorMood.Happy;

    public DescriptorProbeComponent(Context context)
        : base(context)
    {
    }

    [SerializeField]
    public int Hidden { get; set; } = 3;
}

// Its constructor fails while Blocked is set, so Activator cannot make a temporary instance. A registration factory can.
internal sealed partial class GuardedDefaultsComponent : Component
{
    public static bool Blocked;

    [SerializeField]
    public float Speed = 4.5f;

    public GuardedDefaultsComponent(Context context)
        : base(context)
    {
        if (Blocked)
        {
            throw new InvalidOperationException("The constructor is blocked.");
        }
    }

    public static GuardedDefaultsComponent CreateAllowed(Context context)
    {
        var blocked = Blocked;
        Blocked = false;
        try
        {
            return new GuardedDefaultsComponent(context);
        }
        finally
        {
            Blocked = blocked;
        }
    }
}

internal partial class DescriptorBaseComponent : Component
{
    [SerializeField]
    public int BaseValue = 11;

    public DescriptorBaseComponent(Context context)
        : base(context)
    {
    }
}

internal sealed partial class DescriptorDerivedComponent : DescriptorBaseComponent
{
    [SerializeField]
    public int OwnValue = 22;

    public DescriptorDerivedComponent(Context context)
        : base(context)
    {
    }
}
