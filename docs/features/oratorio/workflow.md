# Follow the Oratorio workflow

Use the Board to move a task from intake through Agent work, review, and a recorded decision.

## Find work on the Board

The **Active** view groups current tasks by lifecycle stage. Use **All**, **Cancelled**, and **Archived** for completed or inactive work.

- Search titles and descriptions, or narrow results with `source:` and `label:` qualifiers.
- Filter by repository or assignee.
- Select **Sync sources** to request an immediate GitHub and GitLab update.
- Select a card to open Quick View without losing the current Board filters.

Quick View shows the current status, recent activity, drafts, comments, and only the actions currently allowed by Oratorio.

## Start and follow Agent work

Open a discovered task and choose one of the offered run modes. The available choices depend on the task source and its current state.

Each run uses an Oratorio-managed worktree. Open the linked DotCraft thread when you need the full conversation, plan, tool activity, or file changes. Cancelling an active run requires confirmation.

## Review the task detail

Task Detail keeps the workflow in five stages:

1. **Intake** — problem statement, source metadata, labels, assignee, and base branch.
2. **Analysis** — run attempts, live activity, timeline, diagnostics, and worktree information.
3. **Review** — Agent drafts, findings, suggestions, comments, implementation delivery, and follow-up tasks.
4. **Decision** — approve, request changes, or reject, plus the status of any provider write.
5. **Closed** — the recorded outcome, history, archive, reopen, and re-review actions when available.

![Reviewing an Oratorio task in DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/oratorio/task-review-light.png)

## Deliver or continue the work

Review drafts can contain inline findings and suggested replacements. Resolve or reopen findings before publishing when more discussion is needed.

Implementation drafts can deliver a branch as a GitHub pull request or GitLab merge request when source writes are enabled. Follow-up drafts can be edited and turned into new local tasks. If a provider write fails, Oratorio keeps the recorded decision and offers a targeted retry.

Oratorio refreshes the task and Board after successful commands. A reconnect refreshes state but does not repeat the original user action.

## Related docs

- [Oratorio](../oratorio)
- [Connect GitHub](./github)
- [Connect GitLab](./gitlab)
- [Configure Oratorio](./settings)

