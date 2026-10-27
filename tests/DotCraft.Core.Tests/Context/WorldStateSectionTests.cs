using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Context.WorldState;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class WorldStateSectionTests
{
    private const string AgentAction =
        "Agent mode is active. You may use read, write, shell, and other configured tools when appropriate under the normal approval policy. Use task tools only when the work genuinely benefits from structured tracking. Do not call CreatePlan in Agent mode.";

    private const string PlanAction =
        "Plan mode is active. You must not edit files, run mutating commands, change configuration, commit, push, or otherwise modify the workspace. Do not call goal tools in Plan mode. Read, search, reason, and call CreatePlan when the implementation plan is ready. When a user decision is needed for the plan, use `RequestUserInput` if available instead of plain text questions.";

    private const string TransitionAction =
        "Follow the active or approved plan. Before starting each planned task, update its progress when task tracking is active. After completing each task, mark it completed. Use workspace-changing tools when appropriate and continue until the planned work is complete or blocked. Do not call CreatePlan in Agent mode.";

    private const string SavedPlanAction =
        "Agent mode is active and a saved plan is available. Follow the plan when it applies, keep task progress current for non-trivial work, and use workspace-changing tools when appropriate under the normal approval policy. Do not call CreatePlan in Agent mode.";

    [Fact]
    public void EnvironmentSection_RendersDateZoneAndWorkingDirectory()
    {
        var workspace = Directory.GetCurrentDirectory();
        var text = RenderFull(new EnvironmentSection(), Context(workspacePath: workspace));

        var lines = text!.Split('\n');
        Assert.Equal("## Environment", lines[0]);
        Assert.StartsWith("CurrentDate: ", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("TimeZone: ", lines[2], StringComparison.Ordinal);
        Assert.Equal($"WorkingDirectory: {Path.GetFullPath(workspace)}", lines[3]);
        Assert.Equal(4, lines.Length);
        Assert.DoesNotContain("WorkingDirectory", RenderFull(new EnvironmentSection(), Context()), StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentSection_ListsEveryWorkspaceRootOnlyWhenOneDiffersFromTheWorkingDirectory()
    {
        var workspace = Path.GetFullPath(Directory.GetCurrentDirectory());
        var other = Path.GetFullPath(Path.Combine(workspace, "..", "other"));

        var text = RenderFull(new EnvironmentSection(), Context(workspacePath: workspace) with { WorkspaceRoots = [workspace, other] });

        Assert.EndsWith($"WorkspaceRoots:\n- {workspace}\n- {other}", text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "WorkspaceRoots",
            RenderFull(new EnvironmentSection(), Context(workspacePath: workspace) with { WorkspaceRoots = [workspace] }),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentSection_RestatesOnlyWhatChangedWhenTheThreadEntersAWorktree()
    {
        var workspace = Path.GetFullPath(Directory.GetCurrentDirectory());
        var worktreePath = Path.Combine(workspace, ".craft", "worktrees", "fix");
        var section = new EnvironmentSection();
        var before = section.Snapshot(Context(workspacePath: workspace))!;
        var context = Context(workspacePath: worktreePath) with
        {
            Thread = new SessionThread
            {
                Id = "thread_test",
                WorkspacePath = workspace,
                Worktree = new ThreadWorktreeInfo { Path = worktreePath, BranchName = "dotcraft/fix" }
            }
        };

        var entered = section.RenderDiff(context, PreviousSectionState.Known(before));
        var exited = section.RenderDiff(Context(workspacePath: workspace), PreviousSectionState.Known(section.Snapshot(context)!));

        Assert.Contains($"WorkingDirectory: {worktreePath}", entered, StringComparison.Ordinal);
        Assert.Contains("Worktree: dotcraft/fix", entered, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentDate", entered, StringComparison.Ordinal);
        Assert.Contains($"WorkingDirectory: {workspace}", exited, StringComparison.Ordinal);
        Assert.Contains("Worktree: none", exited, StringComparison.Ordinal);
    }

    [Fact]
    public void ModeSection_RendersEachModeWithItsAction()
    {
        Assert.Equal(
            $"## Mode\nCurrentMode: Agent\n\n## Mode Action\n{AgentAction}",
            RenderFull(new ModeSection(), Context(mode: AgentMode.Agent)));
        Assert.Equal(
            $"## Mode\nCurrentMode: Plan\n\n## Mode Action\n{PlanAction}",
            RenderFull(new ModeSection(), Context(mode: AgentMode.Plan)));
        Assert.Equal(
            $"## Mode\nCurrentMode: Agent\nPlan: Active\n\n## Mode Action\n{SavedPlanAction}",
            RenderFull(new ModeSection(), Context(hasActivePlan: true)));
    }

    [Fact]
    public void ModeSection_RendersTheExitFromPlanNoticeOnce()
    {
        var modeManager = new AgentModeManager();
        modeManager.SwitchMode(AgentMode.Plan);
        modeManager.SwitchMode(AgentMode.Agent);
        var context = Context(modeManager: modeManager);

        var text = RenderFull(new ModeSection(), context);

        Assert.Equal(
            "## Mode\nCurrentMode: Agent\nModeTransition: PlanToAgent\n\n"
            + "## Mode Transition\nYou have exited plan mode for this turn. You now have full workspace access subject to the normal approval policy.\n\n"
            + $"## Mode Action\n{TransitionAction}",
            text);
    }

    [Fact]
    public void ModeSection_DiffsAgainstTheBaselineToNameTransitionsAndRetireEarlierGuidance()
    {
        var section = new ModeSection();
        var context = Context(mode: AgentMode.Agent);
        var previousPlan = new JsonObject { ["mode"] = "Plan", ["planActive"] = false };

        var text = section.RenderDiff(context, PreviousSectionState.Known(previousPlan));

        Assert.Contains("ModeTransition: PlanToAgent", text, StringComparison.Ordinal);
        Assert.Contains("## Mode Transition", text, StringComparison.Ordinal);

        var planSection = new ModeSection();
        var previousAgent = new JsonObject { ["mode"] = "Agent", ["planActive"] = false };

        var retired = planSection.RenderDiff(Context(mode: AgentMode.Plan), PreviousSectionState.Known(previousAgent));

        Assert.Contains("This mode guidance replaces all previously provided mode guidance.", retired, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreadGoalSection_KeepsMovingCountersOutOfTheSection()
    {
        var goal = Goal();

        var text = RenderFull(new ThreadGoalSection(), Context(goal: goal));

        Assert.Contains("Status: Active", text, StringComparison.Ordinal);
        Assert.Contains("CanContinueWork: yes", text, StringComparison.Ordinal);
        Assert.Contains("GoalId: goal_1", text, StringComparison.Ordinal);
        Assert.Contains("TokenBudget: 1000", text, StringComparison.Ordinal);
        Assert.Contains("<untrusted_objective>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TokensUsed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("RemainingTokens", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ElapsedSeconds", text, StringComparison.Ordinal);

        var section = new ThreadGoalSection();
        Assert.True(JsonNode.DeepEquals(
            section.Snapshot(Context(goal: Goal(tokensUsed: 10, elapsedSeconds: 5))),
            section.Snapshot(Context(goal: Goal(tokensUsed: 900, elapsedSeconds: 400)))));
    }

    [Fact]
    public void ThreadGoalSection_RetiresAGoalThatWasClearedOrMoved()
    {
        var section = new ThreadGoalSection();
        var shown = section.Snapshot(Context(goal: Goal()))!;
        var cleared = Context();

        var retraction = section.RenderDiff(cleared, PreviousSectionState.Known(shown));

        Assert.Contains("no longer apply", retraction, StringComparison.Ordinal);
        Assert.Null(section.RenderDiff(cleared, PreviousSectionState.Absent));
        Assert.Null(section.RenderDiff(cleared, PreviousSectionState.Known(section.Snapshot(cleared)!)));

        var movedSection = new ThreadGoalSection();
        var movedShown = movedSection.Snapshot(Context(goal: Goal()))!;

        var moved = movedSection.RenderDiff(
            Context(goal: Goal(status: ThreadGoalStatus.Paused)),
            PreviousSectionState.Known(movedShown));

        Assert.StartsWith("This thread goal replaces", moved, StringComparison.Ordinal);
        Assert.Contains("Status: Paused", moved, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionStartHookSection_RendersTrimmedHookContextOnlyWhenPresent()
    {
        var context = Context(sessionStartHookContext: "  loaded 3 notes  ");

        Assert.Null(RenderFull(new SessionStartHookSection(), Context()));
        Assert.Equal(
            "## SessionStart Hook Context\nloaded 3 notes",
            RenderFull(new SessionStartHookSection(), context));
    }

    [Fact]
    public void WorldState_RendersEverySectionOnTheFirstStepAndNothingOnTheNext()
    {
        var world = WorldStateComposer.Build();
        var context = Context(workspacePath: Directory.GetCurrentDirectory(), goal: Goal());

        var full = world.RenderFull(context);
        Assert.Equal(
            [EnvironmentSection.SectionId, ModeSection.SectionId, ThreadGoalSection.SectionId],
            full.Fragments.Select(static fragment => fragment.SectionId));

        var second = world.Render(context, full.Snapshot, sectionIdsInHistory: null);
        Assert.Empty(second.Fragments);
        Assert.Contains(EnvironmentSection.SectionId, second.SilentSectionIds);
        Assert.Contains(ModeSection.SectionId, second.SilentSectionIds);
        Assert.Contains(ThreadGoalSection.SectionId, second.SilentSectionIds);
    }

    [Fact]
    public void WorldState_TreatsASectionInHistoryWithoutABaselineAsAlreadyShown()
    {
        var world = WorldStateComposer.Build();
        var context = Context();
        var baselineWithoutMode = WorldStateSnapshot.FromJsonObject(new JsonObject
        {
            [EnvironmentSection.SectionId] = new EnvironmentSection().Snapshot(context)
        });

        var render = world.Render(context, baselineWithoutMode, new HashSet<string>(StringComparer.Ordinal) { ModeSection.SectionId });

        var mode = Assert.Single(render.Fragments, fragment => fragment.SectionId == ModeSection.SectionId);
        Assert.Contains("This mode guidance replaces all previously provided mode guidance.", mode.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldState_SurvivesASectionThatThrowsOrDuplicatesAnId()
    {
        var world = WorldStateComposer.Build([new ThrowingSection()]);

        var render = world.RenderFull(Context());

        Assert.Contains(render.Faults, fault => fault.SectionId == "throwing");
        Assert.Contains(render.Fragments, fragment => fragment.SectionId == ModeSection.SectionId);
        Assert.False(render.Snapshot.TryGetSection("throwing", out _));

        var duplicateWorld = WorldStateComposer.Build([new DuplicateModeSection()]);

        var mode = Assert.Single(
            duplicateWorld.RenderFull(Context()).Fragments,
            fragment => fragment.SectionId == ModeSection.SectionId);
        Assert.Contains("CurrentMode: Agent", mode.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldState_KeepsTheDeliveredSnapshotWhenARenderFailsOrFactsAreUnavailable()
    {
        var section = new FlakySection();
        var world = WorldStateComposer.Build([section]);
        var delivered = world.RenderFull(Context()).Snapshot;
        Assert.True(delivered.TryGetSection(FlakySection.SectionId, out var shown));

        section.Value = "second";
        section.Throws = true;
        var failed = world.Render(Context(), delivered, sectionIdsInHistory: null);

        Assert.Contains(failed.Faults, fault => fault.SectionId == FlakySection.SectionId);
        Assert.True(failed.Snapshot.TryGetSection(FlakySection.SectionId, out var kept));
        Assert.True(JsonNode.DeepEquals(shown, kept));

        section.Throws = false;
        var retry = world.Render(Context(), failed.Snapshot, sectionIdsInHistory: null);

        Assert.Contains(retry.Fragments, fragment => fragment.SectionId == FlakySection.SectionId);

        var unavailableSection = new FlakySection();
        var unavailableWorld = WorldStateComposer.Build([unavailableSection]);
        var unavailableDelivered = unavailableWorld.RenderFull(Context()).Snapshot;
        Assert.True(unavailableDelivered.TryGetSection(FlakySection.SectionId, out var unavailableShown));

        unavailableSection.Unavailable = true;
        var skipped = unavailableWorld.Render(Context(), unavailableDelivered, sectionIdsInHistory: null);

        Assert.DoesNotContain(skipped.Fragments, fragment => fragment.SectionId == FlakySection.SectionId);
        Assert.True(skipped.Snapshot.TryGetSection(FlakySection.SectionId, out var unavailableKept));
        Assert.True(JsonNode.DeepEquals(unavailableShown, unavailableKept));

        unavailableSection.Unavailable = false;
        var restored = unavailableWorld.Render(Context(), skipped.Snapshot, sectionIdsInHistory: null);

        Assert.DoesNotContain(restored.Fragments, fragment => fragment.SectionId == FlakySection.SectionId);
    }

    private static string? RenderFull(IWorldStateSection section, WorldStateContext context) =>
        section.RenderDiff(context, PreviousSectionState.Absent);

    private static WorldStateContext Context(
        AgentMode mode = AgentMode.Agent,
        AgentModeManager? modeManager = null,
        string? workspacePath = null,
        bool hasActivePlan = false,
        ThreadGoal? goal = null,
        string? sessionStartHookContext = null)
    {
        if (modeManager == null && mode != AgentMode.Agent)
        {
            modeManager = new AgentModeManager();
            modeManager.SwitchMode(mode);
            modeManager.AcknowledgeTransition();
        }

        return new WorldStateContext
        {
            Thread = new SessionThread { Id = "thread_test", WorkspacePath = workspacePath ?? string.Empty },
            ModeManager = modeManager,
            WorkspacePath = workspacePath,
            HasActivePlan = hasActivePlan,
            ThreadGoal = goal,
            SessionStartHookContext = sessionStartHookContext
        };
    }

    private static ThreadGoal Goal(
        long tokensUsed = 0,
        long elapsedSeconds = 0,
        ThreadGoalStatus status = ThreadGoalStatus.Active) => new()
    {
        ThreadId = "thread_test",
        GoalId = "goal_1",
        Objective = "ship the refactor",
        Status = status,
        TokenBudget = 1000,
        TokensUsed = new TokenUsageInfo { InputTokens = tokensUsed, TotalTokens = tokensUsed },
        TimeUsedSeconds = elapsedSeconds,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch
    };

    private sealed class FlakySection : IWorldStateSection
    {
        public const string SectionId = "flaky";

        public string Value { get; set; } = "first";

        public bool Throws { get; set; }

        public bool Unavailable { get; set; }

        public string Id => SectionId;

        public JsonNode? Snapshot(WorldStateContext context) =>
            Unavailable ? null : new JsonObject { ["value"] = Value };

        public string? RenderDiff(WorldStateContext context, PreviousSectionState previous) =>
            Throws ? throw new InvalidOperationException("boom") : $"## Flaky {Value}";
    }

    private sealed class ThrowingSection : IWorldStateSection
    {
        public string Id => "throwing";

        public JsonNode? Snapshot(WorldStateContext context) => throw new InvalidOperationException("boom");

        public string? RenderDiff(WorldStateContext context, PreviousSectionState previous) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class DuplicateModeSection : IWorldStateSection
    {
        public string Id => ModeSection.SectionId;

        public JsonNode? Snapshot(WorldStateContext context) => new JsonObject();

        public string? RenderDiff(WorldStateContext context, PreviousSectionState previous) => null;
    }
}
