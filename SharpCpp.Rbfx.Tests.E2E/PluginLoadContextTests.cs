using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Urho3D.Plugins;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed class PluginLoadContextTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PluginLoadContextTests-" + Guid.NewGuid().ToString("N"));

    public PluginLoadContextTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        // The plugin contexts are not collectible, so the loaded files stay locked on Windows. The directory is in the temp path.
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Private_dependency_without_a_registry_or_a_plugin_module_is_not_shared()
    {
        var path = Compile("PrivateUtils" + Guid.NewGuid().ToString("N"), "public class Utils { }");
        var assembly = new AssemblyLoadContext("PrivateUtilsOwner").LoadFromAssemblyPath(path);

        Assert.False(PluginLoadContext.IsPluginAssembly(assembly));
        PluginLoadContext.Remember(assembly);
        Assert.Null(PluginLoadContext.FindLoadedFile(path));
    }

    [Fact]
    public void Plugin_assembly_is_shared_by_its_name()
    {
        var path = Compile(
            "SharedPlugin" + Guid.NewGuid().ToString("N"),
            "[assembly: Urho3D.Plugins.PluginModule(typeof(object))] public class Entry { }");
        var assembly = new AssemblyLoadContext("SharedPluginOwner").LoadFromAssemblyPath(path);

        Assert.True(PluginLoadContext.IsPluginAssembly(assembly));
        PluginLoadContext.Remember(assembly);
        Assert.Same(assembly, PluginLoadContext.FindLoadedFile(path));
    }

    [Fact]
    public void Assembly_of_the_same_name_with_another_public_key_is_not_shared()
    {
        var name = "SharedPlugin" + Guid.NewGuid().ToString("N");
        const string source = "[assembly: Urho3D.Plugins.PluginModule(typeof(object))] public class Entry { }";
        var assembly = new AssemblyLoadContext("SharedPluginFirst").LoadFromAssemblyPath(Compile(name, source, "first"));
        PluginLoadContext.Remember(assembly);

        // The second file is delay-signed with a public key, so its identity differs from the first assembly.
        var second = Compile(name, source, "second", CreatePublicKey());
        Assert.NotEmpty(AssemblyName.GetAssemblyName(second).GetPublicKeyToken()!);
        Assert.Null(PluginLoadContext.FindLoadedFile(second));
    }

    [Fact]
    public void Plugins_with_a_private_dependency_of_the_same_name_keep_their_own_copy()
    {
        // Two plugins reference a file "PrivateDep.dll" in their own directories. The files have different content.
        var dependencyName = "PrivateDep" + Guid.NewGuid().ToString("N");
        var first = Compile(dependencyName, "public static class Utils { public static string Id => \"one\"; }", "one");
        var second = Compile(dependencyName, "public static class Utils { public static string Id => \"two\"; }", "two");
        const string probe = "public static class Probe { public static string Get() => Utils.Id; }";
        var firstPlugin = Compile("PluginOne" + Guid.NewGuid().ToString("N"), probe, "one", dependencyPath: first);
        var secondPlugin = Compile("PluginTwo" + Guid.NewGuid().ToString("N"), probe, "two", dependencyPath: second);

        var firstAssembly = PluginRuntime.Load(firstPlugin);
        var secondAssembly = PluginRuntime.Load(secondPlugin);

        Assert.Equal("one", Call(firstAssembly));
        Assert.Equal("two", Call(secondAssembly));
        Assert.NotSame(AssemblyLoadContext.GetLoadContext(firstAssembly), AssemblyLoadContext.GetLoadContext(secondAssembly));
    }

    private static string Call(Assembly plugin) =>
        (string)plugin.GetType("Probe")!.GetMethod("Get")!.Invoke(null, null)!;

    // The public key blob of a strong name: the header of the key, the header of the RSA blob and the RSA public key.
    private static ImmutableArray<byte> CreatePublicKey()
    {
        using var rsa = RSA.Create(1024);
        var parameters = rsa.ExportParameters(false);
        var blob = new List<byte>();
        blob.AddRange(BitConverter.GetBytes(0x2400));
        blob.AddRange(BitConverter.GetBytes(0x8004));
        blob.AddRange(BitConverter.GetBytes(20 + parameters.Modulus!.Length));
        blob.AddRange(new byte[] { 0x06, 0x02, 0x00, 0x00, 0x00, 0x24, 0x00, 0x00 });
        blob.AddRange(Encoding.ASCII.GetBytes("RSA1"));
        blob.AddRange(BitConverter.GetBytes(parameters.Modulus.Length * 8));
        var exponent = new byte[4];
        for (var i = 0; i < parameters.Exponent!.Length; i++)
        {
            exponent[i] = parameters.Exponent[parameters.Exponent.Length - 1 - i];
        }

        blob.AddRange(exponent);
        blob.AddRange(parameters.Modulus.Reverse());
        return blob.ToImmutableArray();
    }

    private string Compile(string name, string source, string? subdirectory = null, ImmutableArray<byte> publicKey = default, string? dependencyPath = null)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Concat(dependencyPath is null ? [] : [MetadataReference.CreateFromFile(dependencyPath)]);
        var compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            publicKey.IsDefaultOrEmpty
                ? new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                : new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithCryptoPublicKey(publicKey).WithDelaySign(true));
        var directory = subdirectory is null ? _directory : Path.Combine(_directory, subdirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".dll");
        var result = compilation.Emit(path);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return path;
    }
}
