# Multi-Folder Local Projects

Status: backend contract for implementation

## 1. Scope

DotCraft local Projects may attach more than one folder. One folder is primary and the
remaining folders are secondary. The Desktop owns Project editing and persistence; Session
Core owns the thread-scoped execution projection described here.

This feature does not change remote Projects. A remote Project continues to expose one folder.
It also does not make a Project folder list a sandbox policy: runtime roots inform context,
first-party tool boundaries, and sandbox construction, while the configured approval and
sandbox policies remain authoritative.

## 2. Design Principles

- A local Project has one primary folder and zero or more secondary folders.
- New chats start in the primary folder.
- Secondary folders are available as runtime content roots but do not become discovery roots.
- Thread state ownership, the working directory, and runtime access boundaries remain separate
  concepts.
- Workspace updates are sticky at thread boundaries and explicit at the protocol boundary.
- Ordered roots are stable: normalization removes duplicates without reordering the first
  occurrence.

## 3. Model

The following paths have different responsibilities and must not be collapsed:

| Concept | Owner | Meaning |
|---|---|---|
| Project primary folder | Desktop Project | Default folder for new chats and the source for project-level discovery. |
| Project secondary folders | Desktop Project | Additional code/data folders available to the chat. |
| `SessionThread.WorkspacePath` | Session Core | Stable state owner and primary-folder snapshot used for rollout, memory, goals, plans, app bindings, project discovery, and thread lookup. |
| `ThreadConfiguration.Cwd` | Session Core | Sticky working directory for tools and relative path resolution. |
| `ThreadConfiguration.RuntimeWorkspaceRoots` | Session Core | Sticky ordered set of roots considered inside the runtime workspace boundary. |
| `ExecutionWorkspaceOverride` | Session Core worktree flow | Temporary effective cwd that replaces the ordinary cwd root without moving thread state. |

For a new local Project chat, Desktop sends:

```text
WorkspacePath          = primary folder (stable for the lifetime of the thread)
Cwd                    = primary folder
RuntimeWorkspaceRoots  = [primary folder, ...secondary folders]
```

The primary folder should be first for a stable presentation order, but runtime semantics do
not depend on the first element. `Cwd` is independent and remains the source of relative paths.

## 4. Resolution Semantics

All persisted roots are normalized absolute paths. Duplicate roots are removed using the host
path comparison rules while preserving the first occurrence.

The effective ordinary cwd is:

```text
Cwd ?? WorkspaceOverride ?? WorkspacePath
```

The effective runtime cwd is:

```text
ExecutionWorkspaceOverride ?? effective ordinary cwd
```

The persisted `RuntimeWorkspaceRoots` field has three states:

- omitted/null: default to `[effective ordinary cwd]`;
- `[]`: explicitly use no runtime roots;
- non-empty: replace the complete ordered roots list.

When only `cwd` is supplied on resume, fork, or turn start, Session Core replaces occurrences of
the previous ordinary cwd in the persisted roots with the new cwd and preserves all other roots.
The result is deduplicated. When `runtimeWorkspaceRoots` is also supplied, it is a complete
replacement and no cwd retargeting is performed.

When `ExecutionWorkspaceOverride` is active, resolution replaces the ordinary cwd entry in the
effective roots with the execution workspace and preserves all secondary roots. The persisted
Project-derived roots are not rewritten by entering or leaving a worktree.

## 5. Project-to-Thread Lifecycle

- New chat: snapshot the Project primary folder as `cwd` and all attached folders as runtime
  roots.
- Existing chat, folder added/removed: Desktop sends the full current roots array on the next
  `thread/resume` or `turn/start`. The new value is sticky for subsequent turns.
- Existing chat, primary changed: existing chats keep their current cwd; new chats use the new
  primary. Existing chats also keep their original `WorkspacePath` discovery/state owner.
  Desktop still synchronizes the attached roots set.
- Existing chat whose cwd folder was removed: Desktop sends the current Project primary as
  `cwd` together with the full roots array.
- Fork: inherit cwd and roots unless explicitly replaced by the fork request.
- Worktree: use the worktree as effective cwd and replace only the ordinary cwd root; preserve
  secondary roots.

This lifecycle avoids hidden mutation of inactive threads and makes the AppServer request that
activates a thread the synchronization boundary.

## 6. Discovery and Runtime Access

Only the thread's primary-folder snapshot (`WorkspacePath`, equal to `cwd` at creation)
participates in project-level discovery:

- project instructions (`AGENTS.md`);
- workspace skills;
- workspace configuration and plugin declarations;
- Git/worktree defaults.

Secondary folders are runtime content roots. First-party file, search, shell working-directory,
LSP, approval-boundary, and sandbox construction code must treat a path inside any runtime root
as inside the workspace. Relative paths continue to resolve against `cwd`.

Changing `cwd` later is a runtime-location update and does not relocate persisted project
discovery/state. Adding a secondary folder must not implicitly load its `.craft`, skills,
plugins, or instruction files. A caller may still explicitly read those files as ordinary
content when policy permits.

## 7. Backend/API Requirements

The C# backend shall:

1. persist `Cwd` and `RuntimeWorkspaceRoots` in `ThreadConfiguration`/rollout snapshots;
2. expose resolved `cwd` and `runtimeWorkspaceRoots` on thread wire objects;
3. accept sticky overrides on `thread/start`, `thread/resume`, `thread/fork`, and `turn/start`;
4. rebuild the thread agent/tool snapshot before execution when either value changes;
5. pass the effective roots to first-party file, shell, LSP, approval, and sandbox boundaries;
6. retain `WorkspacePath` as the state and lookup key; no SQLite thread schema migration is
   required.

The Desktop Project editor, Project persistence, folder picker, and localization are explicitly
outside this backend change.
