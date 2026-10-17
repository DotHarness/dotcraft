# Oratorio GitHub Mention Review Specification

| Field | Value |
| --- | --- |
| Version | 0.8.10 |
| Status | Living |
| Date | 2026-10-17 |
| Parent Spec | [Oratorio Design](./oratorio-design.md) |

This document defines how a GitHub pull request conversation comment requests
an Oratorio review. It is a narrow extension of the GitHub source, review
analysis, review draft and source write contracts.

Reference material:

- [GitHub webhook events and payloads](https://docs.github.com/en/webhooks/webhook-events-and-payloads)
- [GitHub webhook security](https://docs.github.com/en/webhooks/using-webhooks/validating-webhook-deliveries)
- [GitHub pull request reviews API](https://docs.github.com/en/rest/pulls/reviews)

## 1. Overview

An authorized collaborator can request an Oratorio review of a configured
GitHub pull request by commenting:

```text
@dotcraft-ai review
```

The command may carry a one-time review focus:

```text
@dotcraft-ai review for security regressions
```

Oratorio verifies and records the command, syncs that exact pull request, and
runs the ordinary read-only review against its current head. Like every
successful review, the result is published automatically as a GitHub `COMMENT`
review.

`dotcraft-ai` is the fixed public account used to address the command. It is
separate from the GitHub App bot that receives the webhook and publishes the
review.

## 2. Goal

GitHub becomes an entry point for an on-demand review, without a second review
engine and without the requester opening the DotCraft Desktop board.

The feature must:

- recognize one typed `review` command through an extensible command parser;
- accept commands only from verified GitHub pull request comment webhooks;
- serve only repositories that already have Oratorio repository and workspace
  configuration;
- process commands durably and idempotently;
- reuse the existing pull request sync, review run, review draft, check run and
  source write paths, including automatic review publication.

## 3. Scope

Supported:

- newly created top-level comments in the conversation of an open pull
  request;
- the `review` command with its optional `for <focus>`;
- GitHub repositories already configured in Oratorio with a workspace mapping;
- durable command intake, exact pull request sync, review dispatch and
  publication;
- the existing run, review draft, source write, check run, retry and timeline
  surfaces.

## 4. Non-goals

Not provided:

- commands other than `review`;
- free-form `@dotcraft-ai` chat or implementation commands;
- handling of edited or deleted comments;
- issue comments outside pull requests, pull request review summaries or
  inline review comments as command sources;
- repositories merely visible to the GitHub App installation but not
  configured in Oratorio;
- workspace discovery, setup links, managed clones, webhook relay or cloud
  execution;
- reactions, acknowledgement or failure comments, or a new Desktop command UI;
- GitLab mention commands;
- implicit approval, requested changes, merge, close, commit or push.

## 5. Command Contract

### 5.1 Grammar

After trimming surrounding whitespace, the whole comment must be one of:

```text
@dotcraft-ai review
@dotcraft-ai review for <focus>
```

The mention and the `review` verb are case-insensitive, and tokens may be
separated by any run of spaces or tabs. The focus is optional, keeps its case
after trimming, must not be empty when `for` is present, stays on the command's
line, and is at most 500 characters.

Comments with extra lines, prose before the mention, unsupported arguments, or
text after `review` not introduced by `for` are invalid.

The handle is fixed to `dotcraft-ai`; it is not a deployment setting. The
account need not be a collaborator or prior participant, so GitHub may not
autocomplete it and users may have to type it in full.

### 5.2 Parser Result

Parsing is independent of webhook routing. A comment is either not a command
(not addressed to Oratorio), invalid (addressed to Oratorio but breaking the
grammar), unsupported (a recognizable but unsupported verb), or a parsed
`review` command with its optional focus.

A parsed command has a typed kind; adding a verb extends the parser and the
command dispatcher rather than adding another text comparison to webhook
handling.

Invalid, unsupported and unrelated comments create no run and get no GitHub
reply.

## 6. Webhook Intake and Authorization

A command is recognized only from a signed GitHub issue comment webhook for a
newly created comment on a pull request in a repository with a valid owner and
name, verified against the configured webhook secret. Recognition reads the raw
comment text; it does not depend on GitHub rendering the handle as a mention,
notifying the account, or treating `dotcraft-ai` as a collaborator.

A recognized command is recorded and then queued only when:

- the sender is not a bot;
- the commenter's association with the repository is owner, member or
  collaborator;
- the repository is one of Oratorio's GitHub repositories;
- the repository has a workspace mapping.

A command failing any of these is recorded as rejected and creates no run.

Command intake fails closed. Without a configured webhook secret the request is
refused as service unavailable with a stable reason; an invalid signature is
refused as forbidden. No command is accepted in either case.

The association check is the authorization boundary. It deliberately excludes
unaffiliated contributors and does not query GitHub's collaborator permission
API.

Other GitHub webhook events keep the ordinary repository sync. A parsed command
is processed for its pull request only and never triggers a repository-wide
scan.

## 7. Durable Command Lifecycle

Every recognized command is recorded before the webhook answers, with its
GitHub delivery and comment, repository and pull request, actor and
association, command and focus, status, attempts, linked run, error and times.

A command is **Queued** (accepted, waiting), **Dispatched** (linked to a review
run), **Rejected** (refused by a permanent eligibility or authorization rule) or
**Failed** (processing ended without dispatch).

A comment is recorded at most once; a redelivery returns the existing command
and never creates another run.

The webhook answers as accepted once a queued command is stored. Rejected,
invalid, unsupported and unrelated comments queue nothing and get a successful
no-op answer, unless the request is malformed or fails verification.

A background processor handles queued commands. Transient GitHub read failures
are tried at most three times: immediately, then after about 5 and 30 seconds.
Permanent validation, configuration or lifecycle errors fail at once with a
stable reason.

## 8. Target Synchronization and Dispatch

Before dispatch Oratorio reads and updates the exact pull request named by the
signed payload. It never trusts the webhook's head as the target and never
scans all open items to find the pull request.

The run is an ordinary AppServer review analysis run on the current synced
head, in the configured workspace, with the managed worktree and read-only
review rules. It is marked as triggered by a GitHub mention, and the focus
becomes its dispatch note and operator input. Creating the run and binding the
command to it happen atomically.

If the same pull request and head already have an active review run, a new
valid command may bind to it instead of starting another. Any other active run
fails the command with a stable reason. Once no compatible run is active, a new
comment may request another review even when the head is unchanged.

Ordinary lifecycle rules still apply. A comment never reopens or overrides a
closed, merged, archived, rejected or otherwise non-dispatchable target.

## 9. Review Publication

A mention-triggered review publishes exactly like any other review ([Oratorio
Design §6.1](oratorio-design.md#61-structured-review-suggestions)): on success
its draft becomes one GitHub `COMMENT` review with the summary and accepted
inline findings, never an approving or changes-requested review, and never a
merge or branch change. The usual gates apply — GitHub writes and App
authentication available, the current head matching the analyzed head, valid
anchors and suggestion ranges, no warnings or skipped findings. A blocked or
failed publication keeps the draft and its failed source write; no separate
failure comment is posted on GitHub.

## 10. Constraints

- The GitHub webhook endpoint is unchanged; command requests get a
  command-accepted answer and ordinary sync events keep theirs.
- Manual dispatch, Auto Review, review publication, GitLab and Desktop
  behavior are unchanged.
- The feature adds no settings; it reuses repository, workspace, GitHub write,
  App authentication and webhook secret configuration.
- The GitHub App must subscribe to issue comments and keep the permissions to
  read pull requests and contents, publish pull request reviews and write check
  runs.
- The `dotcraft-ai` handle stays separate from the App bot; reviews are
  attributed to the App.
- Command records are backend audit state, not exposed through the API or
  Desktop.
