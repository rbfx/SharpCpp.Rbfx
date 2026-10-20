using Urho3D.Engine;
using Urho3D.IO;
using Urho3D.RenderAPI;
using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

/// <summary>
/// An eastl::optional&lt;T&gt; crosses the boundary as a nullable managed value. A blittable T uses the
/// value path. A handle type T uses the handle path, which returns an owned handle or null.
/// </summary>
public sealed class OptionalMarshallingTests
{
    [Fact]
    public void Mounted_directory_last_modified_time_is_null_for_a_missing_file()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var context = new global::Urho3D.Core.Context();
            using var fileSystem = new FileSystem(context);
            context.RegisterSubsystem(fileSystem);
            using var mountPoint = new MountedDirectory(context, new Ea.String(directory));
            using var fileName = new FileIdentifier(""u8, "missing.txt"u8);

            Assert.Null(mountPoint.GetLastModifiedTime(fileName, false));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Mounted_directory_last_modified_time_has_a_value_for_an_existing_file()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            global::System.IO.File.WriteAllText(Path.Combine(directory, "present.txt"), "data");

            using var context = new global::Urho3D.Core.Context();
            using var fileSystem = new FileSystem(context);
            context.RegisterSubsystem(fileSystem);
            using var mountPoint = new MountedDirectory(context, new Ea.String(directory));
            using var fileName = new FileIdentifier(""u8, "present.txt"u8);

            var time = mountPoint.GetLastModifiedTime(fileName, false);

            Assert.NotNull(time);
            Assert.NotEqual(0u, time.Value);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Application_flavor_matches_returns_the_distance_or_null()
    {
        using var flavor = new ApplicationFlavor(new Ea.String("platform=desktop,linux"));
        using var near = new ApplicationFlavorPattern(new Ea.String("platform=linux"));
        using var any = new ApplicationFlavorPattern(new Ea.String("platform=*"));
        using var other = new ApplicationFlavorPattern(new Ea.String("platform=windows"));

        Assert.Equal(1u, flavor.Matches(near));
        Assert.Equal(2u, flavor.Matches(any));
        Assert.Null(flavor.Matches(other));
    }

    [Fact]
    public void Optional_property_roundtrips_a_value_and_null()
    {
        using var settings = new RenderDeviceSettings();

        settings.AdapterId = 7u;
        Assert.Equal(7u, settings.AdapterId);

        settings.AdapterId = null;
        Assert.Null(settings.AdapterId);

        settings.AdapterId = 0u;
        Assert.Equal(0u, settings.AdapterId);
    }

    [Fact]
    public void Optional_handle_result_is_an_owned_object_or_null()
    {
        using var attribute = global::Urho3D.Bindings.ParseVertexAttribute("iNormal2"u8);
        Assert.NotNull(attribute);
        Assert.Equal(VertexElementSemantic.SemNormal, attribute.Semantic);
        Assert.Equal(2u, attribute.SemanticIndex);

        // The native function matches an empty semantic name for any text, so it never returns an
        // empty optional. A match with the last semantic still crosses as an owned object.
        using var fallback = global::Urho3D.Bindings.ParseVertexAttribute("unknown"u8);
        Assert.NotNull(fallback);
        Assert.Equal(VertexElementSemantic.SemObjectindex, fallback.Semantic);
    }

    static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sharpcpp-optional-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
