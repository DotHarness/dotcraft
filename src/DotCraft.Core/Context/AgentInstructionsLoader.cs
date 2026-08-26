using System.Text;
using System.Security.Cryptography;
using DotCraft.Configuration;
using Microsoft.Extensions.Logging;

namespace DotCraft.Context;

/// <summary>One non-empty source contributing to a rendered AGENTS.md instruction item.</summary>
public sealed record AgentInstructionEntry(
    string SourcePath,
    string Content,
    bool IsUserLevel,
    bool IsTruncated);

/// <summary>Provider-neutral AGENTS.md discovery and rendering result.</summary>
public sealed record AgentInstructionsLoadResult(
    string Content,
    IReadOnlyList<string> Sources,
    IReadOnlyList<AgentInstructionEntry> Entries,
    bool IsTruncated,
    string Fingerprint,
    IReadOnlyList<string> Warnings)
{
    public ContextPageDocument ToContextPageDocument() => new(Content, Sources);
}

/// <summary>
/// Discovers user and project AGENTS.md files without applying provider-specific roles.
/// </summary>
public sealed class AgentInstructionsLoader(ILogger? logger = null)
{
    private static readonly UTF8Encoding LossyUtf8 = new(false, false);
    private static readonly string[] CandidateNames = ["AGENTS.override.md", "AGENTS.md"];

    public AgentInstructionsLoadResult Load(string effectiveCwd, AppConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effectiveCwd);
        ArgumentNullException.ThrowIfNull(config);

        var cwd = Path.GetFullPath(effectiveCwd);
        var entries = new List<AgentInstructionEntry>();
        var warnings = new List<string>();

        LoadUserEntry(config, entries, warnings);
        if (config.ProjectDocMaxBytes > 0)
        {
            var projectEntries = LoadProjectEntries(cwd, config.ProjectDocMaxBytes, warnings);
            entries.AddRange(projectEntries);
        }

        var userContent = entries
            .Where(static entry => entry.IsUserLevel)
            .Select(static entry => entry.Content)
            .ToArray();
        var projectContent = entries
            .Where(static entry => !entry.IsUserLevel)
            .Select(static entry => entry.Content)
            .ToArray();
        var instructions = JoinInstructionContent(userContent, projectContent);
        var rendered = instructions.Length == 0
            ? string.Empty
            : $"# AGENTS.md instructions for {cwd}\n\n<INSTRUCTIONS>\n{instructions}\n</INSTRUCTIONS>";

