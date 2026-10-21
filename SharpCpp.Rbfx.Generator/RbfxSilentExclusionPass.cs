using System.Text.RegularExpressions;
using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

public static class RbfxSilentExclusionPass
{
    private static readonly HashSet<string> BlockedHeaderIncludePaths = new(StringComparer.Ordinal)
    {
        "Container/Ptr.h",
        "Container/Str.h",
        "Container/Hash.h",
        "Container/FlagSet.h",
        "Container/IndexAllocator.h",
        "Container/Functors.h",
        "Container/Allocator.h",
        "Core/Assert.h",
        "Core/Format.h",
        "Core/IteratorRange.h",
        "Core/Macros.h",
        "Core/Main.h",
        "Core/NonCopyable.h",
        "Core/TypeTrait.h",
        "Core/StringUtils.h",
    };

    private const string TypeHierarchyReason = "rbfx URHO3D_OBJECT type hierarchy array is an internal runtime type information detail";

    public static int Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var count = 0;
        foreach (var record in compilation.Records)
        {
            if (record.GetMetadata<bool>(MetadataKeys.SuppressEmission))
            {
                continue;
            }

            if (TryGetBlockedHeaderSilentExclusionReason(record, inheritFromAncestor: false, out var reason))
            {
                if (SetSilentExclusionReasonIfChanged(record, reason))
                {
                    count++;
                }
            }
            else if (TryGetSilentExclusionReason(record.CppQualifiedName, out reason))
            {
                if (IsExplicitlyConfiguredRecord(record) && IsOutsideUrho3DNamespace(record.CppQualifiedName))
                {
                    continue;
                }

                if (SetSilentExclusionReasonIfChanged(record, reason))
                {
                    count++;
                }
            }
        }

        foreach (var enumBinding in compilation.Enums)
        {
            // An external enum that an owned alias names has the namespace of the alias.
            var enumScopeName = enumBinding.ScopeCppQualifiedName;
            if (TryGetBlockedHeaderSilentExclusionReason(enumBinding, inheritFromAncestor: false, out var reason) ||
                TryGetSilentExclusionReason(enumScopeName, out reason))
            {
                if (SetSilentExclusionReasonIfChanged(enumBinding, reason))
                {
                    count++;
                }
            }
        }

        var headerBlockedTypes = CreateHeaderBlockedTypeSet(compilation);

        foreach (var function in compilation.Functions)
        {
            if (TryGetBlockedHeaderSilentExclusionReason(function, inheritFromAncestor: true, out var reason))
            {
                if (SetSilentExclusionReasonIfChanged(function, reason))
                {
                    count++;
                }
            }
            else if (ShouldSilentlyExcludeFunction(function, out reason))
            {
                if (SetSilentExclusionReasonIfChanged(function, reason))
                {
                    count++;
                }
            }
            else if (TryGetDependentFunctionIgnoreReason(function, headerBlockedTypes, out reason))
            {
                SetIgnoreReasonIfMissing(function, reason);
            }
        }

        foreach (var field in compilation.Descendants<FieldBinding>())
        {
            if (IsTypeHierarchyField(field))
            {
                if (SetSilentExclusionReasonIfChanged(field, TypeHierarchyReason))
                {
                    count++;
                }
            }
            else if (TryGetBlockedHeaderSilentExclusionReason(field, inheritFromAncestor: true, out var reason))
            {
                if (SetSilentExclusionReasonIfChanged(field, reason))
                {
                    count++;
                }
            }
            else if (TryGetDependentFieldIgnoreReason(field, headerBlockedTypes, out reason))
            {
                SetIgnoreReasonIfMissing(field, reason);
            }
        }

        DropIgnoredBases(compilation);
        PruneAliasesTargetingIgnoredTypes(compilation, headerBlockedTypes);

