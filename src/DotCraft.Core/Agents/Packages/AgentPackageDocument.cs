using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace DotCraft.Agents.Packages;

public static partial class AgentPackageDocument
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder().Build();

    private static readonly JsonSerializerOptions QuotedScalar = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly HashSet<string> YamlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "yes", "no", "on", "off", "null", "y", "n"
    };

    public static string? ReadName(string markdown) => Text(FrontMatter(markdown), "name");

    public static string? ReadDescription(string markdown) => Text(FrontMatter(markdown), "description");

    public static string WithIdentity(string markdown, string name, string description)
    {
        var lines = markdown.Split('\n').ToList();
        var end = lines.Count > 1 && lines[0].Trim() == "---" ? lines.FindIndex(1, line => line.Trim() == "---") : -1;
        if (end < 0)
            return $"---\nname: {Scalar(name)}\ndescription: {Scalar(description)}\n---\n\n{markdown}";

        var head = lines.GetRange(1, end - 1);
        Replace(head, "description", description);
        Replace(head, "name", name);
        return string.Join('\n', [lines[0], .. head, .. lines.Skip(end)]);
    }

    internal static Dictionary<object, object>? FrontMatter(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 2 || lines[0].Trim() != "---")
            return null;
        var end = Array.FindIndex(lines, 1, line => line.Trim() == "---");
        if (end < 0)
            return null;
        try
        {
            return Yaml.Deserialize<Dictionary<object, object>>(string.Join("\n", lines.Skip(1).Take(end - 1)));
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    private static string? Text(Dictionary<object, object>? values, string key) =>
        values != null && values.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    private static void Replace(List<string> head, string key, string value)
    {
        var line = $"{key}: {Scalar(value)}";
        var index = head.FindIndex(candidate => candidate.StartsWith(key + ":", StringComparison.Ordinal));
        if (index < 0)
        {
            head.Insert(0, line);
            return;
        }

        var next = index + 1;
        while (next < head.Count && (Indented(head[next]) || (head[next].Trim().Length == 0 && ContinuesBlock(head, next))))
            next++;
        head.RemoveRange(index, next - index);
        head.Insert(index, line);
    }

    private static bool ContinuesBlock(List<string> head, int blank) =>
        head.Skip(blank).FirstOrDefault(line => line.Trim().Length > 0) is { } following && Indented(following);

    private static bool Indented(string line) => line.Length > 0 && line[0] is ' ' or '\t';

    private static string Scalar(string value) =>
        PlainScalar().IsMatch(value) && value.Any(char.IsLetter) && !YamlKeywords.Contains(value)
            ? value
            : JsonSerializer.Serialize(value, QuotedScalar);

    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} _.,()/-]*(?<! )$")]
    private static partial Regex PlainScalar();
}
