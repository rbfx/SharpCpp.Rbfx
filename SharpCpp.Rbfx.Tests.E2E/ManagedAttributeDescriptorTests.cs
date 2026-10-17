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

        // Every member has a default from its initializer, so the registration has no factory of a temporary instance.
        Assert.All(generated.Attributes!, static descriptor => Assert.True(descriptor.HasDefault));
        Assert.Null(generated.CreateDefaults);

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

    [Fact]
    public void Descriptor_default_replaces_the_temporary_instance()
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
        Assert.True(descriptors[0].HasDefault);
        var factoryCalls = 0;
        var registry = new TestRegistry(
            new ManagedTypeRegistration(
                typeof(GuardedDefaultsComponent),
                static (ctx, category) => ManagedObjectFactory.RegisterFactoryAndGetTypeInfo<GuardedDefaultsComponent>(ctx, category),
                descriptors,
                ctx =>
                {
                    factoryCalls++;
                    return GuardedDefaultsComponent.CreateAllowed(ctx);
                }));
        var creations = ManagedAttributeRegistrar.ActivatorCreateCount;
        GuardedDefaultsComponent.Blocked = true;
        var plugin = new RegistryTestPlugin(context);
        plugin.RegisterFromRegistry(context, registry, "E2E");
        try
        {
            Assert.Equal(0, factoryCalls);
            Assert.Equal(1, defaultCalls);
            Assert.Equal(creations, ManagedAttributeRegistrar.ActivatorCreateCount);
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
    public void Generated_defaults_of_a_type_with_a_throwing_constructor_need_no_instance_and_no_factory()
    {
        using var context = new Context();
        var generated = ManagedTypeRegistries.Find(typeof(GuardedDefaultsComponent).Assembly)!.Registrations
            .Single(static item => item.Type == typeof(GuardedDefaultsComponent));
        Assert.Null(generated.CreateDefaults);

        // A factory that throws would show a call: the registrar would log a warning and use the variant default 0.
        var factoryCalls = 0;
        var registration = generated with
        {
            CreateDefaults = ctx =>
            {
                factoryCalls++;
                throw new InvalidOperationException("The factory must not be called.");
            },
        };
        var scans = ManagedAttributeRegistrar.ReflectionScanCount;
        var creations = ManagedAttributeRegistrar.ActivatorCreateCount;
        GuardedDefaultsComponent.Blocked = true;
        var plugin = new RegistryTestPlugin(context);
        plugin.RegisterFromRegistry(context, new TestRegistry(registration), "E2E");
        try
        {
            Assert.Equal(0, factoryCalls);
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
    }

    [Fact]
    public void Member_without_a_declaration_default_reads_the_temporary_instance_and_the_others_use_their_default()
    {
        using var context = new Context();
        var generated = ManagedTypeRegistries.Find(typeof(ConstructorDefaultsComponent).Assembly)!.Registrations
            .Single(static item => item.Type == typeof(ConstructorDefaultsComponent));
        Assert.NotNull(generated.CreateDefaults);
        Assert.True(generated.Attributes!.Single(static d => d.MemberName == "Speed").HasDefault);
        Assert.False(generated.Attributes!.Single(static d => d.MemberName == "Scale").HasDefault);

        var factoryCalls = 0;
        var registration = generated with
        {
            CreateDefaults = ctx =>
            {
                factoryCalls++;
                return generated.CreateDefaults!(ctx);
            },
        };
        var creations = ManagedAttributeRegistrar.ActivatorCreateCount;
        var plugin = new RegistryTestPlugin(context);
        plugin.RegisterFromRegistry(context, new TestRegistry(registration), "E2E");
        try
        {
            Assert.Equal(1, factoryCalls);
            Assert.Equal(creations, ManagedAttributeRegistrar.ActivatorCreateCount);
            using var probe = new ConstructorDefaultsComponent(context);
            Assert.Equal(2.5f, ReadDefault(probe, "Speed", static v => v.Float));
            Assert.Equal(6f, ReadDefault(probe, "Scale", static v => v.Float));
        }
        finally
        {
            plugin.Unregister();
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
    public void Derived_type_uses_the_reflection_scan_when_its_listed_base_type_is_not_registered()
    {
        using var context = new Context();
        var scans = ManagedAttributeRegistrar.ReflectionScanCount;
        var plugin = new RegistryTestPlugin(context) { Filter = static type => type != typeof(DescriptorBaseComponent) };
        plugin.RegisterFromRegistry(context, BaseDerivedRegistry(), "E2E");
        try
        {
            // The generated list has no base members, because the generator expects the base type to be registered.
            Assert.Equal(scans + 1, ManagedAttributeRegistrar.ReflectionScanCount);
            Assert.False(context.IsReflected(new StringHash(nameof(DescriptorBaseComponent))));
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
    public void Force_registry_of_a_fully_described_assembly_needs_no_member_scan_and_no_Activator()
    {
        // The generator describes every type of the test assembly. A type has a factory of the temporary instance only when a member has no default.
        var registrations = ManagedTypeRegistries.Find(typeof(DescriptorProbeComponent).Assembly)!.Registrations;
        Assert.NotEmpty(registrations);
        Assert.All(registrations, static item => Assert.NotNull(item.Attributes));
        Assert.All(
            registrations,
            static item => Assert.True(
                item.Attributes!.Any(static descriptor => !descriptor.HasDefault) == (item.CreateDefaults is not null), item.Type.FullName));

        ManagedPlugin.ForceRegistry = true;
        using var context = new Context();
        var scans = ManagedAttributeRegistrar.ReflectionScanCount;
        var creations = ManagedAttributeRegistrar.ActivatorCreateCount;
        var plugin = new RegistryTestPlugin(context);
        try
        {
            plugin.Register();
            Assert.NotEmpty(plugin.RegisteredTypes);
            Assert.Equal(scans, ManagedAttributeRegistrar.ReflectionScanCount);
            Assert.Equal(creations, ManagedAttributeRegistrar.ActivatorCreateCount);
        }
        finally
        {
            plugin.Unregister();
            ManagedPlugin.ForceRegistry = false;
        }
    }

    private static TestRegistry BaseDerivedRegistry() => new(
        new ManagedTypeRegistration(
            typeof(DescriptorBaseComponent),
            static (ctx, category) => ManagedObjectFactory.RegisterFactoryAndGetTypeInfo<DescriptorBaseComponent>(ctx, category),
            [ManagedAttributeDescriptor.Create<int>("BaseValue", nameof(DescriptorBaseComponent.BaseValue), typeof(int), AttributeMode.AmDefault,
                static o => ((DescriptorBaseComponent)o).BaseValue, static (o, v) => ((DescriptorBaseComponent)o).BaseValue = v)],
            static ctx => new DescriptorBaseComponent(ctx)),
        new ManagedTypeRegistration(
            typeof(DescriptorDerivedComponent),
            static (ctx, category) => ManagedObjectFactory.RegisterFactoryAndGetTypeInfo<DescriptorDerivedComponent>(ctx, category),
            [ManagedAttributeDescriptor.Create<int>("OwnValue", nameof(DescriptorDerivedComponent.OwnValue), typeof(int), AttributeMode.AmDefault,
                static o => ((DescriptorDerivedComponent)o).OwnValue, static (o, v) => ((DescriptorDerivedComponent)o).OwnValue = v)],
            static ctx => new DescriptorDerivedComponent(ctx)));

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

// A constructor assigns Scale, so the generator reads no default for it. Speed has a default from its initializer.
internal sealed partial class ConstructorDefaultsComponent : Component
{
    [SerializeField]
    public float Speed = 2.5f;

    [SerializeField]
    public float Scale;

    public ConstructorDefaultsComponent(Context context)
        : base(context)
    {
        Scale = 6;
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
