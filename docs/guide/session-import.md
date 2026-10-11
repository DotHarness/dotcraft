# Import From Other Agents

Bring your tools, project instructions, and chats from Claude Code, ChatGPT, or Cursor into DotCraft. Keep using familiar skills and commands, read earlier conversations, and continue those chats here.

Only chats recorded for the open project come over. Chats from other folders stay where they are.

## When to use it

- You're moving a project to DotCraft and don't want to lose the conversations that got it here.
- You keep using another agent for some tasks and want one place to look back at all of them.

## Choose what to import

1. Open **Settings › Import** in the project.
2. Choose **Import** next to an app, then select the content to bring over.
3. Review the destination and confirm. Existing configuration is not overwritten.

**Tools & setup** applies to all DotCraft projects on the current host. **Current project configuration** applies only to the open project. When connected remotely, both the source files and destinations are on the remote host.

DotCraft imports skills, instructions, commands, hooks, MCP servers, and plugins. Instructions become `AGENTS.md`; commands remain slash commands. Product names in imported instructions, skill descriptions and commands are adapted to DotCraft. Plugin contents stay with their plugin.

After importing, use **Needs attention** to review hooks or finish connecting tools. Hooks remain inactive until trusted. **Import history** shows what succeeded, failed, or needs review, with links to the imported content.

Imported chats appear in the chat list with a badge naming the app they came from. Open one to read it, or send a message to continue it with DotCraft.

## What comes over

Your messages and the agent's replies come over as written. Tool activity, such as commands the agent ran or files it edited, is kept as short text notes inside the reply. Reasoning traces and images are left out.

Each import takes recent chats: those changed in the last 30 days, newest first, up to 50 per app. Chats that were already imported and have grown since receive their new messages on the next import.

Once you continue an imported chat in DotCraft, it belongs to DotCraft. Later messages added in the original app are no longer merged into it.

## Keep imports in sync

With sync on, DotCraft checks the apps you have imported from while the project is open. A later manual import only adjusts the categories it showed. Use **Content to sync › Customize** to choose categories, or explicitly include all categories and future additions. Tools and configuration are imported only when missing; existing content is never replaced or deleted. If you delete an imported item while its category is still selected, a later sync can import it again.

Turn sync off from **Settings › Import** to pause. Your selections and imported content remain. **Check again** refreshes the available imports immediately.

## Related docs

- [Workspace Handoff](./workspace-handoff) — send a DotCraft conversation the other way, to an agent outside DotCraft.
- [Subagents](./subagents) — run an external coding CLI inside DotCraft instead of switching tools.
