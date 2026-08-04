# Default Chat Workspace

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-06-23 |
| **Related Specs** | [Hub Architecture](../architecture/hub-architecture.md), [Session Core](../architecture/session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md), [Unified SDK](../sdk/sdk.md) |

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
- No Desktop renderer redesign in this backend milestone.
- No default execution access to the user's home directory.

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

Desktop should render default Chat workspace threads as a separate `Chats` group rather than as a normal Project row. The physical workspace path is diagnostic information, not the primary label.

When Desktop starts without an explicit target or a restorable foreground entry, it should show the welcome chooser. The chooser provides a Chats entry alongside project workspace selection. Choosing Chats foregrounds the default Chat workspace; choosing a project foregrounds that workspace and adds it to the user's recent projects.

Desktop should remember which surface was last in the foreground. Later local starts should restore Chats after the user has chosen Chats, restore a project workspace while that project still exists, or return to the welcome chooser when Welcome was the last foreground surface. Explicit workspace paths and workspace deep links take precedence over the remembered surface. The explicit `--no-workspace` entry point always opens the welcome chooser, and remote startup must not implicitly restore the local default Chat workspace.

Choosing Chats uses the same workspace readiness and connection flow as any other local workspace. Desktop initializes the default Chat workspace skeleton non-interactively, then routes through Workspace Setup when its effective provider or model configuration is incomplete. Once the workspace is ready, Desktop connects to its AppServer and shows the main conversation UI.

Project workspaces remain visible under `Projects`. Default Chat workspace threads remain ordinary AppServer threads, so Desktop can reuse existing thread row, App Binding, and welcome composer behavior after it connects to the default Chat AppServer.

## 8. Acceptance Checklist

- Hub exposes a reusable default Chat workspace path resolver.
- Default Chat workspace initialization is idempotent and non-interactive.
- SDKs expose default Chat local bootstrap helpers that reuse the existing Hub ensure endpoint.
- Existing workspace AppServer ensure behavior remains unchanged.
- Desktop first launch with no restorable foreground entry shows the welcome chooser.
- The welcome chooser provides both Chats and project workspace selection.
- Desktop restores Chats on a later local start after Chats was the last foreground surface.
- Choosing Chats reuses the normal Workspace Setup and AppServer connection flow.
- Explicit workspace targets override the remembered foreground surface.
- Desktop explicit `--no-workspace` startup shows the welcome chooser instead of restoring Chats or a project.
- Remote startup does not implicitly restore the local default Chat workspace.
- AppServer Protocol and Session Core receive no special chat thread branch.
