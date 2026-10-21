using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

public static class RbfxRmlUiSurfacePass
{
    private static readonly HashSet<string> PublicRecordNames = new(StringComparer.Ordinal)
    {
        "Rml::Context",
        "Rml::DataModelConstructor",
        "Rml::DataModelHandle",
        "Rml::DataTypeRegister",
        "Rml::Element",
        "Rml::ElementDocument",
        "Rml::Event",
        "Rml::Variant",
        "Rml::VariantList"
    };

    private static readonly string[] AllowedTypeNames =
    [
        "Rml::Context",
        "Rml::DataModelConstructor",
        "Rml::DataModelHandle",
        "Rml::DataTypeRegister",
        "Rml::Element",
        "Rml::ElementDocument",
        "Rml::Event",
        "Rml::Variant",
        "Rml::VariantList",
        "Rml::DataEventFunc",
        "Rml::DataGetFunc",
        "Rml::DataSetFunc",
        "Rml::String",
        "Rml::ModalFlag",
        "Rml::FocusFlag",
        // RbfxRmlMathMarshallers.hpp maps these math instantiations to Urho3D math types.
        // Only these exact spellings are allowed. Other instantiations, such as
        // Rml::Vector2<double> or Rml::Vector3i, stay outside the surface.
        "Rml::Vector2f",
        "Rml::Vector2i",
        "Rml::Vector3f",
        "Rml::Vector4f",
        "Rml::Colourf",
        "Rml::Colourb",
        "Rml::Vector2<float>",
        "Rml::Vector2<int>",
        "Rml::Vector3<float>",
        "Rml::Vector4<float>",
        "Rml::Colour<float, 1>",
        "Rml::Colour<unsigned char, 255>",
        "Rml::Colour<Rml::byte, 255>",
        "Rml::Colour<byte, 255>"
    ];

    private static readonly HashSet<string> PublicEnumNames = new(StringComparer.Ordinal)
    {
        "Rml::ModalFlag",
        "Rml::FocusFlag"
    };

    public static int Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var changed = 0;

        foreach (var record in compilation.Records.Where(static record => IsRmlHeader(record.HeaderIncludePath)))
        {
            record.ManagedNamespace = "Rml";
            if (IsRecordOutsideSurface(record))
            {
                record.SetIgnoreReason(RecordOutsideSurfaceReason, overwriteExisting: false);
                record.SetMetadata(MetadataKeys.SilentExclusion, true);
            }
            else
            {
                record.ClearIgnoreReason();
            }

            changed += PruneUnsupportedBases(record);
            changed++;
        }

        changed += EnsureVariantStringTemplateInstantiations(compilation);

        foreach (var enumBinding in compilation.Enums.Where(static enumBinding => IsRmlHeader(enumBinding.HeaderIncludePath)))
        {
            enumBinding.ManagedNamespace = "Rml";
            if (!IsEnumOutsideSurface(enumBinding))
            {
                enumBinding.ClearIgnoreReason();
                enumBinding.RemoveMetadata(MetadataKeys.SilentExclusion);
            }
            else
            {
                enumBinding.SetIgnoreReason(EnumOutsideSurfaceReason, overwriteExisting: false);
                enumBinding.SetMetadata(MetadataKeys.SilentExclusion, true);
            }
            changed++;
        }

        foreach (var function in compilation.Functions.Where(static function => IsRmlHeader(function.HeaderIncludePath)))
        {
            if (IsFunctionOutsideSurface(function))
            {
                function.SetIgnoreReason(FunctionOutsideSurfaceReason, overwriteExisting: false);
                function.SetMetadata(MetadataKeys.SilentExclusion, true);
                changed++;
            }
        }

