using System.ComponentModel;
using System.Text;
using System.Text.Json;
using DotCraft.Lsp;
using DotCraft.Security;

namespace DotCraft.Tools;

/// <summary>
/// Provides language-intelligence operations through configured LSP servers.
/// </summary>
public sealed class LspTool(
    string workspaceRoot,
    LspServerManager manager,
    bool requireApprovalOutsideWorkspace = true,
    int maxFileSize = 10 * 1024 * 1024,
    IApprovalService? approvalService = null,
    PathBlacklist? blacklist = null)
{
    private static readonly HashSet<string> SupportedOperations = new(StringComparer.Ordinal)
    {
        "goToDefinition",
        "findReferences",
        "hover",
        "documentSymbol",
        "workspaceSymbol",
        "goToImplementation",
        "prepareCallHierarchy",
        "incomingCalls",
        "outgoingCalls"
    };

    private readonly string _workspaceRoot = Path.GetFullPath(workspaceRoot);
    private readonly FileAccessGuard _fileAccessGuard =
        new(workspaceRoot, requireApprovalOutsideWorkspace, approvalService, blacklist);

    [Description("Interact with Language Server Protocol (LSP) servers for code intelligence. Supported operations: goToDefinition, findReferences, hover, documentSymbol, workspaceSymbol, goToImplementation, prepareCallHierarchy, incomingCalls, outgoingCalls. line and character are 1-based positions.")]
    [Tool(Icon = "🧭", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.LSP), MaxResultChars = 100_000)]
    public async Task<string> LSP(
        [Description("LSP operation name")] string operation,
        [Description("The absolute or relative file path")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based character position")] int character)
    {
        if (!SupportedOperations.Contains(operation))
            return $"Error: Unsupported LSP operation: {operation}";

        if (line <= 0 || character <= 0)
            return "Error: line and character must be positive 1-based values.";

        var fullPath = _fileAccessGuard.ResolvePath(filePath);
        var pathValidation = await _fileAccessGuard.ValidatePathAsync(fullPath, "read", filePath);
        if (pathValidation != null)
            return pathValidation;

        if (!File.Exists(fullPath))
            return $"Error: File not found: {filePath}";

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length > maxFileSize)
            return $"Error: File too large ({fileInfo.Length} bytes). Max size: {maxFileSize} bytes.";

        if (!manager.IsFileOpen(fullPath))
        {
            var content = await File.ReadAllTextAsync(fullPath);
            await manager.OpenFileAsync(fullPath, content);
        }

        var (method, requestParams) = BuildRequest(operation, fullPath, line, character);
        var result = await manager.SendRequestAsync(
            fullPath,
            method,
            requestParams,
            TimeSpan.FromSeconds(30));

        if (result == null)
            return $"No LSP server available for file type: {Path.GetExtension(fullPath)}";

        if (operation is "incomingCalls" or "outgoingCalls")
        {
            var callItems = result.Value;
            if (callItems.ValueKind != JsonValueKind.Array || callItems.GetArrayLength() == 0)
                return "No call hierarchy item found at this position.";

            var firstItem = callItems[0].Clone();
            var callMethod = operation == "incomingCalls"
                ? "callHierarchy/incomingCalls"
                : "callHierarchy/outgoingCalls";

            result = await manager.SendRequestAsync(
                fullPath,
                callMethod,
                new { item = firstItem },
                TimeSpan.FromSeconds(30));
        }

        return FormatResult(operation, result, _workspaceRoot);
    }

    private static (string Method, object Params) BuildRequest(
        string operation,
        string fullPath,
        int line,
        int character)
    {
        var uri = LspUriHelpers.ToFileUri(fullPath);
        var position = new
        {
            line = line - 1,
            character = character - 1
        };

        return operation switch
        {
            "goToDefinition" => ("textDocument/definition", new
            {
                textDocument = new { uri },
                position
            }),
            "findReferences" => ("textDocument/references", new
            {
                textDocument = new { uri },
                position,
                context = new { includeDeclaration = true }
            }),
            "hover" => ("textDocument/hover", new
            {
                textDocument = new { uri },
                position
            }),
            "documentSymbol" => ("textDocument/documentSymbol", new
            {
                textDocument = new { uri }
            }),
            "workspaceSymbol" => ("workspace/symbol", new
            {
                query = string.Empty
            }),
            "goToImplementation" => ("textDocument/implementation", new
            {
                textDocument = new { uri },
                position
            }),
            "prepareCallHierarchy" => ("textDocument/prepareCallHierarchy", new
            {
                textDocument = new { uri },
                position
            }),
            "incomingCalls" => ("textDocument/prepareCallHierarchy", new
            {
                textDocument = new { uri },
                position
            }),
            "outgoingCalls" => ("textDocument/prepareCallHierarchy", new
            {
                textDocument = new { uri },
                position
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported operation")
        };
    }

    private static string FormatResult(string operation, JsonElement? result, string workspaceRoot)
    {
        if (result == null || result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return NoResultMessage(operation);

        return operation switch
        {
            "goToDefinition" => FormatLocations(result.Value, workspaceRoot, "definition"),
            "goToImplementation" => FormatLocations(result.Value, workspaceRoot, "implementation"),
            "findReferences" => FormatReferences(result.Value, workspaceRoot),
            "hover" => FormatHover(result.Value),
            "documentSymbol" => FormatDocumentSymbols(result.Value, workspaceRoot),
            "workspaceSymbol" => FormatWorkspaceSymbols(result.Value, workspaceRoot),
            "prepareCallHierarchy" => FormatPrepareCallHierarchy(result.Value, workspaceRoot),
            "incomingCalls" => FormatIncomingCalls(result.Value, workspaceRoot),
            "outgoingCalls" => FormatOutgoingCalls(result.Value, workspaceRoot),
            _ => result.Value.ToString()
        };
    }

    private static string NoResultMessage(string operation)
    {
        return operation switch
        {
            "goToDefinition" => "No definition found.",
            "goToImplementation" => "No implementation found.",
            "findReferences" => "No references found.",
            "hover" => "No hover information available.",
            "documentSymbol" => "No symbols found in document.",
            "workspaceSymbol" => "No symbols found in workspace.",
            "prepareCallHierarchy" => "No call hierarchy item found at this position.",
            "incomingCalls" => "No incoming calls found.",
            "outgoingCalls" => "No outgoing calls found.",
            _ => "No result."
        };
    }

    private static string FormatLocations(JsonElement result, string workspaceRoot, string title)
    {
        var locations = ExtractLocations(result, workspaceRoot);
        if (locations.Count == 0)
            return $"No {title} found.";

        if (locations.Count == 1)
            return $"{UppercaseFirst(title)}: {locations[0]}";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {locations.Count} {title} locations:");
        foreach (var item in locations)
            sb.AppendLine($"- {item}");
        return sb.ToString().TrimEnd();
    }

    private static string FormatReferences(JsonElement result, string workspaceRoot)
    {
        var locations = ExtractLocations(result, workspaceRoot);
        if (locations.Count == 0)
            return "No references found.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {locations.Count} references:");
        foreach (var item in locations)
            sb.AppendLine($"- {item}");
        return sb.ToString().TrimEnd();
    }

    private static string FormatHover(JsonElement result)
    {
        if (!result.TryGetProperty("contents", out var contents))
            return "No hover information available.";

        return ExtractMarkupText(contents);
    }

    private static string FormatDocumentSymbols(JsonElement result, string workspaceRoot)
    {
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
            return "No symbols found in document.";

        var lines = new List<string> { "Document symbols:" };
        foreach (var item in result.EnumerateArray())
        {
            if (item.TryGetProperty("location", out _))
            {
                // SymbolInformation[]
                lines.Add(FormatWorkspaceSymbolLine(item, workspaceRoot));
            }
            else
            {
                // DocumentSymbol[]
                lines.AddRange(FormatDocumentSymbolNode(item, 0));
            }
        }

        return string.Join('\n', lines.Where(l => !string.IsNullOrWhiteSpace(l)));
    }

    private static string FormatWorkspaceSymbols(JsonElement result, string workspaceRoot)
    {
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
            return "No symbols found in workspace.";

        var lines = new List<string> { $"Found {result.GetArrayLength()} symbols:" };
        lines.AddRange(result.EnumerateArray().Select(item => FormatWorkspaceSymbolLine(item, workspaceRoot)));
        return string.Join('\n', lines);
    }

    private static string FormatPrepareCallHierarchy(JsonElement result, string workspaceRoot)
    {
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
            return "No call hierarchy item found at this position.";

        var lines = new List<string> { $"Found {result.GetArrayLength()} call hierarchy items:" };
        foreach (var item in result.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : "?";
            var kind = item.TryGetProperty("kind", out var kindEl) ? kindEl.ToString() : "?";
            var location = FormatLocationFromRange(item, workspaceRoot);
            lines.Add($"- {name} (kind {kind}) at {location}");
        }

        return string.Join('\n', lines);
    }

    private static string FormatIncomingCalls(JsonElement result, string workspaceRoot)
    {
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
            return "No incoming calls found.";

        var lines = new List<string> { $"Found {result.GetArrayLength()} incoming calls:" };
        foreach (var call in result.EnumerateArray())
        {
            if (!call.TryGetProperty("from", out var from))
                continue;
            var name = from.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : "?";
            lines.Add($"- {name} at {FormatLocationFromRange(from, workspaceRoot)}");
        }

        return string.Join('\n', lines);
    }

    private static string FormatOutgoingCalls(JsonElement result, string workspaceRoot)
    {
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
            return "No outgoing calls found.";

        var lines = new List<string> { $"Found {result.GetArrayLength()} outgoing calls:" };
        foreach (var call in result.EnumerateArray())
        {
            if (!call.TryGetProperty("to", out var to))
                continue;
            var name = to.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : "?";
            lines.Add($"- {name} at {FormatLocationFromRange(to, workspaceRoot)}");
        }

        return string.Join('\n', lines);
    }

    private static List<string> ExtractLocations(JsonElement result, string workspaceRoot)
    {
        var locations = new List<string>();
        if (result.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in result.EnumerateArray())
            {
                var location = FormatLocationElement(item, workspaceRoot);
                if (!string.IsNullOrWhiteSpace(location))
                    locations.Add(location);
            }
        }
        else if (result.ValueKind == JsonValueKind.Object)
        {
            var location = FormatLocationElement(result, workspaceRoot);
            if (!string.IsNullOrWhiteSpace(location))
                locations.Add(location);
        }

        return locations;
    }

    private static string FormatLocationElement(JsonElement element, string workspaceRoot)
    {
        if (element.TryGetProperty("uri", out var uriElement))
        {
            var uri = uriElement.GetString();
            var path = ToDisplayPath(uri, workspaceRoot);
            if (element.TryGetProperty("range", out var range))
            {
                var line = range.GetProperty("start").GetProperty("line").GetInt32() + 1;
                var character = range.GetProperty("start").GetProperty("character").GetInt32() + 1;
                return $"{path}:{line}:{character}";
            }

            return path;
        }

        if (element.TryGetProperty("targetUri", out var targetUriElement))
        {
            var uri = targetUriElement.GetString();
            var path = ToDisplayPath(uri, workspaceRoot);
            var range = element.TryGetProperty("targetSelectionRange", out var selRange)
                ? selRange
                : element.GetProperty("targetRange");
            var line = range.GetProperty("start").GetProperty("line").GetInt32() + 1;
            var character = range.GetProperty("start").GetProperty("character").GetInt32() + 1;
            return $"{path}:{line}:{character}";
        }

        return string.Empty;
    }

    private static string FormatLocationFromRange(JsonElement item, string workspaceRoot)
    {
        var uri = item.TryGetProperty("uri", out var uriElement) ? uriElement.GetString() : null;
        var path = ToDisplayPath(uri, workspaceRoot);
        if (!item.TryGetProperty("range", out var range))
            return path;

        var line = range.GetProperty("start").GetProperty("line").GetInt32() + 1;
        var character = range.GetProperty("start").GetProperty("character").GetInt32() + 1;
        return $"{path}:{line}:{character}";
    }

    private static IEnumerable<string> FormatDocumentSymbolNode(JsonElement symbol, int indent)
    {
        var prefix = new string(' ', indent * 2);
        var name = symbol.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : "?";
        var kind = symbol.TryGetProperty("kind", out var kindEl) ? kindEl.ToString() : "?";
        var line = symbol.TryGetProperty("range", out var rangeEl)
            ? rangeEl.GetProperty("start").GetProperty("line").GetInt32() + 1
            : 0;
        yield return $"{prefix}- {name} (kind {kind}) line {line}";

        if (symbol.TryGetProperty("children", out var children)
            && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                foreach (var childLine in FormatDocumentSymbolNode(child, indent + 1))
                    yield return childLine;
            }
        }
    }

    private static string FormatWorkspaceSymbolLine(JsonElement symbol, string workspaceRoot)
    {
        var name = symbol.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : "?";
        var kind = symbol.TryGetProperty("kind", out var kindEl) ? kindEl.ToString() : "?";
        if (!symbol.TryGetProperty("location", out var location))
            return $"- {name} (kind {kind})";

        var path = FormatLocationElement(location, workspaceRoot);
        return $"- {name} (kind {kind}) at {path}";
    }

    private static string ExtractMarkupText(JsonElement contents)
    {
        return contents.ValueKind switch
        {
            JsonValueKind.String => contents.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(
                "\n\n",
                contents.EnumerateArray().Select(ExtractMarkupText)),
            JsonValueKind.Object => ExtractMarkupObject(contents),
            _ => contents.ToString()
        };
    }

    private static string ExtractMarkupObject(JsonElement obj)
    {
        if (obj.TryGetProperty("value", out var value))
            return value.GetString() ?? string.Empty;
        return obj.ToString();
    }

    private static string ToDisplayPath(string? uri, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return "<unknown>";

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return uri;

        if (!parsed.IsFile)
            return uri;

        var fullPath = Uri.UnescapeDataString(parsed.LocalPath);
        if (OperatingSystem.IsWindows() && fullPath.StartsWith('/') && fullPath.Length > 2 && fullPath[2] == ':')
            fullPath = fullPath[1..];

        var relative = Path.GetRelativePath(workspaceRoot, fullPath);
        if (!relative.StartsWith(".."))
            return relative.Replace('\\', '/');

        return fullPath.Replace('\\', '/');
    }

    private static string UppercaseFirst(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        if (value.Length == 1)
            return value.ToUpperInvariant();
        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
