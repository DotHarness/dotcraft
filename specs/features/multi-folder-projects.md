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
- Secondary folders are runtime content roots, never discovery roots.
- Thread state ownership, the working directory, and runtime access boundaries are separate
  concepts.
- Workspace updates are sticky at thread boundaries and explicit at the protocol boundary.
- Root order is stable: normalization removes duplicates and keeps the first occurrence.

## 3. Model

| Concept | Owner | Meaning |
|---|---|---|
| Project primary folder | Desktop Project | Default folder for new chats and the source for project-level discovery. |
| Project secondary folders | Desktop Project | Additional code and data folders available to the chat. |
| Thread `workspacePath` | Session Core | Stable state owner and lookup key for rollout, memory, goals, plans, app bindings, and project discovery. |
| Thread `cwd` | Session Core | Sticky working directory for tools and relative path resolution. |
| Thread `runtimeWorkspaceRoots` | Session Core | Sticky ordered set of roots inside the runtime workspace boundary. |
| Execution workspace override | Session Core worktree flow | Effective cwd that replaces the ordinary cwd root without moving thread state. |

For a new local Project chat, Desktop sends:

```text
workspacePath          = primary folder (stable for the lifetime of the thread)
cwd                    = primary folder
runtimeWorkspaceRoots  = [primary folder, ...secondary folders]
```

The primary folder comes first for stable presentation, but runtime semantics do not depend on
order. `cwd` remains the source of relative paths.

## 4. Project-to-Thread Lifecycle

- **New chat:** `cwd` is the Project primary folder and the runtime roots are all attached folders.
- **Folder added or removed:** Desktop sends the full current roots array on the next
  `thread/resume` or `turn/start`; the new value is sticky for later turns.
- **Primary changed:** existing chats keep their `cwd` and their original `workspacePath`; new
  chats use the new primary. Desktop still synchronizes the roots.
- **The chat's `cwd` folder was removed:** Desktop sends the current primary as `cwd` together with
  the full roots array.
- **Fork:** inherits `cwd` and roots unless the fork request replaces them.
- **Worktree:** the worktree replaces only the ordinary cwd root; secondary roots are preserved and
  the persisted roots are not rewritten.

Inactive threads are never mutated in the background; the AppServer request that activates a
thread is the synchronization boundary.

## 5. Discovery and Runtime Access

Only the thread's effective cwd participates in project-level discovery. Project instructions
walk from its nearest `.git` root through that cwd, so a worktree reads its own checkout's files.
Discovery covers project instructions (`AGENTS.md`), workspace skills, workspace configuration and
plugin declarations, and Git/worktree defaults.

Secondary folders are runtime content roots. First-party file, search, shell, LSP, and approval
boundaries treat a path inside any runtime root as inside the workspace; relative paths still
resolve against `cwd`. The Desktop viewer and Files tree likewise treat every Project folder as
inside the workspace.

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
