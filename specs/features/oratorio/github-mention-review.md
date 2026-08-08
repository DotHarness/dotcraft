# Oratorio GitHub Mention Review Specification

| Field | Value |
| --- | --- |
| Version | 0.1.0 |
| Status | Living |
| Date | 2026-07-31 |
| Parent Spec | [Oratorio Design](./oratorio-design.md) |

This document defines the product and behavior contract for triggering an
Oratorio pull request review from a GitHub PR conversation comment. It is a
narrow extension of the existing GitHub source, review-analysis, review draft,
and source-write contracts.

Reference material:

- [GitHub webhook events and payloads](https://docs.github.com/en/webhooks/webhook-events-and-payloads)
- [GitHub webhook security](https://docs.github.com/en/webhooks/using-webhooks/validating-webhook-deliveries)
- [GitHub pull request reviews API](https://docs.github.com/en/rest/pulls/reviews)

---

## 1. Overview

An authorized repository collaborator can request an Oratorio review from a
configured GitHub pull request by posting:

```text
@dotcraft-ai review
```

The same command may carry a one-time review focus:

```text
@dotcraft-ai review for security regressions
```

Oratorio verifies and records the command, synchronizes the exact pull request,
and dispatches the existing read-only `reviewAnalysis` workflow against the
current pull request head. A successful safe draft is published as a standard
GitHub `COMMENT` review.

`dotcraft-ai` is the fixed public agent-facing account used to address the
command. It is separate from the GitHub App bot that receives the webhook and
publishes the review.

## 2. Goal

The feature makes GitHub the entry point for an on-demand Oratorio review
without introducing a second review engine or requiring the requester to open
the DotCraft Desktop board.

The completed behavior must:

- recognize one typed `review` command through an extensible command parser;
- accept commands only from verified GitHub PR comment webhooks;
- serve only repositories that already have Oratorio repository and workspace
  configuration;
- process commands durably and idempotently;
- reuse the existing PR synchronization, AppServer review, review draft, check
  run, and GitHub source-write paths;
- publish mention-triggered results as non-decision `COMMENT` reviews when the
  existing automatic-publication safety gates pass.

## 3. Scope

The first version supports:

- GitHub `issue_comment` events with action `created`;
- top-level comments in the conversation of an open pull request;
- the `review` command and its optional `for <focus>` argument;
- GitHub-hosted repositories already present in Oratorio's configured
  repositories and repository-to-workspace mappings;
- durable command intake, exact-PR synchronization, review dispatch, and
  automatic review publication;
- existing Oratorio run, review draft, source-write, check-run, retry, and
  timeline surfaces.

## 4. Non-goals

The first version does not provide:

- commands other than `review`;
- free-form `@dotcraft-ai` chat or implementation commands;
- comment edit or deletion handling;
- issue comments outside pull requests, PR review summaries, or inline review
  comments as command sources;
- support for repositories that are merely visible to a GitHub App
  installation but are not configured in Oratorio;
- workspace discovery, setup links, managed clone, webhook relay, or cloud
  execution;
- a reaction, acknowledgement comment, failure comment, or new Desktop command
  UI;
- GitLab mention commands;
- implicit approval, requested changes, merge, close, commit, or push behavior.

## 5. Command Contract

### 5.1 Grammar

After trimming leading and trailing whitespace, the complete comment must match
one of:

```text
@dotcraft-ai review
@dotcraft-ai review for <focus>
```

The mention and `review` verb are case-insensitive. Internal whitespace between
tokens may contain one or more spaces or tabs. The focus:

- is optional;
- preserves its original case after trimming;
- must be non-empty when `for` is present;
- must remain on the same line as the command;
- must not exceed 500 Unicode characters.

Comments containing additional lines, prose before the mention, unsupported
arguments, or text after `review` that is not introduced by `for` are invalid.

The command handle is fixed to `dotcraft-ai` in the first version and is not a
deployment setting. The account does not need to be a collaborator or prior
participant in the repository. GitHub may omit it from mention autocomplete in
that case, so users may need to type the complete handle manually.

### 5.2 Parser Result

Command parsing is independent of webhook routing and returns one of:

- `notCommand`: the comment is not addressed to Oratorio;
- `invalid`: the comment is addressed to Oratorio but violates the grammar;
- `unsupported`: the comment has a syntactically recognizable but unsupported
  verb;
- `parsed`: the comment contains the supported `review` command and optional
  focus.

The parsed result carries a typed command kind. The first version defines only
`review`; adding a later verb must extend the parser and command dispatcher
rather than add another body comparison to webhook handling.

Invalid, unsupported, and unrelated comments do not create a run and receive no
GitHub reply.

## 6. Webhook Intake and Authorization

Command recognition requires all of the following:

- `X-GitHub-Event` is `issue_comment`;
- payload action is `created`;
- the payload contains `issue.pull_request`;
- the payload repository has a valid `owner/name`;
- the webhook signature is valid for the configured GitHub webhook secret.

Recognition operates on the raw `comment.body` delivered by GitHub. It does not
depend on GitHub rendering the handle as a mention, sending an account
notification, or reporting `dotcraft-ai` as a repository collaborator.

A recognized, parsed command is persisted for audit and then queued only when:

- the sender is not a bot;
- `comment.author_association` is `OWNER`, `MEMBER`, or `COLLABORATOR`;
- the repository is listed in Oratorio's GitHub repositories;
- an equivalent repository-to-workspace mapping exists.

Commands that fail one of these eligibility checks are persisted as rejected
and do not create a run.

Command-capable webhook intake fails closed. If the webhook secret is absent,
the request returns service unavailable with stable code
`githubWebhookSecretRequired`. An invalid signature returns forbidden. No
command is accepted in either case.

The association check is the first-version authorization boundary. It
intentionally excludes unaffiliated contributors and does not call GitHub's
collaborator permission API.

Non-command GitHub webhook events retain the existing repository synchronization
behavior. A parsed mention command uses targeted command processing and must not
depend on or enqueue a repository-wide open-item scan.

## 7. Durable Command Lifecycle

Every recognized, parsed command is persisted before the webhook returns. The
record contains:

- GitHub delivery and comment identity;
- repository and pull request number;
- actor and author association;
- typed command and optional focus;
- status, attempt information, linked run, error information, and timestamps.

Command status is one of:

- `queued`: accepted and waiting for processing;
- `dispatched`: linked to an Oratorio review run;
- `rejected`: denied by a permanent eligibility or authorization rule;
- `failed`: processing ended without dispatch.

GitHub comment identity is unique. A redelivery of the same comment returns the
existing command and must not create another run.

The webhook returns `202 Accepted` after a queued command is durable. Rejected,
invalid, unsupported, or unrelated comments do not enqueue work and return a
successful no-op response unless the request itself is malformed or fails
verification.

A background processor handles queued commands. Transient GitHub read failures
are attempted at most three times: the initial attempt, then retries after
approximately 5 seconds and 30 seconds. Permanent validation, configuration, or
lifecycle errors fail immediately with a stable error code.

## 8. Target Synchronization and Dispatch

Before dispatch, Oratorio reads and upserts the exact pull request identified by
the signed payload. It must not trust the webhook's head SHA as the execution
target and must not scan all open repository items to find the pull request.

Dispatch uses:

- AppServer runner mode;
- `reviewAnalysis` purpose;
- the current synchronized pull request head as the target head SHA;
- the existing configured repository workspace;
- the existing managed worktree and read-only review contract;
- dispatch trigger `githubMentionReview`;
- the optional focus as the dispatch note and operator input.

Run creation and command-to-run binding are atomic.

If the same pull request and head already have an active `reviewAnalysis` run, a
new valid command may bind to that active run instead of creating a duplicate.
An incompatible active run fails with `activeRunExists`. Once no compatible run
is active, a distinct new comment may request another review even when the head
SHA has not changed.

All ordinary Oratorio lifecycle restrictions continue to apply. Closed, merged,
archived, rejected, or otherwise non-dispatchable targets are not reopened or
overridden by a GitHub comment.

## 9. Review Publication

A successful `githubMentionReview` run expresses an authorized source
publication intent. Its accepted review draft is eligible for automatic
publication independently of the general Auto Review publication switch and
repository allowlist.

Mention publication still requires:

- GitHub writes and GitHub App authentication to be enabled;
- a current target head matching the analyzed head;
- valid commentable diff anchors and suggestion ranges;
- no warnings or skipped comments that block existing automatic publication;
- every other existing review draft automatic-publication safety gate.

Publication always creates a GitHub `COMMENT` review containing the summary and
accepted inline findings. It never creates an `APPROVE` or
`REQUEST_CHANGES` review and never changes merge or branch state.

When publication is blocked or fails, Oratorio preserves the existing draft and
source-write failure records. The first version does not add a separate GitHub
failure reply.

## 10. Constraints and Compatibility

- The existing webhook route remains stable; command requests add a
  command-accepted response shape while ordinary webhook sync responses remain
  compatible.
- The run API adds the string dispatch trigger `githubMentionReview`.
- Existing manual dispatch, Auto Review, review draft publication, GitLab
  integration, and Desktop routes retain their behavior.
- The feature adds no configuration surface. Repository, workspace, GitHub
  write, App authentication, and webhook secret configuration reuse existing
  settings.
- GitHub App configuration must subscribe to issue comments and retain the
  permissions already required to read pull requests, read contents, publish
  pull request reviews, and write check runs.
- The `dotcraft-ai` command identity remains separate from the installed GitHub
  App bot identity. Review writes continue to be attributed to the App.
- Command records are backend audit state and are not exposed through a new
  first-version API or Desktop view.

## 11. Acceptance Checklist

- [x] Both `@dotcraft-ai` review forms parse into one typed `review` command.
- [x] Only the documented `@dotcraft-ai` handle parses as a command; other
      handles do not.
- [x] Command recognition does not depend on mention autocomplete, account
      notification, or `dotcraft-ai` repository participation.
- [x] Unsupported verbs and malformed Oratorio comments never create runs.
- [x] Only signed `issue_comment.created` events on pull requests can enqueue
      commands.
- [x] Bots, unaffiliated actors, and unconfigured repositories cannot dispatch.
- [x] The same GitHub comment cannot create more than one review command or
      duplicate review run.
- [x] Command processing synchronizes only the targeted pull request and pins
      the current GitHub head SHA.
- [x] The resulting run is visibly attributed to `githubMentionReview`.
- [x] The optional focus reaches the AppServer review prompt.
- [x] A compatible active review is reused; incompatible active work is not
      disturbed.
- [x] A successful safe draft publishes as a GitHub `COMMENT` review even when
      ordinary Auto Review auto-publication is disabled.
- [x] Existing automatic-publication safety gates still prevent unsafe or stale
      writes.
- [x] Manual dispatch, Auto Review, generic webhook sync, and GitLab behavior
      continue to pass their existing tests.
