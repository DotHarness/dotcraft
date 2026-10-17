# Oratorio workflow

Every task on the [Oratorio](./) Board follows the same path: intake, Agent work, review, and a recorded decision.

## Find work on the Board

The **Active** view groups current tasks by lifecycle stage. Use **All**, **Cancelled**, and **Archived** for completed or inactive work.

- Search titles and descriptions, or narrow the results with `source:` and `label:`.
- Filter by repository or assignee.
- Select **Sync sources** to request an immediate GitHub and GitLab update.
- Select a card to open the task beside the Board without losing your current Board filters.

The task panel shows the description, the task's properties, and its Activity, with the one action that fits the task right now at the top. Select **Full details** to open the same task across the whole view.

## Start and follow Agent work

Open a pending task and start a run from the button at the top. Its menu offers the other run modes; the choices depend on where the task came from.

Every run happens in an Oratorio-managed worktree. Open the linked DotCraft thread for the full conversation, plan, tool activity, or file changes. Cancelling a run in progress asks for confirmation.

## Review and decide

Everything about a task reads top to bottom on one page. Agent runs appear like CI checks, Agent drafts as cards, and decisions and comments in between, in the order they happened.

- When a task is ready, select **Review** to approve, request changes, or reject it. Requesting changes needs a comment.
- To close a task, open its status menu at the top left and select **Archive**. An archived task is reopened the same way.
- When a pull request gets new commits after an approval, **Review new revision** starts a fresh review.

![Reviewing an Oratorio task in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/task-review.png)

## Deliver or continue the work

When a review of a pull request or merge request finishes, Oratorio publishes its findings and suggested replacements to GitHub or GitLab right away as a comment-only review. It never approves, requests changes, or merges, and the task still waits for your decision. A published finding stays open until it's handled: select **Mark fixed** or **Dismiss** on the task, and **Reopen** it if it needs another look. If a review can't be published, for example because writes are off or the pull request has new commits, the task shows why. Retry the publication or discard the review.

With source writes enabled, an implementation draft delivers its branch as a GitHub pull request or a GitLab merge request. A follow-up draft can be edited and turned into a new local task. If a provider write fails, Oratorio keeps the recorded decision and offers a retry for that write.

## Related docs

- [Connect GitHub](./github) — sync issues and pull requests onto the Board and write reviews back
- [Connect GitLab](./gitlab) — connect issues and merge requests with a project-scoped token
- [Configure Oratorio](./settings) — tune source sync, Agent execution, and delivery behavior
