using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed class ExternalOwnerSharedPtrTests
{
    [Fact]
    public void Shared_ptr_with_external_owner_keeps_the_pointed_to_object_alive()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "payload.bin"), new byte[17]);

        global::Urho3D.Interop.SharedPtr<global::Urho3D.IO.AbstractFile, global::Urho3D.Container.RefCounted>? openedFile;
        try
        {
            using (var context = new global::Urho3D.Core.Context())
            using (var accessFileSystem = new global::Urho3D.IO.FileSystem(context))
            using (var fileSystem = new global::Urho3D.IO.VirtualFileSystem(context))
            using (var scheme = new global::Ea.String("e2e"))
            using (var path = new global::Ea.String(directory))
            {
                context.RegisterSubsystem(accessFileSystem);
                using var mountPoint = fileSystem.MountDir(scheme, path);
                using var identifier = new global::Urho3D.IO.FileIdentifier("e2e"u8, "payload.bin"u8);
                openedFile = fileSystem.OpenFile(identifier, global::Urho3D.IO.FileMode.FileRead);
                Assert.NotNull(openedFile);
            }

            using (openedFile)
            {
                using var file = openedFile!.Get();
                Assert.Equal(17u, file.Size);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
