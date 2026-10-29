# Getting Started

Install DotCraft Desktop, open your project, connect a model, and send your first message. Four steps and DotCraft is working inside your project.

![Install DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/docs/setup.webp)

## 1. Install Desktop

Download the installer for your system from [GitHub Releases](https://github.com/DotHarness/dotcraft/releases), run it, then open DotCraft.

Releases also carries the Android [phone app](./mobile). Pair it after you finish this guide to follow your chats from your phone.

## 2. Open your project

Select **Open Workspace** and choose the folder that holds your project. The first time you use DotCraft, it asks you to set up a model before the project opens. After that, every folder you open goes straight to its conversation.

To keep project instructions from Claude Code or another agent, [import them](../guide/session-import) once the project is open.

## 3. Configure a model

Select **Start setup** and choose your interface language. Under **Model access**, pick a provider you've already saved, or connect a new one: **Sign in with ChatGPT**, an OpenAI or Anthropic API key, or another OpenAI-compatible service. Then pick a default model; every project starts with it. To use a different model in one project, switch it in that project's conversation box, or ask `$dotcraft-guide` in a conversation to switch for you.

The outline on the left shows what you've chosen so far, and you can select any finished step to change it. When everything looks right, select **Start using DotCraft**.

## 4. Start your first conversation

Type a small request in the conversation box and send it. For example:

```text
Read this project's README and tell me how to start it.
```

Once DotCraft replies, the workspace is ready for real work.

## Related docs

- [Desktop](./desktop) — find your way around threads, approvals, and workspace switching
- [Plugins and tools](../guide/plugins-tools) — give the agent the abilities your tasks need
- [Memory & Dreams](../guide/memory) — carry today's decisions into your next session
