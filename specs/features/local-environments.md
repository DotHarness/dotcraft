# DotCraft Local Environments Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.3 |
| **Status** | Draft |
| **Date** | 2026-10-24 |
| **Related Specs** | [Desktop Client](../clients/desktop-client.md), [Desktop Plugins](../architecture/desktop-plugins.md), [AppServer Protocol](../protocols/appserver-protocol.md), [Session Core](../architecture/session-core.md), [Design System](../architecture/DESIGN.md) |

Purpose: define local environments, which tell DotCraft how to prepare a project's worktrees, how to clean them up, and which commands a conversation can run for that project.

## 1. Scope

A local environment is a file in a project that declares a setup script, a cleanup script, and named actions. The feature covers:

- The environment file: location, discovery, format, and writing.
- Settings › Worktrees › Environments: browsing every local project's environments and creating or editing them.
- The conversation's Environment section: running actions, choosing an environment, and setting one up.
- Choosing an environment for a new worktree, and running its setup script when the worktree is created.
- Running the cleanup script before a worktree is removed.
- Agent-assisted setup and repair.

## 2. Goals and Non-goals

### 2.1 Goals

- A user can create, edit, and delete actions and scripts without editing JSON by hand.
- A new worktree can be ready to work in (dependencies installed, services prepared) before its first Turn starts.
- Every worktree-creation path that names an environment runs its setup the same way, because Core runs it.
- A failed setup never loses work and always leaves the user a way forward.

### 2.2 Non-goals

- Editing environments of remote projects. Desktop reads and writes environment files for local projects only; a remote project's files can still be edited in the repository.
- Choosing an environment for automation run worktrees or for `EnterWorktree`. They create worktrees without an environment.
- Running setup when a thread is handed off to an existing or new worktree (`thread/worktree/handoff`), or when a missing worktree directory is checked out again.
- Capturing environment variables a setup script exports for later shells.
- Other worktree settings (worktree root, fetching upstream, automatic deletion of old worktrees).
- Deleting a whole environment from Settings. Its file can be deleted on disk.
- Keyboard shortcuts for actions.

## 3. Architecture boundary

- **Desktop** owns environment discovery, the editor, selection, and actions. Its trusted host process reads and writes environment files of local projects; the renderer and Desktop Plugins reach them through host APIs.
- **Core** owns script execution in the worktree lifecycle. It reads only the environment file a worktree-creation request names (§8), and records it on the worktree so cleanup (§9) and later runs use the same file.
- The environment file format (§4) is the contract between them. Each side validates what it reads; neither trusts the other's parse.

## 4. Environment files

### 4.1 Location and discovery

- Each `.craft/environments/*.json` file is one environment.
- Desktop reads the environment folders of the project folder and each parent up to the folder containing `.git` (at most 50 levels).
- `environment.json` in the nearest folder is the **default environment**. Others follow by distance, then path.
- Environments found in the project folder belong to the project. Environments found in a parent folder are **inherited**.
- Files are re-read when they change on disk.

### 4.2 Format

```json
{
  "version": 1,
  "name": "dotcraft",
  "setup": {
    "script": "npm install",
    "win32": { "script": "npm install --no-fund" }
  },
  "cleanup": { "script": "docker compose down --remove-orphans" },
  "actions": [
    { "name": "Run", "icon": "run", "command": "npm run dev", "platform": "win32" }
  ]
}
```

- `version` defaults to `1`; any other value is invalid.
- `name` is optional; without it the file name (without `.json`) is shown.
- `setup` and `cleanup` are optional. Each has a default `script` and optional `win32`, `darwin`, and `linux` overrides. A non-empty override replaces the default on that platform. A script that is empty after trimming does nothing.
- `actions` is optional. `icon` is `tool`, `run`, `debug`, or `test` (others show as `tool`). `platform` limits the action to `win32`, `darwin`, or `linux`. Actions with an empty name or command are dropped.
- Strings are trimmed. An unparseable or invalid file is an environment **needing attention**, with the reason.

### 4.3 Writing

- Every write replaces the whole file with the editor's content, keeping top-level fields the editor does not know.
- A write carries the revision (content hash) it was based on. If the file changed or appeared since, nothing is written and the write reports a conflict. Writing an identical file succeeds.
- A new environment in a project is written to `environment.json`, else `environment-2.json`, `environment-3.json`, and so on, in the project's `.craft/environments` folder.
- Saving an invalid file's editor replaces its contents.

### 4.4 Environment root

The folder containing an environment's `.craft` folder is its **root**. In a worktree, the root is the same relative location inside the worktree (the root's path relative to the repository root, under the worktree root). Scripts and actions run in the root.

## 5. Selection

Desktop remembers two selections per local project on this device:

