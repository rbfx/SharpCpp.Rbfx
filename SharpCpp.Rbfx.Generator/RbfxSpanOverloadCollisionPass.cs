using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

/// <summary>
/// Removes overloads that a span parameter makes redundant, ambiguous or unsafe in C#.
/// <list type="bullet">
/// <item>A span overload of a function that keeps the pointer after the call is unsafe, because
/// the managed memory is pinned only for the call. The pass excludes the span overload.</item>
/// <item>A span overload and an overload with the same managed signature, for example a
/// <c>string_view</c> overload, cannot both exist in C#. The pass excludes the span overload.</item>
/// <item>A span overload that stays replaces a pointer and size overload with the same element type
/// and the same constness. The pass excludes the pointer and size overload.</item>
/// </list>
/// The pass applies the rules in this order, so a pointer and size overload stays when its
/// replacement is excluded. Each exclusion is a silent exclusion with a reason, so the generator
/// log shows it.
/// </summary>
public static class RbfxSpanOverloadCollisionPass
{
    public const string RetainsPointerReason = "retains the pointer after the call";

    public const string PointerSizeReason =
        "a span overload replaces this pointer and size overload";

    public const string ManagedSignatureReason =
        "this span overload has the same managed signature as a sibling overload, which stays";

    // Functions that keep the address of a span argument after the call returns.
    private static readonly HashSet<string> RetainsPointerFunctions = new(StringComparer.Ordinal)
    {
        "Urho3D::MemoryBuffer::MemoryBuffer",
    };

    public static int Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var excludedCount = 0;
        var groups = compilation.Functions
            .Where(static function => !function.IsIgnored())
            .GroupBy(static function => OverloadSetKey(function), StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var overloads = group.ToList();
            var spanOverloads = overloads.Where(HasSpanParameter).ToList();
            if (spanOverloads.Count == 0)
            {
                continue;
            }

            var others = overloads.Where(static function => !HasSpanParameter(function)).ToList();
            var keptSpanOverloads = new List<FunctionBinding>();
            foreach (var span in spanOverloads)
            {
                string? reason = null;
                if (span.CppQualifiedName is { } name && RetainsPointerFunctions.Contains(name))
                {
                    reason = RetainsPointerReason;
                }
                else if (others.Any(other => HaveSameManagedSignature(span, other)))
                {
                    reason = ManagedSignatureReason;
                }

                if (reason is null)
                {
                    keptSpanOverloads.Add(span);
                    continue;
                }

                span.SetSilentExclusionReason(reason, overwriteExisting: true);
                excludedCount++;
            }

            foreach (var candidate in others)
            {
                if (keptSpanOverloads.Any(span => ReplacesPointerSize(span, candidate)))
                {
                    candidate.SetSilentExclusionReason(PointerSizeReason, overwriteExisting: true);
                    excludedCount++;
                }
            }
        }

