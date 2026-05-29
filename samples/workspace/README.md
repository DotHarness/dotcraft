# DotCraft Workspace Sample

**[中文](./README_ZH.md) | English**

This sample is a source-based DotCraft workspace that you can run directly from this repository checkout on a development machine or cloud server.

It includes:

- `start.sh` to build and run DotCraft from source
- `start-sandbox.sh` and `start-sandbox.ps1` to start an optional OpenSandbox server
- `config.template.json` as a safe workspace config example

## Before You Start

For most first-time users, the safer default path is still:

```bash
cd /path/to/your-project
dotcraft
```

When DotCraft starts in a fresh workspace, it can initialize `.craft/` for you and open the setup-only Dashboard if configuration is still missing.

Use this `samples/workspace` directory when you specifically want a reusable source checkout workspace or a server-style helper environment.

For a production-style server bot deployment, prefer the newer [Docker server deployment](../../docs/developing/server-deployment.md). It bundles the AppServer, TypeScript channel modules, and optional OpenSandbox so you do not need to run the sandbox helper and channel adapters by hand.

## Sample Contents

| File | Purpose |
|------|---------|
| `start.sh` | Build DotCraft from this repo and start the CLI from `samples/workspace` |
| `start-sandbox.sh` | Start OpenSandbox on Linux / macOS |
| `start-sandbox.ps1` | Start OpenSandbox on Windows PowerShell |
| `config.template.json` | Safe example config you can copy into your own workspace as a starting point |
| `.craft/config.json` | Your live local workspace config; it may contain private data and should not be treated as a distributed sample file |
| `.craft/dashboard/*` | Runtime Dashboard artifacts created locally after startup; not distributed with the repository |
| `.craft/sessions/*` | Runtime session artifacts created locally after startup; not distributed with the repository |

## Prerequisites

### Recommended default path

- DotCraft installed or built on your machine
- A real project directory where you want the workspace to live

### If you use `start.sh`

- Bash
- .NET 10 SDK
- This repository cloned locally
- `screen` if you want detached background execution on Linux servers

### If you use the optional sandbox helpers

- Docker running and accessible from the current shell
- OpenSandbox Server installed and available on `PATH`
- A base sandbox config at `~/.sandbox.toml`, or `SANDBOX_CONFIG_PATH` pointing to one
- Windows: PowerShell plus `python`
- Linux / macOS: `bash` plus `python3`

## Quick Start

### Option 1: Recommended for new users

Use DotCraft in your real project directory and let the setup flow create `.craft/`:

```bash
cd /path/to/your-project
dotcraft
```

### Option 2: Run this sample workspace from source

```bash
cd samples/workspace
mkdir -p .craft
cp config.template.json .craft/config.json
bash start.sh
```

If you prefer, you can also run `bash start.sh` first and let DotCraft initialize `.craft/` on first launch. But if you want the recommended Dashboard / Sandbox example fields from this sample, copy them from `config.template.json` before starting.

On a Linux server, you can keep it running in the background with `screen`:

```bash
cd samples/workspace
screen -dmS dotcraft bash -c "bash start.sh"
```

## Configuration

DotCraft reads global config from `~/.craft/config.json` and workspace overrides from `<workspace>/.craft/config.json`.

For this sample:

- keep secrets in `~/.craft/config.json` whenever possible
- use `samples/workspace/config.template.json` as the sample reference
- copy only the fields you need into your own `<workspace>/.craft/config.json`
- do not assume `.craft/config.json` is shipped with the repository, because it is usually ignored as private workspace data
- do not overwrite an existing live `.craft/config.json` just because this sample contains one locally

Suggested workflow:

1. Review `config.template.json`
2. Copy the fields you need into your own `.craft/config.json`
3. Keep machine-specific paths, tokens, and service endpoints out of the shared sample

Fields you will most likely want to change in the template:

| Field | What to change |
|------|----------------|
| `DashBoard.Host` / `DashBoard.Port` | Change if you need a different local bind address or port |
| `Tools.Sandbox.Enabled` | Turn on only if you actually run OpenSandbox |
| `Tools.Sandbox.Domain` | Point to your OpenSandbox server and keep it different from the Dashboard port |
| `Tools.Sandbox.Image` | Replace with the container image you want the sandbox to use |

## Recommended Start Order

### Without sandbox

1. Start DotCraft with `dotcraft` or `bash start.sh`
2. Complete setup in the Dashboard if DotCraft prompts for it
3. Re-run DotCraft normally after saving config

### With sandbox

1. Confirm Docker and OpenSandbox prerequisites first
2. Start the sandbox helper for your platform
3. Copy the sandbox fields you need from `config.template.json` into your own `.craft/config.json`
4. Start DotCraft

### With QQ / NapCat

1. Start NapCat first
2. Start DotCraft
3. Make sure your QQ external channel config is enabled and points at the correct service endpoints

## Optional: QQ / NapCat

This sample does not require QQ integration. Only use this step if you are testing a QQ external channel workflow.

Linux example:

```bash
screen -dmS napcat bash -c "napcat"
```

You still need to configure the QQ external channel separately in your DotCraft config.

## Optional: Tool Sandbox

The sandbox helpers are optional. They are useful when you want Shell and File tool execution to happen inside OpenSandbox instead of on the host machine.

### Preflight checks

Windows PowerShell:

```powershell
docker info
python --version
opensandbox-server --help
```

Linux / macOS:

```bash
docker info
python3 --version
opensandbox-server --help
```

### Base config

The sandbox scripts expect a base config file at `~/.sandbox.toml` by default. If you keep it somewhere else, set `SANDBOX_CONFIG_PATH` before running the helper.

You can generate the default base config with:

```bash
opensandbox-server init-config ~/.sandbox.toml --example docker
```

### Important port note

Do not assign the same port to both Dashboard and OpenSandbox. The provided `config.template.json` keeps Dashboard and sandbox on different ports on purpose.

### Start on Windows

Run the helper from PowerShell:

```powershell
.\start-sandbox.ps1
```

If local script execution is blocked on your machine, adjust PowerShell execution policy or run the script in a shell where local scripts are allowed.

### Start on Linux / macOS

```bash
bash ./start-sandbox.sh
```

## Verify It Works

After startup, you should be able to confirm:

- DotCraft starts without configuration errors
- the Dashboard is reachable on the configured host and port
- the sandbox helper prints the config path and listening port when sandbox is enabled
- QQ / NapCat is online if you enabled that path

## Troubleshooting

### `dotnet` or build tools are missing

Install the .NET 10 SDK and run `dotnet --info` before using `start.sh`.

### `screen: command not found`

Install `screen`, or run `bash start.sh` directly in the foreground.

### `opensandbox-server` not found

Install OpenSandbox Server in a way that puts the executable on `PATH`, or set `OPENSANDBOX_SERVER_EXE` to the full executable path.

### Docker is installed but the helper still fails

Run `docker info` in the same shell first. On Linux, make sure your current shell session actually has permission to access Docker.

### Base sandbox config is missing

Create `~/.sandbox.toml` with:

```bash
opensandbox-server init-config ~/.sandbox.toml --example docker
```

Or point `SANDBOX_CONFIG_PATH` at an existing config file.

### Sandbox and Dashboard both try to use the same port

Change either `DashBoard.Port` or `Tools.Sandbox.Domain` so they do not collide.

## Related Docs

- [Project README](../../README.md)
- [Configuration Reference](../../docs/developing/configuration.md)
- [Observability](../../docs/features/observability.md)
- [QQ Channel Adapter](../../docs/developing/channels/qq.md)
