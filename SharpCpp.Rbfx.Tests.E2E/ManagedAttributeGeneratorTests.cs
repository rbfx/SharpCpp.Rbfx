using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using SharpCpp.Rbfx.SourceGenerator;

using Urho3D.Plugins;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

// Runs the source generator on a small compilation and checks the attribute descriptors of the generated registry.
public sealed class ManagedAttributeGeneratorTests
{
    private const string Header = """
        using Urho3D.Core;
        using Urho3D.Math;
        using Urho3D.Scene;

        namespace GeneratorTest;

        public enum Mood { Calm, Happy }

        """;

    [Fact]
    public void Assembly_with_a_ManagedPlugin_and_no_managed_types_gets_an_empty_registry()
    {
        var result = Run("""
            using Urho3D.Core;
            using Urho3D.Plugins;

            namespace GeneratorTest;

            public partial class EmptyPlugin : ManagedPlugin
            {
                public EmptyPlugin(Context context) : base(context) { }
            }
            """);

        AssertNoErrors(result);
        Assert.DoesNotContain("new(typeof(", result.Registry, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_members_become_descriptors_in_base_first_order()
    {
        var result = Run(Header + """
            public partial class Base : Component
            {
                [SerializeField] public float Speed = 1;
                public Base(Context context) : base(context) { }
            }

            public partial class Derived : Base
            {
                [SerializeField(Name = "Label Text", Mode = AttributeMode.AmFile | AttributeMode.AmNoedit)] public string Label { get; set; } = "";
                [SerializeField] public Mood Mood;
                [SerializeField, System.NonSerialized] public int Skipped;
                public int NotMarked;
                public Derived(Context context) : base(context) { }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "RBFXSG004");
        AssertNoErrors(result);

        // The base type is registered first, so the derived type copies its attributes and lists only its own members.
        var baseText = result.Registration("GeneratorTest.Base");
        Assert.Contains(
            "global::Urho3D.Plugins.ManagedAttributeDescriptor.Create<float>(\"Speed\", \"Speed\", typeof(float), " +
            "global::Urho3D.Core.AttributeMode.AmDefault, static o => ((global::GeneratorTest.Base)o).Speed, " +
            "static (o, v) => ((global::GeneratorTest.Base)o).Speed = v, static () => 1)",
            baseText);

        var derivedText = result.Registration("GeneratorTest.Derived");
        Assert.DoesNotContain("Speed", derivedText);
        Assert.Contains("Create<string>(\"Label Text\", \"Label\", typeof(string), unchecked((global::Urho3D.Core.AttributeMode)", derivedText);
        Assert.Contains("static o => ((global::GeneratorTest.Derived)o).Label!, static (o, v) => ((global::GeneratorTest.Derived)o).Label = v, static () => \"\")", derivedText);
        Assert.Contains(
            "Create<object>(\"Mood\", \"Mood\", typeof(global::GeneratorTest.Mood), global::Urho3D.Core.AttributeMode.AmDefault, " +
            "static o => (object)((global::GeneratorTest.Derived)o).Mood, static (o, v) => ((global::GeneratorTest.Derived)o).Mood = (global::GeneratorTest.Mood)v, " +
            "static () => (object)default(global::GeneratorTest.Mood))",
            derivedText);
        Assert.DoesNotContain("Skipped", derivedText);

        Assert.DoesNotContain("static ctx =>", baseText);
        Assert.DoesNotContain("static ctx =>", derivedText);
        Assert.DoesNotContain("NotMarked", derivedText);

        // The generator lists the fields of a type first, then its properties.
        Assert.True(derivedText.IndexOf("\"Mood\"", StringComparison.Ordinal) < derivedText.IndexOf("\"Label Text\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Defaults_come_from_the_declaration_initializers()
    {
        var result = Run(Header + """
            public partial class Probe : Component
            {
                public const float Base = 1.5f;
                private const float Hidden = 0.25f;
                public static readonly Vector3 Home = new(4, 5, 6);
                public static int Twice(int value) => value * 2;

                [SerializeField] public float Speed = 2.5f;
                [SerializeField] public int Negative = -3;
                [SerializeField] public Mood Mood = Mood.Happy;
                [SerializeField] public Vector3 Offset = new Vector3(1, 2, 3);
                [SerializeField] public Vector3 One = Vector3.One;
                [SerializeField] public string Label = "hi \"there\"";
                [SerializeField] public float FromConst = Base;
                [SerializeField] public float FromPrivateConst = Hidden * 2;
                [SerializeField] public Vector3 FromStatic = Home;
                [SerializeField] public int FromCall = Twice(4);
                [SerializeField] public double Unset;
                [SerializeField] public string Text = null!;
                [SerializeField] public Mood UnsetMood;
                [SerializeField] public Mood FromZero = 0;
                [SerializeField] public int Auto { get; set; } = 5;
                public Probe(Context context) : base(context) { }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "RBFXSG004");
        AssertNoErrors(result);
        var text = result.Registration("GeneratorTest.Probe");
        Assert.EndsWith("static () => 2.5f),", result.Entry(text, "Speed"));
        Assert.EndsWith("static () => -3),", result.Entry(text, "Negative"));
        Assert.EndsWith("static () => (object)global::GeneratorTest.Mood.Happy),", result.Entry(text, "Mood"));
        Assert.EndsWith("static () => new global::Urho3D.Math.Vector3(1, 2, 3)),", result.Entry(text, "Offset"));
        Assert.EndsWith("static () => global::Urho3D.Math.Vector3.One),", result.Entry(text, "One"));
        Assert.EndsWith("static () => \"hi \\\"there\\\"\"),", result.Entry(text, "Label"));
        Assert.EndsWith("static () => 1.5F),", result.Entry(text, "FromConst"));
        Assert.EndsWith("static () => 0.5F),", result.Entry(text, "FromPrivateConst"));
        Assert.EndsWith("static () => global::GeneratorTest.Probe.Home),", result.Entry(text, "FromStatic"));
        Assert.EndsWith("static () => global::GeneratorTest.Probe.Twice(4)),", result.Entry(text, "FromCall"));
        Assert.EndsWith("static () => default!),", result.Entry(text, "Unset"));
        Assert.EndsWith("static () => (null)!),", result.Entry(text, "Text"));
        Assert.EndsWith("static () => (object)default(global::GeneratorTest.Mood)),", result.Entry(text, "UnsetMood"));
        Assert.EndsWith("static () => (object)(global::GeneratorTest.Mood)(0)),", result.Entry(text, "FromZero"));
        Assert.EndsWith("static () => 5),", result.Entry(text, "Auto"));
        Assert.DoesNotContain("static ctx =>", text);
    }

    [Theory]
    [InlineData("[SerializeField] public float Speed = 1;", "Speed = 2;")]
    [InlineData("[SerializeField] public float Speed;", "this.Speed = 2;")]
    [InlineData("[SerializeField] public float Speed = 1;", "Speed += 1;")]
    [InlineData("[SerializeField] public float Speed = 1;", "Speed++;")]
    [InlineData("[SerializeField] public float Speed = 1;", "Init(out Speed);")]
    [InlineData("[SerializeField] public float Speed = 1;", "(Speed, _) = (2, 3);")]
    [InlineData("[SerializeField] public float Speed { get; set; } = 1;", "Speed = 2;")]
    [InlineData("[SerializeField] private float Speed = 1;", "Speed = 2;")]
    public void Member_assigned_by_a_constructor_is_an_error(string member, string statement)
    {
        var result = Run(Header + "public partial class Probe : Component\n{\n    " + member +
            "\n    private int _unused;\n    private static void Init(out float value) { value = 3; }\n" +
            "    public Probe(Context context) : base(context) { " + statement + " }\n}\n");

        var error = AssertMemberError(result, "Speed");
        Assert.Contains("constructor", error.GetMessage());
        Assert.Contains("literal", error.GetMessage());
        Assert.DoesNotContain("Create<", result.Registration("GeneratorTest.Probe"));
    }

    [Theory]
    [InlineData("Offset.Y = 1;")]
    [InlineData("this.Offset.X = 2;")]
    [InlineData("Offset.X += 1;")]
    [InlineData("Offset.X++;")]
    [InlineData("Init(ref Offset.Z);")]
    [InlineData("Offset.Normalize();")]
    [InlineData("this.Offset.Normalize();")]
    [InlineData("(Offset).X = 1;")]
    public void Part_of_a_value_type_member_written_by_a_constructor_is_an_error(string statement)
    {
        var result = Run(Header + "public partial class Probe : Component\n{\n    [SerializeField] public Vector3 Offset;\n" +
            "    private static void Init(ref float value) { value = 3; }\n" +
            "    public Probe(Context context) : base(context) { " + statement + " }\n}\n");

        Assert.Contains("constructor", AssertMemberError(result, "Offset").GetMessage());
    }

    [Fact]
    public void Part_of_a_value_type_member_that_a_constructor_only_reads_keeps_its_default()
    {
        var result = Run(Header + """
            public partial class Probe : Component
            {
                [SerializeField] public Vector3 Offset = new Vector3(1, 2, 3);
                private float _copy;
                public Probe(Context context) : base(context) { _copy = Offset.X + this.Offset.Y; }
            }
            """);

        AssertNoErrors(result);
        var text = result.Registration("GeneratorTest.Probe");
        Assert.EndsWith("static () => new global::Urho3D.Math.Vector3(1, 2, 3)),", result.Entry(text, "Offset"));
        Assert.DoesNotContain("static ctx =>", text);
    }

    [Fact]
    public void Negative_enum_constant_is_rendered_as_a_parenthesized_cast_operand()
    {
        var result = Run(Header + """
            public enum Layers { None = 0, A = 1, B = 2 }

            public partial class Probe : Component
            {
                [SerializeField] public Layers Mask = ~Layers.None;
                [SerializeField] public Layers Cast = (Layers)(-1);
                public Probe(Context context) : base(context) { }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "RBFXSG004");
        AssertNoErrors(result);
        var text = result.Registration("GeneratorTest.Probe");
        Assert.EndsWith("static () => (object)unchecked((global::GeneratorTest.Layers)(-1))),", result.Entry(text, "Mask"));
        Assert.EndsWith("static () => (object)unchecked((global::GeneratorTest.Layers)(-1))),", result.Entry(text, "Cast"));
    }

    [Fact]
    public void Static_member_of_a_generic_base_type_as_a_default_is_an_error()
    {
        var result = Run(Header + """
            public abstract partial class Base<T> : Component
            {
                private static readonly int DefaultX = 3;
                public static readonly int Shared = 4;
                [SerializeField] public int X = DefaultX;
                [SerializeField] public int Y = Shared;
                [SerializeField] public int Z = 5;
                protected Base(Context context) : base(context) { }
            }

            public partial class D : Base<int>
            {
                public D(Context context) : base(context) { }
            }
            """);

        Assert.Equal(2, result.Diagnostics.Count(static d => d.Id == "RBFXSG004" && d.Severity == DiagnosticSeverity.Error));
        AssertMemberError(result, "X");
        AssertMemberError(result, "Y");
        Assert.EndsWith("static () => 5),", result.Entry(result.Registration("GeneratorTest.D"), "Z"));
    }

    [Fact]
    public void Member_that_a_constructor_only_reads_keeps_its_default()
    {
        var result = Run(Header + """
            public partial class Probe : Component
            {
                [SerializeField] public float Speed = 1;
                private float _copy;
                public Probe(Context context) : base(context) { _copy = Speed * 2; }
            }
            """);

        AssertNoErrors(result);
        var text = result.Registration("GeneratorTest.Probe");
        Assert.EndsWith("static () => 1),", result.Entry(text, "Speed"));
        Assert.DoesNotContain("static ctx =>", text);
    }

    [Fact]
    public void Constructor_of_a_derived_type_that_assigns_a_base_member_is_an_error()
    {
        var result = Run(Header + """
            public abstract partial class Abstract : Component
            {
                [SerializeField] public int Shared = 1;
                [SerializeField] public int Kept = 2;
                protected Abstract(Context context) : base(context) { }
            }

            public partial class Concrete : Abstract
            {
                public Concrete(Context context) : base(context) { Shared = 10; }
            }
            """);

        Assert.Contains("constructor", AssertMemberError(result, "Shared").GetMessage());
        Assert.EndsWith("static () => 2),", result.Entry(result.Registration("GeneratorTest.Concrete"), "Kept"));
    }

    [Fact]
    public void Initializer_that_needs_the_instance_or_a_hidden_symbol_is_an_error()
    {
        var result = Run(Header + """
            public partial class Probe(Context context) : Component(context)
            {
                private static readonly Vector3 Secret = new(1, 2, 3);
                protected static int Counter = 4;

                [SerializeField] public int FromParameter = context.GetHashCode();
                [SerializeField] public Vector3 FromPrivate = Secret;
                [SerializeField] public int FromProtected = Counter;
                [SerializeField] public int FromLambda = ((System.Func<int>)(() => 3))();
                [SerializeField] public int WithBody { get => _body; set => _body = value; }
                [SerializeField] public virtual int Virtual { get; set; } = 1;
                [SerializeField] public float Plain = 7;
                private int _body;
            }
            """);

        foreach (var name in new[] { "FromParameter", "FromPrivate", "FromProtected", "FromLambda", "WithBody", "Virtual" })
        {
            AssertMemberError(result, name);
        }

        Assert.Equal(6, result.Diagnostics.Count(static d => d.Id == "RBFXSG004"));
        Assert.EndsWith("static () => 7),", result.Entry(result.Registration("GeneratorTest.Probe"), "Plain"));
    }

    [Fact]
    public void Initializer_text_does_not_depend_on_the_usings_of_the_declaration()
    {
        var result = Run("""
            using Urho3D.Core;
            using Urho3D.Math;
            using Urho3D.Scene;

            namespace Other
            {
                using M = System.Math;

                public enum Level { Low = 1, High = 2 }

                public partial class Probe : Component
                {
                    [SerializeField] public float Rounded = M.Abs(-2.5f);
                    [SerializeField] public Level Level = Level.High;
                    [SerializeField] public Vector2 Pair = new(x: 1, y: 2);
                    public Probe(Context context) : base(context) { }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "RBFXSG004");
        AssertNoErrors(result);
        var text = result.Registration("Other.Probe");
        Assert.EndsWith("static () => global::System.Math.Abs(-2.5F)),", result.Entry(text, "Rounded"));
        Assert.EndsWith("static () => (object)global::Other.Level.High),", result.Entry(text, "Level"));
        Assert.EndsWith("static () => new(x: 1, y: 2)),", result.Entry(text, "Pair"));
    }

    [Fact]
    public void Members_of_an_unregistered_base_type_come_first_in_the_derived_list()
    {
        var result = Run(Header + """
            public abstract partial class Abstract : Component
            {
                [SerializeField] public int Shared;
                protected Abstract(Context context) : base(context) { }
            }

            public partial class Concrete : Abstract
            {
                [SerializeField] public double Own;
                public Concrete(Context context) : base(context) { }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "RBFXSG004");
        AssertNoErrors(result);
        var text = result.Registration("GeneratorTest.Concrete");
        Assert.Contains("Create<int>(\"Shared\", \"Shared\", typeof(int)", text);
        Assert.Contains("static o => ((global::GeneratorTest.Abstract)o).Shared", text);
        Assert.Contains("Create<double>(\"Own\", \"Own\", typeof(double)", text);
        Assert.True(text.IndexOf("\"Shared\"", StringComparison.Ordinal) < text.IndexOf("\"Own\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Type_without_marked_members_gets_an_empty_list()
    {
        var result = Run(Header + """
            public partial class Plain : Component
            {
                public int NotMarked;
                public Plain(Context context) : base(context) { }
            }
            """);

        AssertNoErrors(result);
        var text = result.Registration("GeneratorTest.Plain");
        Assert.Contains("global::System.Array.Empty<global::Urho3D.Plugins.ManagedAttributeDescriptor>()", text);

        // An empty list never reads default values, so there is no factory.
        Assert.DoesNotContain("static ctx =>", text);
    }

    [Fact]
    public void Private_members_use_UnsafeAccessor_and_the_generated_code_compiles()
    {
        var result = Run(Header + """
            public partial class Hidden : Component
            {
                [SerializeField] public float Open = 1;
                [SerializeField(Name = "Secret Value")] private int _secret = 42;
                [SerializeField] protected Mood Feeling = Mood.Happy;
                [SerializeField] private string Title { get; set; } = "x";
                [SerializeField] public int PrivateSetter { get; private set; } = 3;
                [SerializeField] internal float Inside = 2;
                public Hidden(Context context) : base(context) { }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "RBFXSG004");
        AssertNoErrors(result);
        var text = result.Registry;
        Assert.Contains("UnsafeAccessorKind.Field, Name = \"_secret\")]", text);
        Assert.Contains("private static extern ref int __Field0(global::GeneratorTest.Hidden owner);", text);
        Assert.Contains("UnsafeAccessorKind.Field, Name = \"Feeling\")]", text);
        Assert.Contains("UnsafeAccessorKind.Method, Name = \"get_Title\")]", text);
        Assert.Contains("UnsafeAccessorKind.Method, Name = \"set_Title\")]", text);
        Assert.Contains("UnsafeAccessorKind.Method, Name = \"set_PrivateSetter\")]", text);
        var hidden = result.Registration("GeneratorTest.Hidden");
        Assert.Contains("\"Secret Value\", \"_secret\", typeof(int)", hidden);
        Assert.Contains("static o => __Field0(((global::GeneratorTest.Hidden)o))", hidden);
        Assert.Contains("static (o, v) => __Field0(((global::GeneratorTest.Hidden)o)) = v", hidden);

        // Public and internal members are accessed directly.
        Assert.Contains("static o => ((global::GeneratorTest.Hidden)o).Open", hidden);
        Assert.Contains("static o => ((global::GeneratorTest.Hidden)o).Inside", hidden);
        Assert.DoesNotContain("Name = \"Open\"", text);
        Assert.DoesNotContain("Name = \"Inside\"", text);
    }

    [Fact]
    public void Private_members_of_generic_and_nested_types_compile()
    {
        var result = Run(Header + """
            public abstract partial class Base<T> : Component
            {
                [SerializeField] private int _value;
                [SerializeField] protected float Gain = 2;
                protected Base(Context context) : base(context) { }
            }

            public partial class D : Base<int>
            {
                public D(Context context) : base(context) { }
            }

            public partial class Holder
            {
                public class Nested : Component, SharpCpp.Rbfx.Bindings.Interop.IManagedObjectFactory<Nested>
                {
                    [SerializeField] private int Value;
                    public Nested(Context context) : base(context) { }
                    public static Nested Create(Context context) => new Nested(context);
                    public new static TypeInfo GetTypeInfoStatic() => throw new System.NotSupportedException();
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "RBFXSG004");
        AssertNoErrors(result);
        Assert.Contains("private static extern ref int __Field1<T0>(global::GeneratorTest.Base<T0> owner);", result.Registry);
        Assert.Contains("static o => __Field1<int>(((global::GeneratorTest.Base<int>)o))", result.Registration("GeneratorTest.D"));
        Assert.Contains("private static extern ref float __Field2<T0>(global::GeneratorTest.Base<T0> owner);", result.Registry);
        Assert.Contains("private static extern ref int __Field0(global::GeneratorTest.Holder.Nested owner);", result.Registry);
        Assert.Contains("(global::GeneratorTest.Holder.Nested owner);", result.Registry);
    }

    [Fact]
    public void Private_field_with_a_type_parameter_type_is_an_error()
    {
        var result = Run(Header + """
            public abstract partial class Base<T> : Component
            {
                [SerializeField] private T _value;
                protected Base(Context context) : base(context) { }
            }

            public partial class D : Base<int>
            {
                public D(Context context) : base(context) { }
            }
            """);

        var error = AssertMemberError(result, "_value");
        Assert.Contains("uses a type parameter", error.GetMessage());
    }

    [Theory]
    [InlineData("private")]
    [InlineData("protected")]
    public void Private_or_protected_property_of_a_generic_type_is_an_error(string access)
    {
        var result = Run(Header + "public abstract partial class Base<T> : Component\n{\n    [SerializeField] " + access +
            " float Gain { get; set; } = 1;\n    protected Base(Context context) : base(context) { }\n}\n" +
            "public partial class D : Base<int>\n{\n    public D(Context context) : base(context) { }\n}\n");

        var error = AssertMemberError(result, "Gain");
        Assert.Contains("generic type 'Base'", error.GetMessage());
        Assert.Contains("Use a field", error.GetMessage());
    }

    [Fact]
    public void Nested_generic_types_with_the_same_parameter_name_get_positional_accessor_parameters()
    {
        var result = Run(Header + """
            public class Outer<T>
            {
                public abstract class Inner<T> : Component
                {
                    [SerializeField] private int Value;
                    [SerializeField] private float Gain = 1;
                    protected Inner(Context context) : base(context) { }
                }
            }

            public partial class D : Outer<int>.Inner<string>
            {
                public D(Context context) : base(context) { }
            }
            """);

        AssertNoErrors(result);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "CS0692");
        Assert.Contains("private static extern ref int __Field0<T0, T1>(global::GeneratorTest.Outer<T0>.Inner<T1> owner);", result.Registry);
        Assert.Contains("private static extern ref float __Field1<T0, T1>(global::GeneratorTest.Outer<T0>.Inner<T1> owner);", result.Registry);
        Assert.Contains("__Field0<int, string>(", result.Registration("GeneratorTest.D"));
    }

    [Theory]
    [InlineData("[SerializeField] public readonly int Member = 1;", "readonly")]
    [InlineData("[SerializeField] private readonly int Member = 1;", "readonly")]
    [InlineData("[SerializeField] public int Member { get; init; }", "init-only")]
    [InlineData("[SerializeField] public int Member { get; }", "getter and a setter")]
    [InlineData("[SerializeField] private int Member { get; }", "getter and a setter")]
    [InlineData("[SerializeField] public byte Member;", "not a supported attribute type")]
    [InlineData("[SerializeField] private byte Member;", "not a supported attribute type")]
    [InlineData("[SerializeField] public int? Member;", "not a supported attribute type")]
    [InlineData("[SerializeField] public System.Guid Member;", "not a supported attribute type")]
    [InlineData("[SerializeField] public int this[int index] { get => 0; set { } }", "getter and a setter")]
    [InlineData("[field: SerializeField] public int Member { get; set; }", "backing field")]
    [InlineData("[SerializeField] protected virtual int Member { get; set; } = 1;", "virtual")]
    [InlineData("[SerializeField, System.Obsolete(\"old\", true)] public int Member;", "Obsolete")]
    [InlineData("[SerializeField, System.Obsolete(\"old\", true)] public int Member { get; set; }", "Obsolete")]
    public void Member_the_generator_cannot_describe_is_an_error(string member, string reason)
    {
        var result = Run(Header + "public partial class Probe : Component\n{\n    " + member +
            "\n    public Probe(Context context) : base(context) { }\n}\n");

        var error = Assert.Single(result.Diagnostics, static d => d.Id == "RBFXSG004");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains(reason, error.GetMessage());
        Assert.Contains("GeneratorTest.Probe", error.GetMessage());
    }

    [Fact]
    public void Private_enum_type_of_a_member_is_an_error()
    {
        var result = Run(Header + """
            public partial class Probe : Component
            {
                private enum Hidden { A, B }
                [SerializeField] public Hidden Member = Hidden.B;
                public Probe(Context context) : base(context) { }
            }
            """);

        Assert.Contains("enum", AssertMemberError(result, "Member").GetMessage());
    }

    [Fact]
    public void Private_member_of_a_generic_type_with_constraints_is_an_error()
    {
        var result = Run(Header + """
            public abstract partial class Base<T> : Component where T : class
            {
                [SerializeField] private int _value = 1;
                [SerializeField] public int Open = 1;
                protected Base(Context context) : base(context) { }
            }

            public partial class D : Base<string>
            {
                public D(Context context) : base(context) { }
            }
            """);

        Assert.Contains("constraints", AssertMemberError(result, "_value").GetMessage());
        Assert.Contains("Create<int>(\"Open\"", result.Registration("GeneratorTest.D"));
    }

    [Fact]
    public void Type_that_is_not_a_Serializable_is_an_error()
    {
        var result = Run(Header + """
            public partial class Plain : Urho3D.Core.Object
            {
                [SerializeField] public int Value = 1;
                public Plain(Context context) : base(context) { }
            }
            """);

        var error = Assert.Single(result.Diagnostics, static d => d.Id == "RBFXSG004");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Serializable", error.GetMessage());
    }

    [Fact]
    public void Private_nested_managed_type_is_an_error()
    {
        var result = Run(Header + """
            public partial class Visible : Component
            {
                public Visible(Context context) : base(context) { }
            }

            public partial class Outer
            {
                private class Hidden : Component, SharpCpp.Rbfx.Bindings.Interop.IManagedObjectFactory<Hidden>
                {
                    public Hidden(Context context) : base(context) { }
                    public static Hidden Create(Context context) => new Hidden(context);
                    public new static TypeInfo GetTypeInfoStatic() => throw new System.NotSupportedException();
                }
            }
            """);

        var error = Assert.Single(result.Diagnostics, static d => d.Id == "RBFXSG003");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Hidden", error.GetMessage());
    }

    [Fact]
    public void Member_marked_Obsolete_as_warning_is_described()
    {
        var result = Run(Header + """
            public partial class Probe : Component
            {
                [SerializeField, System.Obsolete("old")] public int Member;
                public Probe(Context context) : base(context) { }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "RBFXSG004");
        AssertNoErrors(result);
        Assert.Contains("Create<int>(\"Member\"", result.Registration("GeneratorTest.Probe"));
    }

    [Fact]
    public void NonSerialized_field_in_a_base_type_of_another_assembly_is_skipped_silently()
    {
        var result = Run(
            Header + """
            public partial class Derived : ForeignBase
            {
                [SerializeField] public int Own;
                public Derived(Context context) : base(context) { }
            }
            """,
            Header + """
            public class ForeignBase : Component
            {
                [SerializeField, System.NonSerialized] public int Skipped;
                public ForeignBase(Context context) : base(context) { }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "RBFXSG004");
        AssertNoErrors(result);
        Assert.DoesNotContain("Skipped", result.Registration("GeneratorTest.Derived"));
    }

    [Fact]
    public void Serialize_field_in_a_base_type_of_another_assembly_is_an_error()
    {
        var result = Run(
            Header + """
            public partial class Derived : ForeignBase
            {
                public Derived(Context context) : base(context) { }
            }
            """,
            Header + """
            public class ForeignBase : Component
            {
                [SerializeField] public int Shared;
                public ForeignBase(Context context) : base(context) { }
            }
            """);

        var error = Assert.Single(result.Diagnostics, static d => d.Id == "RBFXSG004");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("another assembly", error.GetMessage());
    }

    [Fact]
    public void Serialize_field_in_a_base_type_of_another_assembly_with_a_registry_stops_the_chain()
    {
        var result = Run(
            Header + """
            public partial class Boss : ForeignEnemy
            {
                [SerializeField] public int Rage = 4;
                public Boss(Context context) : base(context) { }
            }
            """,
            Header + """
            public partial class ForeignEnemy : Component
            {
                [SerializeField] public float Speed = 1;
                [SerializeField] private int Hidden = 2;
                public ForeignEnemy(Context context) : base(context) { }
            }
            """,
            foreignGenerated: true);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "RBFXSG004");
        AssertNoErrors(result);
        var boss = result.Registration("GeneratorTest.Boss");
        Assert.Contains("\"Rage\"", boss);
        Assert.DoesNotContain("Speed", boss);
        Assert.DoesNotContain("Hidden", boss);
        Assert.DoesNotContain("__Field", result.Registry);
    }

    [Fact]
    public void Serialize_field_in_an_abstract_base_type_of_another_assembly_with_a_registry_is_an_error()
    {
        var result = Run(
            Header + """
            public partial class Boss : ForeignBase
            {
                public Boss(Context context) : base(context) { }
            }
            """,
            Header + """
            public abstract partial class ForeignBase : Component
            {
                [SerializeField] public float Speed = 1;
                protected ForeignBase(Context context) : base(context) { }
            }

            public partial class ForeignConcrete : ForeignBase
            {
                public ForeignConcrete(Context context) : base(context) { }
            }
            """,
            foreignGenerated: true);

        var error = Assert.Single(result.Diagnostics, static d => d.Id == "RBFXSG004");
        Assert.Contains("does not list the type", error.GetMessage());
    }

    [Fact]
    public void Serialize_field_in_a_base_type_of_another_assembly_that_its_registry_does_not_list_is_an_error()
    {
        var result = Run(
            Header + """
            public partial class Boss : EnemyBase
            {
                public Boss(Context context) : base(context) { }
            }
            """,
            Header + """
            public partial class EnemyBase : Component
            {
                [SerializeField] public float Hp = 10;
                protected EnemyBase(Context context) : base(context) { }
            }

            public partial class ForeignConcrete : Component
            {
                public ForeignConcrete(Context context) : base(context) { }
            }
            """,
            foreignGenerated: true);

        var error = Assert.Single(result.Diagnostics, static d => d.Id == "RBFXSG004");
        Assert.Contains("does not list the type", error.GetMessage());
        Assert.Contains("Hp", error.GetMessage());
    }

    [Fact]
    public void Serialize_field_in_a_base_type_of_another_assembly_without_a_registry_names_the_generator()
    {
        var result = Run(
            Header + """
            public partial class Boss : ForeignBase
            {
                public Boss(Context context) : base(context) { }
            }
            """,
            Header + """
            public class ForeignBase : Component
            {
                [SerializeField] public float Speed = 1;
                public ForeignBase(Context context) : base(context) { }
            }
            """);

        var error = Assert.Single(result.Diagnostics, static d => d.Id == "RBFXSG004");
        Assert.Contains("no generated type registry", error.GetMessage());
        Assert.Contains("SharpCpp.Rbfx.SourceGenerator", error.GetMessage());
    }

    [Fact]
    public void Member_of_a_generic_base_type_compiles()
    {
        var result = Run(Header + """
            public abstract class Base<T> : Component
            {
                [SerializeField] public T Value;
                protected Base(Context context) : base(context) { }
            }

            public partial class D : Base<int>
            {
                public D(Context context) : base(context) { }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "RBFXSG004");
        AssertNoErrors(result);
        Assert.Contains("Create<int>(\"Value\"", result.Registration("GeneratorTest.D"));
    }

    [Fact]
    public void Member_of_a_nested_type_compiles()
    {
        var result = Run(Header + """
            public partial class Outer
            {
                // The generator implements the object factory only for types that are not nested, so this type implements it.
                public class Inner : Component, SharpCpp.Rbfx.Bindings.Interop.IManagedObjectFactory<Inner>
                {
                    [SerializeField] public int Value;
                    public Inner(Context context) : base(context) { }
                    public static Inner Create(Context context) => new Inner(context);
                    public new static TypeInfo GetTypeInfoStatic() => throw new System.NotSupportedException();
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "RBFXSG004");
        AssertNoErrors(result);
        Assert.Contains("Create<int>(\"Value\"", result.Registration("GeneratorTest.Outer.Inner"));
    }

    [Fact]
    public void Member_with_a_keyword_name_compiles()
    {
        var result = Run(Header + """
            public partial class Probe : Component
            {
                [SerializeField] public int @class;
                [SerializeField] public float @event { get; set; }
                public Probe(Context context) : base(context) { }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "RBFXSG004");
        AssertNoErrors(result);
        Assert.Contains("Create<int>(\"class\", \"class\"", result.Registration("GeneratorTest.Probe"));
    }

    [Fact]
    public void Attribute_plugin_registry_lists_the_private_members()
    {
        // PluginRuntime.Load keeps the assembly in a plugin load context, as the plugin tests expect.
        var assembly = PluginRuntime.Load(Path.Combine(AppContext.BaseDirectory, "plugins", "AttributePlugin", "SharpCpp.Rbfx.Tests.E2E.AttributePlugin.dll"));
        var registry = ManagedTypeRegistries.Find(assembly)!;
        var byName = registry.Registrations.ToDictionary(static item => item.Type.Name);

        // AttributeComponent has a private [SerializeField] field. DerivedAttributeComponent copies its attributes from the base reflection.
        Assert.Contains(byName["AttributeComponent"].Attributes!, static descriptor => descriptor.Name == "Secret Value");
        var derived = Assert.Single(byName["DerivedAttributeComponent"].Attributes!);
        Assert.Equal("Extra", derived.Name);
        Assert.Equal(typeof(double), derived.ValueType);
    }

    // The generator reports one error for the member, and the message names the member.
    private static Diagnostic AssertMemberError(GeneratorResult result, string member)
    {
        var error = Assert.Single(result.Diagnostics, d => d.Id == "RBFXSG004" && d.GetMessage().Contains("'" + member + "'", StringComparison.Ordinal));
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        return error;
    }

    private static void AssertNoErrors(GeneratorResult result) =>
        Assert.True(result.OutputErrors.Length == 0, string.Join("\n", result.OutputErrors) + "\n" + result.Registry);

    // The optional second source is compiled into another assembly that the tested assembly references.
    // With foreignGenerated, the generator also runs on the other assembly, so that it has a registry.
    private static GeneratorResult Run(string source, string? foreignSource = null, bool foreignGenerated = false)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        if (foreignSource is not null)
        {
            var foreign = CSharpCompilation.Create(
                "GeneratorTestForeign",
                [CSharpSyntaxTree.ParseText(foreignSource)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            if (foreignGenerated)
            {
                var foreignDriver = CSharpGeneratorDriver.Create(new ManagedObjectRttiGenerator());
                foreignDriver.RunGeneratorsAndUpdateCompilation(foreign, out var updated, out _);
                foreign = (CSharpCompilation)updated;
            }

            var image = new MemoryStream();
            var emitted = foreign.Emit(image);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            references.Add(MetadataReference.CreateFromImage(image.ToArray()));
        }

        var compilation = CSharpCompilation.Create(
            "GeneratorTest",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CSharpGeneratorDriver.Create(new ManagedObjectRttiGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var registry = driver.GetRunResult().GeneratedTrees
            .SingleOrDefault(static tree => tree.FilePath.EndsWith("SharpCpp.Rbfx.ManagedTypeRegistry.g.cs", StringComparison.Ordinal))?
            .ToString();
        Assert.True(registry is not null, "The generator emitted no registry:\n" + string.Join("\n", diagnostics));
        var errors = output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.ToString()).ToArray();
        return new GeneratorResult(registry!, diagnostics.ToArray(), errors);
    }

    private sealed record GeneratorResult(string Registry, Diagnostic[] Diagnostics, string[] OutputErrors)
    {
        // The text of the descriptor line of one member, in the text of a registration.
        public string Entry(string registration, string name) =>
            registration.Split('\n').Single(line => line.Contains("Create<") && line.Contains("(\"" + name + "\", ")).TrimEnd();

        // The text of one registration: from its "new(typeof(...))" line to the next registration.
        public string Registration(string typeName)
        {
            var marker = "new(typeof(global::" + typeName + "),";
            var start = Registry.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, "The registry has no registration of " + typeName + ":\n" + Registry);
            var end = Registry.IndexOf("new(typeof(", start + marker.Length, StringComparison.Ordinal);
            return end < 0 ? Registry.Substring(start) : Registry.Substring(start, end - start);
        }
    }
}
