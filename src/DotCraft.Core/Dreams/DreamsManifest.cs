using System.Globalization;
using System.Text;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Memory;

namespace DotCraft.Dreams;

internal static class DreamsManifest
{
    public static string Write(
        DreamsArtifacts artifacts,
        DreamsRunState state,
        DateTimeOffset? lastConsolidatedAt,
        IReadOnlyList<MemoryFileInfo> files,
        IReadOnlyList<DreamsThreadInput> threads)
    {
        var index = MemoryIndex.Load(artifacts.BaselineDirectory(state.Id));
        var manifest = Build(state, lastConsolidatedAt, files, index, threads);
        Directory.CreateDirectory(artifacts.InputDirectory(state.Id));
        File.WriteAllText(artifacts.ManifestPath(state.Id), manifest, Encoding.UTF8);
        var input = new
        {
            runId = state.Id,
            scope = state.Scope,
            lastConsolidatedAt,
            files,
            index = index.Content,
            threads,
            instructions = state.Instructions
        };
        File.WriteAllText(
            Path.Combine(artifacts.InputDirectory(state.Id), "input.json"),
            JsonSerializer.Serialize(input, AppConfig.SerializerOptions) + Environment.NewLine,
            Encoding.UTF8);
        return manifest;
    }

    private static string Build(
        DreamsRunState state,
        DateTimeOffset? lastConsolidatedAt,
        IReadOnlyList<MemoryFileInfo> files,
        MemoryIndex index,
        IReadOnlyList<DreamsThreadInput> threads)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Dream run manifest")
            .AppendLine()
            .Append("- Run: ").AppendLine(state.Id)
            .Append("- Store: ").AppendLine(state.Scope == null ? "the workspace store" : $"the own store of scope `{state.Scope}`")
            .Append("- Last consolidated: ").AppendLine(lastConsolidatedAt is { } last ? FormatTime(last) : "never")
            .AppendLine()
            .AppendLine("## Memory files")
            .AppendLine();
        if (files.Count == 0)
            sb.AppendLine("(none)");
        foreach (var file in files)
        {
            sb.Append("- ");
            if (file.Type != null)
                sb.Append('[').Append(file.Type).Append("] ");
            sb.Append(file.FileName).Append(" (").Append(FormatDate(file.LastModified)).Append(')');
            if (file.Description != null)
                sb.Append(": ").Append(file.Description);
            sb.AppendLine();
        }

        sb.AppendLine()
            .AppendLine("## MEMORY.md")
            .AppendLine()
            .AppendLine(index.IsEmpty && index.Notices.Count == 0 ? "(empty)" : index.Text)
            .AppendLine()
            .AppendLine("## Recent threads")
            .AppendLine()
            .AppendLine("Threads that used this store and completed a turn since the last consolidation, most recent first.")
            .AppendLine();
        if (threads.Count == 0)
            sb.AppendLine("(none)");
        foreach (var thread in threads)
        {
            sb.Append("- ").Append(thread.ThreadId)
                .Append(" | ").Append(thread.DisplayName ?? "(untitled)")
                .Append(" | ").Append(thread.OriginChannel)
                .Append(" | ").Append(thread.Status.ToString().ToLowerInvariant())
                .Append(" | created ").Append(FormatTime(thread.CreatedAt))
                .Append(" | last active ").AppendLine(FormatTime(thread.LastActiveAt));
        }

        return sb.ToString();
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
