using System.Text;
using System.Text.RegularExpressions;

namespace DotCraft.SourceControl;

public static class PerforceChangelistIds
{
    public const string Default = "default";
}

public static class PerforceChangelistCodes
{
    public const string Prepared = "Prepared";
    public const string Created = "Created";
    public const string NoFiles = "NoFiles";
    public const string Timeout = "Timeout";
    public const string LoginRequired = "LoginRequired";
    public const string ChangelistNotFound = "ChangelistNotFound";
    public const string FileAlreadyInOtherChangelist = "FileAlreadyInOtherChangelist";
    public const string P4ExecutableNotFound = "P4ExecutableNotFound";
    public const string P4CommandFailed = "P4CommandFailed";
}

public sealed record PerforceWorkspaceCommandOptions
{
    public string WorkspacePath { get; init; } = string.Empty;
    public string ConnectionMode { get; init; } = SourceControlConnectionModes.P4Config;
    public string Port { get; init; } = string.Empty;
    public string Client { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
    public string Charset { get; init; } = string.Empty;
}

public sealed record PerforceChangelistEntry
{
    public string Id { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
    public string Description { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
    public string Client { get; init; } = string.Empty;
    public string Status { get; init; } = "pending";
}

public sealed record PerforceChangelistDiagnostic(string Code, string FallbackText);

public sealed record PerforceChangelistPrepareResult
{
    public string Status { get; init; } = "ok";
    public string Code { get; init; } = PerforceChangelistCodes.Prepared;
    public string Changelist { get; init; } = PerforceChangelistIds.Default;
    public bool Created { get; init; }
    public IReadOnlyList<string> MovedPaths { get; init; } = [];
    public IReadOnlyList<string> SkippedPaths { get; init; } = [];
    public IReadOnlyList<PerforceChangelistDiagnostic> Warnings { get; init; } = [];
    public IReadOnlyList<PerforceChangelistDiagnostic> Errors { get; init; } = [];
}

/// <summary>
/// Thin Perforce pending-changelist workflow wrapper. Does not submit or shelve changes.
/// </summary>
public sealed class PerforceChangelistManager(
    IPerforceCommandRunner runner,
    PerforceWorkspaceCommandOptions options)
{
    private static readonly Regex CreatedChangeRegex = new(@"Change\s+(\d+)\s+(?:created|updated)\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OpenedChangeRegex = new(@"\bchange\s+(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<IReadOnlyList<PerforceChangelistEntry>> ListAsync(CancellationToken ct = default)
    {
        var identity = await ResolveIdentityFiltersAsync(ct).ConfigureAwait(false);
        var result = await runner.RunAsync(
            [.. BuildGlobals(), "-ztag", "changes", "-s", "pending", .. BuildIdentityFilters(identity.User, identity.Client)],
            null,
            ct).ConfigureAwait(false);

        ThrowIfFailed(result, "Unable to list pending Perforce changelists.");

        var entries = new List<PerforceChangelistEntry>
        {
            new()
            {
                Id = PerforceChangelistIds.Default,
                IsDefault = true,
                Description = "Default changelist",
                User = identity.User,
                Client = identity.Client,
                Status = "pending"
            }
        };
        entries.AddRange(ParseTaggedChanges(result.StdOut));
        return entries;
    }

    public async Task<PerforceChangelistEntry> CreateAsync(string description, CancellationToken ct = default)
    {
        var spec = BuildChangeSpec("new", description);
        var result = await runner.RunAsync([.. BuildGlobals(), "change", "-i"], spec, ct).ConfigureAwait(false);
        ThrowIfFailed(result, "Unable to create Perforce changelist.");

        var id = ParseCreatedChangeId(result.StdOut + "\n" + result.StdErr);
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("Perforce did not return a created changelist id.");

        return new PerforceChangelistEntry
        {
            Id = id,
            IsDefault = false,
            Description = NormalizeDescription(description),
            User = options.User,
            Client = options.Client,
            Status = "pending"
        };
    }

    public async Task<bool> ChangelistExistsAsync(string changelist, CancellationToken ct = default)
    {
        changelist = NormalizeChangelist(changelist);
        if (changelist == PerforceChangelistIds.Default)
            return true;

        var result = await runner.RunAsync([.. BuildGlobals(), "change", "-o", changelist], null, ct).ConfigureAwait(false);
        if (result.Ok)
            return true;
        if (ClassifyFailureCode(result) == PerforceChangelistCodes.ChangelistNotFound)
            return false;
        ThrowIfFailed(result, $"Unable to read Perforce changelist {changelist}.");
        return false;
    }

    public async Task UpdateDescriptionAsync(string changelist, string description, CancellationToken ct = default)
    {
        changelist = NormalizeChangelist(changelist);
        if (changelist == PerforceChangelistIds.Default || string.IsNullOrWhiteSpace(description))
            return;

        var specResult = await runner.RunAsync([.. BuildGlobals(), "change", "-o", changelist], null, ct).ConfigureAwait(false);
        ThrowIfFailed(specResult, $"Unable to read Perforce changelist {changelist}.");

        var updatedSpec = ReplaceDescription(specResult.StdOut, description);
        var updateResult = await runner.RunAsync([.. BuildGlobals(), "change", "-i"], updatedSpec, ct).ConfigureAwait(false);
        ThrowIfFailed(updateResult, $"Unable to update Perforce changelist {changelist}.");
    }

    public async Task<PerforceChangelistPrepareResult> PrepareAsync(
        IReadOnlyList<string> paths,
        string target,
        string description,
        CancellationToken ct = default)
    {
        var normalizedPaths = NormalizePaths(paths);
        if (normalizedPaths.Count == 0)
        {
            return Error(
                PerforceChangelistCodes.NoFiles,
                "No files were provided for changelist preparation.",
                PerforceChangelistIds.Default);
        }

        target = NormalizeChangelist(target);
        var created = false;
        if (target != PerforceChangelistIds.Default)
        {
            var targetError = await EnsureNumberedTargetReadyAsync(target, description, ct).ConfigureAwait(false);
            if (targetError != null)
                return targetError;
        }

        var toMove = new List<string>();
        var skipped = new List<string>();
        var warnings = new List<PerforceChangelistDiagnostic>();
        foreach (var path in normalizedPaths)
        {
            var openedResult = await GetOpenedChangelistAsync(path, ct).ConfigureAwait(false);
            if (openedResult.Failure.HasValue)
            {
                var failure = openedResult.Failure.Value;
                return Error(
                    ClassifyFailureCode(failure),
                    FailureText(failure, "Unable to read Perforce opened file state."),
                    target);
            }

            var opened = openedResult.Changelist;
            if (opened is { Length: > 0 }
                && opened != PerforceChangelistIds.Default
                && !string.Equals(opened, target, StringComparison.Ordinal))
            {
                skipped.Add(path);
                warnings.Add(new PerforceChangelistDiagnostic(
                    PerforceChangelistCodes.FileAlreadyInOtherChangelist,
                    $"File '{Path.GetFileName(path)}' is already opened in changelist {opened}; it was not moved."));
                continue;
            }

            if (target == PerforceChangelistIds.Default || !string.Equals(opened, target, StringComparison.Ordinal))
                toMove.Add(path);
        }

        if (target == PerforceChangelistIds.Default && toMove.Count > 0)
        {
            var createdChange = await CreateAsync(description, ct).ConfigureAwait(false);
            target = createdChange.Id;
            created = true;
        }

        if (toMove.Count > 0)
        {
            var reopen = await runner.RunAsync(
                [.. BuildGlobals(), "reopen", "-c", target, "--", .. toMove],
                null,
                ct).ConfigureAwait(false);
            if (!reopen.Ok)
                return Error(ClassifyFailureCode(reopen), FailureText(reopen, "Unable to move files to the Perforce changelist."), target);
        }

        return new PerforceChangelistPrepareResult
        {
            Status = "ok",
            Code = created ? PerforceChangelistCodes.Created : PerforceChangelistCodes.Prepared,
            Changelist = target,
            Created = created,
            MovedPaths = toMove,
            SkippedPaths = skipped,
            Warnings = warnings
        };
    }

    private async Task<OpenedChangelistLookup> GetOpenedChangelistAsync(string path, CancellationToken ct)
    {
        var result = await runner.RunAsync([.. BuildGlobals(), "opened", path], null, ct).ConfigureAwait(false);
        var text = result.StdOut + "\n" + result.StdErr;
        if (!result.Ok && text.Contains("not opened", StringComparison.OrdinalIgnoreCase))
            return new OpenedChangelistLookup(null, null);
        if (!result.Ok)
            return new OpenedChangelistLookup(null, result);
        if (text.Contains("default change", StringComparison.OrdinalIgnoreCase))
            return new OpenedChangelistLookup(PerforceChangelistIds.Default, null);
        var match = OpenedChangeRegex.Match(text);
        return new OpenedChangelistLookup(match.Success ? match.Groups[1].Value : null, null);
    }

    private async Task<PerforceChangelistPrepareResult?> EnsureNumberedTargetReadyAsync(
        string target,
        string description,
        CancellationToken ct)
    {
        var specResult = await runner.RunAsync([.. BuildGlobals(), "change", "-o", target], null, ct).ConfigureAwait(false);
        if (!specResult.Ok)
        {
            var code = ClassifyFailureCode(specResult);
            var fallback = code == PerforceChangelistCodes.ChangelistNotFound
                ? $"Perforce changelist {target} was not found."
                : $"Unable to read Perforce changelist {target}.";
            return Error(code, FailureText(specResult, fallback), target);
        }

        if (string.IsNullOrWhiteSpace(description))
            return null;

        var updatedSpec = ReplaceDescription(specResult.StdOut, description);
        var updateResult = await runner.RunAsync([.. BuildGlobals(), "change", "-i"], updatedSpec, ct).ConfigureAwait(false);
        if (!updateResult.Ok)
        {
            return Error(
                ClassifyFailureCode(updateResult),
                FailureText(updateResult, $"Unable to update Perforce changelist {target}."),
                target);
        }

        return null;
    }

    private async Task<PerforceIdentityFilters> ResolveIdentityFiltersAsync(CancellationToken ct)
    {
        var user = options.User?.Trim() ?? string.Empty;
        var client = options.Client?.Trim() ?? string.Empty;
        if (options.ConnectionMode == SourceControlConnectionModes.P4Config
            || string.IsNullOrWhiteSpace(user)
            || string.IsNullOrWhiteSpace(client))
        {
            var info = await runner.RunAsync([.. BuildGlobals(), "info"], null, ct).ConfigureAwait(false);
            ThrowIfFailed(info, "Unable to resolve Perforce client and user for changelist listing.");
            user = ParseInfoValue(info.StdOut, "User name", "userName") ?? user;
            client = ParseInfoValue(info.StdOut, "Client name", "clientName") ?? client;
        }

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(client))
            throw new InvalidOperationException("Unable to resolve Perforce client and user for changelist listing.");

        return new PerforceIdentityFilters(user, client);
    }

    private List<string> BuildGlobals()
    {
        var g = new List<string>();
        if (options.ConnectionMode == SourceControlConnectionModes.Manual)
        {
            AddOption(g, "-p", options.Port);
            AddOption(g, "-c", options.Client);
            AddOption(g, "-u", options.User);
        }
        AddOption(g, "-C", options.Charset);
        return g;
    }

    private static List<string> BuildIdentityFilters(string user, string client)
    {
        var args = new List<string>();
        AddOption(args, "-u", user);
        AddOption(args, "-c", client);
        return args;
    }

    private static void AddOption(List<string> args, string flag, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        args.Add(flag);
        args.Add(value.Trim());
    }

    private static IReadOnlyList<PerforceChangelistEntry> ParseTaggedChanges(string stdout)
    {
        var entries = new List<PerforceChangelistEntry>();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Flush()
        {
            if (!fields.TryGetValue("change", out var id) || string.IsNullOrWhiteSpace(id))
                return;
            entries.Add(new PerforceChangelistEntry
            {
                Id = id.Trim(),
                IsDefault = false,
                Description = fields.TryGetValue("desc", out var desc) ? desc.Trim() : string.Empty,
                User = fields.TryGetValue("user", out var user) ? user.Trim() : string.Empty,
                Client = fields.TryGetValue("client", out var client) ? client.Trim() : string.Empty,
                Status = fields.TryGetValue("status", out var status) ? status.Trim() : "pending"
            });
            fields.Clear();
        }

        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!line.StartsWith("... ", StringComparison.Ordinal))
                continue;
            var rest = line[4..];
            var space = rest.IndexOf(' ');
            if (space <= 0)
                continue;
            var key = rest[..space];
            var value = rest[(space + 1)..];
            if (string.Equals(key, "change", StringComparison.OrdinalIgnoreCase) && fields.Count > 0)
                Flush();
            fields[key] = value;
        }
        Flush();
        return entries;
    }

    private static string BuildChangeSpec(string change, string description)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Change:\t{change}");
        sb.AppendLine();
        sb.AppendLine("Status:\tnew");
        sb.AppendLine();
        sb.AppendLine("Description:");
        foreach (var line in NormalizeDescription(description).Split('\n'))
            sb.AppendLine("\t" + line);
        return sb.ToString();
    }

