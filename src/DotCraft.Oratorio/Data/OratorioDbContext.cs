using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DotCraft.Oratorio.Data;

public sealed class OratorioDbContext(DbContextOptions<OratorioDbContext> options) : DbContext(options)
{
    private static readonly ValueConverter<DateTimeOffset, long> DateTimeOffsetConverter =
        new(v => v.ToUnixTimeMilliseconds(), v => DateTimeOffset.FromUnixTimeMilliseconds(v));

    private static readonly ValueConverter<DateTimeOffset?, long?> NullableDateTimeOffsetConverter =
        new(v => v == null ? null : v.Value.ToUnixTimeMilliseconds(), v => v == null ? null : DateTimeOffset.FromUnixTimeMilliseconds(v.Value));

    public DbSet<OratorioItem> Items => Set<OratorioItem>();
    public DbSet<OratorioRound> Rounds => Set<OratorioRound>();
    public DbSet<OratorioRun> Runs => Set<OratorioRun>();
    public DbSet<OratorioAutoReviewRepositoryState> AutoReviewRepositoryStates => Set<OratorioAutoReviewRepositoryState>();
    public DbSet<OratorioAutoReviewItemState> AutoReviewItemStates => Set<OratorioAutoReviewItemState>();
    public DbSet<OratorioGitHubCommentCommand> GitHubCommentCommands => Set<OratorioGitHubCommentCommand>();
    public DbSet<OratorioImplementationFollowUpItemState> ImplementationFollowUpItemStates => Set<OratorioImplementationFollowUpItemState>();
    public DbSet<OratorioComment> Comments => Set<OratorioComment>();
    public DbSet<OratorioDecision> Decisions => Set<OratorioDecision>();
    public DbSet<OratorioTimelineEvent> TimelineEvents => Set<OratorioTimelineEvent>();
    public DbSet<OratorioSourceSnapshot> SourceSnapshots => Set<OratorioSourceSnapshot>();
    public DbSet<OratorioSourceWriteLog> SourceWriteLogs => Set<OratorioSourceWriteLog>();
    public DbSet<OratorioReviewDraft> ReviewDrafts => Set<OratorioReviewDraft>();
    public DbSet<OratorioReviewDraftComment> ReviewDraftComments => Set<OratorioReviewDraftComment>();
    public DbSet<OratorioImplementationDraft> ImplementationDrafts => Set<OratorioImplementationDraft>();
    public DbSet<OratorioFollowUpDraft> FollowUpDrafts => Set<OratorioFollowUpDraft>();
    public DbSet<OratorioDiscussionTurn> DiscussionTurns => Set<OratorioDiscussionTurn>();
    public DbSet<OratorioConfigurationChange> ConfigurationChanges => Set<OratorioConfigurationChange>();
    public DbSet<OratorioTaskShortIdCounter> TaskShortIdCounters => Set<OratorioTaskShortIdCounter>();
    public DbSet<OratorioGitHubSyncJob> GitHubSyncJobs => Set<OratorioGitHubSyncJob>();
    public DbSet<OratorioGitHubSyncRepositoryRun> GitHubSyncRepositoryRuns => Set<OratorioGitHubSyncRepositoryRun>();
    public DbSet<OratorioGitLabSyncJob> GitLabSyncJobs => Set<OratorioGitLabSyncJob>();
    public DbSet<OratorioGitLabSyncProjectRun> GitLabSyncProjectRuns => Set<OratorioGitLabSyncProjectRun>();
    public DbSet<OratorioSourceSyncSchedule> SourceSyncSchedules => Set<OratorioSourceSyncSchedule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureItems(modelBuilder);
        ConfigureRounds(modelBuilder);
        ConfigureRuns(modelBuilder);
        ConfigureAutoReviewRepositoryStates(modelBuilder);
        ConfigureAutoReviewItemStates(modelBuilder);
        ConfigureGitHubCommentCommands(modelBuilder);
        ConfigureImplementationFollowUpItemStates(modelBuilder);
        ConfigureComments(modelBuilder);
        ConfigureDecisions(modelBuilder);
        ConfigureTimelineEvents(modelBuilder);
        ConfigureSourceSnapshots(modelBuilder);
        ConfigureSourceWriteLogs(modelBuilder);
        ConfigureReviewDrafts(modelBuilder);
        ConfigureReviewDraftComments(modelBuilder);
        ConfigureImplementationDrafts(modelBuilder);
        ConfigureFollowUpDrafts(modelBuilder);
        ConfigureDiscussionTurns(modelBuilder);
        ConfigureConfigurationChanges(modelBuilder);
        ConfigureTaskShortIdCounters(modelBuilder);
        ConfigureGitHubSyncJobs(modelBuilder);
        ConfigureGitHubSyncRepositoryRuns(modelBuilder);
        ConfigureGitLabSyncJobs(modelBuilder);
        ConfigureGitLabSyncProjectRuns(modelBuilder);
        ConfigureSourceSyncSchedules(modelBuilder);
    }

    private static void ConfigureItems(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioItem>();
        entity.ToTable("items");
        entity.HasKey(x => x.ItemId);
        entity.Property(x => x.ItemId).HasColumnName("item_id");
        entity.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        entity.Property(x => x.Source).HasColumnName("source").IsRequired();
        entity.Property(x => x.ExternalId).HasColumnName("external_id").IsRequired();
        entity.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().IsRequired();
        entity.Property(x => x.Title).HasColumnName("title").IsRequired();
        entity.Property(x => x.Description).HasColumnName("description");
        entity.Property(x => x.Repository).HasColumnName("repository");
        entity.Property(x => x.Assignee).HasColumnName("assignee");
        entity.Property(x => x.Branch).HasColumnName("branch");
        entity.Property(x => x.ExternalUrl).HasColumnName("external_url");
        entity.Property(x => x.LabelsJson).HasColumnName("labels_json");
        entity.Property(x => x.SourceUpdatedAt).HasColumnName("source_updated_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.IsDraft).HasColumnName("is_draft");
        entity.Property(x => x.HeadSha).HasColumnName("head_sha");
        entity.Property(x => x.SourceState).HasColumnName("source_state").HasConversion<string>().IsRequired();
        entity.Property(x => x.SourceDetailsStatus).HasColumnName("source_details_status").HasConversion<string>().IsRequired();
        entity.Property(x => x.SourceDetailsHydratedAt).HasColumnName("source_details_hydrated_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.SourceDetailsErrorCode).HasColumnName("source_details_error_code");
        entity.Property(x => x.SourceDetailsErrorMessage).HasColumnName("source_details_error_message");
        entity.Property(x => x.SourceClosedAt).HasColumnName("source_closed_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.SourceMergedAt).HasColumnName("source_merged_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.ArchiveReason).HasColumnName("archive_reason").HasConversion<string>();
        entity.Property(x => x.State).HasColumnName("state").HasConversion<string>().IsRequired();
        entity.Property(x => x.CurrentRound).HasColumnName("current_round");
        entity.Property(x => x.CurrentRunId).HasColumnName("current_run_id");
        entity.Property(x => x.LatestSummary).HasColumnName("latest_summary");
        entity.Property(x => x.CheckState).HasColumnName("check_state").HasConversion<string>().IsRequired();
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.LastSourceSyncAt).HasColumnName("last_source_sync_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.ParentItemId).HasColumnName("parent_item_id");
        entity.Property(x => x.GeneratedFromDraftId).HasColumnName("generated_from_draft_id");
        entity.Property(x => x.ShortIdInteger).HasColumnName("short_id_int");
        entity.Property(x => x.ShortId).HasColumnName("short_id");
        entity.Property(x => x.BoardSortOrder).HasColumnName("board_sort_order");
        entity.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
        entity.HasIndex(x => new { x.State, x.UpdatedAt });
        entity.HasIndex(x => new { x.Source, x.State, x.UpdatedAt });
        entity.HasIndex(x => new { x.WorkspaceId, x.BoardSortOrder });
        entity.HasIndex(x => new { x.WorkspaceId, x.ShortId }).IsUnique().HasFilter("short_id IS NOT NULL");
        entity.HasIndex(x => x.ParentItemId);
        entity.HasOne(x => x.ParentItem).WithMany(x => x.ChildItems).HasForeignKey(x => x.ParentItemId);
    }

    private static void ConfigureRounds(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioRound>();
        entity.ToTable("rounds");
        entity.HasKey(x => x.RoundId);
        entity.Property(x => x.RoundId).HasColumnName("round_id");
        entity.Property(x => x.ItemId).HasColumnName("item_id").IsRequired();
        entity.Property(x => x.RoundNumber).HasColumnName("round_number");
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        entity.Property(x => x.PromptContextJson).HasColumnName("prompt_context_json");
        entity.Property(x => x.Summary).HasColumnName("summary");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.HasIndex(x => new { x.ItemId, x.RoundNumber }).IsUnique();
        entity.HasOne(x => x.Item).WithMany(x => x.Rounds).HasForeignKey(x => x.ItemId);
    }

    private static void ConfigureRuns(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioRun>();
        entity.ToTable("runs");
        entity.HasKey(x => x.RunId);
        entity.Property(x => x.RunId).HasColumnName("run_id");
        entity.Property(x => x.ItemId).HasColumnName("item_id").IsRequired();
        entity.Property(x => x.RoundId).HasColumnName("round_id").IsRequired();
        entity.Property(x => x.Attempt).HasColumnName("attempt");
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        entity.Property(x => x.RunnerKind).HasColumnName("runner_kind").IsRequired();
        entity.Property(x => x.ThreadId).HasColumnName("thread_id");
        entity.Property(x => x.TurnId).HasColumnName("turn_id");
        entity.Property(x => x.AppServerEndpoint).HasColumnName("app_server_endpoint");
        entity.Property(x => x.AppServerIdentity).HasColumnName("app_server_identity");
        entity.Property(x => x.PromptContextJson).HasColumnName("prompt_context_json");
        entity.Property(x => x.StartedAt).HasColumnName("started_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.Summary).HasColumnName("summary");
        entity.Property(x => x.ErrorCode).HasColumnName("error_code");
        entity.Property(x => x.ErrorMessage).HasColumnName("error_message");
        entity.Property(x => x.ProgressPercent).HasColumnName("progress_percent");
        entity.Property(x => x.StatusMessage).HasColumnName("status_message");
        entity.Property(x => x.LastHeartbeatAt).HasColumnName("last_heartbeat_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.BaseWorkspacePath).HasColumnName("base_workspace_path");
        entity.Property(x => x.WorktreePath).HasColumnName("worktree_path");
        entity.Property(x => x.WorktreeBranch).HasColumnName("worktree_branch");
        entity.Property(x => x.BaseRef).HasColumnName("base_ref");
        entity.Property(x => x.BaseSha).HasColumnName("base_sha");
        entity.Property(x => x.WorktreeStatus).HasColumnName("worktree_status").HasConversion<string>().IsRequired();
        entity.Property(x => x.WorktreeErrorCode).HasColumnName("worktree_error_code");
        entity.Property(x => x.WorktreeErrorMessage).HasColumnName("worktree_error_message");
        entity.Property(x => x.RetryCount).HasColumnName("retry_count");
        entity.Property(x => x.NextRetryAt).HasColumnName("next_retry_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.LeaseOwner).HasColumnName("lease_owner");
        entity.Property(x => x.LeaseAcquiredAt).HasColumnName("lease_acquired_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.WorktreeCleanupAfterAt).HasColumnName("worktree_cleanup_after_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.WorktreeCleanedAt).HasColumnName("worktree_cleaned_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.MockOutcome).HasColumnName("mock_outcome").HasConversion<string>().IsRequired();
        entity.Property(x => x.MockDurationSeconds).HasColumnName("mock_duration_seconds");
        entity.Property(x => x.Purpose).HasColumnName("purpose").HasConversion<string>().IsRequired();
        entity.Property(x => x.DispatchTrigger).HasColumnName("dispatch_trigger").HasConversion<string>().IsRequired();
        entity.Property(x => x.TargetHeadSha).HasColumnName("target_head_sha");
        entity.Property(x => x.DeliveryPolicy).HasColumnName("delivery_policy").HasConversion<string>().IsRequired();
        entity.Property(x => x.ImplementationTurnCount).HasColumnName("implementation_turn_count");
        entity.HasIndex(x => new { x.RoundId, x.Attempt }).IsUnique();
        entity.HasIndex(x => new { x.ItemId, x.Status });
        entity.HasOne(x => x.Item).WithMany(x => x.Runs).HasForeignKey(x => x.ItemId);
        entity.HasOne(x => x.Round).WithMany().HasForeignKey(x => x.RoundId);
    }

    private static void ConfigureAutoReviewRepositoryStates(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioAutoReviewRepositoryState>();
        entity.ToTable("auto_review_repository_states");
        entity.HasKey(x => x.Repository);
        entity.Property(x => x.Repository).HasColumnName("repository").IsRequired();
        entity.Property(x => x.Enabled).HasColumnName("enabled");
        entity.Property(x => x.InitializedAt).HasColumnName("initialized_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
    }

    private static void ConfigureAutoReviewItemStates(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioAutoReviewItemState>();
        entity.ToTable("auto_review_item_states");
        entity.HasKey(x => x.ItemId);
        entity.Property(x => x.ItemId).HasColumnName("item_id").IsRequired();
        entity.Property(x => x.Repository).HasColumnName("repository").IsRequired();
        entity.Property(x => x.LastObservedHeadSha).HasColumnName("last_observed_head_sha");
        entity.Property(x => x.LastQueuedHeadSha).HasColumnName("last_queued_head_sha");
        entity.Property(x => x.LastQueuedRunId).HasColumnName("last_queued_run_id");
        entity.Property(x => x.LastErrorCode).HasColumnName("last_error_code");
        entity.Property(x => x.LastErrorMessage).HasColumnName("last_error_message");
        entity.Property(x => x.LastErrorAt).HasColumnName("last_error_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
        entity.HasIndex(x => new { x.Repository, x.UpdatedAt });
        entity.HasIndex(x => x.LastQueuedRunId);
        entity.HasOne(x => x.Item).WithOne().HasForeignKey<OratorioAutoReviewItemState>(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.LastQueuedRun).WithMany().HasForeignKey(x => x.LastQueuedRunId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureGitHubCommentCommands(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioGitHubCommentCommand>();
        entity.ToTable("github_comment_commands");
        entity.HasKey(x => x.CommandId);
        entity.Property(x => x.CommandId).HasColumnName("command_id");
        entity.Property(x => x.DeliveryId).HasColumnName("delivery_id");
        entity.Property(x => x.SourceCommentId).HasColumnName("source_comment_id").IsRequired();
        entity.Property(x => x.Repository).HasColumnName("repository").IsRequired();
        entity.Property(x => x.PullRequestNumber).HasColumnName("pull_request_number");
        entity.Property(x => x.ActorLogin).HasColumnName("actor_login").IsRequired();
        entity.Property(x => x.AuthorAssociation).HasColumnName("author_association").IsRequired();
        entity.Property(x => x.CommandKind).HasColumnName("command_kind").HasConversion<string>().IsRequired();
        entity.Property(x => x.Focus).HasColumnName("focus");
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        entity.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        entity.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.RunId).HasColumnName("run_id");
        entity.Property(x => x.ErrorCode).HasColumnName("error_code");
        entity.Property(x => x.ErrorMessage).HasColumnName("error_message");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.HasIndex(x => x.SourceCommentId).IsUnique();
        entity.HasIndex(x => new { x.Status, x.NextAttemptAt, x.CreatedAt });
        entity.HasIndex(x => x.RunId);
        entity.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureImplementationFollowUpItemStates(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioImplementationFollowUpItemState>();
        entity.ToTable("implementation_follow_up_item_states");
        entity.HasKey(x => x.OriginatingItemId);
        entity.Property(x => x.OriginatingItemId).HasColumnName("originating_item_id").IsRequired();
        entity.Property(x => x.GeneratedPrItemId).HasColumnName("generated_pr_item_id");
        entity.Property(x => x.Repository).HasColumnName("repository").IsRequired();
        entity.Property(x => x.LastObservedFindingsKey).HasColumnName("last_observed_findings_key");
        entity.Property(x => x.LastObservedCommentAt).HasColumnName("last_observed_comment_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.LastQueuedHeadSha).HasColumnName("last_queued_head_sha");
        entity.Property(x => x.LastQueuedRoundId).HasColumnName("last_queued_round_id");
        entity.Property(x => x.LastQueuedRunId).HasColumnName("last_queued_run_id");
        entity.Property(x => x.FollowUpRoundCount).HasColumnName("follow_up_round_count");
        entity.Property(x => x.LastErrorCode).HasColumnName("last_error_code");
        entity.Property(x => x.LastErrorMessage).HasColumnName("last_error_message");
        entity.Property(x => x.LastErrorAt).HasColumnName("last_error_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
        entity.HasIndex(x => new { x.Repository, x.UpdatedAt });
        entity.HasIndex(x => x.GeneratedPrItemId);
        entity.HasOne(x => x.OriginatingItem).WithOne().HasForeignKey<OratorioImplementationFollowUpItemState>(x => x.OriginatingItemId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.GeneratedPrItem).WithMany().HasForeignKey(x => x.GeneratedPrItemId).OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(x => x.LastQueuedRun).WithMany().HasForeignKey(x => x.LastQueuedRunId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureComments(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioComment>();
        entity.ToTable("comments");
        entity.HasKey(x => x.CommentId);
        entity.Property(x => x.CommentId).HasColumnName("comment_id");
        entity.Property(x => x.ItemId).HasColumnName("item_id").IsRequired();
        entity.Property(x => x.RoundId).HasColumnName("round_id");
        entity.Property(x => x.AuthorKind).HasColumnName("author_kind").HasConversion<string>().IsRequired();
        entity.Property(x => x.AuthorName).HasColumnName("author_name").IsRequired();
        entity.Property(x => x.Body).HasColumnName("body").IsRequired();
        entity.Property(x => x.Visibility).HasColumnName("visibility").HasConversion<string>().IsRequired();
        entity.Property(x => x.Purpose).HasColumnName("purpose").HasConversion<string>().IsRequired();
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.Source).HasColumnName("source");
        entity.Property(x => x.SourceCommentId).HasColumnName("source_comment_id");
        entity.Property(x => x.ExternalUrl).HasColumnName("external_url");
        entity.Property(x => x.SourceUpdatedAt).HasColumnName("source_updated_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.HasIndex(x => new { x.Source, x.SourceCommentId }).IsUnique().HasFilter("source_comment_id IS NOT NULL");
        entity.HasOne(x => x.Item).WithMany(x => x.Comments).HasForeignKey(x => x.ItemId);
        entity.HasOne(x => x.Round).WithMany().HasForeignKey(x => x.RoundId);
    }

    private static void ConfigureDiscussionTurns(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioDiscussionTurn>();
        entity.ToTable("discussion_turns");
        entity.HasKey(x => x.DiscussionTurnId);
        entity.Property(x => x.DiscussionTurnId).HasColumnName("discussion_turn_id");
        entity.Property(x => x.ItemId).HasColumnName("item_id").IsRequired();
        entity.Property(x => x.RoundId).HasColumnName("round_id");
        entity.Property(x => x.QuestionCommentId).HasColumnName("question_comment_id").IsRequired();
        entity.Property(x => x.ReplyCommentId).HasColumnName("reply_comment_id");
        entity.Property(x => x.BaseRunId).HasColumnName("base_run_id").IsRequired();
        entity.Property(x => x.ThreadId).HasColumnName("thread_id").IsRequired();
        entity.Property(x => x.TurnId).HasColumnName("turn_id");
        entity.Property(x => x.ModelId).HasColumnName("model_id");
        entity.Property(x => x.AppServerEndpoint).HasColumnName("app_server_endpoint");
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        entity.Property(x => x.PromptContextJson).HasColumnName("prompt_context_json");
        entity.Property(x => x.ErrorCode).HasColumnName("error_code");
        entity.Property(x => x.ErrorMessage).HasColumnName("error_message");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.StartedAt).HasColumnName("started_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.HasIndex(x => new { x.ItemId, x.Status });
        entity.HasIndex(x => x.BaseRunId);
        entity.HasOne(x => x.Item).WithMany(x => x.DiscussionTurns).HasForeignKey(x => x.ItemId);
        entity.HasOne(x => x.Round).WithMany().HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.QuestionComment).WithMany().HasForeignKey(x => x.QuestionCommentId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.ReplyComment).WithMany().HasForeignKey(x => x.ReplyCommentId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.BaseRun).WithMany().HasForeignKey(x => x.BaseRunId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureDecisions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioDecision>();
        entity.ToTable("decisions");
        entity.HasKey(x => x.DecisionId);
        entity.Property(x => x.DecisionId).HasColumnName("decision_id");
        entity.Property(x => x.ItemId).HasColumnName("item_id").IsRequired();
        entity.Property(x => x.RoundId).HasColumnName("round_id").IsRequired();
        entity.Property(x => x.Decision).HasColumnName("decision").HasConversion<string>().IsRequired();
        entity.Property(x => x.AuthorName).HasColumnName("author_name").IsRequired();
        entity.Property(x => x.CommentId).HasColumnName("comment_id");
        entity.Property(x => x.Body).HasColumnName("body");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.HasOne(x => x.Item).WithMany(x => x.Decisions).HasForeignKey(x => x.ItemId);
        entity.HasOne(x => x.Round).WithMany().HasForeignKey(x => x.RoundId);
        entity.HasOne(x => x.Comment).WithMany().HasForeignKey(x => x.CommentId);
    }

    private static void ConfigureTimelineEvents(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioTimelineEvent>();
        entity.ToTable("timeline_events");
        entity.HasKey(x => x.EventId);
        entity.Property(x => x.EventId).HasColumnName("event_id");
        entity.Property(x => x.ItemId).HasColumnName("item_id").IsRequired();
        entity.Property(x => x.RoundId).HasColumnName("round_id");
        entity.Property(x => x.RunId).HasColumnName("run_id");
        entity.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().IsRequired();
        entity.Property(x => x.ActorKind).HasColumnName("actor_kind").HasConversion<string>().IsRequired();
        entity.Property(x => x.ActorName).HasColumnName("actor_name").IsRequired();
        entity.Property(x => x.Title).HasColumnName("title").IsRequired();
        entity.Property(x => x.Body).HasColumnName("body");
        entity.Property(x => x.MetadataJson).HasColumnName("metadata_json");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.HasIndex(x => new { x.ItemId, x.CreatedAt });
        entity.HasOne(x => x.Item).WithMany(x => x.TimelineEvents).HasForeignKey(x => x.ItemId);
        entity.HasOne(x => x.Round).WithMany().HasForeignKey(x => x.RoundId);
        entity.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId);
    }

    private static void ConfigureSourceSnapshots(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioSourceSnapshot>();
        entity.ToTable("source_snapshots");
        entity.HasKey(x => x.SnapshotId);
        entity.Property(x => x.SnapshotId).HasColumnName("snapshot_id");
        entity.Property(x => x.ItemId).HasColumnName("item_id").IsRequired();
        entity.Property(x => x.Source).HasColumnName("source").IsRequired();
        entity.Property(x => x.ExternalId).HasColumnName("external_id").IsRequired();
        entity.Property(x => x.Repository).HasColumnName("repository");
        entity.Property(x => x.HeadSha).HasColumnName("head_sha");
        entity.Property(x => x.SourceUpdatedAt).HasColumnName("source_updated_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.PayloadJson).HasColumnName("payload_json").IsRequired();
        entity.Property(x => x.PayloadHash).HasColumnName("payload_hash").IsRequired();
        entity.Property(x => x.SyncedAt).HasColumnName("synced_at").HasConversion(DateTimeOffsetConverter);
        entity.HasIndex(x => new { x.ItemId, x.SyncedAt });
        entity.HasIndex(x => new { x.ItemId, x.Source, x.ExternalId, x.PayloadHash }).IsUnique();
        entity.HasOne(x => x.Item).WithMany(x => x.SourceSnapshots).HasForeignKey(x => x.ItemId);
    }

    private static void ConfigureSourceWriteLogs(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioSourceWriteLog>();
        entity.ToTable("source_write_logs");
        entity.HasKey(x => x.WriteId);
        entity.Property(x => x.WriteId).HasColumnName("write_id");
        entity.Property(x => x.ItemId).HasColumnName("item_id").IsRequired();
        entity.Property(x => x.RoundId).HasColumnName("round_id");
        entity.Property(x => x.DecisionId).HasColumnName("decision_id");
        entity.Property(x => x.Source).HasColumnName("source").IsRequired();
        entity.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().IsRequired();
        entity.Property(x => x.Intent).HasColumnName("intent").IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        entity.Property(x => x.Repository).HasColumnName("repository");
        entity.Property(x => x.Number).HasColumnName("number");
        entity.Property(x => x.HeadSha).HasColumnName("head_sha");
        entity.Property(x => x.RequestJson).HasColumnName("request_json").IsRequired();
        entity.Property(x => x.ResponseJson).HasColumnName("response_json");
        entity.Property(x => x.ExternalId).HasColumnName("external_id");
        entity.Property(x => x.ExternalUrl).HasColumnName("external_url");
        entity.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        entity.Property(x => x.ErrorCode).HasColumnName("error_code");
        entity.Property(x => x.ErrorMessage).HasColumnName("error_message");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.HasIndex(x => new { x.ItemId, x.CreatedAt });
        entity.HasIndex(x => x.Status);
        entity.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId);
        entity.HasOne(x => x.Round).WithMany().HasForeignKey(x => x.RoundId);
        entity.HasOne(x => x.Decision).WithMany().HasForeignKey(x => x.DecisionId);
    }

    private static void ConfigureReviewDrafts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioReviewDraft>();
        entity.ToTable("review_drafts");
        entity.HasKey(x => x.DraftId);
        entity.Property(x => x.DraftId).HasColumnName("draft_id");
        entity.Property(x => x.ItemId).HasColumnName("item_id").IsRequired();
        entity.Property(x => x.RoundId).HasColumnName("round_id").IsRequired();
        entity.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        entity.Property(x => x.SummaryBody).HasColumnName("summary_body").IsRequired();
        entity.Property(x => x.MajorCount).HasColumnName("major_count");
        entity.Property(x => x.MinorCount).HasColumnName("minor_count");
        entity.Property(x => x.SuggestionCount).HasColumnName("suggestion_count");
        entity.Property(x => x.WarningsJson).HasColumnName("warnings_json").IsRequired();
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.PublishedAt).HasColumnName("published_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.SourceWriteId).HasColumnName("source_write_id");
        entity.HasIndex(x => new { x.ItemId, x.CreatedAt });
        entity.HasIndex(x => x.RunId);
        entity.HasOne(x => x.Item).WithMany(x => x.ReviewDrafts).HasForeignKey(x => x.ItemId);
        entity.HasOne(x => x.Round).WithMany().HasForeignKey(x => x.RoundId);
        entity.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId);
        entity.HasOne(x => x.SourceWrite).WithMany().HasForeignKey(x => x.SourceWriteId);
    }

    private static void ConfigureReviewDraftComments(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioReviewDraftComment>();
        entity.ToTable("review_draft_comments");
        entity.HasKey(x => x.DraftCommentId);
        entity.Property(x => x.DraftCommentId).HasColumnName("draft_comment_id");
        entity.Property(x => x.DraftId).HasColumnName("draft_id").IsRequired();
        entity.Property(x => x.Severity).HasColumnName("severity").IsRequired();
        entity.Property(x => x.Title).HasColumnName("title").IsRequired();
        entity.Property(x => x.Body).HasColumnName("body").IsRequired();
        entity.Property(x => x.Path).HasColumnName("path").IsRequired();
        entity.Property(x => x.Line).HasColumnName("line");
        entity.Property(x => x.Side).HasColumnName("side").IsRequired();
        entity.Property(x => x.StartLine).HasColumnName("start_line");
        entity.Property(x => x.StartSide).HasColumnName("start_side");
        entity.Property(x => x.SuggestionOriginal).HasColumnName("suggestion_original");
        entity.Property(x => x.SuggestionReplacement).HasColumnName("suggestion_replacement");
        entity.Property(x => x.CommentOnlyReason).HasColumnName("comment_only_reason");
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        entity.Property(x => x.Warning).HasColumnName("warning");
        entity.Property(x => x.ResolutionState).HasColumnName("resolution_state").HasConversion<string>().IsRequired();
        entity.Property(x => x.ResolutionKind).HasColumnName("resolution_kind").HasConversion<string>();
        entity.Property(x => x.ResolvedByKind).HasColumnName("resolved_by_kind").HasConversion<string>();
        entity.Property(x => x.ResolutionNote).HasColumnName("resolution_note");
        entity.Property(x => x.ResolvedAt).HasColumnName("resolved_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.ResolvedInRunId).HasColumnName("resolved_in_run_id");
        entity.Property(x => x.ResolvedViaDiscussionTurnId).HasColumnName("resolved_via_discussion_turn_id");
        entity.Property(x => x.RemoteThreadId).HasColumnName("remote_thread_id");
        entity.Property(x => x.RemoteResolveWriteId).HasColumnName("remote_resolve_write_id");
        entity.HasIndex(x => x.DraftId);
        entity.HasOne(x => x.Draft).WithMany(x => x.Comments).HasForeignKey(x => x.DraftId);
    }

    private static void ConfigureImplementationDrafts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioImplementationDraft>();
        entity.ToTable("implementation_drafts");
        entity.HasKey(x => x.DraftId);
        entity.Property(x => x.DraftId).HasColumnName("draft_id");
        entity.Property(x => x.ItemId).HasColumnName("item_id").IsRequired();
        entity.Property(x => x.RoundId).HasColumnName("round_id").IsRequired();
        entity.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        entity.Property(x => x.DeliveryPolicy).HasColumnName("delivery_policy").HasConversion<string>().IsRequired();
        entity.Property(x => x.Summary).HasColumnName("summary").IsRequired();
        entity.Property(x => x.TestsJson).HasColumnName("tests_json").IsRequired();
        entity.Property(x => x.RisksJson).HasColumnName("risks_json").IsRequired();
        entity.Property(x => x.ChangedFilesJson).HasColumnName("changed_files_json").IsRequired();
        entity.Property(x => x.ProposedCommitMessage).HasColumnName("proposed_commit_message").IsRequired();
        entity.Property(x => x.ProposedPrTitle).HasColumnName("proposed_pr_title").IsRequired();
        entity.Property(x => x.ProposedPrBody).HasColumnName("proposed_pr_body").IsRequired();
        entity.Property(x => x.BranchName).HasColumnName("branch_name");
        entity.Property(x => x.CommitSha).HasColumnName("commit_sha");
        entity.Property(x => x.PullRequestItemId).HasColumnName("pull_request_item_id");
        entity.Property(x => x.PullRequestUrl).HasColumnName("pull_request_url");
        entity.Property(x => x.SourceWriteId).HasColumnName("source_write_id");
        entity.Property(x => x.ErrorCode).HasColumnName("error_code");
        entity.Property(x => x.ErrorMessage).HasColumnName("error_message");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.DeliveredAt).HasColumnName("delivered_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.HasIndex(x => new { x.ItemId, x.CreatedAt });
        entity.HasIndex(x => x.RunId);
        entity.HasOne(x => x.Item).WithMany(x => x.ImplementationDrafts).HasForeignKey(x => x.ItemId);
        entity.HasOne(x => x.Round).WithMany().HasForeignKey(x => x.RoundId);
        entity.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId);
        entity.HasOne(x => x.PullRequestItem).WithMany().HasForeignKey(x => x.PullRequestItemId);
        entity.HasOne(x => x.SourceWrite).WithMany().HasForeignKey(x => x.SourceWriteId);
    }

    private static void ConfigureFollowUpDrafts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioFollowUpDraft>();
        entity.ToTable("follow_up_drafts");
        entity.HasKey(x => x.DraftId);
        entity.Property(x => x.DraftId).HasColumnName("draft_id");
        entity.Property(x => x.ItemId).HasColumnName("item_id").IsRequired();
        entity.Property(x => x.RoundId).HasColumnName("round_id").IsRequired();
        entity.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        entity.Property(x => x.Title).HasColumnName("title").IsRequired();
        entity.Property(x => x.Body).HasColumnName("body").IsRequired();
        entity.Property(x => x.Rationale).HasColumnName("rationale");
        entity.Property(x => x.Repository).HasColumnName("repository");
        entity.Property(x => x.Assignee).HasColumnName("assignee");
        entity.Property(x => x.Branch).HasColumnName("branch");
        entity.Property(x => x.LabelsJson).HasColumnName("labels_json");
        entity.Property(x => x.CreatedItemId).HasColumnName("created_item_id");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.ResolvedAt).HasColumnName("resolved_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.HasIndex(x => new { x.ItemId, x.CreatedAt });
        entity.HasIndex(x => x.RunId);
        entity.HasOne(x => x.Item).WithMany(x => x.FollowUpDrafts).HasForeignKey(x => x.ItemId);
        entity.HasOne(x => x.Round).WithMany().HasForeignKey(x => x.RoundId);
        entity.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId);
        entity.HasOne(x => x.CreatedItem).WithMany().HasForeignKey(x => x.CreatedItemId);
    }

    private static void ConfigureConfigurationChanges(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioConfigurationChange>();
        entity.ToTable("configuration_changes");
        entity.HasKey(x => x.ChangeId);
        entity.Property(x => x.ChangeId).HasColumnName("change_id");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.Actor).HasColumnName("actor").IsRequired();
        entity.Property(x => x.RemoteAddress).HasColumnName("remote_address");
        entity.Property(x => x.BaseRevision).HasColumnName("base_revision").IsRequired();
        entity.Property(x => x.NewRevision).HasColumnName("new_revision").IsRequired();
        entity.Property(x => x.ChangedFieldsJson).HasColumnName("changed_fields_json").IsRequired();
        entity.Property(x => x.ImpactWarningsJson).HasColumnName("impact_warnings_json").IsRequired();
        entity.Property(x => x.BeforeJson).HasColumnName("before_json").IsRequired();
        entity.Property(x => x.AfterJson).HasColumnName("after_json").IsRequired();
        entity.HasIndex(x => x.CreatedAt);
    }

    private static void ConfigureTaskShortIdCounters(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioTaskShortIdCounter>();
        entity.ToTable("task_short_id_counters");
        entity.HasKey(x => x.WorkspaceId);
        entity.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        entity.Property(x => x.Prefix).HasColumnName("prefix").IsRequired();
        entity.Property(x => x.NextValue).HasColumnName("next_value");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
    }

    private static void ConfigureGitHubSyncJobs(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioGitHubSyncJob>();
        entity.ToTable("github_sync_jobs");
        entity.HasKey(x => x.JobId);
        entity.Property(x => x.JobId).HasColumnName("job_id");
        entity.Property(x => x.Trigger).HasColumnName("sync_trigger").HasConversion<string>().IsRequired();
        entity.Property(x => x.Mode).HasColumnName("mode").HasConversion<string>().IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        entity.Property(x => x.RepositoriesTotal).HasColumnName("repositories_total");
        entity.Property(x => x.RepositoriesCompleted).HasColumnName("repositories_completed");
        entity.Property(x => x.RepositoriesFailed).HasColumnName("repositories_failed");
        entity.Property(x => x.IssuesImported).HasColumnName("issues_imported");
        entity.Property(x => x.PullRequestsImported).HasColumnName("pull_requests_imported");
        entity.Property(x => x.CommentsImported).HasColumnName("comments_imported");
        entity.Property(x => x.Skipped).HasColumnName("skipped");
        entity.Property(x => x.ErrorCode).HasColumnName("error_code");
        entity.Property(x => x.ErrorMessage).HasColumnName("error_message");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.StartedAt).HasColumnName("started_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.HasIndex(x => x.Status);
        entity.HasIndex(x => x.CreatedAt);
    }

    private static void ConfigureGitHubSyncRepositoryRuns(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioGitHubSyncRepositoryRun>();
        entity.ToTable("github_sync_repository_runs");
        entity.HasKey(x => x.RepositoryRunId);
        entity.Property(x => x.RepositoryRunId).HasColumnName("repository_run_id");
        entity.Property(x => x.JobId).HasColumnName("job_id").IsRequired();
        entity.Property(x => x.Repository).HasColumnName("repository").IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        entity.Property(x => x.Phase).HasColumnName("phase").HasConversion<string>().IsRequired();
        entity.Property(x => x.IssuesDiscovered).HasColumnName("issues_discovered");
        entity.Property(x => x.PullRequestsDiscovered).HasColumnName("pull_requests_discovered");
        entity.Property(x => x.IssuesImported).HasColumnName("issues_imported");
        entity.Property(x => x.PullRequestsImported).HasColumnName("pull_requests_imported");
        entity.Property(x => x.CommentsImported).HasColumnName("comments_imported");
        entity.Property(x => x.Skipped).HasColumnName("skipped");
        entity.Property(x => x.ErrorCode).HasColumnName("error_code");
        entity.Property(x => x.ErrorMessage).HasColumnName("error_message");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.StartedAt).HasColumnName("started_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.HasIndex(x => new { x.JobId, x.Repository }).IsUnique();
        entity.HasIndex(x => new { x.Repository, x.Status, x.CompletedAt });
        entity.HasOne(x => x.Job).WithMany(x => x.RepositoryRuns).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureGitLabSyncJobs(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioGitLabSyncJob>();
        entity.ToTable("gitlab_sync_jobs");
        entity.HasKey(x => x.JobId);
        entity.Property(x => x.JobId).HasColumnName("job_id");
        entity.Property(x => x.Trigger).HasColumnName("sync_trigger").HasConversion<string>().IsRequired();
        entity.Property(x => x.Mode).HasColumnName("mode").HasConversion<string>().IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        entity.Property(x => x.ProjectsTotal).HasColumnName("projects_total");
        entity.Property(x => x.ProjectsCompleted).HasColumnName("projects_completed");
        entity.Property(x => x.ProjectsFailed).HasColumnName("projects_failed");
        entity.Property(x => x.IssuesImported).HasColumnName("issues_imported");
        entity.Property(x => x.MergeRequestsImported).HasColumnName("merge_requests_imported");
        entity.Property(x => x.CommentsImported).HasColumnName("comments_imported");
        entity.Property(x => x.Skipped).HasColumnName("skipped");
        entity.Property(x => x.ErrorCode).HasColumnName("error_code");
        entity.Property(x => x.ErrorMessage).HasColumnName("error_message");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.StartedAt).HasColumnName("started_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.HasIndex(x => x.Status);
        entity.HasIndex(x => x.CreatedAt);
    }

    private static void ConfigureGitLabSyncProjectRuns(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioGitLabSyncProjectRun>();
        entity.ToTable("gitlab_sync_project_runs");
        entity.HasKey(x => x.ProjectRunId);
        entity.Property(x => x.ProjectRunId).HasColumnName("project_run_id");
        entity.Property(x => x.JobId).HasColumnName("job_id").IsRequired();
        entity.Property(x => x.ProjectPath).HasColumnName("project_path").IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        entity.Property(x => x.Phase).HasColumnName("phase").HasConversion<string>().IsRequired();
        entity.Property(x => x.IssuesDiscovered).HasColumnName("issues_discovered");
        entity.Property(x => x.MergeRequestsDiscovered).HasColumnName("merge_requests_discovered");
        entity.Property(x => x.IssuesImported).HasColumnName("issues_imported");
        entity.Property(x => x.MergeRequestsImported).HasColumnName("merge_requests_imported");
        entity.Property(x => x.CommentsImported).HasColumnName("comments_imported");
        entity.Property(x => x.Skipped).HasColumnName("skipped");
        entity.Property(x => x.ErrorCode).HasColumnName("error_code");
        entity.Property(x => x.ErrorMessage).HasColumnName("error_message");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.StartedAt).HasColumnName("started_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.HasIndex(x => new { x.JobId, x.ProjectPath }).IsUnique();
        entity.HasIndex(x => new { x.ProjectPath, x.Status, x.CompletedAt });
        entity.HasOne(x => x.Job).WithMany(x => x.ProjectRuns).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureSourceSyncSchedules(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OratorioSourceSyncSchedule>();
        entity.ToTable("source_sync_schedules");
        entity.HasKey(x => x.Provider);
        entity.Property(x => x.Provider).HasColumnName("provider").IsRequired();
        entity.Property(x => x.Enabled).HasColumnName("enabled");
        entity.Property(x => x.IntervalSeconds).HasColumnName("interval_seconds");
        entity.Property(x => x.NextRunAt).HasColumnName("next_run_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.LastScheduledAt).HasColumnName("last_scheduled_at").HasConversion(NullableDateTimeOffsetConverter);
        entity.Property(x => x.LastJobId).HasColumnName("last_job_id");
        entity.Property(x => x.LastErrorCode).HasColumnName("last_error_code");
        entity.Property(x => x.LastErrorMessage).HasColumnName("last_error_message");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(DateTimeOffsetConverter);
        entity.HasIndex(x => new { x.Enabled, x.NextRunAt });
    }
}