- **Conversation environment**: the environment whose actions a local conversation shows. Until the user chooses, it is the default environment. The user can choose **No environment**.
- **Worktree environment**: the environment a new worktree uses. Until the user chooses, it is **No environment**.

A thread bound to a worktree uses the environment recorded on that worktree (§8.5) instead of the conversation environment. A selection naming a file that no longer exists is shown as unavailable and treated as **No environment**.

## 6. Settings › Worktrees

Settings has a **Worktrees** page with two tabs.

### 6.1 General

The current worktree settings (branch prefix and the worktree list with removal) move here from Settings › Source control. They apply to the foreground Git workspace.

### 6.2 Environments

- **Project list.** Every local project from Hub, with an **Add project** button that uses the existing add-project flow. Each project row opens its default environment and has an **Add environment** button. Under it, one row per project environment (name, and file name when it differs; "needs attention" for an invalid file) and a collapsed **Inherited environments (n)** group whose rows say which folder they come from.
- **Breadcrumb.** Environments › project › edit. A create flow opened from elsewhere shows **Back** instead and returns there after saving.
- **View.** The environment's name, an **Edit** button, and three read-only cards: setup script (platform tabs only when an override exists; a tab without one says it uses the default), cleanup script, and actions (icon, name, first command line, expandable). A project without environments offers **Create local environment**.
- **Edit.**
  - Name (required; a new environment defaults to the project folder name).
  - Setup script and Cleanup script, each with Default / Windows / macOS / Linux tabs and a multi-line code field. Setup has a **Variables** popover listing the variables of §8.3.
  - Actions: **Add action** appends a card with icon picker, name, multi-line command, platform choice (All platforms / Windows / macOS / Linux), and **Delete**. A card with only one of name or command marks the missing field.
  - **Save** is disabled, with the reason as its tooltip, while saving, while the file cannot be read, without a name, with an incomplete action, or with no changes. A new or invalid file always counts as changed.
  - A conflict keeps the edits, says the file changed on disk, and turns Save into **Discard edits**, which reloads the file. Another failure turns Save into **Retry save**. Success returns to the view and confirms with a toast. The first environment saved in a project becomes its conversation environment.
  - Leaving with unsaved edits asks to discard them.

## 7. Conversation Environment section

The conversation Summary's Environment section ([Desktop Client §5.18](../clients/desktop-client.md#518-project-actions)) changes as follows.

- **Actions.** Actions are ordered by most recent run in the project; the first is the primary button. Running an action opens or focuses the conversation's terminal tab for that action, restarts its shell in the environment root (§4.4, inside the worktree for a worktree thread), and enters the command.
- **Menu.** Each action; **Add action**; **Change environment** (No environment, each environment, Environment settings); **Open terminal**.
- **Add action dialog.** Icon, name, and command, with an **Environment settings** link. Saving appends the action to the environment file and records it as the most recent action so it becomes the primary button. It does not run it.
- **Environment needing attention.** The menu offers **Fix environment**, which opens it in Settings.
- **No environments.** The menu offers **Set up with agent** (§10.1) and **Set up manually**. Set up manually opens **Set up this project**: one setup script field (with an **Advanced options** section holding the full editor) and a **Set up project** button. It creates the project's `environment.json`, selects it as the conversation and worktree environment, and runs the setup script now in the conversation's terminal at the environment root, with the §8.3 variables both naming the current execution workspace.
- **Onboarding tip.** In a worktree thread of a local project without environments, a dismissible tip suggests setting the project up once to reuse setup and Run/Test actions, and offers the same two ways to set it up. Dismissal is remembered on this device for every project.
- **Selecting a created environment.** In a worktree thread, selecting the environment that Set up manually or Set up with agent created also records it on the thread's worktree (§8.5).

## 8. Setup

### 8.1 Choosing the environment

- Welcome's location menu ("Work in") offers **Local** and **New worktree**. Only in worktree mode does the footer show the environment selector, after the location and before the base branch: **Work without environment**, each environment (the default first and marked), and **Environment settings** (or **Set up project** when there are none). It edits the project's worktree environment. Remote projects have no selector.
- Forking into a new worktree uses the project's worktree environment.
- Desktop sends the chosen file as `environmentPath` on `worktree/createAndStart` and `worktree/createAndFork`. Without it, no setup runs.

### 8.2 When it runs

Core runs setup after the worktree is created and its included files are copied, before the thread is created and before the request returns. The request reports each step (create worktree, copy files, set up environment) as it runs, and streams the script's output.

Desktop shows creation under the held message as a quiet title line ("Preparing a worktree", or "Environment setup failed") above a bordered box that lists every step: a spinner on the running step, a check on finished ones, an error mark on a failed one, and an empty circle on pending ones. The box footer has the details toggle on the left and the actions on the right; the setup output opens inside the box, expanded on failure. Once creation finishes, the conversation takes its place.

