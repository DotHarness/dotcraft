using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Security.ShellCommands;

public sealed class LearnedShellRuleStore(string filePath)
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly Lock _lock = new();

    public DateTime LastWriteTimeUtc =>
        File.Exists(filePath) ? File.GetLastWriteTimeUtc(filePath) : DateTime.MinValue;

    public IReadOnlyList<ShellPrefixRule> Load()
    {
        lock (_lock)
            return ReadRules();
    }

    public void Append(ShellPrefixRule rule)
    {
        lock (_lock)
        {
            var rules = ReadRules();
            if (rules.Contains(rule))
                return;

            rules.Add(rule);
            var array = new JsonArray();
            foreach (var stored in rules)
                array.Add(ShellPrefixRuleJson.Write(stored));

            WriteAtomically(new JsonObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["rules"] = array
            });
        }
    }

    private List<ShellPrefixRule> ReadRules()
    {
        if (!File.Exists(filePath))
            return [];

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var schema)
                || schema != SchemaVersion
                || !root.TryGetProperty("rules", out var rules))
            {
                return [];
            }

            return [.. ShellPrefixRuleJson.Read(rules)];
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void WriteAtomically(JsonNode document)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, document.ToJsonString(SerializerOptions));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
        }
    }
}
