using System.Text.RegularExpressions;
using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

/// <summary>
/// Removes the declarations whose wrapper code references a symbol that a Windows (PE) SDK
/// library does not export. Such a library exports only the declarations with <c>URHO3D_API</c>,
/// so the scope is wider than the scope of the filter for an ELF or a Mach-O library.
/// </summary>
public static class SdkPeExportFilter
{
    private static readonly Regex QualifiedNameRegex = new(
        @"[A-Za-z_][A-Za-z0-9_]*(?:::[A-Za-z_][A-Za-z0-9_]*)*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public readonly record struct Result(
        int FreeFunctions,
        int StaticMethods,
        int InstanceMethods,
        int Constructors,
        int StaticFields,
        int DestructorRecords,
        int VirtualTableRecords,
        int RecordConstructors,
        int ByValueFunctions)
    {
        public int Total => FreeFunctions + StaticMethods + InstanceMethods + Constructors + StaticFields + DestructorRecords + VirtualTableRecords + RecordConstructors + ByValueFunctions;
    }

    public static Result Apply(BindingCompilation compilation, SdkPeExportTable exports)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(exports);

        var result = default(Result);

        // The names of the types that have their name in the encoded type of an export.
        var typeNames = compilation.Records
            .Where(static record => !record.IsGeneric)
            .Select(static record => record.CppQualifiedName)
            .Concat(compilation.Enums.Select(static enumBinding => enumBinding.CppQualifiedName))
            .ToHashSet(StringComparer.Ordinal);

        // A thunk takes the address of its function, and ConstructImpl calls its constructor.
        foreach (var function in compilation.Functions)
        {
            if (function.IsIgnored() || !IsLinkedByName(function))
            {
                continue;
            }

            var kind = function.IsConstructor ? "constructor" : "function";
            if (!exports.ContainsName(function.CppQualifiedName))
            {
                function.SetIgnoreReason($"{kind} '{function.CppQualifiedName}' is not exported by the linked rbfx SDK library");
            }
            else if (!exports.ContainsOverload(function.CppQualifiedName, GetParameterTypeNames(function, typeNames)))
            {
                function.SetIgnoreReason($"this overload of {kind} '{function.CppQualifiedName}' is not exported by the linked rbfx SDK library");
            }
            else
            {
                continue;
            }

            if (function.IsConstructor)
            {
                result = result with { Constructors = result.Constructors + 1 };
                continue;
            }

            result = function.Parent is not RecordBinding
                ? result with { FreeFunctions = result.FreeFunctions + 1 }
                : function.IsStatic
                    ? result with { StaticMethods = result.StaticMethods + 1 }
                    : result with { InstanceMethods = result.InstanceMethods + 1 };
        }

        // An accessor takes the address of its static field.
        foreach (var field in compilation.Records.Where(static record => !record.IsGeneric).SelectMany(static record => record.Members.OfType<FieldBinding>()))
        {
            var fieldName = $"{field.OwningRecordCppTypeName}::{field.NativeName}";
            if (!field.IsStatic || field.IsIgnored() || !IsLinkedByName(field, fieldName) || exports.ContainsName(fieldName))
            {
                continue;
            }

            field.SetIgnoreReason($"static field '{fieldName}' is not exported by the linked rbfx SDK library");
            result = result with { StaticFields = result.StaticFields + 1 };
        }