        return count;
    }

    private const string NoApiSurfaceMarker = "has no user-defined API surface";

    /// <summary>The first record or namespace root that a type diagnostic blames for an external type.</summary>
    private static readonly Regex ExternalTypeReasonRegex = new(
        @"(?:record '(?<record>[^']+)' is ignored|declaration namespace root '(?<root>[^']+)' is outside the configured namespace mappings)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] ThirdPartyNamespaceRoots =
    {
        "std", "pugi", "Diligent", "embree3", "CLI", "tracy"
    };

    private static readonly Regex ThirdPartyGlobalNameRegex = new(
        @"^(?:(?:bt|b2|rc|dt)[A-Z0-9]|_?SDL_|Im[A-Z])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Marks declarations in the log as silent exclusions when the reason is by design.
    /// Each rule keeps the original reason text. The generated code does not change, only the comments.
    /// 1. A declaration in a Dear ImGui or RmlUI header that the surface pass excludes later.
    /// 2. A record without a user-defined API surface.
    /// 3. A callable or field that uses a third-party or standard library type that rbfx does not bind.
    /// 4. A declaration inside a record that is a silent exclusion or has suppress_emission.
    ///    A declaration inside a mapped Rml math template, for example Rml::Vector2, is also silent.
    /// 5. The warning for a dropped base that is excluded on purpose. The base stays dropped.
    /// The pass must run before the pipeline writes the diagnostic log.
    /// eastl and Urho3D types are not third-party: they stay visible as gaps.
    /// </summary>
    public static int ApplyIntentionalGaps(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var count = 0;
        foreach (var record in compilation.Records.Where(static record => record.IsIgnored()))
        {
            if (RbfxImGuiSurfacePass.IsRecordOutsideSurface(record) ||
                RbfxRmlUiSurfacePass.IsRecordOutsideSurface(record) ||
                (record.GetIgnoreReason() ?? string.Empty).Contains(NoApiSurfaceMarker, StringComparison.Ordinal))
            {
                count += MarkSilent(record);
            }
        }

        foreach (var enumBinding in compilation.Enums.Where(static enumBinding => enumBinding.IsIgnored()))
        {
            if (RbfxRmlUiSurfacePass.IsEnumOutsideSurface(enumBinding))
            {
                count += MarkSilent(enumBinding);
            }
        }

        foreach (var function in compilation.Functions.Where(static function => function.IsIgnored()))
        {
            if (RbfxImGuiSurfacePass.IsFunctionOutsideSurface(function) ||
                RbfxRmlUiSurfacePass.IsFunctionOutsideSurface(function) ||
                IsBlamedOnThirdPartyType(function.GetIgnoreReason()))
            {
                count += MarkSilent(function);
            }
        }

        foreach (var field in compilation.Descendants<FieldBinding>().Where(static field => field.IsIgnored()))
        {
            if (IsBlamedOnThirdPartyType(field.GetIgnoreReason()))
            {
                count += MarkSilent(field);
            }
        }

        foreach (var record in compilation.Records.Where(static record => record.IsSilentlyExcluded()))
        {
            foreach (var member in record.Descendants<BindingNode>().Where(static member => member.IsIgnored()))
            {
                count += MarkSilent(member);
            }
        }

        // A configured managed type replaces a record with suppress_emission, for example ImVec2.
        // The record has no wrapper class, so its members have no managed surface.
        // The marshallers of the Rml math templates are specializations of concrete instantiations.
        // Thus the generic template record has no suppress_emission, but it also has no wrapper class.
        foreach (var record in compilation.Records.Where(static record =>
                     record.GetMetadata<bool>(MetadataKeys.SuppressEmission) ||
                     RbfxRmlUiSurfacePass.MappedMathTemplateNames.Contains(record.CppQualifiedName)))
        {
            foreach (var member in record.Descendants<BindingNode>().Where(static member => member.IsIgnored()))
            {
                count += MarkSilent(member);
            }
        }

        RemoveIntentionalBaseDropWarnings(compilation);
        return count;
    }

    private static readonly Regex DroppedBaseWarningRegex = new(
        @"^dropping base '(?<base>[^']+)' from record '[^']+' because",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The core pipeline warns when it drops an unavailable base, before rbfx marks that base as excluded.
    /// This removes the warning when the drop is by design. The base is dropped in both cases.
    /// By design means: a silently excluded base, a class template instantiation,
    /// or a nested type of a class template base that an earlier drop already removed.
    /// </summary>
    private static void RemoveIntentionalBaseDropWarnings(BindingCompilation compilation)
    {
        var recordsByName = new Dictionary<string, RecordBinding>(StringComparer.Ordinal);
        foreach (var candidate in compilation.Records)
        {
            recordsByName.TryAdd(candidate.CppQualifiedName, candidate);
        }

        var templateBaseOwners = new List<RecordBinding>();
        foreach (var record in compilation.Records)
        {
            if (GetDroppedBaseNames(record).Any(static name => name.Contains('<', StringComparison.Ordinal)))
            {
                templateBaseOwners.Add(record);
            }
        }

        foreach (var record in compilation.Records)
        {
            var warnings = record.GetMetadata<List<string>>(MetadataKeys.Warnings);
            if (warnings is null)
            {
                continue;
            }

            warnings.RemoveAll(warning =>
            {
                var match = DroppedBaseWarningRegex.Match(warning);
                return match.Success && IsIntentionalDroppedBaseName(match.Groups["base"].Value, recordsByName, templateBaseOwners);
            });
            if (warnings.Count == 0)
            {
                record.RemoveMetadata(MetadataKeys.Warnings);
            }
        }
    }

    private static IEnumerable<string> GetDroppedBaseNames(RecordBinding record)
    {
        foreach (var warning in record.GetWarnings())
        {
            var match = DroppedBaseWarningRegex.Match(warning);
            if (match.Success)
            {
                yield return match.Groups["base"].Value;
            }
        }
    }

    private static bool IsIntentionalDroppedBaseName(
        string baseName,
        IReadOnlyDictionary<string, RecordBinding> recordsByName,
        IReadOnlyList<RecordBinding> templateBaseOwners)
    {
        if (baseName.Contains('<', StringComparison.Ordinal))
        {
            return true;
        }

        var lookupName = baseName.StartsWith("ea::", StringComparison.Ordinal) ? "eastl::" + baseName["ea::".Length..] : baseName;
        if (recordsByName.TryGetValue(lookupName, out var baseRecord))
        {
            return baseRecord.IsSilentlyExcluded() ||
                (baseRecord.GetIgnoreReason() ?? string.Empty).Contains(NoApiSurfaceMarker, StringComparison.Ordinal);
        }

        var scopeEnd = baseName.LastIndexOf("::", StringComparison.Ordinal);
        if (scopeEnd <= 0)
        {
            return false;
        }

        var qualifier = baseName[..scopeEnd];
        return templateBaseOwners.Any(owner =>
            string.Equals(owner.CppQualifiedName, qualifier, StringComparison.Ordinal) ||
            owner.CppQualifiedName.EndsWith("::" + qualifier, StringComparison.Ordinal));
    }

    private static int MarkSilent(BindingNode node)
    {
        if (node.IsSilentlyExcluded())
        {
            return 0;
        }

        node.SetSilentExclusionReason(node.GetIgnoreReason()!, overwriteExisting: true);
        return 1;
    }

    private static bool IsBlamedOnThirdPartyType(string? reason)
    {
        if (string.IsNullOrEmpty(reason) ||
            !(reason.Contains("is defined outside headers owned by this compilation", StringComparison.Ordinal) ||
                reason.Contains("is outside the configured namespace mappings", StringComparison.Ordinal)))
        {
            return false;
        }

        var match = ExternalTypeReasonRegex.Match(reason);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups["record"].Success ? match.Groups["record"].Value : match.Groups["root"].Value;
        return IsThirdPartyTypeName(name);
    }

    internal static bool IsThirdPartyTypeName(string name)
    {
        var template = name.IndexOf('<', StringComparison.Ordinal);
        var plain = template < 0 ? name : name[..template];
        var scope = plain.IndexOf("::", StringComparison.Ordinal);
        var root = scope < 0 ? plain : plain[..scope];
        if (ThirdPartyNamespaceRoots.Contains(root, StringComparer.Ordinal))
        {
            return true;
        }

        return ThirdPartyGlobalNameRegex.IsMatch(root);
    }

    /// <summary>
    /// Marks the FlagSet class template and its ignored members as silent exclusions.
    /// <see cref="RbfxFlagSetLoweringPass"/> lowers FlagSet&lt;TEnum&gt; to the enum, so these members have no managed surface.
    /// The pass must run before the pipeline writes the diagnostic log.
    /// Declarations in other headers that use FlagSet keep their own reason.
    /// </summary>
    public static int ApplyFlagSetTemplate(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        const string reason = "rbfx FlagSet<TEnum> is lowered to the enum managed surface";
        var count = 0;
        foreach (var record in compilation.Records.Where(static record => string.Equals(record.NativeName, "FlagSet", StringComparison.Ordinal)))
        {
            foreach (var node in record.DescendantsAndSelf<BindingNode>().Where(static node => node.IsIgnored()))
            {
                if (SetSilentExclusionReasonIfChanged(node, reason))
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// The static array that each URHO3D_OBJECT class declares. The managed type information replaces it.
    /// The owner record must also have the static GetTypeInfoStatic function that the same macro declares.
    /// </summary>
    private static bool IsTypeHierarchyField(FieldBinding field)
    {
        return field.IsStatic &&
            field.Type.IsFixedArray &&
            string.Equals(field.NativeName, "TypeHierarchy", StringComparison.Ordinal) &&
            field.Parent is RecordBinding owner &&
            owner.Members.OfType<FunctionBinding>().Any(static function =>
                function.IsStatic && string.Equals(function.NativeName, "GetTypeInfoStatic", StringComparison.Ordinal));
    }

    private static bool SetSilentExclusionReasonIfChanged(BindingNode node, string reason)
    {
        if (node.IsSilentlyExcluded() && string.Equals(node.GetIgnoreReason(), reason, StringComparison.Ordinal))
        {
            return false;
        }

        node.SetSilentExclusionReason(reason, overwriteExisting: true);
        return true;
    }

    private static bool SetIgnoreReasonIfMissing(BindingNode node, string reason)
    {
        if (node.IsIgnored())
        {
            return false;
        }

        node.SetIgnoreReason(reason, overwriteExisting: false);
        return true;
    }

    private sealed class HeaderBlockedTypeSet
    {
        public HashSet<string> RecordIds { get; } = new(StringComparer.Ordinal);

        public HashSet<string> EnumIds { get; } = new(StringComparer.Ordinal);

        public HashSet<string> CppTypeNames { get; } = new(StringComparer.Ordinal);
    }

    private static HeaderBlockedTypeSet CreateHeaderBlockedTypeSet(BindingCompilation compilation)
    {
        var result = new HeaderBlockedTypeSet();
        foreach (var record in compilation.Records.Where(static record => IsDefinedInBlockedHeader(record, inheritFromAncestor: false)))
        {
            result.RecordIds.Add(record.SymbolId);
            AddBlockedCppTypeName(result, record.CppQualifiedName);
            AddBlockedCppTypeName(result, record.NativeName);
        }

        foreach (var enumBinding in compilation.Enums.Where(static enumBinding => IsDefinedInBlockedHeader(enumBinding, inheritFromAncestor: false)))
        {
            result.EnumIds.Add(enumBinding.SymbolId);
            AddBlockedCppTypeName(result, enumBinding.CppQualifiedName);
            AddBlockedCppTypeName(result, enumBinding.NativeName);
        }

        return result;
    }

    private static void AddBlockedCppTypeName(HeaderBlockedTypeSet set, string cppTypeName)
    {
        var normalized = NormalizeReferencedCppTypeName(cppTypeName);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            set.CppTypeNames.Add(normalized);
        }
    }

    private static bool TryGetBlockedHeaderSilentExclusionReason(BindingNode node, bool inheritFromAncestor, out string reason)
    {
        var headerIncludePath = ResolveEffectiveHeaderIncludePath(node, inheritFromAncestor);
        var canonicalHeader = CanonicalizeIncludePath(headerIncludePath);
        if (canonicalHeader is not null && BlockedHeaderIncludePaths.Contains(canonicalHeader))
        {
            reason = $"declaration is defined in rbfx SDK header '{canonicalHeader}' that is provided by custom marshaller/runtime support";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool IsDefinedInBlockedHeader(BindingNode node, bool inheritFromAncestor)
    {
        var canonicalHeader = CanonicalizeIncludePath(ResolveEffectiveHeaderIncludePath(node, inheritFromAncestor));
        return canonicalHeader is not null && BlockedHeaderIncludePaths.Contains(canonicalHeader);
    }

    private static string? ResolveEffectiveHeaderIncludePath(BindingNode node, bool inheritFromAncestor)
    {
        if (!string.IsNullOrWhiteSpace(node.HeaderIncludePath))
        {
            return node.HeaderIncludePath;
        }

        if (!inheritFromAncestor)
        {
            return null;
        }

        foreach (var ancestor in node.Ancestors())
        {
            if (!string.IsNullOrWhiteSpace(ancestor.HeaderIncludePath))
            {
                return ancestor.HeaderIncludePath;
            }
        }

        return null;
    }

    private static string? CanonicalizeIncludePath(string? includePath)
    {
        if (string.IsNullOrWhiteSpace(includePath))
        {
            return null;
        }

        var path = includePath.Replace('\\', '/').Trim();
        while (path.StartsWith("./", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        var urho3DSegmentIndex = path.LastIndexOf("/Urho3D/", StringComparison.Ordinal);
        if (urho3DSegmentIndex >= 0)
        {
            path = path[(urho3DSegmentIndex + "/Urho3D/".Length)..];
        }

        const string urho3DPrefix = "Urho3D/";
        if (path.StartsWith(urho3DPrefix, StringComparison.Ordinal))
        {
            path = path[urho3DPrefix.Length..];
        }

        return path;
    }

    private static void DropIgnoredBases(BindingCompilation compilation)
    {
        var recordsById = compilation.Records
            .GroupBy(static record => record.SymbolId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        foreach (var record in compilation.Records)
        {
            if (record.IsIgnored())
            {
                continue;
            }

            foreach (var baseBinding in record.Bases.ToArray())
            {
                if (!recordsById.TryGetValue(baseBinding.RecordId, out var baseRecord) ||
                    !baseRecord.IsIgnored())
                {
                    continue;
                }

                if (!IsIntentionalBaseDrop(baseBinding, baseRecord))
                {
                    record.SetWarning($"dropping base '{baseBinding.CppQualifiedName}' from record '{record.CppQualifiedName}' because the base record is not available for a coherent managed hierarchy (base record ignored: {baseRecord.GetIgnoreReason()}); the derived record will still be generated without that base");
                }

                record.Bases.Remove(baseBinding);

                if (string.Equals(record.ManagedBaseTypeName, baseBinding.ManagedName, StringComparison.Ordinal))
                {
                    record.ManagedBaseTypeName = PickRemainingManagedBaseTypeName(record, recordsById);
                }

                foreach (var projectedMethod in record.Members.OfType<FunctionBinding>())
                {
                    if (projectedMethod.SymbolId.StartsWith("projected-secondary:", StringComparison.Ordinal) &&
                        string.Equals(projectedMethod.NativeDispatchRecordCppTypeName, baseBinding.CppQualifiedName, StringComparison.Ordinal))
                    {
                        var reason = $"projected inherited member '{projectedMethod.CppQualifiedName}' is sourced from ignored rbfx implementation base '{baseRecord.CppQualifiedName}'";
                        SetIgnoreReasonIfMissing(projectedMethod, reason);
                    }
                }
            }
        }
    }

    /// <summary>
    /// A base that rbfx excludes on purpose or that has no concrete record is dropped without a warning.
    /// Examples are NonCopyable, third-party bases and class template bases.
    /// A base that is ignored for any other reason keeps the warning, because it can be a real gap.
    /// </summary>
    internal static bool IsIntentionalBaseDrop(RecordBaseBinding baseBinding, RecordBinding baseRecord)
    {
        if (baseRecord.IsSilentlyExcluded() || baseBinding.CppQualifiedName.Contains('<', StringComparison.Ordinal))
        {
            return true;
        }

        var reason = baseRecord.GetIgnoreReason() ?? string.Empty;
        return reason.StartsWith("generic record ", StringComparison.Ordinal) ||
            reason.Contains(NoApiSurfaceMarker, StringComparison.Ordinal) ||
            reason.Contains("cannot be expressed as a C# generic", StringComparison.Ordinal);
    }

    private static string? PickRemainingManagedBaseTypeName(RecordBinding record, IReadOnlyDictionary<string, RecordBinding> recordsById)
    {
        foreach (var remainingBase in record.Bases)
        {
            if (!remainingBase.IsVirtual &&
                recordsById.TryGetValue(remainingBase.RecordId, out var remainingBaseRecord) &&
                !remainingBaseRecord.IsIgnored())
            {
                return remainingBase.ManagedName;
            }
        }

        return null;
    }

    private static void PruneAliasesTargetingIgnoredTypes(BindingCompilation compilation, HeaderBlockedTypeSet headerBlockedTypes)
    {
        foreach (var alias in compilation.TypeAliases.ToArray())
        {
            if (ReferencesIgnoredType(alias.TargetType, headerBlockedTypes))
            {
                compilation.TypeAliases.Remove(alias);
            }
        }
    }

    private static bool ReferencesIgnoredType(BindingType type, HeaderBlockedTypeSet headerBlockedTypes)
    {
        return (type.RecordId is not null && headerBlockedTypes.RecordIds.Contains(type.RecordId)) ||
            (type.EnumId is not null && headerBlockedTypes.EnumIds.Contains(type.EnumId)) ||
            headerBlockedTypes.CppTypeNames.Contains(NormalizeReferencedCppTypeName(type.CppTypeName)) ||
            type.GenericArguments.Any(argument => ReferencesIgnoredType(argument, headerBlockedTypes));
    }

    private static string NormalizeReferencedCppTypeName(string cppTypeName)
    {
        cppTypeName = NormalizeCppTypeName(cppTypeName);
        foreach (var prefix in new[] { "class ", "struct ", "enum " })
        {
            while (cppTypeName.StartsWith(prefix, StringComparison.Ordinal))
            {
                cppTypeName = cppTypeName[prefix.Length..].TrimStart();
            }
        }

        return cppTypeName;
    }

    private static bool IsExplicitlyConfiguredRecord(RecordBinding record)
    {
        return !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) ||
            !string.IsNullOrWhiteSpace(record.ManagedTypeFormat) ||
            !string.IsNullOrWhiteSpace(record.HandleMarshallerTypeName) ||
            record.Members.OfType<FunctionBinding>().Any(static member => member.HasMetadata(MetadataKeys.MarshallerExposedMethod));
    }

    private static bool IsOutsideUrho3DNamespace(string cppQualifiedName)
    {
        var parts = cppQualifiedName.Split("::", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length < 2 || !string.Equals(parts[0], "Urho3D", StringComparison.Ordinal);
    }

    private static bool ShouldSilentlyExcludeFunction(FunctionBinding function, out string reason)
    {
        if (TryGetFlagSetOperatorSilentExclusionReason(function, out reason))
        {
            return true;
        }

        if (function.Parent is RecordBinding parentRecord)
        {
            return TryGetSmartPointerSilentExclusionReason(parentRecord.NativeName, function.CppQualifiedName, out reason) ||
                TryGetSmartPointerSilentExclusionReason(parentRecord.CppQualifiedName, function.CppQualifiedName, out reason) ||
                TryGetSmartPointerDeductionGuideSilentExclusionReason(function, out reason);
        }

        if (TryGetSilentExclusionReason(function.CppQualifiedName, out reason) ||
            TryGetSmartPointerSilentExclusionReason(function.NativeName, function.CppQualifiedName, out reason) ||
            TryGetSmartPointerDeductionGuideSilentExclusionReason(function, out reason))
        {
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool TryGetDependentFunctionIgnoreReason(FunctionBinding function, HeaderBlockedTypeSet headerBlockedTypes, out string reason)
    {
        if (ReferencesIgnoredType(function.ReturnType, headerBlockedTypes) ||
            function.Parameters.Any(parameter => ReferencesIgnoredType(parameter.Type, headerBlockedTypes)))
        {
            reason = $"function signature references ignored rbfx implementation type: '{function.CppQualifiedName}'";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool TryGetDependentFieldIgnoreReason(FieldBinding field, HeaderBlockedTypeSet headerBlockedTypes, out string reason)
    {
        if (ReferencesIgnoredType(field.Type, headerBlockedTypes))
        {
            reason = $"field type references ignored rbfx implementation type: '{field.ManagedName}'";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool TryGetSmartPointerDeductionGuideSilentExclusionReason(FunctionBinding function, out string reason)
    {
        var declarationText = function.GetMetadata<string>(MetadataKeys.DeclarationDisplayText) ?? string.Empty;
        if ((declarationText.Contains(" SharedPtr(", StringComparison.Ordinal) && declarationText.Contains(" -> SharedPtr<", StringComparison.Ordinal)) ||
            (declarationText.Contains(" WeakPtr(", StringComparison.Ordinal) && declarationText.Contains(" -> WeakPtr<", StringComparison.Ordinal)))
        {
            reason = $"rbfx smart pointer deduction guide '{function.CppQualifiedName}' is provided by custom marshaller support";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool TryGetSilentExclusionReason(string cppQualifiedName, out string reason)
    {
        if (string.IsNullOrWhiteSpace(cppQualifiedName))
        {
            reason = string.Empty;
            return false;
        }

        var parts = cppQualifiedName.Split("::", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            reason = $"declaration '{cppQualifiedName}' is outside the Urho3D namespace";
            return true;
        }

        if (!string.Equals(parts[0], "Urho3D", StringComparison.Ordinal))
        {
            reason = $"declaration namespace root '{parts[0]}' is outside the Urho3D namespace";
            return true;
        }

        if (TryGetSmartPointerSilentExclusionReason(parts[^1], cppQualifiedName, out reason))
        {
            return true;
        }

        if (parts.Skip(1).Take(parts.Length - 2).Any(static part =>
                string.Equals(part, "detail", StringComparison.Ordinal) ||
                string.Equals(part, "Detail", StringComparison.Ordinal)))
        {
            reason = $"declaration is in an internal rbfx detail namespace: '{cppQualifiedName}'";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool TryGetSmartPointerSilentExclusionReason(string name, string cppQualifiedName, out string reason)
    {
        if (IsSmartPointerWrapperName(name))
        {
            reason = $"rbfx smart pointer wrapper '{cppQualifiedName}' is provided by custom marshaller support";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool IsSmartPointerWrapperName(string name)
    {
        var namespaceDelimiterIndex = name.LastIndexOf("::", StringComparison.Ordinal);
        if (namespaceDelimiterIndex >= 0)
        {
            name = name[(namespaceDelimiterIndex + 2)..];
        }

        var genericDelimiterIndex = name.IndexOf('<', StringComparison.Ordinal);
        if (genericDelimiterIndex >= 0)
        {
            name = name[..genericDelimiterIndex];
        }

        return string.Equals(name, "SharedPtr", StringComparison.Ordinal) ||
            string.Equals(name, "WeakPtr", StringComparison.Ordinal);
    }

    private static bool TryGetFlagSetOperatorSilentExclusionReason(FunctionBinding function, out string reason)
    {
        var declarationText = function.GetMetadata<string>(MetadataKeys.DeclarationDisplayText) ?? string.Empty;
        if (!IsFlagSetOperator(function, declarationText))
        {
            reason = string.Empty;
            return false;
        }

        if (TryGetFlagSetArgument(declarationText, out _))
        {
            reason = RbfxFlagSetLoweringPass.GetFlagSetOperatorReason(function);
            return true;
        }

        if (!TryGetFlagSetArgument(function.ReturnType.CppTypeName, out var enumType))
        {
            reason = string.Empty;
            return false;
        }

        foreach (var parameter in function.Parameters)
        {
            if (!IsSameFlagSetFamilyType(parameter.CppTypeName, enumType) &&
                !IsSameFlagSetFamilyType(parameter.Type.CppTypeName, enumType))
            {
                reason = string.Empty;
                return false;
            }
        }

        reason = RbfxFlagSetLoweringPass.GetFlagSetOperatorReason(function);
        return true;
    }

    private static bool IsFlagSetOperator(FunctionBinding function, string declarationText)
    {
        var token = function.OperatorToken;
        if (string.IsNullOrWhiteSpace(token) && function.NativeName.StartsWith("operator", StringComparison.Ordinal))
        {
            token = function.NativeName["operator".Length..];
        }

        return token is "&" or "^" or "|" or "~" ||
            declarationText.Contains(" operator&(", StringComparison.Ordinal) ||
            declarationText.Contains(" operator^(", StringComparison.Ordinal) ||
            declarationText.Contains(" operator|(", StringComparison.Ordinal) ||
            declarationText.Contains(" operator~(", StringComparison.Ordinal);
    }

    private static bool IsSameFlagSetFamilyType(string cppTypeName, string enumType)
    {
        return TryGetFlagSetArgument(cppTypeName, out var flagSetArgument)
            ? string.Equals(flagSetArgument, enumType, StringComparison.Ordinal)
            : string.Equals(NormalizeCppTypeName(cppTypeName), enumType, StringComparison.Ordinal);
    }

    private static bool TryGetFlagSetArgument(string cppTypeName, out string enumType)
    {
        cppTypeName = NormalizeCppTypeName(cppTypeName);
        var prefixIndex = cppTypeName.IndexOf("FlagSet<", StringComparison.Ordinal);
        if (prefixIndex < 0)
        {
            enumType = string.Empty;
            return false;
        }

        var argumentStart = prefixIndex + "FlagSet<".Length;
        var argumentEnd = cppTypeName.IndexOf('>', argumentStart);
        if (argumentEnd < 0)
        {
            enumType = string.Empty;
            return false;
        }

        enumType = NormalizeCppTypeName(cppTypeName[argumentStart..argumentEnd]);
        return enumType.Length > 0;
    }

    private static string NormalizeCppTypeName(string cppTypeName)
    {
        cppTypeName = cppTypeName.Trim();
        while (cppTypeName.StartsWith("const ", StringComparison.Ordinal))
        {
            cppTypeName = cppTypeName["const ".Length..].TrimStart();
        }

        return cppTypeName.TrimEnd('&', '*').Trim();
    }
}
