using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

/// <summary>
/// Lowers FlagSet&lt;TEnum&gt; to the enum in signatures, fields and aliases.
/// The pass runs before the pipeline validates the surface.
/// The validation then checks each lowered declaration as a plain enum declaration.
/// A declaration stays ignored only when another type in it is not supported.
/// FlagSet&lt;TEnum&gt; has one member of the underlying integer type of the enum.
/// So the enum has the same size as the FlagSet and the native glue converts it by value.
/// The pass lowers only a by-value FlagSet, a const reference parameter and a by-value return.
/// A pointer or a non-const reference to a FlagSet keeps the FlagSet type, so the validation reports it.
/// </summary>
public static class RbfxFlagSetLoweringPass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        for (var i = 0; i < compilation.TypeAliases.Count; i++)
        {
            var alias = compilation.TypeAliases[i];
            if (TryLowerFlagSetType(compilation, alias.TargetType, out var enumType, out _))
            {
                compilation.TypeAliases[i] = alias with { TargetType = enumType };
            }
        }

        foreach (var function in compilation.Functions)
        {
            var returnedFlagSet = false;
            if (IsLowerableSpelling(function.CppReturnTypeName, allowConstReference: false) &&
                TryLowerFlagSetType(compilation, function.ReturnType, out var returnType, out _))
            {
                function.ReturnType = returnType;
                returnedFlagSet = true;
            }

            foreach (var parameter in function.Parameters)
            {
                if (parameter.Direction == BindingParameterDirection.In &&
                    parameter.AbiKind == BindingParameterAbiKind.Value &&
                    IsLowerableSpelling(parameter.CppTypeName, allowConstReference: true) &&
                    TryLowerFlagSetType(compilation, parameter.Type, out var parameterType, out var parameterEnum))
                {
                    parameter.Type = parameterType;
                    LowerDefaultValue(parameter, parameterEnum, parameterType.ManagedTypeName);
                }
            }

            if (returnedFlagSet && IsFlagEnumOperator(function))
            {
                // The C# [Flags] enum has these operators already.
                function.SetSilentExclusionReason(GetFlagSetOperatorReason(function), overwriteExisting: true);
            }
        }

        foreach (var field in compilation.Records.SelectMany(static record => record.Members.OfType<FieldBinding>()))
        {
            if (IsLowerableSpelling(field.CppTypeName, allowConstReference: false) &&
                TryLowerFlagSetType(compilation, field.Type, out var fieldType, out _))
            {
                field.Type = fieldType;
            }
        }

        foreach (var record in compilation.Records.Where(static record => string.Equals(record.NativeName, "FlagSet", StringComparison.Ordinal)))
        {
            record.SetMetadata(MetadataKeys.SuppressEmission, true);
            record.SetIgnoreReason("rbfx FlagSet<TEnum> is lowered to the enum managed surface", overwriteExisting: true);
        }

        for (var i = compilation.TypeAliases.Count - 1; i >= 0; i--)
        {
            if (TargetsUnavailableIgnoredRecord(compilation, compilation.TypeAliases[i].TargetType))
            {
                compilation.TypeAliases.RemoveAt(i);
            }
        }

        compilation.InvalidateCaches();
    }

    private static bool TryLowerFlagSetType(BindingCompilation compilation, BindingType type, out BindingType enumType, out EnumBinding enumBinding)
    {
        enumType = BindingType.Void;
        enumBinding = null!;
        if (!IsFlagSetType(compilation, type) || type.GenericArguments.Count != 1)
        {
            return false;
        }

        var enumArgument = type.GenericArguments[0];
        if (enumArgument.Kind != BindingTypeKind.Enum || !TryFindEnum(compilation, enumArgument, out enumBinding))
        {
            return false;
        }

        enumBinding.IsFlags = true;
        enumType = BindingType.CreateEnum(enumArgument.CppTypeName, GetQualifiedEnumManagedName(compilation, enumBinding), enumId: enumBinding.SymbolId);
        return true;
    }

    /// <summary>
    /// Checks the C++ spelling after the template arguments.
    /// A pointer and an rvalue reference are not lowered. A reference is lowered only when it is const.
    /// </summary>
    internal static bool IsLowerableSpelling(string? cppTypeName, bool allowConstReference)
    {
        if (string.IsNullOrWhiteSpace(cppTypeName))
        {
            return true;
        }

        var tail = cppTypeName[(cppTypeName.LastIndexOf('>') + 1)..];
        if (tail.Contains('*') || tail.Contains("&&", StringComparison.Ordinal))
        {
            return false;
        }

        if (!tail.Contains('&'))
        {
            return true;
        }

        return allowConstReference &&
            (tail.Contains("const", StringComparison.Ordinal) ||
             cppTypeName.TrimStart().StartsWith("const ", StringComparison.Ordinal));
    }

    /// <summary>
    /// The parser has no managed default for a FlagSet parameter, so it marks the default as unexpressible.
    /// This method sets the managed default for the enum when the native default is convertible.
    /// Otherwise the parameter keeps the unexpressible mark and the native default.
    /// The renderer then makes the parameter nullable or adds an overload without it.
    /// </summary>
    private static void LowerDefaultValue(ParameterBinding parameter, EnumBinding enumBinding, string managedTypeName)
    {
        if (!parameter.TryGetMetadata<string>(MetadataKeys.NativeDefaultValueExpression, out var nativeDefault))
        {
            return;
        }

        if (TryConvertEnumDefault(nativeDefault, enumBinding, managedTypeName, out var managedDefault))
        {
            parameter.ManagedDefaultValueExpression = managedDefault;
            parameter.RemoveMetadata(MetadataKeys.UnexpressibleDefault);
            return;
        }

        parameter.ManagedDefaultValueExpression = null;
        parameter.SetMetadata(MetadataKeys.UnexpressibleDefault, true);
    }

    /// <summary>
    /// Converts the native default of a FlagSet parameter to a C# constant of the enum.
    /// It accepts an empty brace list, 0, an enum constant and a bitwise or of these.
    /// </summary>
    internal static bool TryConvertEnumDefault(string? nativeDefault, EnumBinding enumBinding, string managedTypeName, out string managedDefault)
    {
        managedDefault = string.Empty;
        var expression = StripOuterGrouping(nativeDefault);
        if (expression.Length == 0)
        {
            managedDefault = "default";
            return true;
        }

        var terms = new List<string>();
        foreach (var rawTerm in SplitTopLevelOr(expression))
        {
            var term = StripOuterGrouping(rawTerm);
            if (term.TrimEnd('u', 'U') == "0")
            {
                continue;
            }

            var separator = term.LastIndexOf("::", StringComparison.Ordinal);
            var name = (separator >= 0 ? term[(separator + 2)..] : term).Trim();
            if (name.Length == 0 || !name.All(static c => char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }

            var value = enumBinding.Values.FirstOrDefault(candidate => string.Equals(candidate.NativeName, name, StringComparison.Ordinal));
            if (value is null)
            {
                return false;
            }

            terms.Add($"{managedTypeName}.{value.ManagedName}");
        }

        managedDefault = terms.Count == 0 ? "default" : string.Join(" | ", terms);
        return true;
    }

    private static IEnumerable<string> SplitTopLevelOr(string expression)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < expression.Length; i++)
        {
            switch (expression[i])
            {
                case '(' or '{':
                    depth++;
                    break;
                case ')' or '}':
                    depth--;
                    break;
                case '|' when depth == 0:
                    yield return expression[start..i];
                    start = i + 1;
                    break;
            }
        }

        yield return expression[start..];
    }

    private static string StripOuterGrouping(string? expression)
    {
        var result = (expression ?? string.Empty).Trim();
        while (result.Length >= 2 &&
            ((result[0] == '(' && result[^1] == ')') || (result[0] == '{' && result[^1] == '}')) &&
            EncloseWholeExpression(result))
        {
            result = result[1..^1].Trim();
        }

        return result;
    }

    private static bool EncloseWholeExpression(string expression)
    {
        var depth = 0;
        for (var i = 0; i < expression.Length; i++)
        {
            if (expression[i] is '(' or '{')
            {
                depth++;
            }
            else if (expression[i] is ')' or '}')
            {
                depth--;
                if (depth == 0 && i < expression.Length - 1)
                {
                    return false;
                }
            }
        }

        return depth == 0;
    }

    internal static string GetFlagSetOperatorReason(FunctionBinding function)
    {
        return $"rbfx FlagSet operator '{function.CppQualifiedName}' is lowered to the enum managed surface";
    }

    /// <summary>
    /// Returns true for the free |, &amp;, ^ and ~ operators that URHO3D_FLAGSET declares for a flag enum.
    /// The function must have the lowered enum in its return type and in all its parameters.
    /// </summary>
    private static bool IsFlagEnumOperator(FunctionBinding function)
    {
        if (function.Parent is RecordBinding ||
            function.ReturnType.Kind != BindingTypeKind.Enum ||
            function.Parameters.Count is < 1 or > 2)
        {
            return false;
        }

        var token = function.OperatorToken;
        if (string.IsNullOrWhiteSpace(token) && function.NativeName.StartsWith("operator", StringComparison.Ordinal))
        {
            token = function.NativeName["operator".Length..];
        }

        if (token is not ("|" or "&" or "^" or "~"))
        {
            return false;
        }

        return function.Parameters.All(parameter =>
            parameter.Type.Kind == BindingTypeKind.Enum &&
            string.Equals(parameter.Type.EnumId, function.ReturnType.EnumId, StringComparison.Ordinal));
    }

    private static bool IsFlagSetType(BindingCompilation compilation, BindingType type)
    {
        if (type.Kind != BindingTypeKind.Handle || string.IsNullOrWhiteSpace(type.RecordId))
        {
            return false;
        }

        return compilation.Records.Any(record =>
            string.Equals(record.SymbolId, type.RecordId, StringComparison.Ordinal) &&
            string.Equals(record.NativeName, "FlagSet", StringComparison.Ordinal));
    }

    private static bool TryFindEnum(BindingCompilation compilation, BindingType enumType, out EnumBinding enumBinding)
    {
        enumBinding = compilation.FindEnumBySymbolId(enumType.EnumId)!;
        return enumBinding is not null;
    }

    private static string GetQualifiedEnumManagedName(BindingCompilation compilation, EnumBinding enumBinding)
    {
        return $"global::{enumBinding.ManagedNamespace ?? compilation.ManagedNamespace}.{enumBinding.ManagedName}";
    }

    private static bool TargetsUnavailableIgnoredRecord(BindingCompilation compilation, BindingType type)
    {
        if (type.Kind == BindingTypeKind.Handle && !string.IsNullOrWhiteSpace(type.RecordId))
        {
            var records = compilation.Records
                .Where(record => string.Equals(record.SymbolId, type.RecordId, StringComparison.Ordinal))
                .ToArray();
            if (records.Length == 0 || records.All(IsUnavailableIgnoredRecord))
            {
                return true;
            }
        }

        return type.GenericArguments.Any(argument => TargetsUnavailableIgnoredRecord(compilation, argument));
    }

    private static bool IsUnavailableIgnoredRecord(RecordBinding record)
    {
        if (!record.IsIgnored() || record.GetMetadata<bool>(MetadataKeys.SuppressEmission))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) ||
            !string.IsNullOrWhiteSpace(record.HandleMarshallerTypeName))
        {
            return false;
        }

        return true;
    }
}
