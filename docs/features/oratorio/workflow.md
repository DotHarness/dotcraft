# Oratorio workflow

Every task on the [Oratorio](../oratorio) Board follows the same path: intake, Agent work, review, and a recorded decision.

## Find work on the Board

The **Active** view groups current tasks by lifecycle stage. Use **All**, **Cancelled**, and **Archived** for completed or inactive work.

- Search titles and descriptions, or narrow the results with `source:` and `label:`.
- Filter by repository or assignee.
- Select **Sync sources** to request an immediate GitHub and GitLab update.
- Select a card to open Quick View without losing your current Board filters.

Quick View shows the task's status, recent activity, drafts, and comments, along with the actions allowed right now.

## Start and follow Agent work

Open a pending task and pick one of the offered run modes. The choices depend on where the task came from and which stage it is in.

Every run happens in an Oratorio-managed worktree. Open the linked DotCraft thread for the full conversation, plan, tool activity, or file changes. Cancelling a run in progress asks for confirmation.

## The five stages of a task

Open a task's full detail and the workflow unfolds in five stages:

1. **Intake** — problem statement, source metadata, labels, assignee, and base branch.
2. **Analysis** — run attempts, live activity, timeline, and diagnostics.
3. **Review** — Agent drafts, findings, and suggested changes.
4. **Decision** — approve, request changes, or reject, plus the result of any provider write.
5. **Closed** — the recorded outcome and history, with archive, reopen, and re-review available when they apply.

![Reviewing an Oratorio task in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/task-review-light.png)

## Deliver or continue the work

Review drafts carry inline findings and suggested replacements. Resolve or reopen a finding before publishing when a point still needs discussion.

With source writes enabled, an implementation draft delivers its branch as a GitHub pull request or a GitLab merge request. A follow-up draft can be edited and turned into a new local task. If a provider write fails, Oratorio keeps the recorded decision and offers a retry for that write.

## Related docs

- [Connect GitHub](./github) — sync issues and pull requests onto the Board and write reviews back
- [Connect GitLab](./gitlab) — connect issues and merge requests with a project-scoped token
- [Configure Oratorio](./settings) — tune source sync, Agent execution, and delivery behavior
