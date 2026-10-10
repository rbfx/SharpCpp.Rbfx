using System.Reflection.PortableExecutable;
using System.Text;

namespace SharpCpp.Rbfx.Generator;

/// <summary>
/// The export name table of a Windows (PE) SDK library, as C++ qualified names.
/// </summary>
/// <remarks>
/// The reader and the name decoder are managed code, so they work on each host system. The decoder
/// reads only the qualified name at the start of an MSVC decorated name; the encoded type that
/// follows is not necessary for a match by name. It does not decode a name with a template, an
/// anonymous namespace or a local scope: <see cref="ContainsName"/> has no answer for such a name
/// and the filter must not examine it.
/// </remarks>
public sealed class SdkPeExportTable
{
    private const string ConversionOperatorName = "operator?";

    // The MSVC codes of the special names that follow "??".
    private static readonly Dictionary<string, string> OperatorNames = new(StringComparer.Ordinal)
    {
        ["2"] = "operatornew",
        ["3"] = "operatordelete",
        ["4"] = "operator=",
        ["5"] = "operator>>",
        ["6"] = "operator<<",
        ["7"] = "operator!",
        ["8"] = "operator==",
        ["9"] = "operator!=",
        ["A"] = "operator[]",
        ["B"] = ConversionOperatorName,
        ["C"] = "operator->",
        ["D"] = "operator*",
        ["E"] = "operator++",
        ["F"] = "operator--",
        ["G"] = "operator-",
        ["H"] = "operator+",
        ["I"] = "operator&",
        ["J"] = "operator->*",
        ["K"] = "operator/",
        ["L"] = "operator%",
        ["M"] = "operator<",
        ["N"] = "operator<=",
        ["O"] = "operator>",
        ["P"] = "operator>=",
        ["Q"] = "operator,",
        ["R"] = "operator()",
        ["S"] = "operator~",
        ["T"] = "operator^",
        ["U"] = "operator|",
        ["V"] = "operator&&",
        ["W"] = "operator||",
        ["X"] = "operator*=",
        ["Y"] = "operator+=",
        ["Z"] = "operator-=",
        ["_0"] = "operator/=",
        ["_1"] = "operator%=",
        ["_2"] = "operator>>=",
        ["_3"] = "operator<<=",
        ["_4"] = "operator&=",
        ["_5"] = "operator|=",
        ["_6"] = "operator^=",
        ["_U"] = "operatornew[]",
        ["_V"] = "operatordelete[]",
        ["__M"] = "operator<=>",
    };

    // The MSVC codes that precede the name of a class, a struct, a union or an enum in a type.
    // '@' ends the previous fragment: a type that is first a scope (X::Y before X) is not 'VX@'.
    private const string TypeNamePrefixes = "VUT4@";
    private const int MaxBackReferences = 10;

    // The decorated names of the exports, by decoded C++ qualified name.
    private readonly Dictionary<string, List<string>> _names = new(StringComparer.Ordinal);
    private readonly HashSet<string> _virtualTableRecords = new(StringComparer.Ordinal);

    private SdkPeExportTable(int exportCount)
    {
        ExportCount = exportCount;
    }

    /// <summary>The number of names in the export name table.</summary>
    public int ExportCount { get; }

    /// <summary>The number of distinct C++ qualified names that the decoder found.</summary>
    public int QualifiedNameCount => _names.Count;

    /// <summary>The number of records with an exported virtual table.</summary>
    public int VirtualTableCount => _virtualTableRecords.Count;

    public static bool IsPortableExecutable(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);

