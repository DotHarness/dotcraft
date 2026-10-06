using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DotCraft.Agents.Packages;

public sealed record AgentPackageItem(AgentPackageEntry Entry, string? Directory);

public static class AgentPackageWriter
{
    private static readonly HashSet<string> InstallerMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        ".builtin",
        ".dotcraft-skill.json",
        ".dotcraft-market.json"
    };

    public static string FileName(string agentName)
    {
        var slug = new string(agentName.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return $"{(slug.Length == 0 ? "agent" : slug[..Math.Min(slug.Length, 80)])}.agent.zip";
    }

    public static void Write(
        string path,
        string name,
        string? description,
        string document,
        IReadOnlyList<AgentPackageItem> packages,
        string stagingDirectory,
        DateTimeOffset exportedAt)
    {
        Directory.CreateDirectory(stagingDirectory);
        var entries = new List<AgentPackageEntry>();
        var files = new List<(string Entry, string Path)>();
        long embedded = 0;
        foreach (var package in packages)
        {
            if (package.Directory is null)
            {
                entries.Add(package.Entry with { File = null });
                continue;
            }

            var file = $"packages/{package.Entry.Kind}-{package.Entry.Name}.zip";
            var zip = Path.Combine(stagingDirectory, Path.GetFileName(file));
            ZipDirectory(package.Directory, zip, AgentPackageLimits.MaximumBytes - embedded);
            embedded += new FileInfo(zip).Length;
            using (var stream = File.OpenRead(zip))
                entries.Add(package.Entry with { Sha256 = Convert.ToHexStringLower(SHA256.HashData(stream)), File = file });
            files.Add((file, zip));
        }

        var manifest = new AgentPackageManifest(
            AgentPackageManifest.CurrentFormat,
            name,
            description,
            AgentPackageManifest.ProfileFileName,
            exportedAt,
            entries);
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
        {
            WriteText(archive, AgentPackageManifest.FileName, JsonSerializer.Serialize(manifest, AgentPackageJson.Options));
            WriteText(archive, AgentPackageManifest.ProfileFileName, document);
            foreach (var (entry, source) in files)
                archive.CreateEntryFromFile(source, entry, CompressionLevel.NoCompression);
        }

        if (new FileInfo(path).Length > AgentPackageLimits.MaximumBytes)
            throw TooLarge();
        _ = AgentPackageReader.Read(path);
    }

    public static long DirectoryBytes(string directory) =>
        new DirectoryInfo(directory)
            .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Sum(file => file.Length);

    private static void ZipDirectory(string directory, string path, long budget)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var (file, relative) in Files(directory).OrderBy(file => file.Relative, StringComparer.Ordinal))
        {
            archive.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
            if (output.Length > budget)
                throw TooLarge();
        }
    }

    private static IEnumerable<(string Path, string Relative)> Files(string directory)
    {
        var root = Path.GetFullPath(directory);
        var pending = new Stack<string>([root]);
        while (pending.TryPop(out var current))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw AgentPackageException.Invalid($"{Path.GetFileName(root)} contains a link at {Path.GetRelativePath(root, path)}.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(path);
                    continue;
                }

                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (!InstallerMarkers.Contains(relative))
                    yield return (path, relative);
            }
        }
    }

    private static void WriteText(ZipArchive archive, string path, string text)
    {
        using var stream = archive.CreateEntry(path, CompressionLevel.Optimal).Open();
        stream.Write(new UTF8Encoding(false).GetBytes(text));
    }

    private static AgentPackageException TooLarge() =>
        AgentPackageException.TooLarge("An Agent package must be 64 MiB or smaller. Leave out a skill or plugin.");
}
