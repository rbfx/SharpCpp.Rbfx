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
        Assert.Empty(result.OutputErrors);

        // The base type is registered first, so the derived type copies its attributes and lists only its own members.
        var baseText = result.Registration("GeneratorTest.Base");
        Assert.Contains(
            "global::Urho3D.Plugins.ManagedAttributeDescriptor.Create<float>(\"Speed\", \"Speed\", typeof(float), " +
            "global::Urho3D.Core.AttributeMode.AmDefault, static o => ((global::GeneratorTest.Base)o).Speed, " +
            "static (o, v) => ((global::GeneratorTest.Base)o).Speed = v)",
            baseText);

        var derivedText = result.Registration("GeneratorTest.Derived");
        Assert.DoesNotContain("Speed", derivedText);
        Assert.Contains("Create<string>(\"Label Text\", \"Label\", typeof(string), unchecked((global::Urho3D.Core.AttributeMode)", derivedText);
        Assert.Contains("static o => ((global::GeneratorTest.Derived)o).Label!, static (o, v) => ((global::GeneratorTest.Derived)o).Label = v)", derivedText);
        Assert.Contains(
            "Create<object>(\"Mood\", \"Mood\", typeof(global::GeneratorTest.Mood), global::Urho3D.Core.AttributeMode.AmDefault, " +
            "static o => (object)((global::GeneratorTest.Derived)o).Mood, static (o, v) => ((global::GeneratorTest.Derived)o).Mood = (global::GeneratorTest.Mood)v)",
            derivedText);
        Assert.DoesNotContain("Skipped", derivedText);
        Assert.DoesNotContain("NotMarked", derivedText);

        // The reflection path lists the fields of a type first, then its properties.
        Assert.True(derivedText.IndexOf("\"Mood\"", StringComparison.Ordinal) < derivedText.IndexOf("\"Label Text\"", StringComparison.Ordinal));
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
        Assert.Empty(result.OutputErrors);
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

        Assert.Empty(result.OutputErrors);
        Assert.Contains("global::System.Array.Empty<global::Urho3D.Plugins.ManagedAttributeDescriptor>()", result.Registration("GeneratorTest.Plain"));
    }

    [Fact]
    public void Private_member_reports_a_warning_and_the_type_falls_back_to_reflection()
    {
        var result = Run(Header + """
            public partial class Hidden : Component
            {
                [SerializeField] public float Open;
                [SerializeField] private int _secret;
                public Hidden(Context context) : base(context) { }
            }

            public partial class Visible : Component
            {
                [SerializeField] public float Open;
                public Visible(Context context) : base(context) { }
            }
            """);

        var warning = Assert.Single(result.Diagnostics, static d => d.Id == "RBFXSG004");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("_secret", warning.GetMessage());
        Assert.Contains("GeneratorTest.Hidden", warning.GetMessage());
        Assert.Empty(result.OutputErrors);

        // The type has no list at all, not even for its public members.
        var hidden = result.Registration("GeneratorTest.Hidden");
        Assert.DoesNotContain("ManagedAttributeDescriptor", hidden);
        Assert.Contains("ManagedAttributeDescriptor.Create<float>", result.Registration("GeneratorTest.Visible"));
    }

    [Theory]
    [InlineData("[SerializeField] protected int Member;")]
    [InlineData("[SerializeField] public readonly int Member;")]
    [InlineData("[SerializeField] public int Member { get; init; }")]
    [InlineData("[SerializeField] public int Member { get; }")]
    [InlineData("[SerializeField] public int Member { get; private set; }")]
    [InlineData("[SerializeField] public byte Member;")]
    [InlineData("[SerializeField] public int? Member;")]
    [InlineData("[SerializeField] public System.Guid Member;")]
    public void Member_the_generated_code_cannot_describe_falls_back_to_reflection(string member)
    {
        var result = Run(Header + "public partial class Probe : Component\n{\n    " + member + "\n    public Probe(Context context) : base(context) { }\n}\n");

        Assert.Single(result.Diagnostics, static d => d.Id == "RBFXSG004");
        Assert.Empty(result.OutputErrors);
        Assert.DoesNotContain("ManagedAttributeDescriptor", result.Registration("GeneratorTest.Probe"));
    }

    [Fact]
    public void Attribute_plugin_registry_has_descriptors_only_for_types_without_private_members()
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "plugins", "AttributePlugin", "SharpCpp.Rbfx.Tests.E2E.AttributePlugin.dll"));
        var registry = ManagedTypeRegistries.Find(assembly)!;
        var byName = registry.Registrations.ToDictionary(static item => item.Type.Name);

        // AttributeComponent has a private [SerializeField] field. DerivedAttributeComponent copies its attributes from the base reflection.
        Assert.Null(byName["AttributeComponent"].Attributes);
        var derived = Assert.Single(byName["DerivedAttributeComponent"].Attributes!);
        Assert.Equal("Extra", derived.Name);
        Assert.Equal(typeof(double), derived.ValueType);
    }

    private static GeneratorResult Run(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "GeneratorTest",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CSharpGeneratorDriver.Create(new ManagedObjectRttiGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var registry = driver.GetRunResult().GeneratedTrees
            .Single(static tree => tree.FilePath.EndsWith("SharpCpp.Rbfx.ManagedTypeRegistry.g.cs", StringComparison.Ordinal))
            .ToString();
        var errors = output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.ToString()).ToArray();
        return new GeneratorResult(registry, diagnostics.ToArray(), errors);
    }

    private sealed record GeneratorResult(string Registry, Diagnostic[] Diagnostics, string[] OutputErrors)
    {
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
