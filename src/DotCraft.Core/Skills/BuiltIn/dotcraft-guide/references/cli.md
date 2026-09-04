# CLI Surface

The documentation site has no CLI page. This list is the command surface; `--help` on any command is the authority for its flags.

## Inspecting configuration

```
dotcraft config schema [--json] [--section <key>]
dotcraft config show [--json] [--workspace <path>]
```

`config schema` prints the compiled schema: sections, fields, types, defaults, whether a field is sensitive, and its reload tier. It needs no workspace and no running host.

`config show` prints the merged effective config for a workspace, personal file overlaid by workspace file, with sensitive fields masked as `***`. Prefer it over reading a config file. `--workspace` defaults to the current directory.

Both are read-only, so they are available in Plan mode. `config show --json` on a large installation can be long; ask for `--section` on the schema side, and read only the part of `show` you need.

## Running DotCraft

| Command | Does |
|---|---|
| `dotcraft setup` | Initialize or update the current workspace |
| `dotcraft exec` | Run one agent task non-interactively |
| `dotcraft app-server --listen <url>` | Run the AppServer protocol host. `stdio://`, `ws://host:port`, or `ws+stdio://host:port` |
| `dotcraft acp` | Run the ACP bridge over stdin/stdout, for editors |
| `dotcraft hub` | Run the workspace-independent local Hub |
| `dotcraft dashboard` | Run the read-only workspace dashboard |

## Managing the installation

| Command | Does |
|---|---|
| `dotcraft auth openai login \| logout \| status` | OpenAI credentials, including Sign in with ChatGPT |
| `dotcraft skill verify \| install` | Verify and install a skill candidate. Use through `$skill-installer` |
| `dotcraft context export \| search` | Export one thread as Markdown, or search saved thread context |
| `dotcraft stack init \| add-project \| doctor \| status \| logs \| restart \| upgrade \| webhook` | Self-hosted Stack deployment |
| `dotcraft tool-host setup \| workspace \| policy \| autostart \| token \| status \| serve \| register \| unregister \| list \| test` | Remote Tool Host |
| `dotcraft --version` | Version |

Hidden, for tooling rather than users: `dotcraft model-catalog --provider-id <id>` and `dotcraft workflow-worker`.

There is no `dotcraft mcp` command and no top-level `dotcraft doctor`. MCP servers are configured in `McpServers` or through Desktop; troubleshooting is the `dotcraft-doctor` skill.

## When the binary is not on PATH

Desktop bundles its own AppServer executable and does not need `dotcraft` on PATH, so `Exec` can fail even on a healthy installation. Look in:

- Desktop Settings > Connection, which shows the local AppServer binary path
- The DotCraft installation directory
- `~/.craft/bin`

If you still cannot run it, say so and fall back to the documentation site and this skill's references. A missing binary is a fact to report, not a reason to install anything.
