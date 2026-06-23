# Default Chat Workspace Desktop Handoff

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-06-23 |
| **Related Specs** | [Default Chat Workspace](../runtime/default-chat-workspace.md), [Desktop Client](desktop-client.md), [Hub Architecture](../runtime/hub-architecture.md) |

## Goal

Desktop should present threads from the default Chat workspace as a first-class `Chats` group, not as another Project.

The backend and SDK expose the default Chat workspace as a concrete normal workspace at:

```text
~/.craft/workspaces/chats
```

## Product Shape

Recommended sidebar structure:

```text
Projects
  project A
    project-bound thread

Chats
  general chat thread
  app-assisted consulting thread
```

`Chats` is a product label for the default Chat workspace. The physical path should appear only in diagnostics, settings, or developer-facing details.

## Backend Contract

- No AppServer Protocol change is required.
- No new thread kind exists.
- Desktop can connect to the default Chat AppServer through the backend/SDK default Chat helper.
- Thread rows, App Binding controls, welcome composer app selection, and origin app badges can reuse existing thread behavior once connected.

## Frontend Boundaries

- Do not show `~/.craft/workspaces/chats` under `Projects`.
- Do not expose project actions such as open folder, remove project, or copy project path as primary Chats actions.
- `New chat` in `Chats` should create a thread in the default Chat workspace.
- Project-bound threads should remain under `Projects`.
- If the user binds an App Binding app from a Chat thread, use the existing App Binding UX.

## Out Of Scope For Backend Milestone

- Renderer layout and responsive styling.
- Migration of existing project-bound threads into Chats.
- Automatic switching from a project thread to a Chat thread during app handoff.