        // The constructors (also the implicit ones), the clone and the destroy thunk of a record
        // are compiled into the wrapper library. They reference the destructor of the record and
        // each virtual function. The check runs also when the library exports the virtual table:
        // the link reports the functions that such a record inherits from a base without export
        // (LNK2001 for ActionState::Update). The wrapper object seems to emit a copy of the virtual
        // table; no object file confirms this.
        var unlinkableRecords = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in compilation.Records)
        {
            if (record.IsIgnored())
            {
                continue;
            }

            var destructorDependency = FindMissingDependency(record, MetadataKeys.DestructorLinkDependencies, exports);
            var virtualTableDependency = FindMissingDependency(record, MetadataKeys.VirtualTableLinkDependencies, exports);
            if (destructorDependency is null && virtualTableDependency is null)
            {
                IgnoreImplicitDefaultConstructor(record, exports, ref result);
                continue;
            }

            unlinkableRecords.Add(record.CppQualifiedName);
            record.SetMetadata(MetadataKeys.NonPublicDestructor, true);
            record.HasCopyConstructor = false;
            result = destructorDependency is not null
                ? result with { DestructorRecords = result.DestructorRecords + 1 }
                : result with { VirtualTableRecords = result.VirtualTableRecords + 1 };

            var reason = destructorDependency is not null
                ? $"record '{record.CppQualifiedName}' needs destructor symbol '{destructorDependency}', which the linked rbfx SDK library does not export"
                : $"record '{record.CppQualifiedName}' needs virtual function '{virtualTableDependency}', which the linked rbfx SDK library does not export";
            foreach (var constructor in record.Members.OfType<FunctionBinding>())
            {
                if (constructor.IsConstructor && !constructor.IsIgnored())
                {
                    constructor.SetIgnoreReason(reason);
                    result = result with { RecordConstructors = result.RecordConstructors + 1 };
                }
            }
        }

        // A parameter or a return value of such a record type makes the thunk copy and destroy it.
        if (unlinkableRecords.Count > 0)
        {
            foreach (var function in compilation.Functions)
            {
                if (function.IsIgnored() || FindByValueRecord(function, unlinkableRecords) is not { } recordName)
                {
                    continue;
                }

                function.SetIgnoreReason($"function '{function.CppQualifiedName}' passes record '{recordName}' by value, which needs symbols that the linked rbfx SDK library does not export");
                result = result with { ByValueFunctions = result.ByValueFunctions + 1 };
            }
        }

        return result;
    }

    // The wrapper references the symbol of a function by name when the translation unit has a
    // declaration and no definition (an inline definition that follows the declaration counts).
    // The parser gives no definition state to a synthetic constructor. A pure virtual function
    // has no symbol. The export table cannot answer for a template name.
    private static bool IsLinkedByName(FunctionBinding function)
    {
        return !function.IsPureVirtual && IsLinkedByName(function, function.CppQualifiedName);
    }

    private static bool IsLinkedByName(BindingNode node, string cppQualifiedName)
    {
        return node.TryGetMetadata<bool>(MetadataKeys.DeclaredOnly, out var declaredOnly) &&
            declaredOnly &&
            cppQualifiedName.Contains("::", StringComparison.Ordinal) &&
            !cppQualifiedName.Contains('<', StringComparison.Ordinal);
    }

    // The names (without scope) of the records and the enums in the parameter types. A type alias
    // has no entry in the set and the name of a template has a different code, so they do not
    // take part in the match.
    private static HashSet<string> GetParameterTypeNames(FunctionBinding function, HashSet<string> typeNames)
    {
        var parameterTypeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in function.Parameters)
        {
            var cppTypeName = parameter.CppTypeName;
            foreach (Match match in QualifiedNameRegex.Matches(cppTypeName))
            {
                var isTemplateName = cppTypeName.AsSpan(match.Index + match.Length).TrimStart().StartsWith("<");
                if (!isTemplateName && typeNames.Contains(match.Value))
                {
                    parameterTypeNames.Add(match.Value[(match.Value.LastIndexOf(':') + 1)..]);
                }
            }
        }

        return parameterTypeNames;
    }

    // The synthetic or defaulted default constructor calls the default constructor of each base
    // and each by-value field. Another constructor of the record does not call it.
    private static void IgnoreImplicitDefaultConstructor(RecordBinding record, SdkPeExportTable exports, ref Result result)
    {
        var dependency = FindMissingDependency(record, MetadataKeys.DefaultConstructorLinkDependencies, exports);
        if (dependency is null)
        {
            return;
        }

        var reason = $"record '{record.CppQualifiedName}' needs default constructor '{dependency}', which the linked rbfx SDK library does not export";
        foreach (var constructor in record.Members.OfType<FunctionBinding>())
        {
            if (constructor.IsConstructor &&
                !constructor.IsIgnored() &&
                constructor.Parameters.Count == 0 &&
                !(constructor.TryGetMetadata<bool>(MetadataKeys.DeclaredOnly, out var declaredOnly) && declaredOnly))
            {
                constructor.SetIgnoreReason(reason);
                result = result with { RecordConstructors = result.RecordConstructors + 1 };
            }
        }
    }

    private static string? FindMissingDependency(RecordBinding record, string metadataKey, SdkPeExportTable exports)
    {
        return record.GetMetadata<string[]>(metadataKey)?
            .FirstOrDefault(dependency => !dependency.Contains('<', StringComparison.Ordinal) && !exports.ContainsName(dependency));
    }

    private static string? FindByValueRecord(FunctionBinding function, HashSet<string> recordNames)
    {
        if (!function.IsConstructor &&
            StripConst(function.CppReturnTypeName ?? function.ReturnType.CppTypeName) is { } returnTypeName &&
            recordNames.Contains(returnTypeName))
        {
            return returnTypeName;
        }

        foreach (var parameter in function.Parameters)
        {
            if (StripConst(parameter.CppTypeName) is { } parameterTypeName && recordNames.Contains(parameterTypeName))
            {
                return parameterTypeName;
            }
        }

        return null;
    }

    // A pointer or a reference type name keeps its '*' or '&' and does not match a record name.
    private static string? StripConst(string? cppTypeName)
    {
        if (string.IsNullOrWhiteSpace(cppTypeName))
        {
            return null;
        }

        var typeName = cppTypeName.Trim();
        return typeName.StartsWith("const ", StringComparison.Ordinal) ? typeName["const ".Length..].TrimStart() : typeName;
    }
}