    private static string ReplaceDescription(string spec, string description)
    {
        var lines = spec.Replace("\r\n", "\n").Split('\n').ToList();
        var start = lines.FindIndex(line => line.Trim().Equals("Description:", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
            return spec.TrimEnd() + "\n\nDescription:\n\t" + NormalizeDescription(description).Replace("\n", "\n\t") + "\n";

        var end = start + 1;
        while (end < lines.Count)
        {
            var line = lines[end];
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
                break;
            end++;
        }

        var replacement = NormalizeDescription(description).Split('\n').Select(line => "\t" + line);
        lines.RemoveRange(start + 1, end - start - 1);
        lines.InsertRange(start + 1, replacement);
        return string.Join('\n', lines);
    }

    private List<string> NormalizePaths(IReadOnlyList<string> paths)
    {
        var result = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            var full = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(options.WorkspacePath, path));
            if (!IsInside(options.WorkspacePath, full))
                throw new InvalidOperationException($"Path escapes workspace: {path}");
            result.Add(full);
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string NormalizeChangelist(string? changelist)
    {
        var value = changelist?.Trim();
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, PerforceChangelistIds.Default, StringComparison.OrdinalIgnoreCase))
        {
            return PerforceChangelistIds.Default;
        }

        if (!value.All(char.IsDigit))
            throw new ArgumentException("Perforce changelist must be 'default' or a numbered changelist id.");
        return value;
    }

