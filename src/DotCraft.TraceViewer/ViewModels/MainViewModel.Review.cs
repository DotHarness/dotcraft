using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DotCraft.TraceViewer.Analysis;
using DotCraft.TraceViewer.Services;

namespace DotCraft.TraceViewer.ViewModels;

public sealed partial class MainViewModel
{
    private TraceSnapshot? _reviewSnapshot;
    private TraceReview? _review;
    private readonly List<TraceConversationMessage> _conversation = [];
    private ReviewOperation? _activeReviewOperation;

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

    public bool CanAnalyze => SelectedSession is not null
                              && !IsBusy
                              && !IsReviewBusy
                              && _activeReviewOperation is null;

    public bool CanAskReview => _review is not null
                                && _reviewSnapshot is not null
                                && !IsBusy
                                && !IsReviewBusy
                                && _activeReviewOperation is null
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

    public Task AnalyzeTraceAsync()
    {
        if (_source is null || SelectedSession is null || !CanAnalyze)
            return Task.CompletedTask;

        var operation = BeginReviewOperation(_source, WorkspacePath, SelectedSession.SessionKey);
        operation.Completion = AnalyzeTraceCoreAsync(operation);
        return operation.Completion;
    }

    private async Task AnalyzeTraceCoreAsync(ReviewOperation operation)
    {
        ReviewStatus = TraceReviewStatus.Analyzing;
        ReviewError = string.Empty;
        ReviewProgressText = "Preparing trace evidence…";
        try
        {
            var snapshot = await Task.Run(
                () => operation.Source.CreateSnapshot(operation.SessionKey),
                operation.Cancellation.Token);
            var progress = new Progress<string>(message =>
            {
                if (IsCurrent(operation)) ReviewProgressText = message;
            });
            var review = await _analyst.AnalyzeAsync(
                snapshot,
                operation.DataPath,
                progress,
                operation.Cancellation.Token);
            if (!IsCurrent(operation))
                return;

            _reviewSnapshot = snapshot;
            _review = review;
            _conversation.Clear();
            _reviewStore.Save(operation.WorkspacePath, new StoredTraceReview(review, snapshot, _conversation));
            _analyst.CommitEvidence(snapshot);
            ApplyReview(review);
            ReviewProgressText = string.Empty;
            ReviewStatus = TraceReviewStatus.Ready;
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(operation)) ReviewStatus = TraceReviewStatus.Cancelled;
        }
        catch (Exception exception)
        {
            if (IsCurrent(operation))
            {
                ReviewError = exception.Message;
                ReviewStatus = IsProviderFailure(exception)
                    ? TraceReviewStatus.ProviderUnavailable
                    : TraceReviewStatus.Failed;
            }
        }
        finally
        {
            CompleteReviewOperation(operation);
        }
    }

    public Task AskReviewAsync()
    {
        if (_source is null || _review is null || _reviewSnapshot is null || !CanAskReview)
            return Task.CompletedTask;

        var question = ReviewQuestion.Trim();
        var attachment = ReviewAttachment;
        var review = _review;
        var snapshot = _reviewSnapshot;
        ReviewQuestion = string.Empty;
        var userMessage = new TraceConversationMessage("You", question, DateTimeOffset.Now);
        _conversation.Add(userMessage);
        ReviewMessages.Add(PresentMessage(userMessage));
        SaveReviewState();
        var operation = BeginReviewOperation(_source, WorkspacePath, review.SessionKey);
        operation.Completion = AskReviewCoreAsync(operation, review, snapshot, question, attachment);
        return operation.Completion;
    }

    private async Task AskReviewCoreAsync(
        ReviewOperation operation,
        TraceReview review,
        TraceSnapshot snapshot,
        string question,
        string attachment)
    {
        ReviewStatus = TraceReviewStatus.Analyzing;
        ReviewProgressText = "Preparing follow-up…";
        try
        {
            var progress = new Progress<string>(message =>
            {
                if (IsCurrent(operation)) ReviewProgressText = message;
            });
            var answer = await _analyst.AskAsync(
                snapshot,
                operation.DataPath,
                review,
                question,
                attachment,
                progress,
                operation.Cancellation.Token);
            if (!IsCurrent(operation))
                return;

            var analystMessage = new TraceConversationMessage("Analyst", answer, DateTimeOffset.Now);
            _conversation.Add(analystMessage);
            ReviewMessages.Add(PresentMessage(analystMessage));
            SaveReviewState();
            ReviewAttachment = string.Empty;
            ReviewProgressText = string.Empty;
            var currentSnapshot = await Task.Run(
                () => operation.Source.CreateSnapshot(operation.SessionKey),
                operation.Cancellation.Token);
            if (!IsCurrent(operation))
                return;
            ReviewStatus = !string.Equals(review.Revision, currentSnapshot.Revision, StringComparison.Ordinal)
                ? TraceReviewStatus.TraceUpdated
                : TraceReviewStatus.Ready;
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(operation)) ReviewStatus = TraceReviewStatus.Cancelled;
        }
        catch (Exception exception)
        {
            if (IsCurrent(operation))
            {
                ReviewError = exception.Message;
                ReviewStatus = TraceReviewStatus.Failed;
            }
        }
        finally
        {
            CompleteReviewOperation(operation);
        }
    }

    public void CancelAnalysis() => CancelReviewOperation(invalidate: false);

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

        var range = TrajectoryProjection.ResolveRange(
            TimelineMarkers,
            TimelineScaleMode,
            startRatio,
            endRatio);
        if (range is not null)
            ReviewAttachment = $"Timeline range {range.Value.StartId} → {range.Value.EndId}";
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

    private ReviewOperation BeginReviewOperation(
        WorkspaceTraceSource source,
        string workspacePath,
        string sessionKey)
    {
        var operation = new ReviewOperation(source, workspacePath, sessionKey);
        _activeReviewOperation = operation;
        OnPropertyChanged(nameof(CanAnalyze));
        OnPropertyChanged(nameof(CanAskReview));
        return operation;
    }

    private bool IsCurrent(ReviewOperation operation) =>
        ReferenceEquals(_activeReviewOperation, operation)
        && !operation.Invalidated
        && string.Equals(WorkspacePath, operation.WorkspacePath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(SelectedSession?.SessionKey, operation.SessionKey, StringComparison.Ordinal);

    private void CancelReviewOperation(bool invalidate)
    {
        var operation = _activeReviewOperation;
        if (operation is null)
            return;

        if (invalidate)
            operation.Invalidated = true;
        operation.Cancellation.Cancel();
        _analyst.Cancel();
    }

    private async Task StopReviewOperationAsync()
    {
        var operation = _activeReviewOperation;
        if (operation is null)
            return;

        CancelReviewOperation(invalidate: true);
        await operation.Completion;
    }

    private void CompleteReviewOperation(ReviewOperation operation)
    {
        if (!ReferenceEquals(_activeReviewOperation, operation))
            return;

        _activeReviewOperation = null;
        operation.Cancellation.Dispose();
        OnPropertyChanged(nameof(CanAnalyze));
        OnPropertyChanged(nameof(CanAskReview));
    }

    private ReviewMessageItem PresentMessage(TraceConversationMessage message) => new(
        message,
        _reviewSnapshot!.EventsById.Keys.ToHashSet(StringComparer.Ordinal));

    private sealed class ReviewOperation(
        WorkspaceTraceSource source,
        string workspacePath,
        string sessionKey)
    {
        public WorkspaceTraceSource Source { get; } = source;
        public string WorkspacePath { get; } = workspacePath;
        public string DataPath { get; } = source.DataPath;
        public string SessionKey { get; } = sessionKey;
        public CancellationTokenSource Cancellation { get; } = new();
        public Task Completion { get; set; } = Task.CompletedTask;
        public bool Invalidated { get; set; }
    }
}
