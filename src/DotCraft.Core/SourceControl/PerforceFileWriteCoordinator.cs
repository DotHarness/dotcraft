using System.Text.RegularExpressions;

namespace DotCraft.SourceControl;

/// <summary>
/// Coordinates FileTools writes with Perforce opened-file state.
/// </summary>
public sealed class PerforceFileWriteCoordinator(
    IPerforceCommandRunner runner,
    PerforceWorkspaceCommandOptions options,
    Func<string> changelistProvider) : ISourceControlWriteCoordinator
{
    private static readonly Regex OpenedChangeRegex = new(@"\bchange\s+(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<SourceControlWriteResult> BeforeWriteAsync(string fullPath, bool fileExists, CancellationToken ct = default)
    {
        var target = PerforceChangelistManager.NormalizeChangelist(changelistProvider());
        if (target != PerforceChangelistIds.Default)
        {
            var targetError = await ValidateChangelistAsync(target, ct).ConfigureAwait(false);
            if (targetError != null)
                return targetError;
        }

        if (!fileExists)
            return SourceControlWriteResult.Ok();

        var opened = await GetOpenedChangelistAsync(fullPath, ct).ConfigureAwait(false);
        if (opened is { Length: > 0 })
        {
            if (opened != PerforceChangelistIds.Default
                && !string.Equals(opened, target, StringComparison.Ordinal))
            {
                return SourceControlWriteResult.Ok(
                    $"Perforce: file is already opened in changelist {opened}; DotCraft left it there.");
            }

            return SourceControlWriteResult.Ok();
        }

        var args = new List<string>(BuildGlobals()) { "edit" };
        AddChangelistOption(args, target);
        args.Add(fullPath);
        var edit = await runner.RunAsync(args, null, ct).ConfigureAwait(false);
        if (!edit.Ok)
            return SourceControlWriteResult.Error(MapFailure(edit, "Perforce edit failed before writing the file."));
        return SourceControlWriteResult.Ok();
    }

    public async Task<SourceControlWriteResult> AfterWriteAsync(string fullPath, bool fileExistedBefore, CancellationToken ct = default)
    {
        if (fileExistedBefore)
            return SourceControlWriteResult.Ok();

        var target = PerforceChangelistManager.NormalizeChangelist(changelistProvider());
        var args = new List<string>(BuildGlobals()) { "add" };
        AddChangelistOption(args, target);
        args.Add(fullPath);
        var add = await runner.RunAsync(args, null, ct).ConfigureAwait(false);
        if (!add.Ok)
            return SourceControlWriteResult.Ok(MapFailure(add, "Perforce add failed after writing the file."));
        return SourceControlWriteResult.Ok();
    }

    private async Task<SourceControlWriteResult?> ValidateChangelistAsync(string changelist, CancellationToken ct)
    {
        var result = await runner.RunAsync([.. BuildGlobals(), "change", "-o", changelist], null, ct).ConfigureAwait(false);
        if (result.Ok)
            return null;
        return SourceControlWriteResult.Error(MapFailure(result, $"Unable to read Perforce changelist {changelist}."));
    }

    private async Task<string?> GetOpenedChangelistAsync(string fullPath, CancellationToken ct)
    {
        var opened = await runner.RunAsync([.. BuildGlobals(), "opened", fullPath], null, ct).ConfigureAwait(false);
        var text = opened.StdOut + "\n" + opened.StdErr;
        if (!opened.Ok && text.Contains("not opened", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!opened.Ok)
            return null;
        if (text.Contains("default change", StringComparison.OrdinalIgnoreCase))
            return PerforceChangelistIds.Default;
        var match = OpenedChangeRegex.Match(text);
        return match.Success ? match.Groups[1].Value : null;
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

    private static void AddChangelistOption(List<string> args, string changelist)
    {
        if (changelist == PerforceChangelistIds.Default)
            return;
        args.Add("-c");
        args.Add(changelist);
    }

    private static void AddOption(List<string> args, string flag, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        args.Add(flag);
        args.Add(value.Trim());
    }

    private static string MapFailure(PerforceCommandResult result, string fallback)
    {
        if (result.ExecutableMissing)
            return "p4 was not found in the server environment.";
        if (result.TimedOut)
            return "The Perforce command timed out.";
        var text = FirstNonEmptyLine(result.StdErr) ?? FirstNonEmptyLine(result.StdOut);
        if (text?.Contains("no such changelist", StringComparison.OrdinalIgnoreCase) == true)
            return "The selected Perforce changelist was not found.";
        if (text?.Contains("login", StringComparison.OrdinalIgnoreCase) == true
            || text?.Contains("ticket", StringComparison.OrdinalIgnoreCase) == true)
            return "Perforce login is required before writing files.";
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
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
}
