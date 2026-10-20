using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

/// <summary>
/// Lowers FlagSet&lt;TEnum&gt; to the enum in signatures, fields and aliases.
/// The pass runs before the pipeline validates the surface.
/// The validation then checks each lowered declaration as a plain enum declaration.
/// A declaration stays ignored only when another type in it is not supported.
/// FlagSet&lt;TEnum&gt; has one member of the underlying integer type of the enum.
/// So the enum has the same size as the FlagSet and the native glue converts it by value.
/// </summary>
public static class RbfxFlagSetLoweringPass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        for (var i = 0; i < compilation.TypeAliases.Count; i++)
        {
            var alias = compilation.TypeAliases[i];
            if (TryLowerFlagSetType(compilation, alias.TargetType, out var enumType))
            {
                compilation.TypeAliases[i] = alias with { TargetType = enumType };
            }
        }

        foreach (var function in compilation.Functions)
        {
            var returnedFlagSet = false;
            if (TryLowerFlagSetType(compilation, function.ReturnType, out var returnType))
            {
                function.ReturnType = returnType;
                returnedFlagSet = true;
            }

            foreach (var parameter in function.Parameters)
            {
                if (TryLowerFlagSetType(compilation, parameter.Type, out var parameterType))
                {
                    parameter.Type = parameterType;
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
            if (TryLowerFlagSetType(compilation, field.Type, out var fieldType))
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

    private static bool TryLowerFlagSetType(BindingCompilation compilation, BindingType type, out BindingType enumType)
    {
        enumType = BindingType.Void;
        if (!IsFlagSetType(compilation, type) || type.GenericArguments.Count != 1)
        {
            return false;
        }

        var enumArgument = type.GenericArguments[0];
        if (enumArgument.Kind != BindingTypeKind.Enum || !TryFindEnum(compilation, enumArgument, out var enumBinding))
        {
            return false;
        }

        enumBinding.IsFlags = true;
        enumType = BindingType.CreateEnum(enumArgument.CppTypeName, GetQualifiedEnumManagedName(compilation, enumBinding), enumId: enumBinding.SymbolId);
        return true;
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
