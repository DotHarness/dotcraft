# DotCraft Getting Started

This path is for first-time DotCraft users: install Desktop, choose a project folder, configure a model provider, then run your first session. After that, move into TUI, AppServer, API, SDK, or Automations as needed.

## Quick Start

### 1. Download Desktop

Go to [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) and download the desktop app:

| Platform | Recommended file |
|----------|------------------|
| Windows x64 | `DotCraft-vX.Y.Z-win-x64-Setup.exe` |
| Windows ARM64 | `DotCraft-vX.Y.Z-win-arm64-Setup.exe` |
| macOS | `DotCraft-vX.Y.Z-macos-x64.dmg` |

Desktop is the recommended first entry point because workspace selection, model configuration, sessions, diffs, plans, automation review, and runtime status live in one UI.

If you only need the CLI and TUI binaries, install them directly:

```bash
curl -fsSL https://dotharness.github.io/dotcraft/install.sh | bash
```

Windows PowerShell:

```powershell
irm https://dotharness.github.io/dotcraft/install.ps1 | iex
```

The Windows PowerShell installer auto-selects the x64 or ARM64 CLI/TUI archive. Linux and macOS CLI release archives are currently x64-only.

![DotCraft](https://github.com/DotHarness/resources/raw/master/dotcraft/desktop.png)

If you prefer building from source, install the [.NET 10 SDK](https://dotnet.microsoft.com/download), Rust toolchain, and Node.js, then run this from the repository root:

```bash
build.bat
```

On Linux / macOS, run:

```bash
bash build_linux.bat
```

### 2. Initialize a Workspace

On first launch, choose a real project folder as the workspace. DotCraft keeps that project's configuration, sessions, tasks, skills, and attachments with the project, so Desktop, terminal, and automation entry points can continue from the same context.

To complete first-time setup from a terminal, run this in a project directory that does not yet have `.craft/`:

```bash
dotcraft
```

Start from a real project folder instead of an empty directory so the agent can read repository structure, existing docs, and build scripts.

When Desktop finds an existing project-level `AGENTS.md` or `CLAUDE.md` during first-time setup, it offers to copy one of those files into `.craft/AGENTS.md`. This is a one-time snapshot import: the original file stays where it is, and future DotCraft behavior comes from `.craft/AGENTS.md`.

![Setup](https://github.com/DotHarness/resources/raw/master/dotcraft/setup.gif)

### 3. Configure a Model

In Desktop, the setup wizard handles this end to end: choose a provider, paste an API key, and pick a model — no files to edit. DotCraft works with three common paths:

| Path | Best for |
|------|----------|
| Anthropic | Calling Claude models through the native Anthropic client |
| OpenAI / OpenAI-compatible | OpenAI API, OpenRouter, DeepSeek, MiMo, and compatible providers |
| ChatGPT subscription | Reuse an existing ChatGPT Plus / Pro / Team / Business / Enterprise plan — no separate API key |

Already paying for ChatGPT Plus / Pro / Team / Business / Enterprise? Pick **Sign in with ChatGPT** in the wizard's OpenAI template, or run `dotcraft auth openai login` after setup, to reuse that subscription instead of an API key.

Prefer to edit configuration directly? The minimal config is a list of providers plus the selected `ProviderId` and `Model`:

```json
{
  "ProviderId": "anthropic",
  "Model": "claude-sonnet-4-5",
  "Providers": {
    "anthropic": {
      "DisplayName": "Anthropic",
      "Protocol": "anthropic",
      "ApiKey": "${ANTHROPIC_API_KEY}"
    },
    "openrouter": {
      "DisplayName": "OpenRouter",
      "Protocol": "openai-chat-completions",
      "ApiKey": "${OPENROUTER_API_KEY}",
      "EndPoint": "https://openrouter.ai/api/v1"
    }
  }
}
```

Keep API keys and endpoints in the global file at `~/.craft/config.json`; a workspace usually overrides only `ProviderId` and `Model`. For every field — protocols, endpoints, and the `/v1` rule for OpenAI-compatible services — see the [Configuration Reference](./developing/configuration).

### 4. Run the First Session

Open the workspace in Desktop, create a session, and send a lightweight request:

```text
Read this repository's README and docs/index.md, then tell me how to start the project.
```

If you prefer a script-friendly command-line entry, run a one-shot task from the project directory:

```bash
dotcraft exec "Read this repository's README and docs/index.md, then tell me how to start the project."
```

Running plain `dotcraft` only handles first-time setup — in a workspace that already has `.craft/`, it won't open an interactive chat. For a terminal session, continue with the [TUI guide](./features/entry-points/tui).

## One workspace, every entry point

DotCraft connects Desktop, the terminal, IDEs, bots, and automations to the same project workspace. A conversation you start in one place can continue in another, with the same sessions, memory, and tools.

![Unified entry model](https://github.com/DotHarness/resources/raw/master/dotcraft/entry.png)

Curious how one engine powers every entry point? See [Architecture Overview](./developing/architecture/overview).

## Configuration

First-time setup only needs a few fields:

| Field | Purpose | Recommended location |
|-------|---------|----------------------|
| `Providers` | Model provider registry, including API keys and endpoints | Global config |
| `ProviderId` | Current model provider id | Global or workspace config |
| `Model` | Default model name | Global or workspace config |
| `DashBoard.Enabled` | Enable web debugging and visual configuration | Workspace config |

If unsure, put providers globally and let the workspace override only `ProviderId` and `Model`.

## Choose the Next Step by Goal

| Goal | Next step |
|------|-----------|
| Work visually with sessions and diffs | [Desktop](./features/entry-points/desktop) |
| Use a full terminal interface | [TUI](./features/entry-points/tui) |
| Share a workspace across remote or multiple clients | [AppServer Mode](./developing/lifecycle/appserver) |
| Connect an IDE or editor | [IDE / Editors (ACP)](./features/entry-points/editors) |
| Build bots or external adapters | [Channels & Bots](./features/entry-points/channels) |
| Run local automation tasks | [Automations & Goals](./features/agent-system/automations) |
| Inspect traces, tool calls, and merged configuration | [Observability](./features/self-hosted/observability) |

## Explore More

### Social Channels

DotCraft integrates with Telegram, WeChat, Feishu/Lark, QQ, WeCom, and other social channels through SDK extensions. See [Channels & Bots](./features/entry-points/channels), the [Python SDK](./developing/sdks/python), and the [TypeScript SDK](./developing/sdks/typescript).

| Telegram (Python SDK) | WeChat (TypeScript SDK) |
|:---:|:---:|
| ![Telegram channel example](https://github.com/DotHarness/resources/raw/master/dotcraft/telegram.jpg) | ![WeChat channel example](https://github.com/DotHarness/resources/raw/master/dotcraft/wechat.jpg) |

### Automations

Automations are for running local workspace tasks. Scheduling, thread binding, templates, Goals, and retry flows are covered in [Automations & Goals](./features/agent-system/automations).

| Desktop Automations |
|:---:|
| ![Desktop automations panel](https://github.com/DotHarness/resources/raw/master/dotcraft/desktop_automations.png) |

### Dashboard

Dashboard is DotCraft's visual inspection and configuration surface for sessions, traces, and workspace settings. See [Observability](./features/self-hosted/observability) for the page overview.

| Usage overview | Session trace |
|:---:|:---:|
| ![Dashboard usage overview](https://github.com/DotHarness/resources/raw/master/dotcraft/dashboard.png) | ![Dashboard session trace](https://github.com/DotHarness/resources/raw/master/dotcraft/trace.png) |

## Advanced Topics

- Use [Hooks](./features/self-hosted/security#hooks) to run scripts on lifecycle events.
- Use [Security & Sandbox](./features/self-hosted/security) to constrain file, shell, and network access.
- Use [Samples & Templates](./resources/samples) to validate a complete workspace template.
- For an architectural view, jump to [Architecture Overview](./developing/architecture/overview).
