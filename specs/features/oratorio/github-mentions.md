# Oratorio GitHub Mentions Specification

| Field | Value |
| --- | --- |
| Version | 0.10.0 |
| Status | Living |
| Date | 2026-10-26 |
| Parent Spec | [Oratorio Design](./oratorio-design.md) |

This document defines how GitHub comments that mention Oratorio request a pull
request review or ask a question, how Oratorio finds such comments with or
without webhooks, and how it answers on the source issue or pull request. It extends the
GitHub source, review run, Discussion Turn, finding resolution and source write
contracts of the parent spec.

Reference material:

- [GitHub issue comments API](https://docs.github.com/en/rest/issues/comments)
- [GitHub pull request review comments API](https://docs.github.com/en/rest/pulls/comments)
- [GitHub reactions API](https://docs.github.com/en/rest/reactions/reactions)
- [GitHub webhook events and payloads](https://docs.github.com/en/webhooks/webhook-events-and-payloads)

## 1. Overview and Scope

An authorized collaborator addresses Oratorio by starting a pull request
comment with `@dotcraft-ai`:

- `@dotcraft-ai /review` (optionally followed by a focus), or just
  `@dotcraft-ai review`, requests a read-only review of the current head;
- `@dotcraft-ai <question>` asks about the pull request, its review or one of
  its findings. Oratorio answers on the pull request.

Issue conversation comments accept questions using the subset defined in §11.

Commands are accepted from new top-level comments in the pull request
conversation and from new inline review comments, including replies in an
existing review thread. Oratorio discovers them by polling the repository's
comments, so no public webhook endpoint is required; a configured GitHub
webhook only delivers them sooner.

`dotcraft-ai` is the fixed public account used to address Oratorio. It is
separate from the GitHub App bot that reads comments and publishes reviews,
replies and reactions; everything Oratorio writes is attributed to the App.

Non-goals:

- implementation commands, commits or pushes requested from a comment;
- commands in pull request review summaries, issue titles or bodies, or edited comments;
- repositories not configured in Oratorio with a workspace mapping;
- GitLab mentions;
- reacting to or answering comments written by bots, including Oratorio's own
  App;
- implicit approval, requested changes, merge or close.

## 2. Command Contract

A comment is addressed to Oratorio when, after trimming surrounding whitespace,
it starts with `@dotcraft-ai` followed by whitespace or the end of the
comment. The mention is case-insensitive and is matched against the raw text,
independent of how GitHub renders or notifies it. A comment that mentions
`@dotcraft-ai` only later in its text is not addressed to Oratorio.

The text after the mention is:

- a **command** when it starts with a slash token: `/` followed by letters or
  hyphens, then whitespace or the end of the comment. The only command is
  `/review`, matched case-insensitively. The rest of its line, trimmed, is an
  optional focus that keeps its case and is at most 500 characters; nothing may
  follow that line. The single word `review`, case-insensitive and with nothing
  else after the mention, is the same command without a focus;
- otherwise a **question**: the remaining text, which may span lines, is
  passed to the agent unchanged. A question must not be empty and is at most
  8,000 characters.

A comment that is addressed to Oratorio is invalid when it has no text after
the mention, names an unknown command, puts text on lines after `/review`, or
exceeds a length limit. Invalid and unaddressed comments create nothing and get
no reply.

## 3. Discovery

Every repository configured in Oratorio with a workspace mapping is scanned for
new comments at a fixed short interval. A scan reads the repository's pull
request conversation comments and its inline review comments, ordered by
update time and starting from the repository's stored scan cursor, using
conditional requests so that an unchanged repository costs no rate limit. A
repository's scan failure is recorded with a stable reason and never blocks
other repositories or the next scan.

A comment is a command candidate only when:

- it is a pull request comment or an issue conversation comment;
- it was created after the repository's scan baseline and after the previous
  scan's high-water mark, less a short overlap. A comment created earlier and
  edited later is never a candidate;
- its author is not a bot.

When scanning is first enabled for a repository, its baseline is the current
time: existing comments are never answered.

A configured, verified GitHub webhook delivering an `issue_comment` or
`pull_request_review_comment` event with action `created` hands the comment to
the same intake. Webhook delivery is optional and never required for any
behavior in this document. A webhook comment that the scan later sees again, or
a scanned comment that a webhook redelivers, is the same command.

## 4. Intake, Authorization and Acknowledgement

Each candidate is recorded once, keyed by its GitHub comment identity, before
any work starts. A recorded comment is never recorded or answered again,
whatever later scans, webhook redeliveries or edits show.

A recorded command is accepted only when:

- the commenter's association with the repository is owner, member or
  collaborator;
- the repository is configured in Oratorio and has a workspace mapping;
- the pull request is open.

A command failing any of these is recorded as rejected, creates no work and
gets no reply. The association check is the authorization boundary.

When GitHub writes are enabled, Oratorio adds an `eyes` reaction to every
accepted command's comment. The reaction is idempotent, attributed to the App
and best effort: a failed reaction is recorded and never blocks the command.
Rejected and invalid comments get no reaction.

Command records are backend audit state with their comment, repository, target
kind and number, actor, association, command kind, text, status, attempts, linked work, error
and times. They are not exposed through the API or Desktop. A command is
**Queued**, **Dispatched** (linked to its review run or question), **Rejected**
or **Failed**. Transient GitHub read failures are retried at most three times:
immediately, then after about 5 and 30 seconds.

## 5. Review Commands

A review command syncs the exact pull request it names and dispatches an
ordinary read-only review of its current synced head, marked as triggered by a
GitHub mention, with the focus as its dispatch note. If the same pull request
and head already have an active review run, the command binds to it instead of
starting another. A review command never overrides a non-dispatchable item. The
result publishes like any other review ([Oratorio Design
§6.1](./oratorio-design.md#61-structured-review-suggestions)); a blocked or
failed publication posts no separate failure comment.

## 6. Questions

A question is answered in the pull request's conversation thread, the same
DotCraft thread its review rounds use ([Oratorio Design
§6.3](./oratorio-design.md#63-agent-discussion-turns)). It never creates a round
or run, never changes the item's lifecycle or check state and never requests a
review.

Delivery follows the thread's state at the moment the question is dispatched:

- a turn is running on the thread (a review run or another question): the
  question is steered into that turn;
- no turn is running but the thread exists: the question starts a new turn on
  it;
- the pull request has no thread yet: Oratorio starts the thread exactly as a
  first review round would, at the current head, and asks the agent to answer
  the question only, without reviewing.

The question input names the commenter, links the comment, carries the
question text and, for an inline comment, its file, line and the review thread
it belongs to. It carries the pull request state changes the thread has not
yet been told (Oratorio Design §8). When no review run is active, it states
that the agent should answer the question only and submit no review draft. A
steered question into a review run leaves the review unchanged: the run must
still submit its draft as usual.

Each question is a Discussion Turn whose origin is the GitHub comment. The
agent answers through the discussion reply tool, naming the Discussion Turn it
answers. Oratorio records the reply and publishes it as a source write:

- a top-level question gets a pull request conversation comment that quotes or
  links the question;
- an inline question gets a reply in the same review thread.

Each question takes exactly one reply; a second reply for the same question
fails with a stable reason. A published reply carries a hidden marker naming
its question, and Oratorio checks for that marker on the pull request before
retrying a failed reply write, so a retry never posts the answer twice.

A question finishes when its reply is recorded. When the turn that carries it
ends without a reply, Oratorio keeps waiting while the thread has background
work, follows the turns that task notifications start, and fails the question
only after no background work remains and a short settle period passes. The run
timeout bounds the wait. A failed question posts nothing on GitHub.

Questions on one pull request are answered in the order received. Questions on
different pull requests are independent.

## 7. Findings in Review Threads

When an inline question is a reply in a review thread whose first comment is a
published Oratorio finding, the question input identifies that finding. If the
discussion concludes the finding is a non-issue or already handled, the agent
may resolve it as dismissed through the finding resolution tool, which also
resolves the GitHub thread ([Oratorio Design
§6.4](./oratorio-design.md#64-review-finding-resolution)). Resolution never
substitutes for the reply: the agent still answers the question.

## 8. Oratorio's Own Comments

Comments, reviews and replies authored by Oratorio's App are never commands
and never enter a prompt as imported source comments. Where the agent needs
what Oratorio said, the prompt uses Oratorio's stored state — published
findings, review summaries and recorded replies — instead. Oratorio recognizes
its own comments by its App's bot identity; when that identity cannot be
resolved, it imports comments as before and tries again on the next sync. A
review thread maps to a finding only through the thread root Oratorio recorded
when it published the finding, never through text in a comment.

## 9. Configuration and Permissions

The feature adds no settings. It reuses repository, workspace, GitHub write and
App authentication configuration, and the webhook secret when a webhook is
configured. The GitHub App needs read access to pull requests, issues and
contents, and write access to pull requests and issues to publish reviews,
replies and reactions, plus check runs for review commands.

## 10. Acceptance Checklist

- With no webhook configured, `@dotcraft-ai /review` and `@dotcraft-ai
  <question>` on an open pull request of a configured repository are picked up
  within one scan interval.
- A comment delivered by both webhook and scan, rescanned, or edited after
  recording is processed and answered once.
- Comments from bots, unaffiliated contributors, unconfigured repositories,
  closed pull requests, and comments created before scanning was enabled get
  no reaction and no reply.
- Accepted commands get one `eyes` reaction.
- A question during a running review is steered into the review turn, is
  answered on the pull request, and the review still publishes once.
- A question on a never-reviewed pull request is answered without a review
  draft, round or check run; a later review reuses the same thread.
- A question whose agent waits on background work is answered once that work
  ends, not failed at the first turn end.
- An inline reply on an Oratorio finding can dismiss the finding and resolve
  its GitHub thread, and still gets a reply in the thread.
- Retrying a reply write whose earlier attempt reached GitHub posts no second
  reply.

## 11. Issue Questions

New conversation comments on open issues also accept `@dotcraft-ai <question>`.
They use the same discovery, authorization, acknowledgement, Discussion Turn,
reply publication and deduplication contracts as pull request questions. Issue
titles and bodies do not trigger questions. `/review` and the single word
`review` are rejected on issues with `githubCommentCommandUnsupportedForIssue`.
Closed issues are rejected without acknowledgement or reply.

Candidates and command audit records identify the target kind (issue or pull
request) and number. Intake never assumes a conversation comment belongs to a
pull request. Dispatch syncs that exact target, even if it has not been imported,
and refreshes its discussion context before delivering the question. An issue
question includes the issue title, body, URL, source state and human discussion
history, with Oratorio's own answers taken from stored discussion state.

An issue question reuses the item's conversation thread. An active run or
discussion receives the question in its existing execution context. A prior
implementation thread keeps its work branch. Otherwise, starting or resuming an
idle independent issue discussion queries the repository's actual default
branch and fetches its latest head using the configured source credentials.
Fetch, SHA resolution and worktree preparation are serialized by the repository
lock. The managed worktree is keyed by item, while the base workspace's branch,
HEAD and uncommitted files are left unchanged. The prompt names the resolved
branch, commit and execution path. No local-HEAD fallback is permitted on failure.
Starting an independent issue discussion requires managed worktrees; disabling
them fails with `issueDiscussionManagedWorktreeRequired`.

Issue questions create no round, run, review draft, check or implementation
delivery. A question's wording never authorizes implementation or follow-up
work. Repeated questions reuse the same thread; publication remains one reply
per question. No additional settings, permissions or HTTP endpoints are added.

Acceptance additionally covers issue questions discovered by scan and webhook,
first questions before import, idle-thread reuse, questions during implementation,
closed and unauthorized targets, unsupported review commands, and retry-safe
publication. Real Git fixtures must verify that a stale checkout on a feature
branch produces a discussion worktree at the remote default branch's latest
head (including a default branch other than `main`) without changing the checkout.
