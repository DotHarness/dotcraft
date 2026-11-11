# Desktop

Desktop puts the workspace, chats, diffs, plans, model configuration, and live status in one window, so you drive the agent visually instead of remembering commands. It's the easiest place to meet DotCraft for the first time.

For download, workspace selection, and model setup, follow [Getting started](./). This page covers what Desktop does for you once it's installed.

The window below runs in your browser with a sample project, so you can open a chat, read its diff, or check a plan.

<LiveExample feature="desktop" :examples="['review-changes']" />

## See what the agent did

Every step the agent takes is laid out in the window. Open a chat to walk through what it read and what it changed, review file edits as diffs, and approve writes and commands before they run. Results from [automations and goals](../guide/automations) wait here for your review too.

For a closer look, open Trace or Dashboard to see which tools a task called and how many tokens it spent.

Fenced `mermaid` blocks in a reply render as diagrams, falling back to the source block when a diagram can't be drawn. Images you paste into a chat survive a restart — reopen the chat and the thumbnails are still there.

## Explore answers in place

When a calculator, comparison, or quiz answers a question better than text, DotCraft puts one in the reply as an [interactive answer](../guide/interactive-answers). Ask for a chart, a simulation, or a few design mockups, and DotCraft draws a [visualization](../guide/visualizations) you can explore and refine without leaving the chat.

## Ask a side question

A side chat answers a quick question without interrupting the main chat. Open one with **New side chat** in the chat's **…** menu, with **Ctrl+Alt+S** (**⌘⌥S** on macOS), or by typing `/side` followed by your question. It opens as a tab in the side panel and already knows the chat so far, so you can ask what a change does or where something lives while the agent keeps working.

