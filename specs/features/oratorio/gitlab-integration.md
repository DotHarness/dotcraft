# Oratorio GitLab Integration Specification

| Field | Value |
| --- | --- |
| Version | 0.7.8 |
| Status | Living |
| Date | 2026-09-28 |
| Parent Spec | [Oratorio Design](./oratorio-design.md) |

This document defines the product and behavior contract for GitLab as a
first-class Oratorio source. It is intentionally a core design and flow
specification, not an implementation plan.

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

Oratorio supports GitLab issues and merge requests as source-backed Tasks with
the same Oratorio lifecycle, review rounds, AppServer dispatch, review draft
control, source write audit, and implementation delivery concepts that exist
for GitHub-backed work.

The integration must:

- preserve GitHub provider behavior while adding the GitLab provider;
- treat GitLab as a source provider with its own endpoint, credentials,
  project identity, webhook verification, diff anchors, commit status, and
  merge request semantics;
- support GitLab.com, self-managed GitLab, and Dedicated-style endpoint shapes;
- make GitHub and GitLab understandable side by side in Desktop Settings;
- keep every external write backend-owned, explicit, auditable, retryable, and
  separate from agent tool execution.

The integration must not:

- merge GitLab merge requests;
- silently approve GitLab merge requests;
- let agents push branches, create merge requests, or write source comments
  directly;
- require GitLab Premium or Ultimate for the baseline read/write flow;
- assume GitLab.com-only project or URL behavior;
- require OAuth browser connection setup for the baseline token flow.

## 2. Source Provider Model

