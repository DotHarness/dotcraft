using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Acornima;
using Acornima.Ast;

namespace DotCraft.DynamicWorkflows;

public sealed partial class DynamicWorkflowParser
{
    private static readonly string[] ProhibitedIdentifiers =
    [
        "import", "require", "eval", "Function", "AsyncFunction", "GeneratorFunction",
        "Date", "Temporal", "setTimeout", "setInterval", "performance", "crypto",
        "WebAssembly", "System", "clr", "process.env", "fetch", "XMLHttpRequest", "WebSocket"
    ];

    public ParsedDynamicWorkflow Parse(string source, int maxBytes = 256 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (Encoding.UTF8.GetByteCount(source) > maxBytes)
            throw new DynamicWorkflowValidationException("script_too_large", "Workflow script exceeds the configured size limit.");

        var match = MetaPrefixRegex().Match(source);
        if (!match.Success || source[..match.Index].Any(character => !char.IsWhiteSpace(character)))
            throw new DynamicWorkflowValidationException("meta_required", "The first executable declaration must be literal 'export const meta = { ... };'.");

        var syntaxSurface = MaskStringsAndComments(source);
        foreach (var identifier in ProhibitedIdentifiers)
        {
            if (Regex.IsMatch(syntaxSurface, identifier.Contains('.')
                    ? Regex.Escape(identifier)
                    : $@"\b{Regex.Escape(identifier)}\b", RegexOptions.CultureInvariant))
            {
                throw new DynamicWorkflowValidationException("prohibited_syntax", $"Workflow scripts cannot use '{identifier}'.");
            }
        }
        if (Regex.IsMatch(syntaxSurface, @"\bMath\s*\.\s*random\b", RegexOptions.CultureInvariant))
            throw new DynamicWorkflowValidationException("prohibited_syntax", "Workflow scripts cannot use Math.random.");

        var metadata = ParseMetadata(match.Value);
        var body = source.Remove(match.Index, match.Length);
        var executable = $"(async () => {{\n{body}\n}})()";
        try
        {
            new Parser().ParseScript(executable);
        }
        catch (Exception ex)
        {
            throw new DynamicWorkflowValidationException("script_syntax_invalid", ex.Message, ex);
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        return new ParsedDynamicWorkflow(metadata, executable, hash);
    }

    private static string MaskStringsAndComments(string source)
    {
        var characters = source.ToCharArray();
        var quote = '\0';
        var lineComment = false;
        var blockComment = false;
        for (var index = 0; index < characters.Length; index++)
        {
            var current = characters[index];
            var next = index + 1 < characters.Length ? characters[index + 1] : '\0';
            if (lineComment)
            {
                if (current is '\r' or '\n') lineComment = false;
                else characters[index] = ' ';
                continue;
            }
            if (blockComment)
            {
                characters[index] = ' ';
                if (current == '*' && next == '/') { characters[++index] = ' '; blockComment = false; }
                continue;
            }
            if (quote != '\0')
            {
                characters[index] = ' ';
                if (current == '\\' && next != '\0') characters[++index] = ' ';
                else if (current == quote) quote = '\0';
                continue;
            }
            if (current is '\'' or '"' or '`') { quote = current; characters[index] = ' '; continue; }
            if (current == '/' && next == '/') { lineComment = true; characters[index] = characters[++index] = ' '; continue; }
            if (current == '/' && next == '*') { blockComment = true; characters[index] = characters[++index] = ' '; }
        }
        return new string(characters);
    }

    private static DynamicWorkflowMetadata ParseMetadata(string source)
    {
        Acornima.Ast.Module module;
        try { module = new Parser().ParseModule(source); }
        catch (Exception ex) { throw new DynamicWorkflowValidationException("meta_invalid", ex.Message, ex); }
        if (module.Body.Count != 1
            || module.Body[0] is not ExportNamedDeclaration { Declaration: VariableDeclaration declaration }
            || declaration.Kind != VariableDeclarationKind.Const
            || declaration.Declarations.Count != 1
            || declaration.Declarations[0] is not { Id: Identifier { Name: "meta" }, Init: ObjectExpression metadata })
            throw InvalidMetadata();

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var node in metadata.Properties)
        {
            if (node is not ObjectProperty { Computed: false, Method: false } property)
                throw InvalidMetadata();
            var key = property.Key switch
            {
                Identifier identifier => identifier.Name,
                StringLiteral literal => literal.Value,
                _ => throw InvalidMetadata()
            };
            if (values.ContainsKey(key)) throw InvalidMetadata();
            values[key] = property.Value switch
            {
                StringLiteral literal => literal.Value,
                ArrayExpression array => ParseStringArray(array),
                _ => throw InvalidMetadata()
            };
        }

        var name = RequiredMetadataString(values, "name");
        var description = RequiredMetadataString(values, "description");
        var whenToUse = OptionalMetadataString(values, "whenToUse");
        var phases = values.TryGetValue("phases", out var phasesValue)
            ? phasesValue as IReadOnlyList<string> ?? throw InvalidMetadata()
            : [];
        return new DynamicWorkflowMetadata(name, description, whenToUse, phases);
    }

    private static IReadOnlyList<string> ParseStringArray(ArrayExpression array)
    {
        var values = new List<string>(array.Elements.Count);
        foreach (var element in array.Elements)
        {
            if (element is not StringLiteral literal) throw InvalidMetadata();
            values.Add(literal.Value);
        }
        return values;
    }

    private static string RequiredMetadataString(IReadOnlyDictionary<string, object?> values, string key) =>
        OptionalMetadataString(values, key) is { Length: > 0 } value ? value : throw InvalidMetadata();

    private static string? OptionalMetadataString(IReadOnlyDictionary<string, object?> values, string key) =>
        !values.TryGetValue(key, out var value) ? null
        : value is string text ? text.Trim()
        : throw InvalidMetadata();

    private static DynamicWorkflowValidationException InvalidMetadata() =>
        new("meta_invalid", "Workflow metadata must be a literal export const object with string fields and string-array phases.");

    [GeneratedRegex(@"\A\s*export\s+const\s+meta\s*=\s*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}\s*;", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex MetaPrefixRegex();

}

public sealed class DynamicWorkflowValidationException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}
