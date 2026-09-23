# Import From Other Agents

If you've been working on the same project with Claude Code, ChatGPT, or Cursor, DotCraft can bring those chats into the workspace. You read them next to your DotCraft chats, continue any of them here, and let new ones keep arriving while the project is open.

Only chats recorded for the open project come over. Chats from other folders stay where they are.

## When to use it

- You're moving a project to DotCraft and don't want to lose the conversations that got it here.
- You keep using another agent for some tasks and want one place to look back at all of them.

## Import chats

1. Open **Settings › Import** in the project.
2. Each app found on this machine shows how many chats are ready. Click **Import** next to one.
3. Leave **Keep imports in sync** checked to have new and updated chats arrive on their own, then confirm.

Imported chats appear in the chat list with a badge naming the app they came from. Open one to read it, or send a message to continue it with DotCraft.

## What comes over

Your messages and the agent's replies come over as written. Tool activity, such as commands the agent ran or files it edited, is kept as short text notes inside the reply. Reasoning traces and images are left out.

Each import takes recent chats: those changed in the last 30 days, newest first, up to 50 per app. Chats that were already imported and have grown since receive their new messages on the next import.

Once you continue an imported chat in DotCraft, it belongs to DotCraft. Later messages added in the original app are no longer merged into it.

## Keep imports in sync

With sync on, DotCraft checks the connected apps while the project is open and imports what's new. Turn it off from **Settings › Import** to pause. Your app selections are kept, so turning it back on resumes without setup. **Check again** runs a check right away.

## Related docs

- [Workspace Handoff](./workspace-handoff) — send a DotCraft conversation the other way, to an agent outside DotCraft.
- [Subagents](./subagents) — run an external coding CLI inside DotCraft instead of switching tools.
