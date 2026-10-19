# Oratorio GitLab Integration Specification

| Field | Value |
| --- | --- |
| Version | 0.9.1 |
| Status | Living |
| Date | 2026-10-19 |
| Parent Spec | [Oratorio Design](./oratorio-design.md) |

This document defines the product and behavior contract for GitLab as a
first-class Oratorio source.

Reference material:

- [GitLab REST API authentication](https://docs.gitlab.com/api/rest/authentication/)
- [GitLab Project Access Tokens](https://docs.gitlab.com/user/project/settings/project_access_tokens/)
- [GitLab Issues API](https://docs.gitlab.com/api/issues/)
- [GitLab Merge Requests API](https://docs.gitlab.com/api/merge_requests/)
- [GitLab Notes API](https://docs.gitlab.com/api/notes/)
- [GitLab Discussions API](https://docs.gitlab.com/api/discussions/)
- [GitLab Draft Notes API](https://docs.gitlab.com/api/draft_notes/)
- [GitLab Commits API](https://docs.gitlab.com/api/commits/)
- [GitLab Merge Request Approvals API](https://docs.gitlab.com/api/merge_request_approvals/)
- [GitLab Webhooks](https://docs.gitlab.com/user/project/integrations/webhooks/)
- [GitLab Project Webhooks API](https://docs.gitlab.com/api/project_webhooks/)
- [GitLab Group Webhooks API](https://docs.gitlab.com/api/group_webhooks/)

## 1. Product Goal and Boundaries

Oratorio supports GitLab issues and merge requests as source-backed tasks with
the same lifecycle, review rounds, AppServer dispatch, review drafts, source
write audit and implementation delivery as GitHub-backed work.

The integration must:

- treat GitLab as a provider with its own endpoint, credentials, project
  identity, webhook verification, diff anchors, commit status and merge request
  semantics;
- support GitLab.com, self-managed GitLab and Dedicated-style endpoints;
- keep every external write backend-owned, explicit, auditable, retryable and
  separate from agent tool execution.

The integration must not:

- merge GitLab merge requests;
- silently approve GitLab merge requests;
- let agents push branches, create merge requests or write source comments;
- require GitLab Premium or Ultimate for the baseline read and write flow;
- assume GitLab.com-only project or URL behavior;
- require an OAuth browser flow for the baseline token setup.

## 2. Source Provider Model

Provider capabilities, sync jobs, source write audit and canonical routing keys
follow [Oratorio Design](oratorio-design.md#source-provider-model).

Operators configure the full namespace path, including subgroups, and paths
are normalized without dropping them. A project's canonical key is GitLab, its
instance and that full path; changing the instance requires workspace routes
and automation lists to use the new keys. Issues and merge
requests are identified by the project and GitLab's project-scoped number
(`#N` for issues, `!N` for merge requests). GitLab's numeric project id is
cached metadata, never routing identity.

## 3. GitLab Provider Contract

The baseline uses token authentication. OAuth setup, automatic webhook
creation, RBAC administration and enterprise SSO are out of scope.

### 3.1 Configuration and Authentication

GitLab configuration holds whether the provider is enabled, the instance
endpoint (GitLab.com by default), the configured project paths, write
enablement, a local-development webhook bypass, and one access record per
configured project.

A project's access record is keyed by its canonical project key and holds the
token kind, the write-only token, and an optional write-only webhook secret and
signing token. Settings writes GitLab credentials only through these access
records. The token kind (project, group or personal access token) is a
label for the operator, not a permission claim. Matching is exact per project:
a group or personal token is used for several projects by entering it on each,
with no inheritance by group prefix.

The endpoint is the only URL the operator configures; the API address is always
derived from it, and Desktop never asks for a separate API URL.

Reading a project needs a token that can read it. Writing needs a token that
can create notes, discussions and statuses, push branches and open merge
requests in it. The UI and diagnostics show token presence and the capability
observed per project, and never claim a scope that no capability check has
verified.

Writes act as the project's token identity.

### 3.2 Read Sync

Configured projects sync independently. A project that fails to resolve or
sync fails with a stable reason without blocking the others. When some projects
have access records but a configured project has none, that project fails read
sync while the provider may still report partial capability.

GitLab issues import with title, description, assignee, labels, link, created
and updated times, source state, closed time when available, project and a
source snapshot.

GitLab merge requests import as review targets with title, description,
assignees and reviewers when useful, labels, link, source and target branch,
draft state, created and updated times, source state, closed or merged time
when available, head commit, diff refs when available, project and a source
snapshot.

GitLab's opened, closed and merged states map to Oratorio's open, closed and
merged; anything else is unknown.

Detail hydration loads context that does not belong in every board sync: issue
and merge request notes, merge request discussions, relevant system notes, the
current diff refs and, when available, the detailed merge status.

Imported context is source context, never Oratorio operator feedback.

Closed issues and closed or merged merge requests are archived when no run is
active. If a source item reopens after a source-driven archive, Oratorio
returns it to Discovered. A manual archive stays operator-owned and is never
undone by sync.

### 3.3 Webhooks

GitLab webhooks queue provider sync jobs; they never change tasks directly.

Verification uses the access record of the project named in the payload:
GitLab's Standard Webhooks signature when signing headers are present, then
GitLab's secret token. A project without a configured secret rejects the
delivery as forbidden. Verification may be disabled only through the
provider-level local-development bypass.

Supported events are issues, merge requests, notes and pushes. Events outside
configured projects are ignored, with an audit-visible diagnostic when
feasible.

### 3.4 Decisions, Review Drafts, and Statuses

GitLab writes are possible only when GitLab is configured, writes are enabled,
the target project is configured and has a token, and the task is GitLab-backed
with a valid project and number. When a gate fails, Oratorio records a failed
source write with a stable reason; the task decision and review draft are not
rolled back.

Decision writes:

| Oratorio item | Operator action | GitLab write |
| --- | --- | --- |
| Issue | Approve, Request changes or Reject | Issue note |
| Merge request | Approve | MR note plus `oratorio/review` commit status success |
| Merge request | Request changes | MR note plus `oratorio/review` commit status failed |
| Merge request | Reject | MR note plus `oratorio/review` commit status failed |
| Merge request | Re-review | No GitLab write |
| Local task | Any decision | No external write |

The commit status targets the merge request's current head. Without a known
head the status write fails on its own; the note may still succeed as a
separately audited write.

Oratorio does not use GitLab's merge request approval API.

Review draft submission is provider-neutral; GitLab-specific behavior happens
in validation and publication.

Anchor validation uses the merge request's current diff refs (base, start and
head commits), the old and new path, and the old or new line. A right-side
finding anchors to the new line, a left-side finding to the old line. Renamed
files are checked against both paths; new files accept only new-side anchors;
deleted files only old-side anchors. Invalid findings are skipped with
warnings and never published as misleading overview notes.

A successful review run's draft is published automatically ([Oratorio
Design §6.1](oratorio-design.md#61-structured-review-suggestions)):

- a summary-only draft publishes as one merge request note;
- each accepted inline finding becomes one GitLab discussion or draft note;
- a concrete suggestion is emitted only when Oratorio has resolved its
  existing text to an exact, unique new-side range that GitLab can render
  safely; multi-line replacements use GitLab's offset-aware suggestion form so
  a discussion anchored on the last new-side line covers the preceding lines;
- comment-only findings carry no suggestion and keep their reason in Oratorio
  for audit;
- every write links back to the draft.

On top of the shared gates, publication also requires the merge request's
current head to match the reviewed head and its diff refs to be current. A
blocked or failed publication is a failed source write the operator can retry
or discard; a published draft is immutable. Publication never approves,
requests changes, merges, closes, records an Oratorio decision or resolves
GitLab discussions; discussions are resolved only through finding resolution
([Oratorio Design §6.4](oratorio-design.md#64-review-finding-resolution)).

### 3.5 Implementation Delivery

GitLab implementation delivery is eligible when:

- the run is an implementation run;
- the item is a GitLab issue or an Oratorio local task, never a merge request;
- the item has, or can infer, exactly one GitLab project route;
- GitLab writes are enabled and the project has a token;
- managed worktrees are enabled, the run's worktree is ready and its diff is
  not empty;
- the delivery policy allows manual or automatic delivery.

Merge requests remain review targets and are never changed by implementation
runs.

Delivery:

1. Check eligibility and clear earlier delivery errors.
2. Compute the changed files from the managed worktree.
3. Commit locally with the reviewed commit message.
4. Push to a branch under the Oratorio branch prefix.
5. Open a merge request with the reviewed title and body.
6. Add the merge request as a GitLab-backed task.
7. Link it to the originating issue or local task.
8. Mark the implementation draft delivered.

The local commit, branch push and merge request creation are each a source
write. If a later step fails, earlier writes stay succeeded and the draft
records the failed step. Retry re-checks current state, reuses completed steps
and retries only what remains, and never duplicates a pushed branch or merge
request that an earlier write already created. Retrying a failed merge request
creation goes through this delivery retry, not the generic note or status
retry.

The merge request uses the Oratorio implementation branch as source; the task's
branch, otherwise the project's default branch when known, otherwise `main`,
as target; the reviewed title; the reviewed body plus a reference to the
origin when available; and is not a draft merge request.

Generated merge requests start as Discovered review targets. Approving the
originating task accepts the handoff; it never approves or merges the
generated merge request.

Delivery reports a stable reason for a missing route, an ambiguous local task
route, missing credentials, an invalid worktree, an empty diff, an invalid
branch name, a failed commit, push or merge request creation, a conflict adding
the generated task, and insufficient token permission.

## 4. Settings, Diagnostics, and Security

Clients distinguish GitLab projects and merge requests from GitHub repositories
and pull requests. Settings shows the endpoint, project routes, read, write and
webhook capability, per-project credential presence and observed sync failures.

GitLab credentials can be replaced, cleared or left unchanged, and are never
shown. A project's access record lives as long as its project is configured or
routed on the current instance; removing both removes its secrets on the next
save. Changing the endpoint host clears the previous instance's access
records. Saved changes apply without a restart. Repository lists such as Auto
Review use canonical project keys.

Diagnostics report the endpoint and derived API address without credentials,
query or fragment, provider and per-project credential presence, read, write
and webhook capability, and recent sync and write failures. They follow the
parent's redacted audit contract; unknown provider settings fail validation.
