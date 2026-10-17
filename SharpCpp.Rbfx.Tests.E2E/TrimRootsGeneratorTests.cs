using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using SharpCpp.Rbfx.SourceGenerator;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

/// <summary>
/// Tests of TrimRootsGenerator. The heads have no TrimmerRootAssembly items: the generated DynamicDependency
/// attributes are the only roots of the members that only native code reaches.
/// </summary>
public sealed class TrimRootsGeneratorTests
{
    private const string Prefix = "[global::System.Diagnostics.CodeAnalysis.DynamicDependency(";

    [Fact]
    public void Concrete_plugin_classes_are_roots_with_their_public_constructors()
    {
        var roots = Run("""
            using Urho3D.Core;
            using Urho3D.Plugins;

            namespace Sample;

            public class SamplePlugin : Plugin
            {
                public SamplePlugin(Context context) : base(context) { }
            }

            public abstract class AbstractPlugin : Plugin
            {
                protected AbstractPlugin(Context context) : base(context) { }
            }

            public class NotAPlugin { }
            """);

        Assert.Contains(Prefix + "global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors, typeof(global::Sample.SamplePlugin))]", roots.Source);
        Assert.DoesNotContain("AbstractPlugin", roots.Source);
        Assert.DoesNotContain("NotAPlugin", roots.Source);
    }

    [Fact]
    public void The_first_visible_plugin_class_gets_the_assembly_attribute()
    {
        var roots = Run("""
            using Urho3D.Core;
            using Urho3D.Plugins;

            namespace Sample;

            public abstract class AbstractPlugin : Plugin
            {
                protected AbstractPlugin(Context context) : base(context) { }
            }

            public class Outer
            {
                private class HiddenPlugin : Plugin
                {
                    public HiddenPlugin(Context context) : base(context) { }
                }
            }

            public class FirstPlugin : Plugin
            {
                public FirstPlugin(Context context) : base(context) { }
            }

            public class SecondPlugin : Plugin
            {
                public SecondPlugin(Context context) : base(context) { }
            }
            """);

        Assert.Contains("[assembly: global::Urho3D.Plugins.GeneratedPluginType(typeof(global::Sample.FirstPlugin))]", roots.Source);
        Assert.Equal(1, Count(roots.Source, "GeneratedPluginType("));
    }

    [Fact]
    public void An_assembly_without_a_visible_plugin_class_gets_no_assembly_attribute()
    {
        var roots = Run("""
            using Urho3D.Core;
            using Urho3D.Plugins;

            namespace Sample;

            public class Outer
            {
                private class HiddenPlugin : Plugin
                {
                    public HiddenPlugin(Context context) : base(context) { }
                }
            }
            """);

        Assert.DoesNotContain("GeneratedPluginType", roots.Source);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void Entry_points_without_an_address_of_use_are_roots()
    {
        var roots = Run("""
            using System.Diagnostics.CodeAnalysis;
            using System.Runtime.InteropServices;

            namespace Sample;

            public static unsafe class Entries
            {
                [UnmanagedCallersOnly]
                public static int ByName() => 1;

                [UnmanagedCallersOnly]
                private static int ByAddress() => 2;

                [RequiresUnreferencedCode("Not for trimmed applications.")]
                [UnmanagedCallersOnly]
                public static int NotTrimSafe() => 3;

                public static int NotAnEntry() => 4;

                public static nint Address() => (nint)(delegate* unmanaged<int>)&ByAddress;

                private static class Hidden
                {
                    [UnmanagedCallersOnly]
                    private static void Callback() { }
                }
            }
            """);

        Assert.Contains(Prefix + "\"ByName\", typeof(global::Sample.Entries))]", roots.Source);
        Assert.DoesNotContain("\"ByAddress\"", roots.Source);
        Assert.DoesNotContain("\"NotTrimSafe\"", roots.Source);
        Assert.DoesNotContain("\"NotAnEntry\"", roots.Source);
        // The generated class cannot see a private nested type: the attribute names it and the assembly.
        Assert.Contains(Prefix + "\"Callback\", \"Sample.Entries.Hidden\", \"GeneratorTest\")]", roots.Source);
    }

    [Fact]
    public void Roots_of_referenced_assemblies_are_chained()
    {
        var roots = Run(
            "namespace Sample; public static class Empty { }",
            foreignSource: """
                using Urho3D.Core;
                using Urho3D.Plugins;

                namespace Foreign;

                public class ForeignPlugin : Plugin
                {
                    public ForeignPlugin(Context context) : base(context) { }
                }
                """);

        Assert.Contains(Prefix + "\"Keep\", \"SharpCpp.Rbfx.Generated.TrimRoots\", \"GeneratorTestForeign\")]", roots.Source);
        Assert.DoesNotContain("ForeignPlugin", roots.Source);
    }

    [Fact]
    public void The_test_assembly_chains_to_the_roots_of_the_bindings()
    {
        // The real build: the generator also runs on SharpCpp.Rbfx.Bindings and on this assembly.
        var keep = typeof(TrimRootsGeneratorTests).Assembly
            .GetType("SharpCpp.Rbfx.Generated.TrimRoots")?
            .GetMethod("Keep", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(keep);
        Assert.Contains(keep.GetCustomAttributes<DynamicDependencyAttribute>(), static dependency =>
            dependency.MemberSignature == "Keep" &&
            dependency.TypeName == "SharpCpp.Rbfx.Generated.TrimRoots" &&
            dependency.AssemblyName == "SharpCpp.Rbfx.Bindings");
    }

    private static GeneratorResult Run(string source, string? foreignSource = null)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable);
        if (foreignSource is not null)
        {
            var foreign = CSharpCompilation.Create("GeneratorTestForeign", [CSharpSyntaxTree.ParseText(foreignSource)], references, options);
            CSharpGeneratorDriver.Create(new TrimRootsGenerator()).RunGeneratorsAndUpdateCompilation(foreign, out var updated, out _);
            var image = new MemoryStream();
            var emitted = updated.Emit(image);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            references.Add(MetadataReference.CreateFromImage(image.ToArray()));
        }

        var compilation = CSharpCompilation.Create("GeneratorTest", [CSharpSyntaxTree.ParseText(source)], references, options);
        var driver = CSharpGeneratorDriver.Create(new TrimRootsGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var generated = driver.GetRunResult().GeneratedTrees
            .SingleOrDefault(static tree => tree.FilePath.EndsWith("SharpCpp.Rbfx.TrimRoots.g.cs", StringComparison.Ordinal))?
            .ToString();
        Assert.True(generated is not null, "The generator emitted no trimmer roots.");
        var errors = output.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors) + "\n" + generated);
        return new GeneratorResult(generated!);
    }

    private sealed record GeneratorResult(string Source);
}
