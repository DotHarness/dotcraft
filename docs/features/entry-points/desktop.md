# Desktop

Desktop puts the workspace, threads, diffs, plans, model configuration, and live status in one window, so you drive the agent visually instead of remembering commands. It's the easiest place to meet DotCraft for the first time.

For download, workspace selection, and model setup, follow [Getting started](../../getting-started). This page covers what Desktop does for you once it's installed.

## See what the agent did

Every step the agent takes is laid out in the window. Open a thread to walk through what it read and what it changed, review file edits as diffs, and approve writes and commands before they run. Results from [automations and goals](../agent-system/automations) wait here for your review too.

For a closer look, open Trace or Dashboard to see which tools a task called and how many tokens it spent.

Fenced `mermaid` blocks in a reply render as diagrams, falling back to the source block when a diagram can't be drawn. Images you paste into a conversation survive a restart — reopen the thread and the thumbnails are still there.

## Explore interactive visualizations

![An interactive weather visualization rendered inside a DotCraft conversation](https://github.com/DotHarness/resources/raw/master/dotcraft/docs/inline-visualizations.webp)

Ask DotCraft to turn a comparison, explanation, or result into an interactive visualization, or invoke `$visualize` directly. The view appears inside the reply, where you can explore it without leaving the conversation. Ask for changes and DotCraft updates the same visualization; earlier replies that show it switch to the latest version. Choices you make in a visualization are kept when you return to the conversation, and DotCraft can take them into account in its next reply.

Use the view's actions to send a follow-up after confirming the prompt or copy its current state as an image.

## Get interactive answers

When a calculator, quiz, form, or side-by-side choice answers a question better than text, DotCraft can reply with one right in the message, built from the same controls as the rest of Desktop. Adjust it while you read. Its buttons can send a follow-up, fill in the composer, copy a result, or open a link after you confirm. Changes you make are still there when you reopen the conversation, and the reply's text keeps the answer when you copy it.

## Keep several projects in one window

Switching workspaces switches projects: configuration, skills, memory, and automations all follow the project and stay out of each other's way. The memory switch, Dreams, and memory deletion live under **Settings → Personalization**, and [Memory and Dreams](../agent-system/memory) explains what each one covers.

## Set up a model

Add providers, enter credentials, and pick models under **Settings → Model providers**. Credentials and endpoints go into your personal `~/.craft/config.json` rather than the workspace, so sharing workspace config with a teammate never shares a key. Use **Test** before saving to confirm the credentials and the model list are reachable. If a provider can't list models, type the model name by hand and save anyway. Desktop currently supports OpenAI and Anthropic.

The model you pick here only sets the default for new threads. An existing thread keeps the settings it was created with and can switch on its own from its composer. [Subagents](../agent-system/subagents) follow the main agent's model by default, and you can point them at a faster or cheaper one when it helps.

## See where your tokens go

**Settings → Profile** shows a token activity chart that spreads daily usage across every thread in the current workspace, GitHub-contribution style, alongside lifetime tokens, single-day peak, and usage streaks. Enable tracing for the workspace first, or the chart has nothing to plot.

## Meet your pet

The companion in the composer is your pet. It finds things to wear while you work: every so often, after enough agent activity, a new item lands in its bag and a notification shows what arrived. Select **Wear it** on the notification, or open **Settings → Pet** to change its colour, dress it from the bag, and trade ten items of one rarity for one of the next. Turning pet customization off there brings back the classic blue companion and pauses finds; the bag keeps everything for later.

## Run locally or connect to a server

By default Desktop starts or takes over the AppServer for the current workspace on this machine, and other entries share that same process without any work from you. Threads you start here aren't locked to Desktop either — pick one up from another [entry point](./).

To reach a DotCraft running on a server, enter the remote AppServer address under **Settings → Connections → Workspace**. Desktop probes the connection before saving it, so a bad address is never stored and never traps you on the next start. For the full server-side setup, see [Server Deployment](../self-hosted/server-deployment).

Remote tool calls let your agent work on another PC while the conversation stays here. Pair the PC under **Settings → Connections → Satellites**, then select it in **Run on** below the composer. See [DotCraft Satellite](../agent-system/satellite) for setup and screen viewing.

To follow and answer chats from your phone, pair it under **Settings → Connections → Phones**. See [Phone app](./mobile) for the download and setup.

## Stay on the latest version

On startup, DotCraft checks [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) for a newer version. When an installer exists for your platform, a download button appears in the title bar: open it to read the release notes and download the installer with progress, then DotCraft quits and opens it for you.

After an upgrade, **What's New** appears once when you enter a workspace and walks through the version's new capabilities with animated previews. Reopen it any time from **Help → What's New** or the version label at the bottom of the sidebar.

## Related docs

- [Entry points overview](./) — when to switch to the CLI, an editor, or a group chat
- [Connected Apps](../agent-system/connected-apps) — let a thread reach the products and services you already use
- [Observability](../self-hosted/observability) — open Dashboard to review traces, diffs, and token usage
