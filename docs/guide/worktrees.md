# Worktrees

A worktree gives a chat its own copy of the repository on its own branch. The agent can edit, build, and run tests there while your checkout stays exactly as you left it, so you can keep working or run several chats in parallel.

Worktrees need a Git workspace on the same machine as Desktop.

## Start a chat in a worktree

In a new chat, choose **New worktree** under the composer instead of **Work locally**. The branch control picks the branch the worktree starts from. DotCraft creates a new branch for the worktree when you send the first message.

To branch off an existing chat instead, use **Fork into new worktree** from the chat header or its menu in the sidebar.

## Move a chat between local and a worktree

Open the location control under the composer of an idle chat:

- **Handoff to worktree** moves the chat into a new worktree, or into an existing one no other chat uses, which keeps its branch. Its uncommitted changes go with it, so your checkout is clean afterwards. Leave the branch name empty to let DotCraft name it.
- **Handoff to branch** brings the chat back. Your checkout switches to the worktree's branch, the worktree's uncommitted changes are applied there, and the worktree is removed. Your checkout needs to be clean first: if it has uncommitted changes, the dialog lists them and nothing moves until you commit or stash them.

The dialog shows each step as it runs. Handoff waits until the chat is idle and has no queued messages.

You can also ask the agent to work in a worktree, for example "do this in a worktree". It moves itself in, carries on in the same turn, and moves back when you ask; it never carries changes into your checkout on its own.

## Bring ignored files along

A new worktree contains only what Git tracks. To copy files Git ignores, such as `.env`, list them in a `.worktreeinclude` file at the repository root, using the same patterns as `.gitignore`:

```text
.env
config/local.json
```

## Clean up

Archiving a chat removes its worktree when nothing in it is uncommitted; the branch stays, and unarchiving checks it out again. Deleting a chat removes its clean worktree too, and deletes the branch when it holds no new commits. A worktree with uncommitted changes always stays on disk.

**Settings › Worktrees** lists every worktree of the workspace and removes the ones you no longer need. It also sets the prefix DotCraft names new worktree branches with (`dotcraft` by default).
