using System.Text;
using DotCraft.Security;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace DotCraft.Memory;

/// <summary>One memory file. <see cref="Type"/> is null when the frontmatter is missing or names no known type.</summary>
public sealed record MemoryFileInfo(string FileName, string? Type, string? Description, DateTimeOffset LastModified);

public static class MemoryFiles
{
    public const int MaxListed = 200;

    private const int MaxFrontmatterLines = 50;

    private static readonly HashSet<string> Types = new(StringComparer.Ordinal) { "user", "feedback", "project", "reference" };

    /// <summary>The store's memory files, newest first: every top-level Markdown file except the index.</summary>
    public static IReadOnlyList<MemoryFileInfo> List(string storeDirectory)
    {
        if (!Directory.Exists(storeDirectory))
            return [];

        return new DirectoryInfo(storeDirectory)
            .EnumerateFiles()
            .Where(static file => string.Equals(file.Extension, ".md", StringComparison.OrdinalIgnoreCase)
                                  && !string.Equals(file.Name, MemoryStore.IndexFileName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static file => file.LastWriteTimeUtc)
            .ThenBy(static file => file.Name, StringComparer.Ordinal)
            .Take(MaxListed)
            .Select(static file =>
            {
                var (type, description) = ReadFrontmatter(file.FullName);
                return new MemoryFileInfo(file.Name, type, description, new DateTimeOffset(file.LastWriteTimeUtc));
            })
            .ToArray();
    }

    /// <summary>The note a read of <paramref name="fullPath"/> ends with when it is a memory file last modified more than one day ago, or null.</summary>
    public static string? ReadNote(string storeDirectory, string fullPath)
    {
        if (!string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase)
            || !WorkspaceBoundary.IsWithin(fullPath, storeDirectory)
            || MemoryStore.IsIndexFile(storeDirectory, fullPath)
            || !File.Exists(fullPath))
        {
            return null;
        }

        var days = (int)Math.Floor((DateTime.UtcNow - File.GetLastWriteTimeUtc(fullPath)).TotalDays);
        return days > 1
            ? $"This memory is {days} days old. Memories are point-in-time observations, not live state; claims about code or file:line citations may be outdated. Verify against current code before asserting them as fact."
            : null;
    }

    private static (string? Type, string? Description) ReadFrontmatter(string path)
    {
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            if (reader.ReadLine()?.Trim() != "---")
                return default;

            var yaml = new StringBuilder();
            for (var i = 0; i < MaxFrontmatterLines && reader.ReadLine() is { } line; i++)
            {
                if (line.Trim() == "---")
                    return ParseFrontmatter(yaml.ToString());
                yaml.AppendLine(line);
            }

            return default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    private static (string? Type, string? Description) ParseFrontmatter(string yaml)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root)
                return default;

            var type = Child(root, "metadata") is YamlMappingNode metadata ? Scalar(metadata, "type") : null;
            return (type != null && Types.Contains(type) ? type : null, Scalar(root, "description"));
        }
        catch (YamlException)
        {
            return default;
        }
    }

    private static YamlNode? Child(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string? Scalar(YamlMappingNode node, string key) =>
        Child(node, key) is YamlScalarNode { Value: { } value } && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
}
