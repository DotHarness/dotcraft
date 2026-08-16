using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DotCraft.TraceViewer.Analysis;

namespace DotCraft.TraceViewer.ViewModels;

public sealed partial class MainViewModel
{
    private TraceSnapshot? _reviewSnapshot;
    private TraceReview? _review;
    private readonly List<TraceConversationMessage> _conversation = [];

    public ObservableCollection<ReviewFindingItem> ReviewFindings { get; } = [];

    public ObservableCollection<ReviewMessageItem> ReviewMessages { get; } = [];

    public bool HasReviewMessages => ReviewMessages.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAnalyze))]
    [NotifyPropertyChangedFor(nameof(CanAskReview))]
    public partial TraceReviewStatus ReviewStatus { get; set; } = TraceReviewStatus.NotAnalyzed;

    [ObservableProperty]
    public partial string ReviewSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReviewModel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReviewGeneratedAt { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReviewError { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReviewProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReviewQuestion { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReviewAttachment { get; set; } = string.Empty;

    public bool HasReview => _review is not null;

    public bool HasNoReview => _review is null;

    public bool IsReviewBusy => ReviewStatus == TraceReviewStatus.Analyzing;

    public bool IsReviewIdle => !IsReviewBusy;

    public bool CanAnalyze => SelectedSession is not null && !IsReviewBusy;

    public bool CanAskReview => _review is not null
                                && _reviewSnapshot is not null
                                && !IsReviewBusy
                                && !string.IsNullOrWhiteSpace(ReviewQuestion);

    public string ReviewStatusLabel => ReviewStatus switch
    {
        TraceReviewStatus.NotAnalyzed => "Not analyzed",
        TraceReviewStatus.Analyzing => "Analyzing trace…",
        TraceReviewStatus.Ready => "Ready",
        TraceReviewStatus.TraceUpdated => "Trace updated",
        TraceReviewStatus.Cancelled => "Cancelled",
        TraceReviewStatus.ProviderUnavailable => "Provider unavailable",
        _ => "Analysis failed"
    };

    public string ReviewCountSummary
    {
        get
        {
            var major = _review?.Findings.Count(item => item.Severity == TraceFindingSeverity.Major) ?? 0;
            var minor = _review?.Findings.Count(item => item.Severity == TraceFindingSeverity.Minor) ?? 0;
            var suggestion = _review?.Findings.Count(item => item.Severity == TraceFindingSeverity.Suggestion) ?? 0;
            return $"{major} Major · {minor} Minor · {suggestion} Suggestions";
        }
    }

    partial void OnReviewQuestionChanged(string value) => OnPropertyChanged(nameof(CanAskReview));

    partial void OnReviewStatusChanged(TraceReviewStatus value)
    {
        OnPropertyChanged(nameof(ReviewStatusLabel));
        OnPropertyChanged(nameof(IsReviewBusy));
        OnPropertyChanged(nameof(IsReviewIdle));
        OnPropertyChanged(nameof(CanAnalyze));
        OnPropertyChanged(nameof(CanAskReview));
    }

    public void ShowTimeline() => SessionMode = "Timeline";

    public void ShowReview() => SessionMode = "Review";

    public async Task AnalyzeTraceAsync()
    {
        if (_source is null || SelectedSession is null || IsReviewBusy)
            return;

        ReviewStatus = TraceReviewStatus.Analyzing;
        ReviewError = string.Empty;
        ReviewProgressText = "Preparing trace evidence…";
        try
        {
            var snapshot = await Task.Run(() => _source.CreateSnapshot(SelectedSession.SessionKey));
            var progress = new Progress<string>(message => ReviewProgressText = message);
            var review = await _analyst.AnalyzeAsync(snapshot, _source.DataPath, progress, CancellationToken.None);
            _reviewSnapshot = snapshot;
            _review = review;
            _conversation.Clear();
            _reviewStore.Save(WorkspacePath, new StoredTraceReview(review, snapshot, _conversation));
            _analyst.CommitEvidence(snapshot);
            ApplyReview(review);
            ReviewProgressText = string.Empty;
            ReviewStatus = TraceReviewStatus.Ready;
        }
        catch (OperationCanceledException)
        {
            ReviewStatus = TraceReviewStatus.Cancelled;
        }
        catch (Exception exception)
        {
            ReviewError = exception.Message;
            ReviewStatus = IsProviderFailure(exception)
                ? TraceReviewStatus.ProviderUnavailable
                : TraceReviewStatus.Failed;
        }
    }

    public async Task AskReviewAsync()
    {
        if (_source is null || _review is null || _reviewSnapshot is null || !CanAskReview)
            return;

        var question = ReviewQuestion.Trim();
        ReviewQuestion = string.Empty;
        var userMessage = new TraceConversationMessage("You", question, DateTimeOffset.Now);
        _conversation.Add(userMessage);
        ReviewMessages.Add(PresentMessage(userMessage));
        SaveReviewState();
        ReviewStatus = TraceReviewStatus.Analyzing;
        ReviewProgressText = "Preparing follow-up…";
        try
        {
            var progress = new Progress<string>(message => ReviewProgressText = message);
            var answer = await _analyst.AskAsync(
                _reviewSnapshot, _source.DataPath, _review, question, ReviewAttachment, progress, CancellationToken.None);
            var analystMessage = new TraceConversationMessage("Analyst", answer, DateTimeOffset.Now);
            _conversation.Add(analystMessage);
            ReviewMessages.Add(PresentMessage(analystMessage));
            SaveReviewState();
            ReviewAttachment = string.Empty;
            ReviewProgressText = string.Empty;
            var currentSnapshot = await Task.Run(() => _source.CreateSnapshot(_review.SessionKey));
            ReviewStatus = !string.Equals(_review.Revision, currentSnapshot.Revision, StringComparison.Ordinal)
                ? TraceReviewStatus.TraceUpdated
                : TraceReviewStatus.Ready;
        }
        catch (OperationCanceledException)
        {
            ReviewStatus = TraceReviewStatus.Cancelled;
        }
        catch (Exception exception)
        {
            ReviewError = exception.Message;
            ReviewStatus = TraceReviewStatus.Failed;
        }
    }

    public void CancelAnalysis() => _analyst.Cancel();

    public void AttachFinding(ReviewFindingItem finding) =>
        ReviewAttachment = $"Finding {finding.Id} ({finding.Severity}, {finding.Dimension}): {finding.Title}";

    public void ShowEvidence(ReviewEvidenceItem evidence, bool switchToTimeline)
    {
        if (_reviewSnapshot is null)
            return;

        if (_loadedEvents.All(item => item.Id != evidence.EventId))
        {
            _loadedEvents.Clear();
            _loadedEvents.AddRange(_reviewSnapshot.Events);
            _hasOlderEvents = false;
            ApplyEventFilter();
        }
        SelectEvent(evidence.EventId);
        ReviewAttachment = $"Evidence {evidence.RangeLabel}: {evidence.Label}";
        if (switchToTimeline)
            SessionMode = "Timeline";
    }

    public void AttachTimelineRange(double startRatio, double endRatio)
    {
        if (TimelineMarkers.Count == 0 || endRatio - startRatio < 0.005)
            return;

        var ordered = TimelineMarkers.OrderBy(item => item.Start).ToArray();
        var startIndex = Math.Clamp((int)Math.Floor(startRatio * ordered.Length), 0, ordered.Length - 1);
        var endIndex = Math.Clamp((int)Math.Ceiling(endRatio * ordered.Length) - 1, startIndex, ordered.Length - 1);
        ReviewAttachment = $"Timeline range {ordered[startIndex].RowId} → {ordered[endIndex].RowId}";
    }

    private void LoadReview(string sessionKey)
    {
        if (_source is null)
            return;

        var stored = _reviewStore.Load(WorkspacePath, sessionKey);
        if (stored is null)
        {
            ClearReview();
            return;
        }

        _review = stored.Review;
        _reviewSnapshot = stored.Snapshot;
        _conversation.Clear();
        _conversation.AddRange(stored.Conversation);
        ReviewMessages.Clear();
        ApplyReview(_review);
        foreach (var message in _conversation)
            ReviewMessages.Add(PresentMessage(message));

        var currentSnapshot = _source.CreateSnapshot(sessionKey);
        ReviewStatus = !string.Equals(_review.Revision, currentSnapshot.Revision, StringComparison.Ordinal)
            ? TraceReviewStatus.TraceUpdated
            : TraceReviewStatus.Ready;
    }

    private void SaveReviewState()
    {
        if (_review is not null && _reviewSnapshot is not null)
            _reviewStore.Save(WorkspacePath, new StoredTraceReview(_review, _reviewSnapshot, _conversation));
    }

    private void ApplyReview(TraceReview review)
    {
        ReviewSummary = review.Summary;
        ReviewModel = review.ModelId;
        ReviewGeneratedAt = review.GeneratedAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
        ReviewFindings.Clear();
        foreach (var finding in review.Findings)
            ReviewFindings.Add(new ReviewFindingItem(finding));
        OnPropertyChanged(nameof(HasReview));
        OnPropertyChanged(nameof(HasNoReview));
        OnPropertyChanged(nameof(ReviewCountSummary));
        OnPropertyChanged(nameof(CanAskReview));
    }

    private void ClearReview()
    {
        _review = null;
        _reviewSnapshot = null;
        ReviewSummary = string.Empty;
        ReviewFindings.Clear();
        ReviewMessages.Clear();
        _conversation.Clear();
        ReviewError = string.Empty;
        ReviewProgressText = string.Empty;
        ReviewStatus = TraceReviewStatus.NotAnalyzed;
        OnPropertyChanged(nameof(HasReview));
        OnPropertyChanged(nameof(HasNoReview));
        OnPropertyChanged(nameof(ReviewCountSummary));
        OnPropertyChanged(nameof(CanAskReview));
    }

    private static bool IsProviderFailure(Exception exception) =>
        exception.Message.Contains("provider", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("API key", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("credential", StringComparison.OrdinalIgnoreCase);

    private ReviewMessageItem PresentMessage(TraceConversationMessage message) => new(
        message,
        _reviewSnapshot!.EventsById.Keys.ToHashSet(StringComparer.Ordinal));
}
