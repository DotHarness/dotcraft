# DotCraft Getting Started

This path is for first-time DotCraft users: install Desktop, choose a project folder, configure a model provider, then run your first session. After that, move into TUI, AppServer, API, SDK, or Automations as needed.

## Quick Start

### 1. Download Desktop

Go to [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) and download the desktop app:

| Platform | Recommended file |
|----------|------------------|
| Windows | `DotCraft-vX.Y.Z-win-x64-Setup.exe` |
| macOS | `DotCraft-vX.Y.Z-macos-x64.dmg` |

Desktop is the recommended first entry point because workspace selection, model configuration, sessions, diffs, plans, automation review, and runtime status live in one UI.

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

DotCraft uses a provider registry for model services. Common paths include:

| Path | Best for |
|------|----------|
| Anthropic | Calling Claude models through the native Anthropic client |
| OpenAI / OpenAI-compatible | OpenAI API, OpenRouter, DeepSeek, MiMo, and compatible providers |
| ChatGPT subscription | Reuse an existing ChatGPT Plus / Pro / Team / Business / Enterprise plan — no separate API key |

The minimal configuration usually contains a `Providers` registry plus the selected `ProviderId` and `Model`:

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

`Protocol: "anthropic"` uses the native Anthropic interface and defaults to `https://api.anthropic.com` when `EndPoint` is omitted. OpenAI-compatible Chat Completions services use `Protocol: "openai-chat-completions"`; third-party compatible endpoints usually need an `EndPoint` ending in `/v1`. Use `openai-responses` for endpoints that support the OpenAI Responses API. DeepSeek V4 and MiMo V2.5 work out of the box on either OpenAI protocol with native reasoning controls.

Already paying for ChatGPT Plus / Pro / Team / Business / Enterprise? Pick **Sign in with ChatGPT** in the setup wizard's OpenAI template, or run `dotcraft auth openai login` after setup, to reuse that subscription instead of an API key.

Put secrets and endpoints in the global `Providers` registry under `~/.craft/config.json`; workspaces usually save only `ProviderId` and `Model` overrides. If you need to edit files directly, the paths are global `~/.craft/config.json` and workspace `<workspace>/.craft/config.json`. See [Configuration Reference](./developing/configuration.md) for the full field list.

### 4. Run the First Session

Open the workspace in Desktop, create a session, and send a lightweight request:

```text
Read this repository's README and docs/index.md, then tell me how to start the project.
```

If you prefer a script-friendly command-line entry, run a one-shot task from the project directory:

```bash
dotcraft exec "Read this repository's README and docs/index.md, then tell me how to start the project."
```

In an initialized workspace, `dotcraft` does not enter an interactive chat. Use the TUI for terminal interaction.

For a richer terminal UI, continue with the [TUI guide](./features/entry-points/tui.md).

## Understand the Entry Model

DotCraft organizes its entry points around the **Unified Session Core**: command-line runs, Desktop, IDEs, bots, and automations do not each maintain their own agent loop, but reuse the same execution engine and session model.

| Dimension | Gateway | Unified Session Core |
|-----------|---------|----------------------|
| Client customization | Hard to customize once everything is flattened into a message bus | Flexible, native client experiences |
| Approval / HITL | Cannot express platform-native approval flows | Rendered with native platform UI |
| Cross-channel resume | Not supported | Conversations can resume across channels |
| Workspace persistence | Not supported | Designed around the workspace |

![Unified entry model](https://github.com/DotHarness/resources/raw/master/dotcraft/entry.png)

DotCraft connects different entry points to the same project-scoped workspace, while the Unified Session Core handles execution, state, and orchestration.

## Configuration

First-time setup only needs a few fields:

| Field | Purpose | Recommended location |
|-------|---------|----------------------|
| `Providers` | Model provider registry, including API keys and endpoints | Global config |
| `ProviderId` | Current model provider id | Global or workspace config |
| `Model` | Default model name | Global or workspace config |
| `Language` | UI language: `Chinese` / `English` | Global config |
| `DashBoard.Enabled` | Enable web debugging and visual configuration | Workspace config |

If unsure, put providers globally and let the workspace override only `ProviderId` and `Model`.

## Choose the Next Step by Goal

| Goal | Next step |
|------|-----------|
| Work visually with sessions and diffs | [Desktop](./features/entry-points/desktop.md) |
| Use a full terminal interface | [TUI](./features/entry-points/tui.md) |
| Share a workspace across remote or multiple clients | [AppServer Mode](./developing/appserver.md) |
| Connect an IDE or editor | [IDE / Editors (ACP)](./features/entry-points/editors.md) |
| Build bots or external adapters | [Channels & Bots](./features/entry-points/channels.md) |
| Run local automation tasks | [Automations & Goals](./features/automations.md) |
| Inspect traces, tool calls, and merged configuration | [Observability](./features/observability.md) |

## Explore More

### Social Channels

DotCraft integrates with Telegram, WeChat, Feishu/Lark, QQ, WeCom, and other social channels through SDK extensions. See [Channels & Bots](./features/entry-points/channels.md), the [Python SDK](./developing/sdk-python.md), and the [TypeScript SDK](./developing/sdk-typescript.md).

| Telegram (Python SDK) | WeChat (TypeScript SDK) |
|:---:|:---:|
| ![Telegram channel example](https://github.com/DotHarness/resources/raw/master/dotcraft/telegram.jpg) | ![WeChat channel example](https://github.com/DotHarness/resources/raw/master/dotcraft/wechat.jpg) |

### Automations

Automations are for running local workspace tasks. Scheduling, thread binding, templates, Goals, and retry flows are covered in [Automations & Goals](./features/automations.md).

| Desktop Automations |
|:---:|
| ![Desktop automations panel](https://github.com/DotHarness/resources/raw/master/dotcraft/desktop_automations.png) |

### Dashboard

Dashboard is DotCraft's visual inspection and configuration surface for sessions, traces, and workspace settings. See [Observability](./features/observability.md) for the page overview.

| Usage overview | Session trace |
|:---:|:---:|
| ![Dashboard usage overview](https://github.com/DotHarness/resources/raw/master/dotcraft/dashboard.png) | ![Dashboard session trace](https://github.com/DotHarness/resources/raw/master/dotcraft/trace.png) |

## Advanced Topics

- Use [Hooks](./features/security.md#hooks) to run scripts on lifecycle events.
- Use [Security & Sandbox](./features/security.md) to constrain file, shell, and network access.
- Use [Samples & Templates](./resources/samples.md) to validate a complete workspace template.
- For an architectural view, jump to [Architecture Overview](./developing/architecture.md).

## Troubleshooting

### Desktop cannot find `dotcraft`

Make sure the DotCraft CLI is on `PATH`, or set the AppServer / `dotcraft` binary path in Desktop settings. Source-build users can run `build.bat` from the repository root first.

### Model requests fail

Check that the current `ProviderId` points to a configured `Providers[id]`, and that the provider `Protocol`, `ApiKey`, `EndPoint`, and `Model` belong to the same service. `Protocol: "openai-chat-completions"` compatible endpoints usually end with `/v1`; `Protocol: "anthropic"` uses Anthropic's official default endpoint when `EndPoint` is omitted.

### Workspace configuration does not apply

Confirm the config is in the current workspace's `.craft/config.json`, then restart Desktop or the relevant host. Some AppServer and entry-point settings are read only at startup.
