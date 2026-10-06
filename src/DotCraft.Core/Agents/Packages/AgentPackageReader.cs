using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DotCraft.Agents.Packages;

public sealed record AgentPackageContent(bool Markdown, AgentPackageManifest Manifest, string Document);

public static class AgentPackageReader
{
    private const string SkillMarker = "SKILL.md";
    private const string PluginMarker = ".craft-plugin/plugin.json";

    public static AgentPackageContent Read(string path) => Damaged(() => ReadFile(path));

    public static void ExtractPackage(string path, AgentPackageEntry package, string destination) =>
        Damaged(() => Extract(path, package, destination));

    private static T Damaged<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (InvalidDataException)
        {
            throw AgentPackageException.Invalid("The file is damaged.");
        }
    }

    private static AgentPackageContent ReadFile(string path)
    {
        if (new FileInfo(path).Length > AgentPackageLimits.MaximumBytes)
            throw AgentPackageException.TooLarge("An Agent package must be 64 MiB or smaller.");
        if (!IsZip(path))
        {
            using var stream = File.OpenRead(path);
            var document = ReadDocument(stream);
            return new AgentPackageContent(
                true,
                new AgentPackageManifest(
                    AgentPackageManifest.CurrentFormat,
                    AgentPackageDocument.ReadName(document) ?? string.Empty,
                    AgentPackageDocument.ReadDescription(document),
                    AgentPackageManifest.ProfileFileName,
                    DateTimeOffset.MinValue,
                    []),
                document);
        }

        using var archive = Open(path);
        var entries = Entries(archive.Entries, "The Agent package");
        if (!entries.TryGetValue(AgentPackageManifest.FileName, out var manifestEntry))
            throw AgentPackageException.Invalid("The Agent package has no agent.json.");
        var manifest = ReadManifest(manifestEntry);
        if (!entries.TryGetValue(manifest.Profile, out var profileEntry))
            throw AgentPackageException.Invalid("The Agent package names a document it does not hold.");
        string profile;
        using (var stream = profileEntry.Open())
            profile = ReadDocument(stream);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages)
        {
            if (!seen.Add($"{package.Kind}|{package.Name}"))
                throw AgentPackageException.Invalid($"The Agent package lists {package.Kind} {package.Name} twice.");
            if (package.File is null)
                continue;
            if (!entries.TryGetValue(package.File, out var file))
                throw AgentPackageException.Invalid($"The Agent package does not hold {package.File}.");
            using var zip = OpenPackage(file);
            Inspect(zip, package, destination: null);
        }

        return new AgentPackageContent(false, manifest, profile);
    }

    private static bool Extract(string path, AgentPackageEntry package, string destination)
    {
        using var archive = Open(path);
        var file = archive.GetEntry(package.File ?? string.Empty)
                   ?? throw AgentPackageException.Invalid($"The Agent package does not hold {package.File}.");
        using var zip = OpenPackage(file);
        Directory.CreateDirectory(destination);
        Inspect(zip, package, destination);
        return true;
    }

    private static bool IsZip(string path)
    {
        Span<byte> head = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        return stream.ReadAtLeast(head, 4, throwOnEndOfStream: false) == 4
               && head[0] == 'P' && head[1] == 'K' && head[2] == 3 && head[3] == 4;
    }

    private static ZipArchive Open(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            throw AgentPackageException.Invalid("The file is neither an Agent package nor a Markdown document.");
        }
    }

    private static Dictionary<string, ZipArchiveEntry> Entries(IEnumerable<ZipArchiveEntry> archive, string owner)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive)
        {
            var name = entry.FullName;
            if (name.Length == 0 || name.StartsWith('/') || name.Contains('\\') || name.Contains(':')
                || name.Split('/').Any(segment => segment is "." or "..")
                || !entries.TryAdd(name, entry))
                throw AgentPackageException.Invalid($"{owner} holds an entry it may not: {name}.");
        }
        return entries;
    }

    private static AgentPackageManifest ReadManifest(ZipArchiveEntry entry)
    {
        AgentPackageManifest? manifest;
        try
        {
            var bytes = ReadBounded(entry, AgentPackageLimits.MaximumManifestBytes,
                () => AgentPackageException.Invalid("agent.json must be 1 MiB or smaller."));
            manifest = JsonSerializer.Deserialize<AgentPackageManifest>(bytes, AgentPackageJson.Options);
        }
        catch (JsonException)
        {
            throw AgentPackageException.Invalid("The Agent package's agent.json cannot be read.");
        }
        if (manifest is null || manifest.Format != AgentPackageManifest.CurrentFormat)
            throw AgentPackageException.Invalid($"The Agent package is not a {AgentPackageManifest.CurrentFormat} package.");
        if (manifest.Profile is null)
            throw AgentPackageException.Invalid("The Agent package names a document it does not hold.");

        return manifest with
        {
            Name = manifest.Name ?? string.Empty,
            Packages = (manifest.Packages ?? []).Select(Normalize).ToArray()
        };
    }

    private static AgentPackageEntry Normalize(AgentPackageEntry? package)
    {
        if (package is not { Kind: AgentPackageKinds.Skill or AgentPackageKinds.Plugin } || string.IsNullOrWhiteSpace(package.Name))
            throw AgentPackageException.Invalid("The Agent package lists a package of an unknown kind.");
        if (package.File is null && (package.Marketplace is null || package.Kind == AgentPackageKinds.Skill))
            throw AgentPackageException.Invalid($"The Agent package names neither a marketplace nor a file for {package.Name}.");
        return package with
        {
            DisplayName = string.IsNullOrWhiteSpace(package.DisplayName) ? package.Name : package.DisplayName,
            Sha256 = package.Sha256 ?? string.Empty,
            Skills = package.Skills ?? [],
            McpServers = package.McpServers ?? [],
            Marketplace = package.Marketplace is { } marketplace
                ? marketplace with { SparsePaths = marketplace.SparsePaths ?? [] }
                : null
        };
    }

    private static string ReadDocument(Stream stream)
    {
        var bytes = new MemoryStream();
        CopyBounded(stream, bytes, AgentPackageLimits.MaximumDocumentBytes,
            () => AgentPackageException.Invalid("The Agent document must be 64 KiB or smaller."));
        bytes.Position = 0;
        using var reader = new StreamReader(bytes, new UTF8Encoding(false, throwOnInvalidBytes: true));
        try
        {
            return reader.ReadToEnd().Replace("\r\n", "\n");
        }
        catch (DecoderFallbackException)
        {
            throw AgentPackageException.Invalid("The Agent document is not UTF-8 text.");
        }
    }

    private static MemoryStream OpenPackage(ZipArchiveEntry file) =>
        new(ReadBounded(file, AgentPackageLimits.MaximumBytes,
            () => AgentPackageException.TooLarge("An Agent package must be 64 MiB or smaller.")));

    private static byte[] ReadBounded(ZipArchiveEntry entry, long limit, Func<AgentPackageException> exceeded)
    {
        if (entry.Length > limit)
            throw exceeded();
        using var source = entry.Open();
        using var buffer = new MemoryStream();
        CopyBounded(source, buffer, limit, exceeded);
        return buffer.ToArray();
    }

    private static void Inspect(Stream zip, AgentPackageEntry package, string? destination)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw AgentPackageException.Invalid($"{package.File} is not a zip.");
        }

        using (archive)
        {
            var skill = package.Kind == AgentPackageKinds.Skill;
            var marker = skill ? SkillMarker : PluginMarker;
            var maximumFiles = skill ? AgentPackageLimits.MaximumSkillFiles : AgentPackageLimits.MaximumPluginFiles;
            var maximumBytes = skill ? AgentPackageLimits.MaximumSkillBytes : AgentPackageLimits.MaximumPluginBytes;
            var tooLarge = () => AgentPackageException.TooLarge(
                $"{package.Name} unpacks to more than {maximumFiles} files or {maximumBytes / (1024 * 1024)} MiB.");
            byte[]? markerBytes = null;
            var files = 0;
            long total = 0;
            foreach (var (name, entry) in Entries(archive.Entries, package.File!))
            {
                var target = destination is null ? null : Path.Combine(destination, name);
                if (name.EndsWith('/'))
                {
                    if (target is not null)
                        Directory.CreateDirectory(target);
                    continue;
                }
                if (++files > maximumFiles)
                    throw tooLarge();

                var isMarker = string.Equals(name, marker, StringComparison.Ordinal);
                if (target is not null)
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var source = entry.Open();
                using var output = isMarker ? new MemoryStream()
                    : target is null ? Stream.Null
                    : new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                total += CopyBounded(source, output, maximumBytes - total, tooLarge);
                if (!isMarker)
                    continue;
                markerBytes = ((MemoryStream)output).ToArray();
                if (target is not null)
                    File.WriteAllBytes(target, markerBytes);
            }

            if (markerBytes is null)
                throw AgentPackageException.Invalid($"{package.File} is not a {package.Kind} package.");
            var claimed = skill ? SkillName(markerBytes) : PluginId(markerBytes);
            if (!string.Equals(claimed, package.Name, StringComparison.OrdinalIgnoreCase))
                throw AgentPackageException.Invalid($"{package.File} holds {package.Kind} {claimed}, not {package.Kind} {package.Name}.");
        }
    }

    private static string? SkillName(byte[] skill)
    {
        var lines = Encoding.UTF8.GetString(skill).Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
            return null;
        foreach (var line in lines.Skip(1).TakeWhile(line => line.Trim() != "---"))
        {
            var separator = line.IndexOf(':');
            if (separator > 0 && line[..separator].Trim() == "name")
                return line[(separator + 1)..].Trim().Trim('"', '\'');
        }
        return null;
    }

    private static string? PluginId(byte[] manifest)
    {
        try
        {
            using var document = JsonDocument.Parse(manifest, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("id", out var id)
                   && id.ValueKind == JsonValueKind.String
                ? id.GetString()?.Trim()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long CopyBounded(Stream source, Stream destination, long limit, Func<AgentPackageException> exceeded)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            copied += read;
            if (copied > limit)
                throw exceeded();
            destination.Write(buffer, 0, read);
        }
        return copied;
    }
}
