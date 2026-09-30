using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

/// <summary>
/// Tags rbfx <c>TestDrawables(Drawable** start, Drawable** end, bool inside)</c> (the octree query
/// interface and every override) as a pointer range so it binds as one
/// <c>ReadOnlySpan&lt;Drawable?&gt;</c> parameter. The SDK headers cannot carry the attribute.
/// </summary>
public static class RbfxPointerRangesPass
{
    public static int Apply(BindingCompilation compilation)
    {
        var taggedCount = 0;
        foreach (var function in compilation.Functions)
        {
            if (!function.CppQualifiedName.EndsWith("::TestDrawables", StringComparison.Ordinal) ||
                function.Parameters.Count != 3 ||
                function.Parameters[0].NativeName != "start" ||
                function.Parameters[1].NativeName != "end" ||
                function.Parameters[0].PointerPointer == PointerPointerKind.None)
            {
                continue;
            }

            function.Parameters[0].SetMetadata(MetadataKeys.EndedBy, "end");
            taggedCount++;
        }

        return taggedCount;
    }
}