    private static string NormalizeDescription(string description)
    {
        var value = description.Trim();
        return string.IsNullOrWhiteSpace(value) ? "DotCraft changes" : value.Replace("\r\n", "\n");
    }

    private static string? ParseCreatedChangeId(string text)
    {
        var match = CreatedChangeRegex.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ParseInfoValue(string stdout, string plainKey, string taggedKey)
    {
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith(plainKey + ":", StringComparison.OrdinalIgnoreCase))
                return line[(plainKey.Length + 1)..].Trim();
            var taggedPrefix = "... " + taggedKey + " ";
            if (line.StartsWith(taggedPrefix, StringComparison.OrdinalIgnoreCase))
                return line[taggedPrefix.Length..].Trim();
        }
        return null;
    }

    private static bool IsInside(string root, string candidate)
    {
        var r = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(r, c, cmp)
            || c.StartsWith(r + Path.DirectorySeparatorChar, cmp)
            || c.StartsWith(r + Path.AltDirectorySeparatorChar, cmp);
    }

    private static void ThrowIfFailed(PerforceCommandResult result, string fallback)
    {
        if (result.Ok)
            return;
        throw new InvalidOperationException(FailureText(result, fallback));
    }

    private static PerforceChangelistPrepareResult Error(string code, string fallback, string changelist) => new()
    {
        Status = "error",
        Code = code,
        Changelist = changelist,
        Errors = [new PerforceChangelistDiagnostic(code, fallback)]
    };

