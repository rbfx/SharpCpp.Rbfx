using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

public static class RbfxUnsupportedApisPass
{
    public static int Apply(BindingCompilation compilation)
    {
        var suppressedCount = 0;
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
            else if (function.CppQualifiedName.EndsWith("::TestDrawables", StringComparison.Ordinal) &&
                function.Parameters.Any(static parameter => NormalizeTypeName(parameter.CppTypeName).EndsWith("Drawable**", StringComparison.Ordinal)))
            {
                // OctreeQuery::TestDrawables(Drawable** start, Drawable** end) takes an input pointer range
                // [start, end) with no count; SharpCpp's T** shapes (counted array or single out value)
                // cannot express a begin/end pair, and the default would bind both as 'out'.
                function.SetIgnoreReason("Drawable** begin/end range has no count; SharpCpp has no pointer-range projection");
                suppressedCount++;
            }
        }

        return suppressedCount;
    }

    private static string NormalizeTypeName(string cppTypeName)
        => cppTypeName.Replace(" ", string.Empty, StringComparison.Ordinal);
}
