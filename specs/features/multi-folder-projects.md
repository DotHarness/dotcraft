# Multi-Folder Local Projects

| Field | Value |
|---|---|
| Version | 0.9.1 |
| Status | Living |
| Date | 2026-10-19 |
| Related Specs | [Session Core](../architecture/session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md), [Desktop Client](../clients/desktop-client.md) |

## 1. Scope

DotCraft local Projects may attach more than one folder. One folder is primary and the others are
secondary. Desktop owns Project editing and persistence; Session Core owns the thread-scoped
workspace projection, whose resolution rules are defined in
[Session Core §12.2](../architecture/session-core.md#122-thread-configuration).

Remote Projects expose one folder. Runtime roots inform context and first-party tool boundaries,
while the configured approval policy remains authoritative.

## 2. Design Principles

- A local Project has one primary folder and zero or more secondary folders.
- New chats start in the primary folder.
- Project identity is explicit and stable: changing the primary folder never changes the Project's
  state workspace or its chats' membership. Source folder membership never determines ownership.
- Secondary folders are runtime content roots, never discovery roots.
- Thread state ownership, the working directory, and runtime access boundaries are separate
  concepts.
- Workspace updates are sticky at thread boundaries and explicit at the protocol boundary.
- Root order is stable: normalization removes duplicates and keeps the first occurrence.

## 3. Model

| Concept | Owner | Meaning |
|---|---|---|
| Project identity / state workspace | Desktop Project | Stable workspace that owns the Project's connection and chats; assigned when the Project is created and unchanged by source-folder edits. |
| Project primary folder | Desktop Project | Mutable default working directory for new chats and the source for project-level discovery. |
| Project secondary folders | Desktop Project | Additional code and data folders available to the chat. |
| Thread `workspacePath` | Session Core | Stable state owner and lookup key for rollout, memory, goals, plans, app bindings, and project discovery. |
| Thread `cwd` | Session Core | Sticky working directory for tools and relative path resolution. |
| Thread `runtimeWorkspaceRoots` | Session Core | Sticky ordered set of roots inside the runtime workspace boundary. |
| Execution workspace override | Session Core worktree flow | Effective cwd that replaces the ordinary cwd root without moving thread state. |

For a new local Project chat, Desktop sends:

```text
workspacePath          = Project state workspace (stable for the lifetime of the Project)
cwd                    = current primary folder
runtimeWorkspaceRoots  = [primary folder, ...secondary folders]
```

The primary folder comes first for stable presentation, but runtime semantics do not depend on
order. `cwd` remains the source of relative paths.

## 4. Project-to-Thread Lifecycle

- **New chat:** `cwd` is the Project primary folder and the runtime roots are all attached folders.
- **Folder added or removed:** Desktop sends the full current roots array on the next
  `turn/start`, including a single-root array when the last secondary folder is removed. The new
  value is sticky for later turns. Before queueing user input, Desktop updates the workspace through
  `thread/resume` because `turn/enqueue` does not accept workspace overrides.
- **History opened:** reading and subscribing to a thread does not change its workspace; the next
  submitted Turn synchronizes its Project roots.
- **Side chat:** the fork and each new Turn synchronize roots using the parent chat's captured
  Project identity, not the currently foreground Project. Steering a running Turn does not update roots.
- **Primary changed:** update the existing Project's ordered folders without deleting its record,
  changing its identity, or reopening another state workspace. Existing chats keep their `cwd` and
  `workspacePath`; new chats use the new primary as `cwd` while retaining the same Project state
  workspace. All chats continue to synchronize the Project's current roots after further edits.
- **The chat's `cwd` folder was removed:** Desktop sends the current primary as `cwd` together with
  the full roots array.
- **Fork:** inherits `cwd` and roots unless the fork request replaces them.
- **Worktree:** the worktree replaces only a runtime root exactly equal to the ordinary cwd;
  secondary roots are preserved and persisted roots are not rewritten. If a source edit replaces
  that exact root with an ancestor, Desktop updates the ordinary cwd to the current primary before
  retargeting; containment alone cannot retain the execution root. Ordinary non-worktree chats may
  keep a cwd nested within a current root. Side chats inherit the execution override and use the
  same projection rule.
- **Worktree creation inputs:** resolve the environment selection, Git branch list, and base ref from
  the current primary source repository, independently of the fixed Project state workspace. Changing
  that source clears the previous repository's base ref and branch-name draft; thread identity and
  welcome text drafts remain attached to the fixed Project. Desktop Git controls validate against
  current Project content roots, not only the state workspace; unrelated external paths remain denied.

Inactive threads are never mutated in the background; an explicit workspace update or Turn submission
is the synchronization boundary. Desktop resolves Project roots from its existing project identity,
separately from the thread's effective cwd, and uses that same Project selection for the Files tree.
Desktop persists the mutable primary folder in the existing Project record keyed by its state
workspace; it does not infer membership from current or historical source-folder paths.
Remote and default Chat workspaces do not inherit roots from local Project folder membership.

## 5. Discovery and Runtime Access

Only the thread's effective cwd participates in project-level discovery. Project instructions
walk from its nearest `.git` root through that cwd, so a worktree reads its own checkout's files.
Discovery covers project instructions (`AGENTS.md`), workspace skills, workspace configuration and
plugin declarations, and Git/worktree defaults.

Secondary folders are runtime content roots. First-party file, search, shell, LSP, and approval
boundaries treat a path inside any runtime root as inside the workspace; relative paths still
resolve against `cwd`. The Desktop viewer and Files tree likewise treat every current Project folder
as inside the workspace. A state workspace removed from the Project's sources does not remain an
implicit content root merely because it still owns the thread state.

Changing `cwd`, or entering or leaving a worktree, does not relocate persisted state but replaces
the project-instruction context for the next Turn. Adding a secondary folder never implicitly loads
its `.craft`, skills, plugins, or instruction files; those may still be read explicitly as ordinary
content when policy permits.

## 6. Server Contract

- `cwd` and `runtimeWorkspaceRoots` are persisted with the thread configuration and exposed,
  resolved, on thread wire objects.
- `thread/start`, `thread/resume`, `thread/fork`, and `turn/start` accept them as sticky overrides.
- A change applies to queued and future Turns; a running Turn keeps its captured snapshot.
- `workspacePath` remains the state and lookup key.
- Workspace changes do not revoke connection-owned runtime dynamic tool bindings.