        var sources = entries.Select(static entry => entry.SourcePath).ToArray();
        return new AgentInstructionsLoadResult(
            rendered,
            sources,
            entries,
            entries.Any(static entry => entry.IsTruncated),
            ComputeFingerprint(rendered, sources),
            warnings);
    }

    private void LoadUserEntry(
        AppConfig config,
        ICollection<AgentInstructionEntry> entries,
        ICollection<string> warnings)
    {
        var root = ResolveUserCraftDirectory(config);
        foreach (var candidateName in CandidateNames)
        {
            var candidate = Path.GetFullPath(Path.Combine(root, candidateName));
            if (!TryGetRegularFile(candidate, out var error))
            {
                if (error != null)
                    Warn(warnings, $"Failed to inspect user instruction file '{candidate}': {error.Message}", error);
                continue;
            }

            try
            {
                var content = LossyUtf8.GetString(File.ReadAllBytes(candidate)).Trim();
                if (content.Length == 0)
                {
                    Warn(warnings, $"User instruction file '{candidate}' is empty; trying the default file.");
                    continue;
                }

                entries.Add(new AgentInstructionEntry(candidate, content, IsUserLevel: true, IsTruncated: false));
                return;
            }
            catch (Exception ex) when (IsFileAccessException(ex))
            {
                Warn(warnings, $"Failed to read user instruction file '{candidate}'; trying the default file: {ex.Message}", ex);
            }
        }
    }

    private IReadOnlyList<AgentInstructionEntry> LoadProjectEntries(
        string cwd,
        int maxBytes,
        ICollection<string> warnings)
    {
        var result = new List<AgentInstructionEntry>();
        var remaining = maxBytes;

        try
        {
            foreach (var directory in EnumerateProjectDirectories(cwd))
            {
                var selected = SelectProjectFile(directory);
                if (selected == null)
                    continue;

                var read = ReadProjectFile(selected, remaining);
                if (read == null)
                    continue;

                remaining -= read.Value.Bytes.Length;
                var content = LossyUtf8.GetString(read.Value.Bytes).Trim();
                if (content.Length == 0)
                {
                    if (remaining <= 0)
                        break;
                    continue;
                }

                result.Add(new AgentInstructionEntry(
                    selected,
                    content,
                    IsUserLevel: false,
                    read.Value.IsTruncated));
                if (remaining <= 0)
                    break;
            }
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            Warn(warnings, $"Failed to load project AGENTS.md chain for '{cwd}': {ex.Message}", ex);
            result.Clear();
        }

        return result;
    }

    private static IReadOnlyList<string> EnumerateProjectDirectories(string cwd)
    {
        var current = new DirectoryInfo(cwd);
        DirectoryInfo? projectRoot = null;
        for (var cursor = current; cursor != null; cursor = cursor.Parent)
        {
            var marker = Path.Combine(cursor.FullName, ".git");
            if (File.Exists(marker) || Directory.Exists(marker))
            {
                projectRoot = cursor;
                break;
            }
        }

        if (projectRoot == null)
            return [current.FullName];

        var descending = new List<string>();
        for (var cursor = current; cursor != null; cursor = cursor.Parent)
        {
            descending.Add(cursor.FullName);
            if (PathEquals(cursor.FullName, projectRoot.FullName))
                break;
        }
        descending.Reverse();
        return descending;
    }

    private static string? SelectProjectFile(string directory)
    {
        foreach (var candidateName in CandidateNames)
        {
            var candidate = Path.GetFullPath(Path.Combine(directory, candidateName));
            if (TryGetRegularFile(candidate, out var error))
                return candidate;
            if (error != null)
                throw error;
        }

        return null;
    }

    private static (byte[] Bytes, bool IsTruncated)? ReadProjectFile(string path, int remaining)
    {
        if (remaining <= 0)
            return null;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var length = Math.Min((long)remaining, stream.Length);
            var bytes = new byte[checked((int)length)];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0)
                    break;
                offset += read;
            }
            if (offset != bytes.Length)
                Array.Resize(ref bytes, offset);
            return (bytes, stream.Length > bytes.Length);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static bool TryGetRegularFile(string path, out Exception? error)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            error = null;
            return !attributes.HasFlag(FileAttributes.Directory);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            error = null;
            return false;
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            error = ex;
            return false;
        }
    }

    private static string ResolveUserCraftDirectory(AppConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.GlobalConfigPath))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(config.GlobalConfigPath));
            if (!string.IsNullOrWhiteSpace(directory))
                return directory;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".craft");
    }

    private static string JoinInstructionContent(
        IReadOnlyList<string> userContent,
        IReadOnlyList<string> projectContent)
    {
        var user = string.Join("\n\n", userContent);
        var project = string.Join("\n\n", projectContent);
        if (user.Length == 0)
            return project;
        if (project.Length == 0)
            return user;
        return $"{user}\n\n--- project-doc ---\n\n{project}";
    }

    private void Warn(ICollection<string> warnings, string message, Exception? exception = null)
    {
        warnings.Add(message);
        if (exception == null)
            logger?.LogWarning("{Warning}", message);
        else
            logger?.LogWarning(exception, "{Warning}", message);
    }

    private static bool IsFileAccessException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string ComputeFingerprint(string content, IReadOnlyList<string> sources)
    {
        var payload = string.Concat(content, "\0", string.Join("\0", sources));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