Select text in a reply and choose **Ask in side chat** to quote it there. A queued message can go to a side chat with **Send in side chat** instead of waiting for the current task. **Ctrl+/** moves between the main composer and the side chat.

A side chat is for questions: it reads and answers, and changes files only when you ask it to. Approvals it needs appear in its own tab. Side chats are temporary: closing the tab deletes one. Nothing from a side chat reaches the main chat unless you ask it to pass something on; that message then appears in the main chat marked **Sent from a side chat**.

## Keep several projects in one window

<LiveExample feature="desktop" :examples="['several-projects']" />

Switching workspaces switches projects: configuration, skills, memory, and automations all follow the project and stay out of each other's way. The memory switch, Dreams, and memory deletion live under **Settings → Personalization**, and [Memory and Dreams](../guide/memory) explains what each one covers.

On local startup, Desktop restores your last foreground workspace, then reopens up to four recent local projects in the background, including that foreground project. Chats stays available separately. Other projects remain in the sidebar and open when selected. Opening more than four projects never closes another project or interrupts its work.

## Chat without a project

Choose **Chats** instead of a project when a question doesn't belong to one. Each new chat gets its own folder under `Documents/DotCraft/Chats`, named after the date and the first words of your first message, such as `2026-10-11/write-a-release-note-for-v2`, or `new-chat` when the message has no English letters or digits. The agent keeps drafts and scratch files in the chat's `work` folder and saves what it makes for you in `outputs`, so one chat's files never mix with another's. The Files panel, the terminal, and **Open in** all work in that folder. Archiving, renaming, or deleting a chat leaves its folder where it is.

To keep chat files somewhere else, choose **Change** under **Settings → General → Chats folder**. Chats you start afterwards use the new location, and **Reset** goes back to `Documents/DotCraft/Chats`.

## Set up a model

Add providers, enter credentials, and pick models under **Settings → Model providers**. Credentials and endpoints go into your personal `~/.craft/config.json` rather than the workspace, so sharing workspace config with a teammate never shares a key. Use **Test** before saving to confirm the credentials and the model list are reachable. If a provider can't list models, type the model name by hand and save anyway. Pick a provider from the same list first-run setup offers: ChatGPT, OpenAI, Anthropic, DeepSeek, OpenRouter, model backends added by plugins, or a custom endpoint for any OpenAI- or Anthropic-compatible service. To use a ChatGPT subscription or an OpenRouter account, choose Sign in and sign in there instead of entering a key. When the selected provider uses the official DeepSeek API with an API key, the composer shows your DeepSeek account balance; click it for granted and topped-up amounts and a link to top up.

The model you pick here only sets the default for new chats. An existing chat keeps the settings it was created with and can switch on its own from its composer. [Subagents](../guide/subagents) follow the main agent's model by default, and you can point them at a faster or cheaper one when it helps.

## Open files in your own apps

Open a local project in VS Code, a JetBrains IDE, Windows Terminal, File Explorer, or another installed app from **Open in** in the chat's **⋯** menu or in a chat's right-click menu in the sidebar. The file viewer's **Open** button opens the current file the same way. A file reference in a reply opens at its line in VS Code, Cursor, Zed, Sublime Text, and JetBrains IDEs.

Desktop remembers the app you pick from **Open in** or the file viewer, for that project and as your default elsewhere. Choosing an app from a file reference's menu opens the file there once without changing the default. Set the default for every project under **Settings → General → Default file open destination**.

### Add your own app

To open files in an app Desktop doesn't list, or in your own script, add it to `Desktop.CustomFileHandlers` in `~/.craft/config.json`. Each entry is keyed by an id made of letters, digits, `.`, `_`, and `-`, and appears after the built-in apps.

```json
{
  "Desktop": {
    "CustomFileHandlers": {
      "notepad-plus": {
        "Label": "Notepad++",
        "Command": "C:\\Program Files\\Notepad++\\notepad++.exe"
      },
      "review": {
        "Label": "Review tool",
        "Command": "review-tool",
        "Args": ["--open"],
        "Input": "jsonStdin"
      }
    }
  }
}
```

| Field | Description | Default |
|---|---|---|
| `Label` | Name shown in the menus | Required |
| `Command` | Absolute path of the program, or a command name found on `PATH` | Required |
| `Args` | Arguments placed before the path or JSON | `[]` |
| `Input` | `path` appends the path as the last argument. `jsonArgument` appends one JSON argument, and `jsonStdin` writes the same JSON to standard input | `path` |
| `Icon` | Absolute path of an image, or a `data:image/…` URL | Generic app icon |

The JSON is `{ "target": "custom:review", "path": "<absolute path>", "location": { "line": 12, "column": 1 } }`, with `location` set to `null` when no line applies. Desktop reads the list each time a menu opens, so a saved change shows up without a restart. An entry with a missing field, an invalid id, or a command that doesn't exist is left out.

## Run project commands

Save the commands you keep typing in a project, such as starting the dev server, running the tests, or opening the project in Unity, as actions and run them from the chat. They live in the **Environment** section of the **Summary** beside a local chat. The button with an action's icon runs the action you ran most recently, and the **Actions** menu lists every action and has **Open terminal** for a terminal in the project folder. Each action runs in its own terminal tab, named after the action, and running it again restarts that tab.

The agent can read what is in the chat's terminal tabs, so after an action fails or you sign in to a server yourself, ask "why did that fail?" without pasting the output. It only reads them and never types into them.

To add the first action, choose **Actions → Set up local environment**, enter a name, an icon, and the command, and save. Desktop writes `.craft/environments/environment.json` in the project and runs the action. Add more from **Actions → Add action**.

### Edit actions in the file

Each JSON file in `.craft/environments/` is one environment, a named set of actions. Desktop also reads the `.craft/environments/` folders of parent folders up to the repository root, so the projects in a monorepo can share actions. Edits to the files show up without a restart.

```json
{
  "version": 1,
  "name": "Web",
  "actions": [
    { "name": "Dev server", "icon": "run", "command": "npm run dev" },
    { "name": "Test", "icon": "test", "command": "npm test" },
    { "name": "Reveal", "icon": "tool", "command": "explorer .", "platform": "win32" }
  ]
}
```

| Field | Description |
|---|---|
| `name` | Shown in the menu and on the terminal tab |
| `icon` | `run`, `debug`, `test`, or `tool`. Other values show as `tool` |
| `command` | Entered in the terminal, in the folder that contains the environment's `.craft` folder |
| `platform` | `win32`, `darwin`, or `linux`. The action then appears only on that system |

The project's own `environment.json` is used by default. When there are several environments, pick one from **Actions → Change environment**. Desktop remembers the choice for the project. When the file can't be read, choose **Actions → Fix local environment** to open it.

## See where your tokens go

<LiveExample feature="desktop" :examples="['token-activity']" />

**Settings → Profile** shows daily token activity on this device alongside lifetime tokens, single-day peak, and usage streaks. **Settings → Usage** breaks down token use, estimated cost, tools, and skills. Top chats shows each chat's workspace and opens it there.

Archiving or deleting a chat keeps its recorded usage and Profile activity. Deleted chats leave Top chats, which ranks existing chats only. Workspace tracing must be enabled to collect new activity, but turning it off does not hide previously recorded statistics.

## Meet your pet

The companion in the composer is your pet. It finds things to wear while you work: every so often, after enough agent activity, a new item lands in its bag and a notification shows what arrived. Select **Wear it** on the notification, or open **Settings → Pet** to change its colour, dress it from the bag, and trade ten items of one rarity for one of the next. Turning pet customization off there brings back the classic blue companion and pauses finds; the bag keeps everything for later.

## Run locally or connect to a server

By default Desktop starts or takes over the AppServer for the current workspace on this machine, and other entries share that same process without any work from you. Chats you start here aren't locked to Desktop either — pick one up from another [entry point](./entry-points).

To reach a DotCraft running on a server, enter the remote AppServer address under **Settings → Connections → Workspace**. Desktop probes the connection before saving it, so a bad address is never stored and never traps you on the next start. For the full server-side setup, see [Server Deployment](../self-hosting/server-deployment).

Remote tool calls let your agent work on another PC while the chat stays here. Pair the PC under **Settings → Connections → Satellites**, then select it in **Run on** below the composer. See [DotCraft Satellite](../guide/satellite) for setup and screen viewing.

To work on another computer that already runs DotCraft, pair with it under **Settings → Connections → Control other devices**. See [Control another computer](#control-another-computer).

To follow and answer chats from your phone, pair it under **Settings → Connections → Control this PC**. See [Phone app](./mobile) for the download and setup.

## Control another computer

Open projects on another computer from this Desktop. The agent and your files stay on that computer, and it works the same on your network or through a relay.

1. On the other computer, go to **Settings → Connections → Control this PC** (**Control this Mac** on macOS), turn on device access, and select **Add device**.
2. Select **Copy link** and get the link to this computer.
3. On this computer, go to **Settings → Connections → Control other devices**, select **Add device**, and paste the link.
4. Back on the other computer, check that it names this computer and choose **Allow**.

Open a project from the computer's page, or add a folder by its path. Projects you open stay in the sidebar, and you can remove one there or from that page. A paired computer can do anything you can do in DotCraft on the computer it controls, so pair only your own.

The two computers need to run the same DotCraft version or the other one newer. To reach a computer on another network, set up a relay on it, as described in [Access from anywhere](./mobile#access-from-anywhere). To install or update DotCraft on a machine, use [SSH](#run-locally-or-connect-to-a-server) instead.

## Stay on the latest version

On startup, DotCraft checks [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) for a newer version. When an installer exists for your platform, a download button appears in the title bar: open it to read the release notes and download the installer with progress, then DotCraft quits and opens it for you.

After an upgrade, **What's New** appears once when you enter a workspace and walks through the version's new capabilities with animated previews. Reopen it any time from **Help → What's New** or the version label at the bottom of the sidebar.

## Related docs

- [Entry points overview](./entry-points) — when to switch to the CLI, an editor, or a group chat
- [Connected Apps](../guide/connected-apps) — let a chat reach the products and services you already use
- [Observability](../self-hosting/observability) — open Dashboard to review traces, diffs, and token usage
