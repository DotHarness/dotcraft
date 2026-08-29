# Desktop

Desktop puts the workspace, threads, diffs, plans, model configuration, and live status in one window, so you drive the agent visually instead of remembering commands. It's the easiest place to meet DotCraft for the first time.

For download, workspace selection, and model setup, follow [Getting started](../../getting-started). This page covers what Desktop does for you once it's installed.

## See what the agent did

Every step the agent takes is laid out in the window. Open a thread to walk through what it read and what it changed, review file edits as diffs, and approve writes and commands before they run. Results from [automations and goals](../agent-system/automations) wait here for your review too.

For a closer look, open Trace or Dashboard to see which tools a task called and how many tokens it spent.

Fenced `mermaid` blocks in a reply render as diagrams, falling back to the source block when a diagram can't be drawn. Images you paste into a conversation survive a restart — reopen the thread and the thumbnails are still there.

## Keep several projects in one window

Switching workspaces switches projects: configuration, skills, memory, and automations all follow the project and stay out of each other's way. The memory switches, Dreams, and one-click memory reset live under **Settings → Personalization**, and [Memory and Dreams](../agent-system/memory) explains what each one covers.

## Set up a model

Add providers, enter credentials, and pick models under **Settings → Model providers**. Credentials and endpoints go into your personal `~/.craft/config.json` rather than the workspace, so sharing workspace config with a teammate never shares a key. Use **Test** before saving to confirm the credentials and the model list are reachable. If a provider can't list models, type the model name by hand and save anyway. Desktop currently supports OpenAI and Anthropic.

The model you pick here only sets the default for new threads. An existing thread keeps the settings it was created with and can switch on its own from its composer. [Subagents](../agent-system/subagents) follow the main agent's model by default, and you can point them at a faster or cheaper one when it helps.

## See where your tokens go

**Settings → Profile** shows a token activity chart that spreads daily usage across every thread in the current workspace, GitHub-contribution style, alongside lifetime tokens, single-day peak, and usage streaks. Enable tracing for the workspace first, or the chart has nothing to plot.

## Run locally or connect to a server

By default Desktop starts or takes over the AppServer for the current workspace on this machine, and other entries share that same process without any work from you. Threads you start here aren't locked to Desktop either — pick one up from another [entry point](./).

To reach a DotCraft running on a server, enter the remote AppServer address under **Settings → Connections**. Desktop probes the connection before saving it, so a bad address is never stored and never traps you on the next start. For the full server-side setup, see [Server Deployment](../self-hosted/server-deployment).

## Stay on the latest version

On startup, DotCraft checks [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) for a newer version. When an installer exists for your platform, a download button appears in the title bar: open it to read the release notes and download the installer with progress, then DotCraft quits and opens it for you.

After an upgrade, **What's New** appears once when you enter a workspace and walks through the version's new capabilities with animated previews. Reopen it any time from **Help → What's New** or the version label at the bottom of the sidebar.

## Related docs

- [Entry points overview](./) — when to switch to the CLI, an editor, or a group chat
- [Connected Apps](../agent-system/connected-apps) — let a thread reach the products and services you already use
- [Observability](../self-hosted/observability) — open Dashboard to review traces, diffs, and token usage
