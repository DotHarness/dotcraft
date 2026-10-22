# Worktrees

A worktree gives a conversation its own copy of the repository on its own branch. The agent can edit, build, and run tests there while your checkout stays exactly as you left it, so you can keep working or run several conversations in parallel.

Worktrees need a Git workspace on the same machine as Desktop.

## Start a conversation in a worktree

In a new conversation, choose **New worktree** under the composer instead of **Work locally**. The branch control picks the branch the worktree starts from. DotCraft creates a new branch for the worktree when you send the first message.

To branch off an existing conversation instead, use **Fork into new worktree** from the conversation header or its menu in the sidebar.

## Move a conversation between local and a worktree

Open the location control under the composer of an idle conversation:

- **Handoff to worktree** moves the conversation into a new worktree. Its uncommitted changes go with it, so your checkout is clean afterwards. Leave the branch name empty to let DotCraft name it.
- **Handoff to branch** brings the conversation back. Your checkout switches to the worktree's branch, the worktree's uncommitted changes are applied there, and the worktree is removed. If your checkout has its own changes to the same files, the dialog lists them and nothing moves until you commit or stash them.

You can also ask the agent to work in a worktree, for example "do this in a worktree". It moves itself in, carries on in the same turn, and moves back when you ask; it never carries changes into your checkout on its own.

## Bring ignored files along

A new worktree contains only what Git tracks. To copy files Git ignores, such as `.env`, list them in a `.worktreeinclude` file at the repository root, using the same patterns as `.gitignore`:

```text
.env
config/local.json
```

## Clean up

Archiving a conversation removes its worktree when nothing in it is uncommitted; the branch stays, and unarchiving checks it out again. Deleting a conversation removes its clean worktree too, and deletes the branch when it holds no new commits. A worktree with uncommitted changes always stays on disk.

**Settings › Source control › Worktrees** lists every worktree of the workspace and removes the ones you no longer need. It also sets the prefix DotCraft names new worktree branches with (`dotcraft` by default).
