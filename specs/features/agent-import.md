# Agent import

Status: Living. Sources: Claude Code, Codex, and Cursor on the AppServer host.

## Ownership and scope

The import module owns detection, conversion, atomic installation, history, and scheduled passes.
Session conversion and continuation follow [Session import](session-import.md). Configuration uses
the `AgentImport` section exclusively. Desktop presents user-scoped tools and setup, current-project
configuration, and chat sessions as separate selectable groups. Import never creates another workspace.

User files go to the configured user data directory; workspace files go to the workspace data
directory. Project instructions go to the workspace root. Source files are read-only. Detection
does not change source files, destination files, or session ledger checkpoints.

## Content

Supported categories are `skills`, `instructions`, `commands`, `hooks`, `mcp`, `plugins`, and
`sessions`. Settings, agents, memory, and Cowork are outside this contract.

- Skills are complete bundles. Existing names, including enabled shared user skills, are not copied.
- Instructions become `AGENTS.md`. Nonempty destinations or effective `AGENTS.override.md` prevent import.
- Commands remain Markdown commands, retain namespaces and supported parameters, and never become skills.
  Unsupported execution or permission semantics prevent importing that command.
- Hooks import only into a missing or empty `hooks.json`. Command handlers remain untrusted.
- MCP declarations merge missing server names into the destination scope's `McpServers` object.
  Authentication sessions are not copied. Diagnostics never contain credential values.
- Plugins are user-scoped bundles converted to `.craft-plugin/plugin.json`. Skills, commands, hooks,
  and MCP remain plugin-owned. Unsupported contributions are diagnosed; no supported contribution
  means the plugin cannot be imported. Remote fetches are noninteractive and never run install scripts.

Claude brand variants are replaced case-insensitively at word boundaries. Cursor replaces the exact
case-sensitive word `Cursor`; Codex replaces `Codex`. Targets use `DotCraft`. Brand replacement
applies to prose only: fenced code, inline code, and path, package, or domain forms keep the source
name. Source instruction filenames become `AGENTS.md` everywhere. Rewriting applies to instructions,
SKILL.md, commands, and descriptions, not arbitrary scripts or binary files. Hook paths and plugin root
variables have dedicated conversions.

## Transactions and protocol

`import/detect`, `import/run`, `import/settings/get`, `import/settings/set`, and `import/history/list`
are exposed through `extensions.agentImport`. Progress uses `import/progress` and `import/completed`.
There are no `import/sessions/*` aliases. All repository clients update with the protocol.

Detection returns source, category, scope, identity, name, source/destination paths, state, and a
machine-readable diagnostic. Clients submit identities and fingerprints, never destination paths.
Run revalidates candidates. Concurrent destination creation is reported as already present, and
changed sources require detection again. Each successful item commits independently.

JSON edits strictly parse the existing document under a cross-process lock, preserve unrelated keys,
and atomically replace it. Invalid documents are errors, not empty configuration. Directory installs
stage beside the destination, validate confinement and reject filesystem links, then atomically rename.

History is stored under each scope's `imports/` directory. Only metadata and diagnostic codes are
persisted, not credential-bearing declarations. User imports publish a revision so other workspace
runtimes refresh before subsequent capability reads and turns. Running turns are not interrupted.

## Sync and presentation

`AgentImport` stores `SyncEnabled`, `Sources`, `Selection`, and `SyncInterval` (12 hours by default).
Sync defaults off. `Sources` starts empty; an accepted manual import adds its source, and sync does
not run without a source. Selection distinguishes user and workspace categories and sessions; an
explicit all-categories choice includes future categories. A manual import merges into `Selection`:
categories and sessions the run `offered` take the run's choice, others keep their saved value, and a
saved all-categories choice is unchanged. An omitted `offered` equals the run's selection. No old
configuration is migrated or consulted. Pausing retains selection. Startup catches up; an unopened
workspace catches up when next started. Global passes coordinate through a process lock and shared
last-check time.

Setup sync only fills missing destinations, never updates or deletes installed content. Deleting a
destination permits later reimport while that category remains selected. Session append rules remain
unchanged. History and needs-attention entries link to the existing management surfaces and chats.
Hooks require trust; MCP authentication and missing environment variables remain visible actions.
All supported Desktop locales expose the same flows.
