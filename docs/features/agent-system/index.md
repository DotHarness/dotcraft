# Agent System

At the center of DotCraft is one agent that keeps growing with your project. These pages cover its four kinds of ability: extending what it can do, keeping context across conversations, handing work to more agents, and making progress while nobody is watching.

![Overview of the DotCraft Agent system](/agent-system-overview.svg)

## Extend what it can do

[Skills & Self-Learning](./skills) turns a workflow that already worked into a reusable skill, ready for the next task of the same kind. [Plugins & Tools](./plugins-tools) covers the built-in tools, plugins, and MCP servers that give the agent its capabilities, and the trust boundary around each. [Remote Tool Host](./remote-tool-host) runs eligible built-in tools in a workspace on another device without moving the Agent session. When you need more, [Plugin Marketplaces](./plugin-marketplaces) adds plugin catalogs you trust, and [Connected Apps](./connected-apps) lets a conversation work directly with the products and services you already use.

## Keep context across conversations

[Memory & Dreams](./memory) keeps project context, preferences, and decisions available from one conversation to the next. Memory is stored as plain Markdown in your workspace, so you can read and change it at any time.

## Delegate work

[Subagents](./subagents) run the tasks you hand them in their own context, keeping the main conversation clean. [Teams](./teams) puts several agents on one piece of work together. [Dynamic Workflows](./dynamic-workflows) uses reusable orchestration scripts to run large jobs in parallel in the background. [Agent Profiles](./agent-profiles) creates agents with different specialties, ready to reuse in all of the above. When a coding agent outside DotCraft should take over, [Workspace Handoff](./workspace-handoff) exports the conversation as a handoff document.

## Run unattended

[Automations & Goals](./automations) runs routine tasks on a schedule, and can give a conversation a long-running goal to keep pushing on. [Lifecycle Hooks](./hooks) run your own scripts at key moments in a conversation, such as confirming a dangerous command before it executes.
