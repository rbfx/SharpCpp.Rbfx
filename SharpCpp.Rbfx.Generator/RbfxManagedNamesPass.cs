using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

/// <summary>
/// Explicit managed names for members that would otherwise be renamed to <c>Native{Name}</c> because they collide
/// with the SafeHandle/NativeObject surface. Every entry sets <c>ManagedName</c> and
/// <see cref="MetadataKeys.ExplicitManagedRename"/>, so the pipeline leaves the function alone. Runs before
/// pipeline validation, like the pointer-range tagging.
/// </summary>
public static class RbfxManagedNamesPass
{
    private readonly record struct Entry(string RecordName, string NativeName, int ParameterCount, string ManagedName);

    // AbstractFile::Close is virtual: File and NamedPipe are its only overrides in the SDK, and all three share
    // one name so the overrides stay consistent.
    private static readonly Entry[] Entries =
    [
        new("Rml::ElementDocument", "Close", 0, "CloseDocument"),
        new("Urho3D::Graphics", "Close", 0, "CloseWindow"),
        new("Urho3D::Log", "Close", 0, "CloseLog"),
        new("Urho3D::Audio", "Close", 0, "CloseAudio"),
        new("Urho3D::AbstractFile", "Close", 0, "CloseFile"),
        new("Urho3D::File", "Close", 0, "CloseFile"),
        new("Urho3D::NamedPipe", "Close", 0, "CloseFile"),
        new("Urho3D::ScriptRuntimeApi", "Dispose", 1, "DisposeInstance"),
    ];

    /// <summary>Applies the explicit names and returns the number of functions renamed.</summary>
    public static int Apply(BindingCompilation compilation)
    {
        var count = 0;
        foreach (var entry in Entries)
        {
            foreach (var record in compilation.Records.Where(record => record.CppQualifiedName == entry.RecordName))
            {
                foreach (var function in record.Members.OfType<FunctionBinding>().Where(function =>
                             function.NativeName == entry.NativeName && function.Parameters.Count == entry.ParameterCount))
                {
                    function.ManagedName = entry.ManagedName;
                    function.SetMetadata(MetadataKeys.ExplicitManagedRename, true);
                    ++count;
                }
            }
        }

        return count;
    }
}
