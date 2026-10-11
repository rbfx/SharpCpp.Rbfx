using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

public static class RbfxUnsupportedApisPass
{
    public static int Apply(BindingCompilation compilation, bool isBrowserWasm)
    {
        var suppressedCount = 0;
        if (isBrowserWasm)
        {
            foreach (var record in compilation.Records.Where(static record =>
                         record.CppQualifiedName is "Urho3D::BackgroundLoader" or "Urho3D::BackgroundLoadItem"))
            {
                // rbfx declares these types on Web, but compiles out their implementation.
                // Exclude the wrappers so the static package does not retain unresolved RTTI or methods.
                record.SetIgnoreReason("rbfx does not build BackgroundLoader for WebAssembly");
                suppressedCount++;
            }
        }

        foreach (var function in compilation.Functions.Where(static function => !function.IsIgnored()))
        {
            if (function.CppQualifiedName == "Urho3D::Graph::LoadJSON")
            {
                // The rbfx SDK header declares Graph::LoadJSON(ea::string_view),
                // but libUrho3D.so does not define/export the symbol. Emitting a
                // thunk compiles but leaves libSharpCpp.Rbfx.Native.so unloadable.
                function.SetIgnoreReason("rbfx SDK declares this member but the linked library does not define/export it");
                suppressedCount++;
            }
        }

        return suppressedCount;
    }
}
