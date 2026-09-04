# Getting Started

Install DotCraft Desktop, open your project, finish the setup wizard, and send your first message. Four steps and DotCraft is working inside your project.

![Install DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/setup.gif)

## 1. Install Desktop

Download the installer for your system from [GitHub Releases](https://github.com/DotHarness/dotcraft/releases), run it, then open DotCraft.

## 2. Open your project

Select **Open Workspace** and choose the folder that holds your project. DotCraft opens the workspace setup wizard.

If this machine already has a Claude Code configuration, the wizard adds a step that imports it. Skip that step if you'd rather start clean.

## 3. Configure a model

Choose a model provider and model in the wizard. Enter an API key when the provider requires one, or choose **Sign in with ChatGPT** and sign in after setup. To change models later, go back to Settings or ask `$dotcraft-guide` in a conversation to switch for you.

Check the summary on the last page, then select **Create Workspace**.

## 4. Start your first conversation

Type a small request in the conversation box and send it. For example:

```text
Read this project's README and tell me how to start it.
```

Once DotCraft replies, the workspace is ready for real work.

## Related docs

- [Desktop](./features/entry-points/desktop) — find your way around threads, approvals, and workspace switching
- [Plugins and tools](./features/agent-system/plugins-tools) — give the agent the abilities your tasks need
- [Memory & Dreams](./features/agent-system/memory) — carry today's decisions into your next session
