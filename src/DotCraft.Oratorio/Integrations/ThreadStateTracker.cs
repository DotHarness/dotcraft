using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Services;

namespace DotCraft.Oratorio.Integrations;

public sealed class ThreadStateTracker(OratorioDbContext db, IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ThreadStateInput> DescribeAsync(OratorioItem item, string? headSha, string? threadId, CancellationToken ct)
    {
        var current = await LoadCurrentAsync(item, headSha, ct);
        var told = string.IsNullOrWhiteSpace(threadId)
            ? null
            : await db.ThreadStates.AsNoTracking().FirstOrDefaultAsync(x => x.ThreadId == threadId, ct);
        var section = told is null ? RenderFull(current) : RenderChanges(ToState(told), current);
        return new ThreadStateInput(current, section);
    }

    public async Task RecordAsync(string threadId, ItemThreadState state, CancellationToken ct)
    {
        var record = await db.ThreadStates.FirstOrDefaultAsync(x => x.ThreadId == threadId, ct);
        if (record is null)
        {
            record = new OratorioThreadState { ThreadId = threadId };
            db.ThreadStates.Add(record);
        }

        record.ItemId = state.ItemId;
        record.HeadSha = state.HeadSha;
        record.BaseSha = state.BaseSha;
        record.SourceState = state.SourceState;
        record.IsDraft = state.IsDraft;
        record.OpenFindingsJson = JsonSerializer.Serialize(state.OpenFindings, JsonOptions);
        record.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<ItemThreadState> LoadCurrentAsync(OratorioItem item, string? headSha, CancellationToken ct)
    {
        var findings = await db.ReviewDraftComments.AsNoTracking()
            .Where(c =>
                c.Draft!.ItemId == item.ItemId &&
                c.Draft.Status == ReviewDraftStatus.Published &&
                c.Status == ReviewDraftCommentStatus.Accepted &&
                c.ResolutionState == ReviewFindingResolutionState.Open)
            .OrderBy(c => c.Draft!.CreatedAt)
            .ThenBy(c => c.Path)
            .ThenBy(c => c.Line)
            .Select(c => new ThreadStateFinding(c.DraftCommentId, c.Severity, c.Title, c.Path, c.Line))
            .ToListAsync(ct);
        if (item.Kind != ItemKind.PullRequest)
        {
            return new ItemThreadState(item.ItemId, null, null, null, null, findings);
        }

        var snapshots = await db.SourceSnapshots.AsNoTracking()
            .Where(x => x.ItemId == item.ItemId && x.HeadSha == headSha)
            .OrderByDescending(x => x.SyncedAt)
            .Take(20)
            .ToListAsync(ct);
        var baseSha = AppServerPromptBuilder.ResolveReviewDiffTarget(item, headSha, snapshots)?.BaseSha;
        return new ItemThreadState(item.ItemId, headSha, baseSha, item.SourceState, item.IsDraft, findings);
    }

    private static ItemThreadState ToState(OratorioThreadState record) =>
        new(
            record.ItemId,
            record.HeadSha,
            record.BaseSha,
            record.SourceState,
            record.IsDraft,
            JsonSerializer.Deserialize<List<ThreadStateFinding>>(record.OpenFindingsJson, JsonOptions) ?? []);

    private static string? RenderFull(ItemThreadState current)
    {
        var lines = new List<string>();
        if (current.HeadSha is not null)
        {
            lines.Add($"- Head: {current.HeadSha}");
        }
        if (current.BaseSha is not null)
        {
            lines.Add($"- Base: {current.BaseSha}");
        }
        if (current.SourceState is not null)
        {
            lines.Add($"- State: {FormatState(current.SourceState)}");
        }
        if (current.IsDraft is not null)
        {
            lines.Add($"- Draft: {FormatDraft(current.IsDraft)}");
        }
        if (current.OpenFindings.Count > 0)
        {
            lines.Add("- Open published findings:");
            lines.AddRange(current.OpenFindings.Select(FormatFinding));
        }

        return Render("Item state:", lines);
    }

    private static string? RenderChanges(ItemThreadState told, ItemThreadState current)
    {
        var lines = new List<string>();
        if (!string.Equals(told.HeadSha, current.HeadSha, StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"- Head: {told.HeadSha ?? "none"} -> {current.HeadSha ?? "none"}");
        }
        if (!string.Equals(told.BaseSha, current.BaseSha, StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"- Base: {told.BaseSha ?? "none"} -> {current.BaseSha ?? "none"}");
        }
        if (told.SourceState != current.SourceState)
        {
            lines.Add($"- State: {FormatState(told.SourceState)} -> {FormatState(current.SourceState)}");
        }
        if (told.IsDraft != current.IsDraft)
        {
            lines.Add($"- Draft: {FormatDraft(told.IsDraft)} -> {FormatDraft(current.IsDraft)}");
        }

        var toldIds = told.OpenFindings.Select(x => x.FindingId).ToHashSet(StringComparer.Ordinal);
        var currentIds = current.OpenFindings.Select(x => x.FindingId).ToHashSet(StringComparer.Ordinal);
        var opened = current.OpenFindings.Where(x => !toldIds.Contains(x.FindingId)).ToList();
        var closed = told.OpenFindings.Where(x => !currentIds.Contains(x.FindingId)).ToList();
        if (opened.Count > 0)
        {
            lines.Add("- Newly open findings:");
            lines.AddRange(opened.Select(FormatFinding));
        }
        if (closed.Count > 0)
        {
            lines.Add("- No longer open:");
            lines.AddRange(closed.Select(x => $"  - [{x.FindingId}] {x.Title}"));
        }

        return Render("Item state changes:", lines);
    }

    private static string? Render(string heading, List<string> lines)
    {
        if (lines.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine(heading);
        foreach (var line in lines)
        {
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private static string FormatFinding(ThreadStateFinding finding) =>
        $"  - [{finding.FindingId}] {finding.Severity} {finding.Title} ({finding.Path}:{finding.Line})";

    private static string FormatState(SourceState? state) =>
        state?.ToString().ToLowerInvariant() ?? "none";

    private static string FormatDraft(bool? isDraft) =>
        isDraft switch
        {
            true => "yes",
            false => "no",
            null => "none"
        };
}

public sealed record ItemThreadState(
    string ItemId,
    string? HeadSha,
    string? BaseSha,
    SourceState? SourceState,
    bool? IsDraft,
    IReadOnlyList<ThreadStateFinding> OpenFindings);

public sealed record ThreadStateFinding(string FindingId, string Severity, string Title, string Path, int Line);

public sealed record ThreadStateInput(ItemThreadState State, string? Section);