### 8.3 How it runs

- Core accepts only a `.json` file in a `.craft/environments` folder of the source workspace or a parent up to the repository root. Otherwise the request fails before anything is created.
- The script for the host platform (§4.2) runs in the environment root inside the worktree, in the host's shell, stopping at the first failing command: bash with `set -eo pipefail` on macOS and Linux, PowerShell on Windows (else `cmd`).
- Variables: `DOTCRAFT_SOURCE_TREE_PATH` is the source workspace; `DOTCRAFT_WORKTREE_PATH` is the new worktree's workspace path.
- An empty script for the platform is reported as skipped.
- A non-zero exit fails setup. There is no timeout; the client can cancel.

### 8.4 Outcomes

- **Success.** The thread is created and the first message is sent.
- **Failure.** The worktree and thread are kept; the result carries the failure and the end of the output. Desktop holds the first message and offers **Retry** (runs setup again in the same worktree), **Continue anyway** (sends the message), **Edit environment** (opens it in Settings and returns), and **Fix with agent** (§10.2).
- **Cancel.** While creation runs, Desktop offers **Cancel** (back to the composer with the draft) and **Don't use worktree** (start the thread locally). Cancelling stops the script and leaves no worktree, branch, or thread.

### 8.5 Recording

The worktree records the environment file it was created with (`ThreadWorktreeInfo.environmentPath`). Changing the environment from a worktree thread's Environment section updates that record. Retry and cleanup read the recorded file as it is on disk then.

## 9. Cleanup

- Before Core removes a worktree that records an environment — explicit removal, archiving, deleting its thread, and handing its thread back to the local checkout — it runs that environment's cleanup script in the environment root inside the worktree, the same way as setup, without the §8.3 variables. A missing file or an empty script skips cleanup.
- If cleanup fails, the worktree is not removed:
  - Explicit removal fails with the reason and the end of the output; Desktop offers **Remove anyway**, which skips cleanup.
  - Handoff to local fails before changing either checkout.
  - Archiving or deleting the thread still completes; the worktree stays on disk as if it had uncommitted changes.

## 10. Agent assistance

### 10.1 Set up with agent

Available when the conversation can open a side chat. It opens a side chat whose first message asks the agent to inspect the project and write `.craft/environments/environment.json` in the §4.2 format, with setup and actions that work on this platform. The message names the file's absolute path in the project folder, so a worktree thread's agent writes it where discovery (§4.1) finds it. When a side-chat Turn ends and the project has a valid default environment it did not have before, Desktop selects it as the conversation and worktree environment.

### 10.2 Fix with agent

Starts a new thread in a new worktree without an environment and opens it. Its first message names the environment file, the failure, and the end of the setup output, and asks the agent to fix the environment so setup succeeds. The failed creation keeps its card, so the user can retry it after the fix.

## 11. Protocol summary

Owned by [AppServer §4.17.7](../protocols/appserver-protocol.md#4177-environments); listed here for orientation.

- `worktree/createAndStart`, `worktree/createAndFork`: optional `environmentPath` and `operationId`; result adds `setup` (`succeeded`, `failed`, or `skipped`, with exit code and output tail).
- `worktree/createProgress` and `worktree/setupOutput` notifications keyed by `operationId`.
- `worktree/cancelCreate { operationId }`.
- `worktree/runSetup { threadId, operationId? }`: runs setup again in the thread's worktree with its recorded environment.
- `worktree/setEnvironment { path, environmentPath | null }`.
- `worktree/remove` adds `skipCleanup`; error `-32106` with `environmentInvalid`, `creationCancelled`, or `cleanupFailed`.
- `ThreadWorktreeInfo.environmentPath`.

## 12. Acceptance checklist

- [ ] Settings › Worktrees shows General (moved worktree settings) and Environments.
- [ ] Every local project lists its own and inherited environments; invalid files are marked and openable.
- [ ] An environment can be created, renamed, and have scripts and actions added, edited, and deleted; the file on disk matches.
- [ ] Concurrent edits on disk are detected and never overwritten silently.
- [ ] Adding an action from the conversation saves it, makes it primary, and does not run it.
- [ ] A worktree created with an environment runs its platform setup in the right folder with both variables before the first Turn.
- [ ] Setup failure keeps the worktree and offers Retry, Continue anyway, Edit environment, and Fix with agent; Cancel leaves nothing behind.
- [ ] Removing a worktree runs its cleanup; a failing cleanup blocks removal unless skipped.
- [ ] No environments: Set up manually and Set up with agent both end with a selected environment.
- [ ] The onboarding tip works as described.
- [ ] All UI strings exist in every supported locale.

## 13. Open questions

None.
