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

            if (function.CppQualifiedName == "Urho3D::Tetrahedron::GetTriangleFaceIndices")
            {
                // Takes `unsigned (&indices)[3]`, a reference to a fixed-size C array. SharpCpp's
                // generic Marshaller<T&> fallback reinterpret_casts the array's address as
                // Marshaller<unsigned[3]>::abi_type* (NativeArrayAbi, a pointer+length struct),
                // which does not match the raw array's layout -- SharpCpp.Marshaller.hpp's
                // layout-compatibility static_assert correctly rejects it. There is no
                // fixed-array-by-reference marshaller for this shape yet, so skip the member.
                function.SetIgnoreReason("fixed-size array reference parameter has no layout-safe marshaller yet");
                suppressedCount++;
            }
        }

        return suppressedCount;
    }
}
