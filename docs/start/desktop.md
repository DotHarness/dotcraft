# Desktop

Desktop puts the workspace, threads, diffs, plans, model configuration, and live status in one window, so you drive the agent visually instead of remembering commands. It's the easiest place to meet DotCraft for the first time.

For download, workspace selection, and model setup, follow [Getting started](./). This page covers what Desktop does for you once it's installed.

## See what the agent did

Every step the agent takes is laid out in the window. Open a thread to walk through what it read and what it changed, review file edits as diffs, and approve writes and commands before they run. Results from [automations and goals](../guide/automations) wait here for your review too.

For a closer look, open Trace or Dashboard to see which tools a task called and how many tokens it spent.

Fenced `mermaid` blocks in a reply render as diagrams, falling back to the source block when a diagram can't be drawn. Images you paste into a conversation survive a restart — reopen the thread and the thumbnails are still there.

## Explore answers in place

When a calculator, comparison, or quiz answers a question better than text, DotCraft puts one in the reply as an [interactive answer](../guide/interactive-answers). Ask for a chart, a simulation, or a few design mockups, and DotCraft draws a [visualization](../guide/visualizations) you can explore and refine without leaving the conversation.

## Keep several projects in one window

Switching workspaces switches projects: configuration, skills, memory, and automations all follow the project and stay out of each other's way. The memory switch, Dreams, and memory deletion live under **Settings → Personalization**, and [Memory and Dreams](../guide/memory) explains what each one covers.

## Set up a model

Add providers, enter credentials, and pick models under **Settings → Model providers**. Credentials and endpoints go into your personal `~/.craft/config.json` rather than the workspace, so sharing workspace config with a teammate never shares a key. Use **Test** before saving to confirm the credentials and the model list are reachable. If a provider can't list models, type the model name by hand and save anyway. Desktop currently supports OpenAI and Anthropic.

The model you pick here only sets the default for new threads. An existing thread keeps the settings it was created with and can switch on its own from its composer. [Subagents](../guide/subagents) follow the main agent's model by default, and you can point them at a faster or cheaper one when it helps.

## See where your tokens go

**Settings → Profile** shows a token activity chart that spreads daily usage across every thread in the current workspace, GitHub-contribution style, alongside lifetime tokens, single-day peak, and usage streaks. Enable tracing for the workspace first, or the chart has nothing to plot.

## Meet your pet

The companion in the composer is your pet. It finds things to wear while you work: every so often, after enough agent activity, a new item lands in its bag and a notification shows what arrived. Select **Wear it** on the notification, or open **Settings → Pet** to change its colour, dress it from the bag, and trade ten items of one rarity for one of the next. Turning pet customization off there brings back the classic blue companion and pauses finds; the bag keeps everything for later.

## Run locally or connect to a server

By default Desktop starts or takes over the AppServer for the current workspace on this machine, and other entries share that same process without any work from you. Threads you start here aren't locked to Desktop either — pick one up from another [entry point](./entry-points).

To reach a DotCraft running on a server, enter the remote AppServer address under **Settings → Connections → Workspace**. Desktop probes the connection before saving it, so a bad address is never stored and never traps you on the next start. For the full server-side setup, see [Server Deployment](../self-hosting/server-deployment).

Remote tool calls let your agent work on another PC while the conversation stays here. Pair the PC under **Settings → Connections → Satellites**, then select it in **Run on** below the composer. See [DotCraft Satellite](../guide/satellite) for setup and screen viewing.

To follow and answer chats from your phone, pair it under **Settings → Connections → Phones**. See [Phone app](./mobile) for the download and setup.

## Stay on the latest version

On startup, DotCraft checks [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) for a newer version. When an installer exists for your platform, a download button appears in the title bar: open it to read the release notes and download the installer with progress, then DotCraft quits and opens it for you.

After an upgrade, **What's New** appears once when you enter a workspace and walks through the version's new capabilities with animated previews. Reopen it any time from **Help → What's New** or the version label at the bottom of the sidebar.

## Related docs

- [Entry points overview](./entry-points) — when to switch to the CLI, an editor, or a group chat
- [Connected Apps](../guide/connected-apps) — let a thread reach the products and services you already use
- [Observability](../self-hosting/observability) — open Dashboard to review traces, diffs, and token usage
