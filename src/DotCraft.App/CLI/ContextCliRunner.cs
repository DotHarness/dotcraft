using System.Text;
using System.Text.Json;
using DotCraft.ContextExport;

namespace DotCraft.CLI;

internal sealed record ContextExportCommandOptions(
    string ThreadId,
    string? WorkspacePath,
    string? OutputPath,
    string? Profile,
    string? ToolResults,
    string? History);

internal sealed record ContextSearchCommandOptions(
    string Query,
    string? WorkspacePath,
    int? Limit,
    string? Status,
    bool Json);

internal static class ContextCliRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web)
    {
        WriteIndented = true
    };

    public static Task<int> ExportAsync(
        ContextExportCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct = default) => ExecuteAsync(
            () => ExportCoreAsync(options, output, error, ct), error);

    public static Task<int> SearchAsync(
        ContextSearchCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct = default) => ExecuteAsync(
            () => SearchCoreAsync(options, output, error, ct), error);

    private static async Task<int> ExecuteAsync(Func<Task<int>> action, TextWriter error)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Context command cancelled.").ConfigureAwait(false);
            return 130;
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> ExportCoreAsync(
        ContextExportCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct)
    {
        var service = new ContextExportService();
        var result = await service.ExportAsync(new ContextExportOptions
        {
            ThreadId = options.ThreadId.Trim(),
            WorkspacePath = options.WorkspacePath,
            Profile = ParseEnum(options.Profile, ContextExportProfile.Handoff, "--profile"),
            ToolResults = ParseEnum(options.ToolResults, ContextExportToolResultMode.Summary, "--tool-results"),
            History = ParseEnum(options.History, ContextExportHistoryMode.Tail, "--history")
        }, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            await output.WriteAsync(result.Markdown).ConfigureAwait(false);
            return 0;
        }

        var outputPath = Path.GetFullPath(options.OutputPath);
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
            Directory.CreateDirectory(outputDir);

        await File.WriteAllTextAsync(outputPath, result.Markdown, Encoding.UTF8, ct).ConfigureAwait(false);
        await output.WriteLineAsync($"Wrote context export: {outputPath}").ConfigureAwait(false);
        foreach (var warning in result.Warnings)
            await error.WriteLineAsync($"Warning: {warning}").ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> SearchCoreAsync(
        ContextSearchCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct)
    {
        var service = new ContextSearchService();
        var result = await service.SearchAsync(new ContextSearchOptions
        {
            WorkspacePath = options.WorkspacePath,
            Query = options.Query.Trim(),
            Limit = options.Limit ?? 10,
            Status = ParseEnum(options.Status, ContextSearchStatusFilter.All, "--status")
        }, ct).ConfigureAwait(false);

        if (options.Json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions)).ConfigureAwait(false);
            return 0;
        }

        await WriteSearchMarkdownAsync(result, output).ConfigureAwait(false);
        foreach (var warning in result.Warnings)
            await error.WriteLineAsync($"Warning: {warning}").ConfigureAwait(false);
        return 0;
    }

    private static async Task WriteSearchMarkdownAsync(ContextSearchResult result, TextWriter output)
    {
        await output.WriteLineAsync($"# DotCraft Context Search").ConfigureAwait(false);
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync($"Query: `{result.Query}`").ConfigureAwait(false);
        await output.WriteLineAsync().ConfigureAwait(false);

        if (result.Hits.Count == 0)
        {
            await output.WriteLineAsync("No matching sessions found.").ConfigureAwait(false);
            return;
        }

        for (var i = 0; i < result.Hits.Count; i++)
        {
            var hit = result.Hits[i];
            await output.WriteLineAsync($"## {i + 1}. {hit.ThreadId}").ConfigureAwait(false);
            await output.WriteLineAsync($"- Score: {hit.Score}").ConfigureAwait(false);
            await output.WriteLineAsync($"- Status: {hit.Status}").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(hit.DisplayName))
                await output.WriteLineAsync($"- Display Name: {hit.DisplayName}").ConfigureAwait(false);
            if (hit.LastActiveAt.HasValue)
                await output.WriteLineAsync($"- Last Active: {hit.LastActiveAt.Value:O}").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(hit.RolloutPath))
                await output.WriteLineAsync($"- Rollout: `{hit.RolloutPath}`").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(hit.ExportCommand))
                await output.WriteLineAsync($"- Export: `{hit.ExportCommand}`").ConfigureAwait(false);

            await output.WriteLineAsync("- Evidence:").ConfigureAwait(false);
            foreach (var evidence in hit.Evidence)
            {
                var timestamp = evidence.Timestamp.HasValue ? $" {evidence.Timestamp.Value:O}" : string.Empty;
                await output.WriteLineAsync($"  - `{evidence.Source}` `{evidence.SourceId}`{timestamp}: {evidence.Preview}").ConfigureAwait(false);
            }

            await output.WriteLineAsync().ConfigureAwait(false);
        }
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum defaultValue, string optionName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            var candidateName = candidate.ToString()
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal);
            if (string.Equals(candidateName, normalized, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        throw new ArgumentException($"Invalid value for {optionName}: {value}");
    }

}