        var recordsBySymbolId = compilation.Records
            .GroupBy(static record => record.SymbolId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        var enumsBySymbolId = compilation.Enums
            .GroupBy(static enumBinding => enumBinding.SymbolId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        foreach (var function in compilation.Functions.Where(static function => !function.IsIgnored()))
        {
            if (FunctionUsesUnsupportedRmlType(function, recordsBySymbolId, enumsBySymbolId))
            {
                function.SetSilentExclusionReason("RmlUI callable references a type outside the conservative bootstrap surface");
                changed++;
            }
        }

        changed += ExcludeRmlMathOverloadCollisions(compilation);

        foreach (var field in compilation.Records.SelectMany(static record => record.Members.OfType<FieldBinding>()).Where(static field => !field.IsIgnored()))
        {
            if (TypeUsesUnsupportedRmlType(field.Type, field.CppTypeName, recordsBySymbolId, enumsBySymbolId))
            {
                field.SetSilentExclusionReason("RmlUI field references a type outside the conservative bootstrap surface");
                changed++;
            }
        }

        foreach (var record in compilation.Records)
        {
            changed += RemoveUnsupportedGenericInstantiations(record.SupportedGenericInstantiations, recordsBySymbolId, enumsBySymbolId);
        }

        foreach (var function in compilation.Functions)
        {
            changed += RemoveUnsupportedGenericInstantiations(function.SupportedGenericInstantiations, recordsBySymbolId, enumsBySymbolId);
        }

        changed += RemoveUnsupportedTypeAliases(compilation.TypeAliases, recordsBySymbolId, enumsBySymbolId);

        return changed;
    }

    internal const string RecordOutsideSurfaceReason = "RmlUI declaration is outside the conservative bootstrap surface";

    internal const string EnumOutsideSurfaceReason = "RmlUI enum is outside the conservative bootstrap surface";

    internal const string FunctionOutsideSurfaceReason = "RmlUI callable is outside the conservative bootstrap surface";

    public const string RmlMathOverloadCollisionReason =
        "this overload takes an Rml math type and has the same managed signature as a sibling overload, which stays";

    private static readonly string[] ColourbTypeNames = ["Colourb", "Colour<unsignedchar,255>", "Colour<byte,255>"];

    // The managed type of each mapped Rml math type, by the normalized C++ spelling.
    private static readonly (string[] Names, string ManagedType)[] RmlMathManagedTypes =
    [
        (["Vector2f", "Vector2<float>"], "Vector2"),
        (["Vector2i", "Vector2<int>"], "IntVector2"),
        (["Vector3f", "Vector3<float>"], "Vector3"),
        (["Vector4f", "Vector4<float>"], "Vector4"),
        (["Colourf", "Colour<float,1>"], "Color"),
        (ColourbTypeNames, "Color"),
    ];

    /// <summary>
    /// The Rml class templates that RbfxRmlMathMarshallers.hpp maps to Urho3D math types.
    /// The templates have no managed wrapper class.
    /// </summary>
    public static readonly IReadOnlySet<string> MappedMathTemplateNames =
        new HashSet<string>(["Rml::Vector2", "Rml::Vector3", "Rml::Vector4", "Rml::Colour"], StringComparer.Ordinal);

    /// <summary>
    /// The Rml math types map to Urho3D math types, and C# cannot hold two overloads with one signature.
    /// Examples are Urho3D::ToVector2(const Rml::Vector2f&amp;) and Urho3D::ToVector2(const ImVec2&amp;),
    /// and Urho3D::ToColor(const Rml::Colourb&amp;) and Urho3D::ToColor(const Rml::Colourf&amp;).
    /// The pass keeps the overload with the lowest rank and excludes the other overloads.
    /// Rank 0 has no Rml math type. Rank 1 has an Rml math type. Rank 2 has Rml::Colourb.
    /// Thus a Colourf overload stays, because it keeps the float channels without the byte rounding.
    /// </summary>
    private static int ExcludeRmlMathOverloadCollisions(BindingCompilation compilation)
    {
        var changed = 0;
        var groups = compilation.Functions
            .Where(static function => !function.IsIgnored())
            .GroupBy(static function => OverloadSetKey(function), StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var overloads = group.ToList();
            if (overloads.Count < 2)
            {
                continue;
            }

            foreach (var candidate in overloads.Where(static function => RmlMathRank(function) > 0))
            {
                var rank = RmlMathRank(candidate);
                var collides = overloads.Any(other =>
                    !ReferenceEquals(other, candidate) &&
                    !other.IsIgnored() &&
                    RmlMathRank(other) < rank &&
                    HaveSameManagedSignature(candidate, other));
                if (!collides)
                {
                    continue;
                }

                candidate.SetSilentExclusionReason(RmlMathOverloadCollisionReason, overwriteExisting: true);
                changed++;
            }
        }

        return changed;
    }

    // Free functions of one namespace go to one managed Bindings class, also from different headers.
    // Members of one record come from one header. Thus the key does not contain the header.
    private static string OverloadSetKey(FunctionBinding function)
    {
        var qualifiedName = function.CppQualifiedName ?? function.NativeName;
        var separator = qualifiedName.LastIndexOf("::", StringComparison.Ordinal);
        var scope = separator < 0 ? string.Empty : qualifiedName[..separator];
        return $"{scope}|{function.IsConstructor}|{function.ManagedName}";
    }

    private static int RmlMathRank(FunctionBinding function)
    {
        if (function.Parameters.Any(static parameter => IsColourbParameter(parameter)))
        {
            return 2;
        }

        return function.Parameters.Any(static parameter => RmlMathManagedType(parameter) is not null) ? 1 : 0;
    }

    private static bool HaveSameManagedSignature(FunctionBinding left, FunctionBinding right) =>
        left.Parameters.Count == right.Parameters.Count &&
        left.Parameters.Zip(right.Parameters).All(static pair =>
            pair.First.Direction == pair.Second.Direction &&
            string.Equals(ManagedParameterKey(pair.First), ManagedParameterKey(pair.Second), StringComparison.Ordinal));

    // An Rml math type gives the name of its Urho3D math type. Other types give the managed type name
    // without the 'global::' and 'Urho3D.Math.' prefixes, because the renderer writes both spellings.
    private static string ManagedParameterKey(ParameterBinding parameter)
    {
        if (RmlMathManagedType(parameter) is { } mathType)
        {
            return mathType;
        }

        var managedType = parameter.Type.ManagedTypeName ?? string.Empty;
        if (managedType.StartsWith("global::", StringComparison.Ordinal))
        {
            managedType = managedType["global::".Length..];
        }

        return managedType.StartsWith("Urho3D.Math.", StringComparison.Ordinal)
            ? managedType["Urho3D.Math.".Length..]
            : managedType;
    }

    private static string? RmlMathManagedType(ParameterBinding parameter)
    {
        foreach (var (names, managedType) in RmlMathManagedTypes)
        {
            if (IsRmlColourType(parameter.CppTypeName, names) || IsRmlColourType(parameter.Type.CppTypeName, names))
            {
                return managedType;
            }
        }

        return null;
    }

    private static bool IsColourbParameter(ParameterBinding parameter) =>
        IsRmlColourType(parameter.CppTypeName, ColourbTypeNames) || IsRmlColourType(parameter.Type.CppTypeName, ColourbTypeNames);

    // Compares the type without const, references, white space and Rml:: qualifiers.
    // Thus 'const Rml::Colour<Rml::byte, 255> &' and 'Colourb' both match.
    public static bool IsRmlColourType(string? cppTypeName, IReadOnlyList<string> names)
    {
        if (string.IsNullOrWhiteSpace(cppTypeName))
        {
            return false;
        }

        var normalized = cppTypeName.Trim();
        if (normalized.StartsWith("const ", StringComparison.Ordinal))
        {
            normalized = normalized["const ".Length..];
        }

        normalized = string.Concat(normalized.Where(static value => !char.IsWhiteSpace(value) && value != '&'))
            .Replace("Rml::", string.Empty, StringComparison.Ordinal);
        if (normalized.EndsWith("const", StringComparison.Ordinal))
        {
            normalized = normalized[..^"const".Length];
        }

        return names.Contains(normalized, StringComparer.Ordinal);
    }

    /// <summary>
    /// True when <see cref="Apply"/> excludes the record silently.
    /// The silent exclusion pass uses this to hide the record before the diagnostic log.
    /// </summary>
    internal static bool IsRecordOutsideSurface(RecordBinding record)
        => IsRmlHeader(record.HeaderIncludePath) &&
            (!PublicRecordNames.Contains(record.CppQualifiedName) || ShouldIgnoreRecord(record));

    /// <summary>True when <see cref="Apply"/> excludes the enum silently.</summary>
    internal static bool IsEnumOutsideSurface(EnumBinding enumBinding)
        => IsRmlHeader(enumBinding.HeaderIncludePath) && !PublicEnumNames.Contains(enumBinding.CppQualifiedName);

    /// <summary>True when <see cref="Apply"/> excludes the function silently.</summary>
    internal static bool IsFunctionOutsideSurface(FunctionBinding function)
        => IsRmlHeader(function.HeaderIncludePath) &&
            (function.Parent is not RecordBinding owner || !PublicRecordNames.Contains(owner.CppQualifiedName));

    private static bool IsRmlHeader(string? headerIncludePath)
    {
        if (string.IsNullOrWhiteSpace(headerIncludePath))
        {
            return false;
        }

        var normalized = headerIncludePath.Replace('\\', '/');
        return normalized.StartsWith("RmlUi/", StringComparison.Ordinal) ||
            normalized.Contains("/RmlUi/", StringComparison.Ordinal);
    }

    private static int EnsureVariantStringTemplateInstantiations(BindingCompilation compilation)
    {
        var variant = compilation.Records.FirstOrDefault(static record =>
            IsRmlHeader(record.HeaderIncludePath) &&
            string.Equals(record.CppQualifiedName, "Rml::Variant", StringComparison.Ordinal));
        if (variant is null)
        {
            return 0;
        }

        var changed = 0;
        EnsureRmlStringIsEaString(compilation);
        var stringType = CreateRmlStringType(compilation, "Rml::String");
        var stringConstRefType = CreateRmlStringType(compilation, "const Rml::String&", cppIsReference: true);
        if (stringType is null || stringConstRefType is null)
        {
            return 0;
        }

        var get = variant.Members.OfType<FunctionBinding>().FirstOrDefault(static function =>
            string.Equals(function.NativeName, "Get", StringComparison.Ordinal) && function.IsGeneric);
        if (get is not null)
        {
            get.ClearIgnoreReason();
            changed += AddInstantiation(get.SupportedGenericInstantiations, stringType);
        }

        if (!variant.Members.OfType<FunctionBinding>().Any(static function =>
                string.Equals(function.ManagedName, "Assign", StringComparison.Ordinal) && function.IsGeneric))
        {
            var assign = new FunctionBinding(
                "method:Rml::Variant::operator_assign:T",
                "operator=",
                "Assign",
                BindingType.CreateHandle("Rml::Variant&", "Variant", variant.SymbolId),
                entryPointName: "scrbfx_Rml_Variant_operator_assign_generic",
                cppReturnTypeName: "Rml::Variant&",
                cppQualifiedName: "Rml::Variant::operator=")
            {
                HeaderIncludePath = variant.HeaderIncludePath,
                IsStatic = false,
                IsConstMethod = false,
                OwningRecordCppTypeName = "Rml::Variant"
            };
            assign.GenericParameters.Add(new BindingGenericParameter("T", 0));
            assign.Parameters.Add(new ParameterBinding(
                "parameter:Rml::Variant::operator_assign:T:value",
                "value",
                "value",
                BindingType.CreateGenericParameter("T"),
                cppTypeName: "T&&"));
            assign.SupportedGenericInstantiations.Add(new RecordGenericInstantiationBinding([stringConstRefType]));
            variant.Members.Add(assign);
            changed++;
        }

        return changed;
    }

    // Reference the Ea.String wrapper record so SharpCpp resolves its marshaller like any
    // parsed parameter; helper names are the renderer's business.
    // The rbfx RmlUi configuration (RmlUi/Config/RbfxConfig.h) declares Rml::String as
    // eastl::string. EnsureRmlStringIsEaString checks that the parser agrees: a different string
    // type has a different native layout, and a wrong wrapper corrupts every string.
    // A reference to a record keeps CppIsRecordValue like a parsed `const T&` parameter: the
    // handle is the object's address and crosses the ABI as is, not by reference.
    private static BindingType? CreateRmlStringType(BindingCompilation compilation, string cppTypeName, bool cppIsReference = false)
    {
        var eaString = compilation.Records.FirstOrDefault(static record =>
            record.SymbolId == EaStringRecordId && record.HasMetadata(MetadataKeys.ManagedWrapperAssembly));
        if (eaString is null)
        {
            return null;
        }

        return BindingType.CreateHandle(cppTypeName, "global::Ea.String", eaString.SymbolId, cppIsRecordValue: true)
            .WithCppIsReference(cppIsReference);
    }

    private const string EaStringRecordId = "record:eastl::basic_string";

    // The parser resolves each parsed `Rml::String` parameter to its record. Every one must be the
    // Ea.String record; otherwise generation fails instead of writing corrupting wrappers.
    private static void EnsureRmlStringIsEaString(BindingCompilation compilation)
    {
        var parameters = compilation.Functions
            .Where(static function => IsRmlHeader(function.HeaderIncludePath))
            .SelectMany(static function => function.Parameters)
            .Where(static parameter => parameter.CppTypeName is { } cppTypeName && NamesRmlString(cppTypeName))
            .ToArray();
        if (parameters.Length == 0)
        {
            throw new InvalidOperationException(
                "No parsed RmlUI parameter has the type 'Rml::String'; the Rml.Variant string instantiations cannot verify their native type.");
        }

        var mismatch = parameters.FirstOrDefault(static parameter =>
            !string.Equals(parameter.Type.RecordId, EaStringRecordId, StringComparison.Ordinal));
        if (mismatch is not null)
        {
            throw new InvalidOperationException(
                $"RmlUI parameter '{mismatch.SymbolId}' of type '{mismatch.CppTypeName}' resolves to '{mismatch.Type.CppTypeName}' " +
                $"(record '{mismatch.Type.RecordId ?? "none"}'), but the Rml.Variant string instantiations use the '{EaStringRecordId}' wrapper. " +
                "The native and managed string layouts differ.");
        }
    }

    private static bool NamesRmlString(string cppTypeName)
    {
        var name = cppTypeName.Trim();
        if (name.StartsWith("const ", StringComparison.Ordinal))
        {
            name = name["const ".Length..].TrimStart();
        }

        return string.Equals(name.TrimEnd('&', ' '), "Rml::String", StringComparison.Ordinal);
    }

    private static int AddInstantiation(IList<RecordGenericInstantiationBinding> instantiations, BindingType type)
    {
        if (instantiations.Any(instantiation => instantiation.TypeArguments.Count == 1 &&
                string.Equals(instantiation.TypeArguments[0].CppTypeName, type.CppTypeName, StringComparison.Ordinal)))
        {
            return 0;
        }

        instantiations.Add(new RecordGenericInstantiationBinding([type]));
        return 1;
    }

    private static bool ShouldIgnoreRecord(RecordBinding record)
        => record.IsGeneric ||
            record.CppQualifiedName.Contains("::detail::", StringComparison.Ordinal) ||
            record.CppQualifiedName.Contains("::Detail::", StringComparison.Ordinal);

    private static int PruneUnsupportedBases(RecordBinding record)
    {
        var removed = 0;
        for (var index = record.Bases.Count - 1; index >= 0; --index)
        {
            if (!ContainsUnsupportedRmlType(record.Bases[index].CppQualifiedName))
            {
                continue;
            }

            record.Bases.RemoveAt(index);
            removed++;
        }

        var primaryBase = record.Bases.FirstOrDefault();
        record.ManagedBaseTypeName = primaryBase is null || ContainsUnsupportedRmlType(primaryBase.CppQualifiedName)
            ? null
            : primaryBase.ManagedName;
        return removed;
    }

    private static bool FunctionUsesUnsupportedRmlType(
        FunctionBinding function,
        IReadOnlyDictionary<string, RecordBinding> recordsBySymbolId,
        IReadOnlyDictionary<string, EnumBinding> enumsBySymbolId)
    {
        return TypeUsesUnsupportedRmlType(function.ReturnType, function.CppReturnTypeName, recordsBySymbolId, enumsBySymbolId) ||
            function.Parameters.Any(parameter => TypeUsesUnsupportedRmlType(parameter.Type, parameter.CppTypeName, recordsBySymbolId, enumsBySymbolId));
    }

    private static bool TypeUsesUnsupportedRmlType(
        BindingType type,
        string cppTypeName,
        IReadOnlyDictionary<string, RecordBinding> recordsBySymbolId,
        IReadOnlyDictionary<string, EnumBinding> enumsBySymbolId)
    {
        if (ContainsUnsupportedRmlType(cppTypeName) || ContainsUnsupportedRmlType(type.CppTypeName))
        {
            return true;
        }

        if (type.RecordId is not null && recordsBySymbolId.TryGetValue(type.RecordId, out var record) && ContainsUnsupportedRmlType(record.CppQualifiedName))
        {
            return true;
        }

        if (type.EnumId is not null && enumsBySymbolId.TryGetValue(type.EnumId, out var enumBinding) && ContainsUnsupportedRmlType(enumBinding.CppQualifiedName))
        {
            return true;
        }

        return type.GenericArguments.Any(argument => TypeUsesUnsupportedRmlType(argument, argument.CppTypeName, recordsBySymbolId, enumsBySymbolId)) ||
            (type.FixedArrayElementType is not null && TypeUsesUnsupportedRmlType(type.FixedArrayElementType, type.FixedArrayElementType.CppTypeName, recordsBySymbolId, enumsBySymbolId)) ||
            (type.DynamicArrayElementType is not null && TypeUsesUnsupportedRmlType(type.DynamicArrayElementType, type.DynamicArrayElementType.CppTypeName, recordsBySymbolId, enumsBySymbolId)) ||
            (type.CallbackReturnType is not null && TypeUsesUnsupportedRmlType(type.CallbackReturnType, type.CallbackReturnType.CppTypeName, recordsBySymbolId, enumsBySymbolId)) ||
            type.CallbackParameterTypes.Any(parameter => TypeUsesUnsupportedRmlType(parameter, parameter.CppTypeName, recordsBySymbolId, enumsBySymbolId));
    }

    private static int RemoveUnsupportedGenericInstantiations(
        IList<RecordGenericInstantiationBinding> instantiations,
        IReadOnlyDictionary<string, RecordBinding> recordsBySymbolId,
        IReadOnlyDictionary<string, EnumBinding> enumsBySymbolId)
    {
        var removed = 0;
        for (var index = instantiations.Count - 1; index >= 0; --index)
        {
            if (!instantiations[index].TypeArguments.Any(type => TypeUsesUnsupportedRmlType(type, type.CppTypeName, recordsBySymbolId, enumsBySymbolId)))
            {
                continue;
            }

            instantiations.RemoveAt(index);
            removed++;
        }

        return removed;
    }

    private static int RemoveUnsupportedTypeAliases(
        IList<BindingTypeAlias> aliases,
        IReadOnlyDictionary<string, RecordBinding> recordsBySymbolId,
        IReadOnlyDictionary<string, EnumBinding> enumsBySymbolId)
    {
        var removed = 0;
        for (var index = aliases.Count - 1; index >= 0; --index)
        {
            if (!TypeUsesUnsupportedRmlType(aliases[index].TargetType, aliases[index].TargetType.CppTypeName, recordsBySymbolId, enumsBySymbolId))
            {
                continue;
            }

            aliases.RemoveAt(index);
            removed++;
        }

        return removed;
    }

    private static bool ContainsUnsupportedRmlType(string cppTypeName)
    {
        if (cppTypeName.Contains("Style::", StringComparison.Ordinal) ||
            cppTypeName.Contains("PropertyId", StringComparison.Ordinal))
        {
            return true;
        }

        var searchIndex = 0;
        while (true)
        {
            var rmlIndex = cppTypeName.IndexOf("Rml::", searchIndex, StringComparison.Ordinal);
            if (rmlIndex < 0)
            {
                return false;
            }

            var allowedName = AllowedTypeNames.FirstOrDefault(allowed => StartsWithTypeName(cppTypeName, rmlIndex, allowed));
            if (allowedName is null)
            {
                return true;
            }

            // An allowed name is a complete type spelling, so the scan continues after it.
            // Thus 'Rml::byte' in 'Rml::Colour<Rml::byte, 255>' is not checked again.
            searchIndex = rmlIndex + allowedName.Length;
        }
    }

    private static bool StartsWithTypeName(string value, int startIndex, string typeName)
    {
        if (!value.AsSpan(startIndex).StartsWith(typeName, StringComparison.Ordinal))
        {
            return false;
        }

        var nextIndex = startIndex + typeName.Length;
        return nextIndex >= value.Length || !IsIdentifierOrNamespaceChar(value[nextIndex]);
    }

    private static bool IsIdentifierOrNamespaceChar(char value)
        => char.IsLetterOrDigit(value) || value == '_' || value == ':';
}
