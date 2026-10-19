# Oratorio GitHub Mention Review Specification

| Field | Value |
| --- | --- |
| Version | 0.9.1 |
| Status | Living |
| Date | 2026-10-19 |
| Parent Spec | [Oratorio Design](./oratorio-design.md) |

This document defines how a GitHub pull request conversation comment requests
an Oratorio review. It is a narrow extension of the GitHub source, review
analysis, review draft and source write contracts.

Reference material:

- [GitHub webhook events and payloads](https://docs.github.com/en/webhooks/webhook-events-and-payloads)
- [GitHub webhook security](https://docs.github.com/en/webhooks/using-webhooks/validating-webhook-deliveries)
- [GitHub pull request reviews API](https://docs.github.com/en/rest/pulls/reviews)

## 1. Overview and Scope

An authorized collaborator can request an Oratorio review of a configured
GitHub pull request by commenting `@dotcraft-ai review`, optionally with a
one-time focus (`@dotcraft-ai review for security regressions`). Oratorio
verifies and durably records the command, syncs that exact pull request, and
runs the ordinary read-only review against its current head, reusing the
existing review run, review draft, check run, source write, retry and timeline
paths. Like every successful review, the result is published automatically as
a GitHub `COMMENT` review. The requester never needs to open the DotCraft
Desktop board.

`dotcraft-ai` is the fixed public account used to address the command. It is
separate from the GitHub App bot that receives the webhook and publishes the
review; reviews are attributed to the App.

Supported command sources are newly created top-level comments in the
conversation of an open pull request, in GitHub repositories already
configured in Oratorio with a workspace mapping.

Non-goals:

- commands other than `review`, free-form `@dotcraft-ai` chat or
  implementation commands;
- edited or deleted comments;
- issue comments outside pull requests, pull request review summaries or
  inline review comments as command sources;
- repositories visible to the GitHub App installation but not configured in
  Oratorio;
- workspace discovery, setup links, managed clones, webhook relay or cloud
  execution;
- reactions, acknowledgement or failure comments, or a Desktop command UI;
- GitLab mention commands;
- implicit approval, requested changes, merge, close, commit or push.

## 2. Command Contract

After trimming surrounding whitespace, the whole comment must be one of:

```text
@dotcraft-ai review
@dotcraft-ai review for <focus>
```

The mention and the `review` verb are case-insensitive, and tokens may be
separated by any run of spaces or tabs. The focus is optional, keeps its case
after trimming, must not be empty when `for` is present, stays on the command's
line, and is at most 500 characters.

The handle is fixed to `dotcraft-ai`; it is not a deployment setting. The
account need not be a collaborator or prior participant, so GitHub may not
autocomplete it.

Parsing is independent of webhook routing. A comment is either not a command
(not addressed to Oratorio), invalid (addressed to Oratorio but breaking the
grammar — extra lines, prose before the mention, unsupported arguments, or
text after `review` not introduced by `for`), unsupported (a recognizable but
unsupported verb), or a parsed `review` command with its optional focus.
Invalid, unsupported and unrelated comments create no run and get no GitHub
reply.

## 3. Webhook Intake and Authorization

A command is recognized only from a signed GitHub issue comment webhook for a
newly created comment on a pull request in a repository with a valid owner and
name, verified against the configured webhook secret. Commands arrive on the
ordinary GitHub webhook endpoint. Recognition reads the raw comment text; it
does not depend on GitHub rendering the handle as a mention, notifying the
account, or treating `dotcraft-ai` as a collaborator.

A recognized command is recorded and then queued only when:

- the sender is not a bot;
- the commenter's association with the repository is owner, member or
  collaborator;
- the repository is one of Oratorio's GitHub repositories;
- the repository has a workspace mapping.

A command failing any of these is recorded as rejected and creates no run.

Command intake fails closed. Without a configured webhook secret the request is
refused as service unavailable with a stable reason; an invalid signature is
refused as forbidden.

The association check is the authorization boundary. It deliberately excludes
unaffiliated contributors and does not query GitHub's collaborator permission
API.

Other GitHub webhook events keep the ordinary repository sync. A parsed command
is processed for its pull request only and never triggers a repository-wide
scan.

## 4. Durable Command Lifecycle

Every recognized command is recorded before the webhook answers, with its
GitHub delivery and comment, repository and pull request, actor and
association, command and focus, status, attempts, linked run, error and times.
Command records are backend audit state, not exposed through the API or
Desktop.

A command is **Queued** (accepted, waiting), **Dispatched** (linked to a review
run), **Rejected** (refused by a permanent eligibility or authorization rule) or
**Failed** (processing ended without dispatch).

A comment is recorded at most once; a redelivery returns the existing command
and never creates another run.

The webhook answers with a command-accepted response once a queued command is
stored. Rejected, invalid, unsupported and unrelated comments queue nothing and
get a successful no-op answer, unless the request is malformed or fails
verification.

A background processor handles queued commands. Transient GitHub read failures
are tried at most three times: immediately, then after about 5 and 30 seconds.
Permanent validation, configuration or lifecycle errors fail at once with a
stable reason.

## 5. Target Synchronization and Dispatch

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

A mention-triggered review publishes exactly like any other review under the
usual gates ([Oratorio
Design §6.1](oratorio-design.md#61-structured-review-suggestions)). A blocked or
failed publication keeps the draft and its failed source write; no separate
failure comment is posted on GitHub.

## 6. Configuration

The feature adds no settings; it reuses repository, workspace, GitHub write,
App authentication and webhook secret configuration. The GitHub App must
subscribe to issue comments and keep the permissions to read pull requests and
contents, publish pull request reviews and write check runs.
