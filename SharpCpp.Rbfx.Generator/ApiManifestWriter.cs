using System.Text;
using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

/// <summary>
/// Writes the API manifest: one sorted, tab-separated line for each declaration of the final binding model.
/// Columns: kind, key, details, state. The text has no host-dependent data, so it is comparable across platforms.
/// </summary>
internal static class ApiManifestWriter
{
    public const string FileName = "ApiManifest.tsv";

    private const char ColumnSeparator = '\t';
    private const char DetailSeparator = ';';
    private const string StateBound = "bound";
    private const string StateIgnored = "ignored";

    public static string Render(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var lines = new List<string>();

        foreach (var record in compilation.Descendants<RecordBinding>())
        {
            var details = new List<string>();
            details.AddRange(record.Bases
                .Select(static b => b.CppQualifiedName)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .Select(static name => "base:" + name));
            AddFlag(details, record.IsAbstract, "abstract");
            AddFlag(details, record.IsPolymorphic, "polymorphic");
            AddMetadataValue(details, record, MetadataKeys.RecordSize, "size");
            AddMetadataValue(details, record, MetadataKeys.RecordAlignment, "align");
            lines.Add(FormatLine("record", record.CppQualifiedName, details, GetState(record)));
        }

        foreach (var field in compilation.Descendants<FieldBinding>())
        {
            var ownerName = field.Ancestors().OfType<RecordBinding>().FirstOrDefault()?.CppQualifiedName
                ?? field.OwningRecordCppTypeName;
            var details = new List<string> { field.CppTypeName };
            AddFlag(details, field.IsStatic, "static");
            AddFlag(details, field.IsReadOnly, "readonly");
            AddMetadataValue(details, field, MetadataKeys.FieldOffset, "offset");
            lines.Add(FormatLine("field", $"{ownerName}::{field.NativeName}", details, GetState(field)));
        }

        foreach (var function in compilation.Descendants<FunctionBinding>())
        {
            var parameterList = string.Join(",", function.Parameters.Select(static p => Sanitize(p.CppTypeName)));
            var key = $"{function.CppQualifiedName}({parameterList})" + (function.IsConstMethod ? " const" : string.Empty);
            var details = new List<string> { function.ReturnType.CppTypeName };
            AddFlag(details, function.IsStatic, "static");
            AddFlag(details, function.IsVirtual, "virtual");
            AddFlag(details, function.IsPureVirtual, "pure");
            AddFlag(details, function.IsConstructor, "constructor");
            lines.Add(FormatLine("function", key, details, GetState(function)));
        }

        foreach (var enumBinding in compilation.Descendants<EnumBinding>())
        {
            var state = GetState(enumBinding);
            lines.Add(FormatLine("enum", enumBinding.CppQualifiedName, [enumBinding.UnderlyingTypeName], state));
            foreach (var value in enumBinding.Values)
            {
                lines.Add(FormatLine(
                    "enumvalue",
                    $"{enumBinding.CppQualifiedName}::{value.NativeName}",
                    [value.ValueExpression],
                    state));
            }
        }

        foreach (var alias in compilation.TypeAliases)
        {
            lines.Add(FormatLine("alias", alias.CppQualifiedName, [alias.TargetType.CppTypeName], StateBound));
        }

        lines.Sort(StringComparer.Ordinal);

        // The model can hold the same declaration more than once. One line is enough for a comparison.
        var builder = new StringBuilder();
        string? previousLine = null;
        foreach (var line in lines)
        {
            if (line == previousLine)
            {
                continue;
            }

            builder.Append(line).Append('\n');
            previousLine = line;
        }

        return builder.ToString();
    }

    public static void WriteToDirectory(BindingCompilation compilation, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        File.WriteAllText(Path.Combine(directory, FileName), Render(compilation), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string GetState(BindingNode node)
    {
        if (node.IsIgnored() || node.Ancestors().Any(static ancestor => ancestor.IsIgnored()))
        {
            return StateIgnored;
        }

        return StateBound;
    }

    // A layout value (size and alignment in bytes, offset in bits) is absent when the parser does not know it.
    private static void AddMetadataValue(List<string> details, BindingNode node, string key, string label)
    {
        if (node.TryGetMetadata<long>(key, out var value))
        {
            details.Add($"{label}={value}");
        }
    }

    private static void AddFlag(List<string> details, bool condition, string flag)
    {
        if (condition)
        {
            details.Add(flag);
        }
    }

    private static string FormatLine(string kind, string key, IReadOnlyList<string> details, string state)
    {
        return string.Join(
            ColumnSeparator,
            kind,
            Sanitize(key),
            string.Join(DetailSeparator, details.Select(Sanitize)),
            state);
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
