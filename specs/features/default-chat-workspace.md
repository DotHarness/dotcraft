# Default Chat Workspace

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Draft |
| **Date** | 2026-10-19 |
| **Related Specs** | [Hub Architecture](../architecture/hub-architecture.md), [Session Core](../architecture/session-core.md) |

## 1. Overview

The default Chat workspace gives DotCraft a lightweight conversation entry point for use cases that are not naturally tied to a user-selected project directory. It is a product-level alias for a real workspace, not a new AppServer or Session Core thread kind.

The default Chat workspace path is:

```text
~/.craft/workspaces/chats
```

Its state still lives under:

```text
~/.craft/workspaces/chats/.craft
```

## 2. Goals

- Let Desktop, SDK clients, and App Binding integrations start ordinary conversations without asking users to create a temporary project workspace.
- Preserve the invariant that each AppServer owns exactly one workspace runtime and one `.craft` state root.
- Avoid AppServer Protocol changes for default chat bootstrap.
- Keep project-bound threads and default chat threads distinguishable by their workspace path.

## 3. Non-goals

- No new `Chat` thread type.
- No change to `thread/start`, `thread/list`, or `SessionIdentity`.
- No special App Binding behavior for default chat threads.
- No execution access to the user's home directory: a chat reaches only its own folder (§4.1).

## 4. Workspace Contract

The default Chat workspace is a normal local workspace whose root is resolved from the current user's DotCraft home:

- DotCraft home: `~/.craft`
- Default Chat workspace root: `~/.craft/workspaces/chats`
- Default Chat state root: `~/.craft/workspaces/chats/.craft`

The workspace initializer must be non-interactive and idempotent:

- Create the workspace root if it does not exist.
- Create `.craft/`, `.craft/memory/`, `.craft/skills/`, and `.craft/security/`.
- Create `.craft/config.json` with `{}` only when it does not already exist.
- Never overwrite existing workspace config or user files.

### 4.1 Chat Folders

Desktop gives each new chat its own visible folder. The default Chat workspace stays the chat's
configuration and state home, so `identity.workspacePath` is unchanged; only the thread's `cwd` moves.

- **Root.** A Desktop setting, default `DotCraft/Chats` in the user's Documents folder, which
  General settings lets the user change or reset. Changing it affects only chats started afterwards.
- **Path.** `<root>/<YYYY-MM-DD>/<slug>`, with the date in local time. The slug lowercases the first
  message, takes its runs of `a-z` and `0-9`, joins the first six with `-`, and keeps at most 80
  characters; it is `new-chat` when nothing remains. A name already in use gets `-2`, then `-3`, up
  to `-100`, then a random suffix. An existing symbolic link or non-directory at the path is never
  reused.
- **Contents.** The folder holds `work/` for intermediate files and `outputs/` for deliverables.
- **Creation.** Desktop creates the folder when the first message is sent, before `thread/start`,
  and starts the thread with `cwd` set to the folder and the standalone-chat instructions below in
  `config.developerInstructions`. A resumed chat keeps its folder. Desktop never moves, renames, or
  deletes a chat folder; archiving, deleting, or renaming the chat leaves it in place.
- **Entry points.** Every Desktop path that starts a chat — the Welcome composer, the desktop pet's
  Quick Chat, the agent's thread-creation tool, and Skills' try action — uses this. Clients that
  start a chat without `cwd`, such as mobile, App Binding, and SDK clients, run in the workspace
  root.

The standalone-chat instructions are:

```text
### Standalone Chat
This chat runs in its own folder. The folder name is only a file system identifier; do not infer the user's language, locale, or preferences from it.
Prefer answering inline in chat unless local files make the result more useful.
Use work/ for intermediate files, scratch analysis, scripts, drafts, and temporary assets. Use outputs/ only for user-facing deliverables, and link only files in outputs/ when referring to saved deliverables.
Do not write directly in the user's home directory unless the user asks.
```

## 5. Runtime Behavior

Hub and SDK helpers may expose named default Chat entry points, but they must still call the existing Hub AppServer ensure flow with the concrete default Chat workspace path.

After bootstrap:

- Clients connect directly to the returned AppServer WebSocket endpoint.
- `identity.workspacePath` is the concrete default Chat workspace path.
- Threads persist in the default Chat workspace's `.craft` state.
- `thread/list` works exactly like any other workspace list.

## 6. App Binding And External Apps

App Binding integrations bind to ordinary default Chat threads. The binding, tools, approvals, and app-side confirmations use the existing App Binding model.

For read-only consulting against an external observability or business system:

- The DotCraft thread may live in the default Chat workspace.
- External application data remains owned by that application.
- DotCraft owns conversation, thread context, App Binding lifecycle, and model/tool approval.
- Mutating or external operations still require explicit scopes and app-side confirmation.

## 7. Desktop Product Contract

Desktop renders default Chat workspace threads as a separate `Chats` group rather than as a Project row. The physical workspace path is diagnostic information, not the primary label.

When Desktop starts without an explicit target or a restorable foreground entry, it shows the welcome chooser. The chooser provides a Chats entry alongside project workspace selection. Choosing Chats foregrounds the default Chat workspace; choosing a project foregrounds that workspace and adds it to the user's recent projects.

Desktop remembers which surface was last in the foreground. Later local starts restore Chats after the user has chosen Chats, restore a project workspace while that project still exists, or return to the welcome chooser when Welcome was the last foreground surface. Explicit workspace paths and workspace deep links take precedence over the remembered surface. The explicit `--no-workspace` entry point always opens the welcome chooser, and remote startup never implicitly restores the local default Chat workspace.

Choosing Chats uses the same workspace readiness and connection flow as any other local workspace. Desktop initializes the default Chat workspace skeleton non-interactively, then routes through Workspace Setup when its effective provider or model configuration is incomplete. Once the workspace is ready, Desktop connects to its AppServer and shows the main conversation UI.

A chat's Files tree, terminal, and open-in actions act on its folder. Chats offer no commit, fork into a worktree, worktree work location, or project actions.

The default Chat workspace never appears as a project or by its path: the Projects menu, the tray's AppServer list, plugin install locations, and automation targets name it `Chats`, and automations in Chats offer no worktree option.

Project workspaces remain visible under `Projects`. Default Chat workspace threads remain ordinary AppServer threads, so Desktop can reuse existing thread row, App Binding, and welcome composer behavior after it connects to the default Chat AppServer.
