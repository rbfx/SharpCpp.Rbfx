using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Plugins;
using Urho3D.Scene;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

// Tests the attribute descriptors of a ManagedTypeRegistration against the reflection scan of ManagedPlugin.
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
            });
        var mood = ManagedAttributeDescriptor.Create<object>(
            "Mood",
            nameof(DescriptorProbeComponent.Mood),
            typeof(DescriptorMood),
            AttributeMode.AmDefault,
            static o => ((DescriptorProbeComponent)o).Mood,
            static (o, v) => ((DescriptorProbeComponent)o).Mood = (DescriptorMood)v);
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

            // The default values come from a temporary instance, which the getter reads.
            Assert.Equal(2.5f, ReadAttribute(probe, "Fast Speed", static v => v.Float));
            Assert.Equal(2.5f, ReadDefault(probe, "Fast Speed", static v => v.Float));
            Assert.Equal(1, ReadAttribute(probe, "Mood", static v => v.Int));
            Assert.True(reads > 0);

            // The set and get round trip uses the delegates.
            Assert.True(WriteAttribute(probe, "Fast Speed", new Variant(7.5f)));
            Assert.True(WriteAttribute(probe, "Mood", new Variant(2)));
            Assert.Equal(1, writes);
            Assert.Equal(7.5f, probe.Speed);
            Assert.Equal(DescriptorMood.Angry, probe.Mood);
            Assert.Equal(7.5f, ReadAttribute(probe, "Fast Speed", static v => v.Float));
            Assert.Equal(2, ReadAttribute(probe, "Mood", static v => v.Int));

            // The reflection scan would find Speed under its own name and Hidden. The descriptors list neither.
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
    public void Registration_without_descriptors_uses_the_reflection_scan()
    {
        using var context = new Context();
        var registry = new TestRegistry(
            new ManagedTypeRegistration(
                typeof(DescriptorProbeComponent),
                static (ctx, category) => ManagedObjectFactory.RegisterFactoryAndGetTypeInfo<DescriptorProbeComponent>(ctx, category)));
        Assert.Null(registry.Registrations[0].Attributes);
        var plugin = new RegistryTestPlugin(context);
        plugin.RegisterFromRegistry(context, registry, "E2E");
        try
        {
            using var probe = new DescriptorProbeComponent(context);
            Assert.Equal(2.5f, ReadAttribute(probe, "Speed", static v => v.Float));
            Assert.Equal(3, ReadAttribute(probe, "Hidden", static v => v.Int));
            Assert.Equal(1, ReadAttribute(probe, "Mood", static v => v.Int));
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
    public void Reflection_path_scans_members_and_creates_the_temporary_instance_with_Activator()
    {
        using var context = new Context();
        var registry = new TestRegistry(
            new ManagedTypeRegistration(
                typeof(DescriptorProbeComponent),
                static (ctx, category) => ManagedObjectFactory.RegisterFactoryAndGetTypeInfo<DescriptorProbeComponent>(ctx, category)));
        var scans = ManagedAttributeRegistrar.ReflectionScanCount;
        var creations = ManagedAttributeRegistrar.ActivatorCreateCount;
        var plugin = new RegistryTestPlugin(context);
        plugin.RegisterFromRegistry(context, registry, "E2E");
        try
        {
            Assert.Equal(scans + 1, ManagedAttributeRegistrar.ReflectionScanCount);
            Assert.Equal(creations + 1, ManagedAttributeRegistrar.ActivatorCreateCount);
        }
        finally
        {
            plugin.Unregister();
        }
    }

    [Fact]
    public void Factory_makes_the_temporary_instance_when_Activator_cannot()
    {
        using var context = new Context();
        var factoryCalls = 0;
        var descriptors = new[]
        {
            ManagedAttributeDescriptor.Create<float>(
                "Speed",
                nameof(GuardedDefaultsComponent.Speed),
                typeof(float),
                AttributeMode.AmDefault,
                static o => ((GuardedDefaultsComponent)o).Speed,
                static (o, v) => ((GuardedDefaultsComponent)o).Speed = v),
        };
        var withFactory = new TestRegistry(
            new ManagedTypeRegistration(
                typeof(GuardedDefaultsComponent),
                static (ctx, category) => ManagedObjectFactory.RegisterFactoryAndGetTypeInfo<GuardedDefaultsComponent>(ctx, category),
                descriptors,
                ctx =>
                {
                    factoryCalls++;
                    return GuardedDefaultsComponent.CreateAllowed(ctx);
                }));
        var scans = ManagedAttributeRegistrar.ReflectionScanCount;
        var creations = ManagedAttributeRegistrar.ActivatorCreateCount;
        GuardedDefaultsComponent.Blocked = true;
        var plugin = new RegistryTestPlugin(context);
        plugin.RegisterFromRegistry(context, withFactory, "E2E");
        try
        {
            Assert.Equal(1, factoryCalls);
            Assert.Equal(scans, ManagedAttributeRegistrar.ReflectionScanCount);
            Assert.Equal(creations, ManagedAttributeRegistrar.ActivatorCreateCount);
            using var probe = GuardedDefaultsComponent.CreateAllowed(context);
            Assert.Equal(4.5f, ReadDefault(probe, "Speed", static v => v.Float));
        }
        finally
        {
            plugin.Unregister();
            GuardedDefaultsComponent.Blocked = false;
        }

        // Without the factory, Activator runs the blocked constructor. It fails, and the variant default is used.
        var withoutFactory = new TestRegistry(
            new ManagedTypeRegistration(
                typeof(GuardedDefaultsComponent),
                static (ctx, category) => ManagedObjectFactory.RegisterFactoryAndGetTypeInfo<GuardedDefaultsComponent>(ctx, category),
                descriptors));
        GuardedDefaultsComponent.Blocked = true;
        plugin = new RegistryTestPlugin(context);
        plugin.RegisterFromRegistry(context, withoutFactory, "E2E");
        try
        {
            Assert.Equal(creations + 1, ManagedAttributeRegistrar.ActivatorCreateCount);
            using var probe = GuardedDefaultsComponent.CreateAllowed(context);
            Assert.NotEqual(4.5f, ReadDefault(probe, "Speed", static v => v.Float));
        }
        finally
        {
            plugin.Unregister();
            GuardedDefaultsComponent.Blocked = false;
        }
    }

    [Fact]
    public void Generated_registration_of_a_described_type_needs_no_member_scan_and_no_Activator()
    {
        using var context = new Context();
        var generated = ManagedTypeRegistries.Find(typeof(DescriptorProbeComponent).Assembly)!.Registrations
            .Single(static item => item.Type == typeof(DescriptorProbeComponent));
        Assert.NotNull(generated.Attributes);
        Assert.NotNull(generated.CreateDefaults);

        var scans = ManagedAttributeRegistrar.ReflectionScanCount;
        var creations = ManagedAttributeRegistrar.ActivatorCreateCount;
        var plugin = new RegistryTestPlugin(context);
        plugin.RegisterFromRegistry(context, new TestRegistry(generated), "E2E");
        try
        {
            Assert.Equal(scans, ManagedAttributeRegistrar.ReflectionScanCount);
            Assert.Equal(creations, ManagedAttributeRegistrar.ActivatorCreateCount);
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

// A reflection scan finds Speed, Mood and Hidden. The tests give descriptors for some of them only.
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
