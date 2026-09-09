using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotCraft.GeneratedTools.TraceViewer;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.TraceViewer.Analysis;

internal sealed class TraceAnalysisContext
{
    public TraceSnapshot? Snapshot { get; set; }
    public string? AnalystThreadId { get; set; }
    public string? ModelId { get; set; }
    public TraceReview? SubmittedReview { get; set; }
    public IProgress<string>? Progress { get; set; }
}

internal sealed class TraceReviewSubmissionToolSource(TraceAnalysisContext context) : AIFunctionToolSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public override string SourceId => "trace-viewer.trace-review-submission";

    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext planningContext)
    {
        yield return GeneratedToolFunctions.TraceReviewSubmissionToolSource_SubmitTraceReview(this);
    }

    [GeneratedTool]
    [Description("Submits the final Trace Review. Every finding must cite valid Event evidence from the current Evidence Bundle. If rejected, correct the structured input and submit again.")]
    internal string SubmitTraceReview(
        [Description("A concise overall assessment of the recorded Session Trace.")] string summary,
        [Description("Evidence-linked findings. Use an empty array when no supported findings exist.")] TraceFindingSubmission[] findings)
    {
        context.Progress?.Report("Validating review findings…");
        try
        {
            var snapshot = context.Snapshot
                ?? throw new InvalidOperationException("No Trace snapshot is active.");
            if (findings is null)
                throw new InvalidDataException("Review findings are required. Use an empty array when there are no findings.");
            if (string.IsNullOrWhiteSpace(summary))
                throw new InvalidDataException("Review summary is required.");

            var reviewFindings = TraceReviewValidator.ValidateAndOrder(
                findings.Select(ToFinding).ToArray(), snapshot);
            context.SubmittedReview = new TraceReview(
                SchemaVersion: 1,
                SessionKey: snapshot.SessionKey,
                Revision: snapshot.Revision,
                GeneratedAt: DateTimeOffset.UtcNow,
                ModelId: context.ModelId ?? throw new InvalidOperationException("Analyst model is not available."),
                Summary: summary,
                Findings: reviewFindings,
                AnalystThreadId: context.AnalystThreadId
                    ?? throw new InvalidOperationException("Analyst thread is not active."));
            return JsonSerializer.Serialize(new { Accepted = true, Message = "Review accepted." }, JsonOptions);
        }
        catch (InvalidDataException exception)
        {
            return ReviewRejected(exception.Message);
        }
    }

    private static TraceFinding ToFinding(TraceFindingSubmission finding)
    {
        if (finding is null)
            throw new InvalidDataException("Review findings cannot contain null values.");
        if (finding.Evidence is null)
            throw new InvalidDataException($"Finding '{finding.Id}' requires evidence.");

        return new TraceFinding(
            finding.Id,
            finding.Severity,
            DimensionValue(finding.Dimension),
            finding.Title,
            finding.Body,
            finding.Impact,
            finding.Recommendation,
            finding.Basis,
            finding.Evidence.Select(ToEvidence).ToArray());
    }

    private static string DimensionValue(TraceFindingDimension value) => value switch
    {
        TraceFindingDimension.Reliability => "Reliability",
        TraceFindingDimension.Latency => "Latency",
        TraceFindingDimension.ToolBehavior => "Tool behavior",
        TraceFindingDimension.TokenEfficiency => "Token efficiency",
        TraceFindingDimension.PromptCache => "Prompt cache",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static TraceEvidenceReference ToEvidence(TraceEvidenceSubmission evidence)
    {
        if (evidence is null)
            throw new InvalidDataException("Finding evidence cannot contain null values.");
        return new TraceEvidenceReference(evidence.EventId, evidence.EndEventId, evidence.Label);
    }

    private static string ReviewRejected(string message) => JsonSerializer.Serialize(new
    {
        Error = "review_rejected",
        Message = message,
        NextAction = "Correct the structured review input and call SubmitTraceReview again."
    }, JsonOptions);
}