    private static string ClassifyFailureCode(PerforceCommandResult result)
    {
        if (result.ExecutableMissing)
            return PerforceChangelistCodes.P4ExecutableNotFound;
        if (result.TimedOut)
            return PerforceChangelistCodes.Timeout;
        var text = result.StdOut + "\n" + result.StdErr;
        if (text.Contains("login", StringComparison.OrdinalIgnoreCase)
            || text.Contains("password", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ticket", StringComparison.OrdinalIgnoreCase))
        {
            return PerforceChangelistCodes.LoginRequired;
        }
        if (text.Contains("no such changelist", StringComparison.OrdinalIgnoreCase)
            || text.Contains("unknown changelist", StringComparison.OrdinalIgnoreCase))
        {
            return PerforceChangelistCodes.ChangelistNotFound;
        }
        return PerforceChangelistCodes.P4CommandFailed;
    }

    private static string FailureText(PerforceCommandResult result, string fallback)
    {
        if (result.ExecutableMissing)
            return "p4 was not found in the server environment.";
        if (result.TimedOut)
            return "The Perforce command timed out.";
        var detail = FirstNonEmptyLine(result.StdErr) ?? FirstNonEmptyLine(result.StdOut);
        return string.IsNullOrWhiteSpace(detail) ? fallback : detail;
    }

    private static string? FirstNonEmptyLine(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 0)
                return line;
        }
        return null;
    }

    private readonly record struct OpenedChangelistLookup(string? Changelist, PerforceCommandResult? Failure);

    private readonly record struct PerforceIdentityFilters(string User, string Client);
}