        using var stream = File.OpenRead(libraryPath);
        return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
    }

    public static SdkPeExportTable Load(string libraryPath)
    {
        return FromExportNames(ReadExportNames(libraryPath));
    }

    public static SdkPeExportTable FromExportNames(IReadOnlyCollection<string> exportNames)
    {
        ArgumentNullException.ThrowIfNull(exportNames);

        var table = new SdkPeExportTable(exportNames.Count);
        foreach (var exportName in exportNames)
        {
            if (!TryDecodeQualifiedName(exportName, out var qualifiedName, out var isVirtualTable))
            {
                continue;
            }

            if (isVirtualTable)
            {
                table._virtualTableRecords.Add(qualifiedName);
                continue;
            }

            if (!table._names.TryGetValue(qualifiedName, out var decoratedNames))
            {
                table._names.Add(qualifiedName, decoratedNames = []);
            }

            decoratedNames.Add(exportName);
        }

        return table;
    }

    /// <summary>
    /// Reads the export name table of a PE image. <see cref="PEReader.GetSectionData(int)"/> maps
    /// each relative virtual address to its file position.
    /// </summary>
    public static IReadOnlyList<string> ReadExportNames(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);

        using var stream = File.OpenRead(libraryPath);
        using var reader = new PEReader(stream);
        var directory = reader.PEHeaders.PEHeader?.ExportTableDirectory ?? default;
        if (directory.Size == 0)
        {
            return Array.Empty<string>();
        }

        // IMAGE_EXPORT_DIRECTORY: NumberOfNames is at offset 24, AddressOfNames is at offset 32.
        var exportDirectory = ReadSectionData(reader, directory.RelativeVirtualAddress, libraryPath).GetReader();
        exportDirectory.Offset = 24;
        var nameCount = exportDirectory.ReadInt32();
        exportDirectory.Offset = 32;
        var nameTable = ReadSectionData(reader, exportDirectory.ReadInt32(), libraryPath).GetReader();
        if (nameCount < 0 || nameCount > nameTable.RemainingBytes / sizeof(int))
        {
            throw new BadImageFormatException($"The export name count {nameCount} of '{libraryPath}' is not valid.");
        }

        var names = new List<string>(nameCount);
        for (var index = 0; index < nameCount; index++)
        {
            var name = ReadSectionData(reader, nameTable.ReadInt32(), libraryPath).GetReader();
            var length = name.IndexOf(0);
            if (length < 0)
            {
                throw new BadImageFormatException($"The export name {index} of '{libraryPath}' has no terminator.");
            }

            names.Add(name.ReadUTF8(length));
        }

        return names;
    }

    private static PEMemoryBlock ReadSectionData(PEReader reader, int relativeVirtualAddress, string libraryPath)
    {
        if (relativeVirtualAddress < 0)
        {
            throw new BadImageFormatException($"The relative virtual address {relativeVirtualAddress} in '{libraryPath}' is not valid.");
        }

        return reader.GetSectionData(relativeVirtualAddress);
    }

    /// <summary>
    /// True when the library exports a function (or a variable) with this C++ qualified name.
    /// The match ignores the parameters, as the filter for other library formats does.
    /// </summary>
    public bool ContainsName(string cppQualifiedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cppQualifiedName);
        return _names.ContainsKey(ToLookupName(cppQualifiedName));
    }

    /// <summary>
    /// True when one exported overload with this C++ qualified name has each of these type names
    /// (a class, a struct, a union or an enum, without its scope) in its encoded type. This tells
    /// apart the overloads that differ in such a type. It is not a full match of the signature.
    /// </summary>
    public bool ContainsOverload(string cppQualifiedName, IReadOnlyCollection<string> typeNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cppQualifiedName);
        ArgumentNullException.ThrowIfNull(typeNames);

        var lookupName = ToLookupName(cppQualifiedName);
        if (!_names.TryGetValue(lookupName, out var decoratedNames))
        {
            return false;
        }

        // A type name that is also a part of the function name is encoded as a digit.
        var scopeNames = lookupName.Split("::");
        var encodedTypeNames = typeNames.Where(typeName => !scopeNames.Contains(typeName, StringComparer.Ordinal)).ToArray();
        return decoratedNames.Any(decoratedName => encodedTypeNames.All(typeName => HasTypeName(decoratedName, typeName)));
    }

    /// <summary>True when the library exports the virtual table of this record.</summary>
    public bool ContainsVirtualTable(string recordCppQualifiedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordCppQualifiedName);
        return _virtualTableRecords.Contains(recordCppQualifiedName);
    }

    /// <summary>
    /// Decodes the qualified name at the start of an MSVC decorated name, for example
    /// <c>?Bar@Foo@Urho3D@@QEAAXXZ</c> to <c>Urho3D::Foo::Bar</c>. An operator name has no space
    /// (<c>operatornew</c>) and a conversion operator has no type (<c>operator?</c>).
    /// </summary>
    public static bool TryDecodeQualifiedName(string decoratedName, out string qualifiedName, out bool isVirtualTable)
    {
        ArgumentNullException.ThrowIfNull(decoratedName);

        qualifiedName = string.Empty;
        isVirtualTable = false;
        if (decoratedName.Length < 2 || decoratedName[0] != '?')
        {
            return false;
        }

        var position = 1;
        var specialCode = default(string);
        if (decoratedName[position] == '?')
        {
            position++;
            var codeLength = 1;
            while (position + codeLength <= decoratedName.Length && decoratedName[position + codeLength - 1] == '_')
            {
                codeLength++;
            }

            if (position + codeLength > decoratedName.Length)
            {
                return false;
            }

            specialCode = decoratedName.Substring(position, codeLength);
            position += codeLength;
        }

        // The name fragments, from the innermost one. A digit refers to an earlier fragment of the
        // back-reference table, which holds each new fragment but not a repeated one (10 at most).
        var fragments = new List<string>();
        var knownFragments = new List<string>();
        while (true)
        {
            if (position >= decoratedName.Length)
            {
                return false;
            }

            var current = decoratedName[position];
            if (current == '@')
            {
                break;
            }

            if (char.IsAsciiDigit(current))
            {
                var reference = current - '0';
                if (reference >= knownFragments.Count)
                {
                    return false;
                }

                fragments.Add(knownFragments[reference]);
                position++;
                continue;
            }

            // '?' starts a template, an anonymous namespace or a local scope.
            var end = decoratedName.IndexOf('@', position);
            if (current == '?' || end < 0)
            {
                return false;
            }

            var fragment = decoratedName[position..end];
            fragments.Add(fragment);
            if (knownFragments.Count < MaxBackReferences)
            {
                knownFragments.Add(fragment);
            }

            position = end + 1;
        }

        if (fragments.Count == 0)
        {
            return false;
        }

        fragments.Reverse();
        var scope = string.Join("::", fragments);
        switch (specialCode)
        {
            case null:
                qualifiedName = scope;
                return true;
            case "0":
                qualifiedName = $"{scope}::{fragments[^1]}";
                return true;
            case "1":
                qualifiedName = $"{scope}::~{fragments[^1]}";
                return true;
            case "_7":
                qualifiedName = scope;
                isVirtualTable = true;
                return true;
            default:
                if (!OperatorNames.TryGetValue(specialCode, out var operatorName))
                {
                    return false;
                }

                qualifiedName = $"{scope}::{operatorName}";
                return true;
        }
    }

    private static bool HasTypeName(string decoratedName, string typeName)
    {
        var position = 0;
        while ((position = decoratedName.IndexOf(typeName, position, StringComparison.Ordinal)) >= 0)
        {
            var end = position + typeName.Length;
            if (position > 0 && TypeNamePrefixes.Contains(decoratedName[position - 1]) && end < decoratedName.Length && decoratedName[end] == '@')
            {
                return true;
            }

            position++;
        }

        return false;
    }

    // Converts a C++ qualified name to the form of a decoded name: no space, and no type for a
    // conversion operator.
    private static string ToLookupName(string cppQualifiedName)
    {
        const string OperatorKeyword = "operator ";

        var operatorStart = cppQualifiedName.LastIndexOf(OperatorKeyword, StringComparison.Ordinal);
        if (operatorStart >= 0 && (operatorStart == 0 || cppQualifiedName[operatorStart - 1] == ':'))
        {
            var operand = cppQualifiedName[(operatorStart + OperatorKeyword.Length)..].TrimStart();
            var isAllocation = operand.StartsWith("new", StringComparison.Ordinal) || operand.StartsWith("delete", StringComparison.Ordinal);
            if (!isAllocation && operand.Length > 0 && (char.IsAsciiLetter(operand[0]) || operand[0] is '_' or ':'))
            {
                return new StringBuilder(cppQualifiedName, 0, operatorStart, operatorStart + ConversionOperatorName.Length)
                    .Replace(" ", string.Empty)
                    .Append(ConversionOperatorName)
                    .ToString();
            }
        }

        return cppQualifiedName.Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}
