---
name: computer
description: "Operate native desktop applications on the user's Windows computer: read what an app window shows, click, type and use keyboard shortcuts in installed programs such as Notepad, Office or Settings. Use when a task needs a desktop app rather than a file, a command or a web page. For websites, use the browser or chrome skill instead."
tools: NodeReplJs
---

# Computer

`dotcraft.computer` operates Windows desktop apps from `NodeReplJs`. It is already present in the REPL. Do not import anything, start helper programs, or drive input with scripts, PowerShell or native APIs; only `dotcraft.computer` is permitted to touch the user's desktop.

Read [guidance](references/guidance.md) before the first desktop action in a task. Read [confirmations](references/confirmations.md) before any action that sends, submits, deletes, buys, installs or changes settings. Look up exact signatures in [api](references/api.md).

## Workflow

1. Pick the target window from `await dotcraft.computer.list_windows()`. If the app is not open, find it with `list_apps()` and start it with `launch_app({ app: id })`, then list windows again. Use only objects returned by these calls.
2. Observe with `get_window_state({ window })`. Screenshots are attached to the tool result automatically. Add `include_text: true` when you need element indexes.
3. Perform one action, then observe the window again in the same cell before deciding the next step.

The first time you use an app, the user is asked to allow it. Wait for the call to return; if it fails with `app_not_approved`, do not retry that app unless the user asks.

## Stopping

The user can stop computer use at any time with Escape, and locking the computer stops it too. When a call fails with `computer_use_stopped`, make no further `dotcraft.computer` calls in this turn. Tell the user what was completed and what remains.
