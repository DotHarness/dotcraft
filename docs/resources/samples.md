# Samples & Templates

The [`samples/`](https://github.com/DotHarness/dotcraft/tree/master/samples) directory ships ready-to-copy examples and templates. Each one focuses on a single capability — copy what you need.

> [!TIP]
> First-time validation: run [Workspace](#workspace) first, then pick Automations / Hooks / Skills as needed.

## Configuration Templates

| Name | Purpose | Repository |
|---|---|---|
| Automations | Local task templates and a minimal workspace config for Automations | [samples/automations](https://github.com/DotHarness/dotcraft/tree/master/samples/automations) |
| Hooks | Example lifecycle hooks for Linux and Windows | [samples/hooks](https://github.com/DotHarness/dotcraft/tree/master/samples/hooks) |
| Plugins | Reference plugin packages for development and integration testing | [samples/plugins](https://github.com/DotHarness/dotcraft/tree/master/samples/plugins) |

## Workspace Behavior Examples

| Name | Purpose | Repository |
|---|---|---|
| Automations · example-local-task | Local task template (`task.md` + `workflow.md`) | [samples/automations](https://github.com/DotHarness/dotcraft/tree/master/samples/automations) |
| Hooks · windows | PowerShell hooks (block dangerous Exec, log file writes) | [samples/hooks/windows](https://github.com/DotHarness/dotcraft/tree/master/samples/hooks/windows) |
| Hooks · linux | Bash hooks (block dangerous Exec, log file writes) | [samples/hooks/linux](https://github.com/DotHarness/dotcraft/tree/master/samples/hooks/linux) |

## Skills Templates

| Name | Purpose | Repository |
|---|---|---|
| `dev-guide` | Project development standards example with module references | [samples/skills/dev-guide](https://github.com/DotHarness/dotcraft/tree/master/samples/skills/dev-guide) |
| `feature-workflow` | Large feature workflow for breakdown, implementation, validation | [samples/skills/feature-workflow](https://github.com/DotHarness/dotcraft/tree/master/samples/skills/feature-workflow) |
| `dotcraft-llm-error-diagnosis` | Read-only diagnosis flow for LLM / agent / tool / session failures | [samples/skills/llm-error-diagnosis](https://github.com/DotHarness/dotcraft/tree/master/samples/skills/llm-error-diagnosis) |

## Plugin Templates

[`samples/plugins/`](https://github.com/DotHarness/dotcraft/tree/master/samples/plugins) collects reference DotCraft plugin scaffolds. When building your own plugin, scaffold via the built-in `$plugin-creator` first, then refine using these examples.

## Common Workflows

### Workspace

```bash
cd /path/to/your-project
dotcraft setup --provider-mode create \
  --provider-protocol anthropic --provider-id anthropic \
  --model claude-sonnet-4-5 --api-key <anthropic-api-key> \
  --profile developer
```

This is the safest first-time path. After setup, `.craft/` contains workspace config and bootstrap files. If you want a long-running server deployment for social-channel bots, use the Docker path in [Server Deployment](../features/self-hosted/server-deployment):

```bash
cd deploy/docker
cp .env.example .env
docker compose up -d
```

The template's most-edited fields:

| Field | Recommendation |
|---|---|
| `DashBoard.Host` / `DashBoard.Port` | Adjust to local bind address or port |
| `Tools.Sandbox.Enabled` | Only enable when actually running OpenSandbox |
| `Tools.Sandbox.Domain` | Point to your OpenSandbox; do not collide with the Dashboard port |
| `Tools.Sandbox.Image` | Replace with your sandbox container image |

### Hooks

Copy the platform directory under `samples/hooks/<platform>/` to your workspace:

```text
<workspace>/.craft/hooks.json
<workspace>/.craft/hooks/...
```

On Linux / macOS, make scripts executable:

```bash
chmod +x hooks/*.sh
```

> [!WARNING]
> Creating or editing Hooks requires restarting DotCraft to take effect. Startup-level fields don't hot-reload — restart the relevant host after copying any sample config. See [Settings Lifecycle](../developing/lifecycle/settings-lifecycle) for which fields apply immediately.

If you do not want to write hook config by hand, describe the need in chat and let the built-in `create-hooks` skill scaffold it.

### Skills

```text
.craft/skills/dev-guide/
.craft/skills/feature-workflow/
```

Keep the structure after copying and gradually replace placeholders with project-specific terminology, paths, and acceptance criteria.

## Related docs

- [Getting Started](../getting-started)
- [Plugins & Tools](../features/agent-system/plugins-tools)
- [Settings Lifecycle](../developing/lifecycle/settings-lifecycle)
- [Configuration Reference](../developing/configuration)