Provider capabilities, sync jobs, source-write audit and canonical routing keys follow [Oratorio Design](oratorio-design.md#source-provider-model).

GitLab operators configure the full namespace path, including subgroups. A canonical key is `gitlab:<instance>/<group[/subgroup]/project>`. External identities use the project-scoped `iid`: `issue:<instance>/<project-path>#<iid>` or `mr:<instance>/<project-path>!<iid>`. Numeric project ids are cached provider metadata, not routing identity.

## 3. GitLab Provider Contract

GitLab baseline setup uses token-based authentication. OAuth setup, automatic
webhook creation, production RBAC administration, and enterprise SSO are
separate product contracts.

### 3.1 Configuration and Authentication

GitLab configuration includes:

- provider enabled state;
- GitLab instance endpoint, defaulting to `https://gitlab.com`;
- configured project paths;
- one project profile per configured GitLab project;
- write enablement state;
- local-development webhook bypass state.

Each GitLab project profile is keyed by canonical project key:

```text
gitlab:<instance>/<group[/subgroup]/project>
```

The persisted profile data is `Oratorio:GitLab:ProjectProfiles[]` with:

- `Instance`;
- `ProjectPath`;
- `TokenKind`;
- write-only `Token`;
- optional write-only `WebhookSecret`;
- optional write-only `WebhookSigningToken`.

`TokenKind` is an operator-facing label, not a permission claim. The first
implementation supports exact project matching only. Group tokens or personal
tokens can be used by entering the same token in multiple project profiles;
there is no group-prefix inheritance.

The GitLab endpoint is the single operator-facing URL setting. The API URL is
derived as:

```text
<endpoint>/api/v4
```

Desktop must not ask the operator to separately configure an API base URL.
Runtime behavior always follows the endpoint-derived URL.

GitLab read access requires a project profile token that can read the target
project. GitLab write access requires a project profile token that can create
notes, discussions, statuses, push branches, and create merge requests for the
target project. The UI and diagnostics report token presence and observed
capability per project, but they must not claim a specific scope is present
unless a provider capability check has verified it.

GitLab writes happen as the target project profile's token identity. Operators
should prefer project access tokens for single-project automation and use group
or personal tokens only when broader access is intentional.

### 3.2 Read Sync

Configured projects are resolved independently. If one project fails to resolve
or sync, that project run fails with a stable error and does not block other
configured projects.

If at least one project profile exists but a configured project has no matching
profile token, that project fails read sync with
`gitlabProjectProfileTokenMissing`. Provider capability may still be `partial`
when at least one configured project remains readable.

GitLab issues import as Oratorio issues with:

- title;
- description;
- assignee;
- labels;
- web URL;
- source created and updated times;
- source lifecycle state;
- closed time when available;
- project key;
- source snapshot.

GitLab merge requests import as Oratorio review targets with:

- title;
- description;
- assignees and reviewers when useful to source context;
- labels;
- web URL;
- source branch;
- target branch;
- draft state;
- source created and updated times;
- source lifecycle state;
- closed or merged time when available;
- head SHA;
- diff refs when available;
- project key;
- source snapshot.

GitLab source state maps to Oratorio source state:

| GitLab state | Oratorio source state |
| --- | --- |
| `opened` | `open` |
| `closed` | `closed` |
| `merged` | `merged` |
| anything else | `unknown` |

Details hydrate loads source context that should not be part of every board
sync:

- issue notes;
- merge request notes;
- merge request discussions;
- relevant system notes;
- current merge request diff refs;
- current detailed merge status when available.

Imported source context uses source visibility and must not be treated as
Oratorio operator feedback.

Closed GitLab issues and closed or merged GitLab merge requests are archived
when no Oratorio run is active. If a source item reopens and the archive reason
was source-driven, Oratorio reopens it to `discovered`. Manual archive remains
operator-owned and must not be undone by GitLab sync.

### 3.3 Webhooks

GitLab webhooks enqueue provider sync jobs. They do not directly mutate Tasks.

Supported webhook verification modes:

- Standard Webhooks signing token verification when GitLab sends signing
  headers;
- GitLab secret-token verification through `X-Gitlab-Token`;
- disabled verification only when explicitly enabled for local development.

Webhook verification is selected from the profile matching the webhook payload's
project path. Signing headers are verified first when present, then
`X-Gitlab-Token` is checked. Missing profile secrets reject with `403`. The
local unsafe bypass remains a provider-level local-development setting.

Supported event families include issues, merge requests, notes, and pushes.
Events outside configured projects are ignored with an audit-visible diagnostic
when feasible.

### 3.4 Decisions, Review Drafts, and Statuses

GitLab write capability is available only when:

- GitLab is configured;
- writes are enabled;
- the target project is configured;
- the target project has a project profile token;
- the target Task is GitLab-backed and has a valid project path plus iid.

If a gate fails, Oratorio records a failed source write with a stable error.
Task decision state and review draft state are not rolled back.

Decision write mapping:

| Oratorio item | Operator action | GitLab write |
| --- | --- | --- |
| Issue | `approve`, `requestChanges`, or `reject` | issue note |
| Merge request | `approve` | MR note plus commit status `success` |
| Merge request | `requestChanges` | MR note plus commit status `failed` |
| Merge request | `reject` | MR note plus commit status `failed` |
| Merge request | `reReview` | no GitLab write |
| Local task | any decision | no external source write |

The commit status name is:

```text
oratorio/review
```

The status target SHA is the current GitLab merge request head SHA. If no head
SHA is available, the status write fails independently; a note write may still
succeed as a separate audited write.

GitLab MR Approval API is not part of the baseline. If later enabled, it must
be a separate explicit provider capability because eligibility, tier support,
password re-authentication, and author/committer restrictions vary by GitLab
configuration.

Review Draft submission remains source-neutral. GitLab-specific behavior
happens during validation and publication.

GitLab diff anchor validation uses current merge request diff metadata:

- `base_sha`;
- `start_sha`;
- `head_sha`;
- old path and new path;
- old line or new line.

Oratorio draft side maps as:

| Oratorio side | GitLab position |
| --- | --- |
| `RIGHT` | `new_line` |
| `LEFT` | `old_line` |

For renamed files, validation uses both old path and new path. For new files,
only new-side anchors are valid. For deleted files, only old-side anchors are
valid. Invalid comments are skipped with warnings and must never be published
as misleading overview notes.

Publication rules:

- summary-only drafts publish as one merge request note;
- each accepted inline finding creates one GitLab-visible discussion or draft
  note;
- replace-based suggestions are emitted only when Oratorio has resolved
  `suggestion.oldText` to an exact, unique new-side diff range that GitLab can
  render safely;
- multi-line suggestion replacements use GitLab's offset-aware suggestion fence
  form (`suggestion:-N+M`) so a discussion anchored at the final new-side line
  can cover preceding changed lines;
- accepted comment-only findings omit suggestion fences and carry their
  `commentOnly.reason` in Oratorio for operator audit;
- every source write links back to the Oratorio draft;
- successful publication makes the draft immutable;
- failed publication leaves the draft retryable.

Draft auto-publish is allowed only when:

- the project is in the provider-specific publish allowlist;
- the draft has no warnings or skipped comments;
- current MR head SHA matches the reviewed head SHA;
- write capability is available;
- diff refs are current.

Auto-publish never approves, requests changes, merges, closes, resolves the
Oratorio Task, or resolves GitLab discussions.

### 3.5 Implementation Delivery

GitLab implementation delivery is eligible when:

- the run purpose is `implementation`;
- the item is a GitLab issue or an Oratorio local task;
- the item is not a GitLab merge request;
- the item has or can infer exactly one GitLab source project route;
- GitLab writes are enabled;
- the target GitLab project has a project profile token;
- managed worktrees are enabled and the run has a ready managed worktree;
- the managed worktree has a non-empty diff;
- the delivery policy permits manual delivery or automatic review target
  delivery.

GitLab merge requests remain review targets. They must not be mutated by
implementation runs.

Delivery flow:

1. Verify eligibility and clear prior delivery errors.
2. Compute changed files from the managed worktree.
3. Create a local commit with the operator-reviewed commit message.
4. Push the current HEAD to a GitLab branch under the Oratorio branch namespace.
5. Create a GitLab merge request with the operator-reviewed title and body.
6. Upsert the generated merge request as a GitLab-backed Oratorio Task.
7. Link the generated merge request to the originating issue or local task.
8. Mark the implementation draft delivered.

Each side effect has a source write record:

- local commit;
- branch push;
- review target creation.

If a later step fails, completed prior write records remain succeeded and the
draft records the failed step. Retry re-validates current state and must not
duplicate source-visible objects when a prior audit record identifies an
existing pushed branch or merge request.
Implementation delivery retry is step-aware: if the local commit or branch push
already succeeded, Oratorio reuses those audit records and retries only the
remaining review target creation step. Retrying the failed review target
creation source write follows the same delivery retry path rather than the
generic note/status source-write retry path.

GitLab merge request creation uses:

- source branch: the Oratorio-generated implementation branch;
- target branch: the Task branch when present, otherwise the provider project's
  default branch when known, otherwise `main`;
- title: operator-reviewed proposed PR/MR title;
- description: operator-reviewed proposed body plus origin reference when
  available;
- draft state: false by default unless a later policy explicitly introduces
  draft merge requests.

Generated merge requests start as `discovered` review targets. Approving the
originating implementation Task accepts the handoff; it does not approve or
merge the generated merge request.

Delivery failures surface stable errors for missing route, ambiguous local-task
route, missing credentials, invalid managed worktree, empty diff, invalid
branch name, local commit failure, branch push failure, GitLab merge request
creation failure, generated Task upsert conflict, and insufficient token
permission.

## 4. Desktop Settings and Operator UX

Source-neutral clients distinguish GitLab projects and MRs from GitHub repositories and PRs. Configuration exposes the endpoint, project routes, read/write/webhook capability, per-project credential presence and observed sync failures.

GitLab credentials use one-shot replace, clear and unchanged semantics and are never echoed. Profiles persist while their project is configured or routed on the current instance; removing both removes the secrets on the next configuration save. Changing the endpoint host clears prior-instance profiles. Saved changes apply without a server restart.

Auto Review uses the shared first-enable baseline and head-SHA re-review policy once read sync is available. Review and publication allowlists use canonical project keys. Provider-specific layout belongs to the consuming UI.

## 5. Diagnostics, Security, and Operations

Diagnostics report the endpoint and derived API URL without userinfo/query/fragment, provider and per-project credential presence, read/write/webhook capability and recent sync/write failures. They follow the parent's redacted audit contract; unknown provider fields fail validation.

## 6. Configuration Invariants

- Project paths are normalized without truncating subgroups.
- Canonical GitLab project keys include provider and instance.
- The API URL is derived from the configured endpoint.
- Settings writes GitLab credentials only through `ProjectProfiles[]`.
- Changing the endpoint instance requires corresponding workspace routes and
  automation allowlists to use the new canonical project keys.