        return excludedCount;
    }

    // Overloads collide in C# when they have the same scope and managed name.
    private static string OverloadSetKey(FunctionBinding function)
    {
        var qualifiedName = function.CppQualifiedName ?? function.NativeName;
        var separator = qualifiedName.LastIndexOf("::", StringComparison.Ordinal);
        var scope = separator < 0 ? string.Empty : qualifiedName[..separator];
        return $"{scope}|{function.IsConstructor}|{function.ManagedName}";
    }

    internal static bool IsSpanParameter(ParameterBinding parameter) =>
        parameter.Type.Marshaller.IsSpanProjection &&
        parameter.Type.ManagedMarshaller?.Contains("SpanAbiMarshaller<", StringComparison.Ordinal) == true;

    private static bool HasSpanParameter(FunctionBinding function) => function.Parameters.Any(IsSpanParameter);

    // The pointer and size overload has a pointer and an integer where the span overload has
    // the span. All other parameters have the same managed type.
    private static bool ReplacesPointerSize(FunctionBinding span, FunctionBinding pointerSize)
    {
        if (pointerSize.Parameters.Count != span.Parameters.Count + SpanParameterCount(span))
        {
            return false;
        }

        var pointerIndex = 0;
        foreach (var parameter in span.Parameters)
        {
            if (IsSpanParameter(parameter))
            {
                if (pointerIndex + 1 >= pointerSize.Parameters.Count ||
                    !IsPointer(pointerSize.Parameters[pointerIndex]) ||
                    !PointerMatchesSpanElement(pointerSize.Parameters[pointerIndex], parameter) ||
                    !IsInteger(pointerSize.Parameters[pointerIndex + 1]))
                {
                    return false;
                }

                pointerIndex += 2;
                continue;
            }

            if (!SameManagedParameter(parameter, pointerSize.Parameters[pointerIndex]))
            {
                return false;
            }

            pointerIndex++;
        }

        return pointerIndex == pointerSize.Parameters.Count;
    }

    private static int SpanParameterCount(FunctionBinding function) => function.Parameters.Count(IsSpanParameter);

    private static bool HaveSameManagedSignature(FunctionBinding left, FunctionBinding right) =>
        left.Parameters.Count == right.Parameters.Count &&
        left.Parameters.Zip(right.Parameters).All(static pair => SameManagedParameter(pair.First, pair.Second));

    private static bool SameManagedParameter(ParameterBinding left, ParameterBinding right) =>
        left.Direction == right.Direction &&
        string.Equals(left.Type.ManagedTypeName, right.Type.ManagedTypeName, StringComparison.Ordinal);

    private static bool IsPointer(ParameterBinding parameter) =>
        parameter.AbiKind == BindingParameterAbiKind.Pointer ||
        parameter.Type.IsPointerType ||
        parameter.Type.PrimitiveKind == BindingPrimitiveKind.IntPtr;

    // The pointer has the constness and the element type of the span. A void pointer matches a
    // span of bytes.
    private static bool PointerMatchesSpanElement(ParameterBinding pointer, ParameterBinding span)
    {
        var spanIsConst = span.Type.ManagedMarshaller?.Contains("ReadOnlySpanAbiMarshaller<", StringComparison.Ordinal) == true;
        if (span.Type.GenericArguments is not [var element] ||
            !TryGetPointee(pointer.CppTypeName, out var pointee, out var pointeeIsConst) ||
            pointeeIsConst != spanIsConst)
        {
            return false;
        }

        var elementName = StripConst(element.CppTypeName);
        if (pointee == "void")
        {
            return element.Kind == BindingTypeKind.Primitive &&
                element.PrimitiveKind is BindingPrimitiveKind.UInt8 or BindingPrimitiveKind.Int8;
        }

        if (string.Equals(pointee, elementName, StringComparison.Ordinal))
        {
            return true;
        }

        var pointeeKind = BindingType.PrimitiveKindFromCppName(pointee);
        return element.Kind == BindingTypeKind.Primitive &&
            pointeeKind != BindingPrimitiveKind.Unsupported &&
            pointeeKind == element.PrimitiveKind;
    }

    // Splits a single-level pointer type such as "const unsigned char *" into its pointee and
    // the constness of the pointee.
    private static bool TryGetPointee(string cppTypeName, out string pointee, out bool isConst)
    {
        var text = cppTypeName.Trim();
        if (text.EndsWith("const", StringComparison.Ordinal))
        {
            text = text[..^"const".Length].TrimEnd();
        }

        if (!text.EndsWith('*') || text[..^1].Contains('*'))
        {
            pointee = string.Empty;
            isConst = false;
            return false;
        }

        var words = text[..^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        isConst = words.Contains("const");
        pointee = string.Join(' ', words.Where(static word => word != "const"));
        return pointee.Length > 0;
    }

    private static string StripConst(string cppTypeName) =>
        string.Join(' ', cppTypeName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(static word => word != "const"));

    private static bool IsInteger(ParameterBinding parameter) =>
        parameter.Direction == BindingParameterDirection.In &&
        parameter.Type.Kind == BindingTypeKind.Primitive &&
        parameter.Type.PrimitiveKind is
            BindingPrimitiveKind.Int8 or BindingPrimitiveKind.UInt8 or
            BindingPrimitiveKind.Int16 or BindingPrimitiveKind.UInt16 or
            BindingPrimitiveKind.Int32 or BindingPrimitiveKind.UInt32 or
            BindingPrimitiveKind.Int64 or BindingPrimitiveKind.UInt64 or
            BindingPrimitiveKind.NativeInt or BindingPrimitiveKind.NativeUInt;
}
