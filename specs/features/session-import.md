# DotCraft Session Import Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-09-23 |
| **Parent Specs** | [Session Core](../architecture/session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md), [Runtime Module Boundaries](../architecture/runtime-module-boundaries.md), [Desktop Client](../clients/desktop-client.md) |
| **Related Specs** | [External CLI SubAgent](external-cli-subagent.md), [Context Compaction](../architecture/context-compaction.md), [Multi-Folder Projects](multi-folder-projects.md) |

Purpose: define how a workspace imports chat sessions recorded by other coding agents on the same
machine (Claude Code, ChatGPT, Cursor) into DotCraft threads, and how those imports are kept in sync.

---

## 1. Scope and Ownership

Session import is a per-workspace capability owned by the bundled `DotCraft.SessionImport` module and
projected through AppServer. It has four boundaries:

1. **Source adapters** read the on-disk session stores of other agents. They are read-only: an adapter
   never writes to, locks, renames, or repairs a source file.
2. **Session Core** creates and extends threads from normalized import input through
   `ISessionService.ImportThreadAsync` and `ISessionService.AppendImportedTurnsAsync`.
3. **AppServer** exposes detection, import, and sync settings under `import/*` and broadcasts import
   progress notifications.
4. **Desktop** renders the Settings › Import page and the import dialog for the current workspace.

Phase 1 imports chat sessions only. Instructions, settings, skills, plugins, and MCP configuration are
out of scope.

### 1.1 Per-workspace model

DotCraft keeps one workspace per project folder, so import is scoped to the workspace whose AppServer
runs it: a workspace detects and imports only the source sessions whose working directory belongs to
that workspace (§3.2). There is no cross-workspace import, no session-to-project assignment step, and
import never creates a workspace or writes `.craft/` into another folder.

Remote-mode consequences follow from this ownership: an AppServer reads the source stores of the
machine it runs on. Clients must not assume the sources live on the client machine.

---

## 2. Definitions

| Term | Meaning |
|------|---------|
| Source | One supported external agent store: `claude-code`, `codex`, or `cursor`. |
| Source session | One conversation recorded by a source, identified by `(source, sourceId)` and located at `sourcePath`. |
| Candidate | A source session that belongs to the workspace and is within the detection window. |
| Import turn | A DotCraft turn created from a source session. Its `originChannel` is `session-import`. |
| Ledger | The workspace record `.craft/imports/sessions.json` mapping source sessions to threads. |
| Sync | The periodic re-detection that imports new candidates and extends grown ones. |

---

## 3. Detection

### 3.1 Source locations

Adapters resolve the source root from the environment first and the home directory second. Missing
roots mean the source is unavailable, which is not an error.

| Source | Root | Session files | Session identity |
|--------|------|---------------|------------------|
| `claude-code` | `$CLAUDE_CONFIG_DIR`, else `~/.claude` | `projects/<dir>/<sessionId>.jsonl`, top level only; sub-agent transcripts under `<sessionId>/` are ignored | file stem |
| `codex` | `$CODEX_HOME`, else `~/.codex` | `sessions/**/rollout-*.jsonl`; `archived_sessions` and `.zst` files are ignored | first `session_meta.id` |
| `cursor` | `~/.cursor` | `projects/<slug>/agent-transcripts/**/*.jsonl`, skipping any `subagents` directory | file stem (composer id) |

Files are read with sharing enabled for concurrent writers and deleters. A file that cannot be opened
or parsed is skipped for this pass and re-examined on the next one.

### 3.2 Workspace membership

A candidate belongs to the workspace when its working directory equals the workspace root or lies
under it. The working directory comes from the session records, never from directory names:

- `claude-code`: the `cwd` of the first message record (`type` is `user` or `assistant`).
- `codex`: `session_meta.cwd`.
- `cursor`: the transcript directory name. The adapter encodes the workspace root with Cursor's slug
  rule (every character outside `[A-Za-z0-9]` becomes `-`, runs collapse, ends trim) and accepts a
  project directory whose name equals that slug, or starts with the slug followed by `-` when the
  remainder reconstructs to an existing subfolder of the workspace: tokens between `-` are joined
  either as path segments or as hyphenated names, branches whose partial path is not a directory are
  dropped, and at most 128 probes are made. The reconstructed subfolder becomes the session's working
  directory; a remainder that reconstructs to nothing (such as a sibling folder sharing the prefix)
  is not a member.

Paths are compared after full-path normalization, with separators unified and, on Windows, without
case sensitivity. A candidate whose working directory is a subfolder is imported with
`Configuration.Cwd` set to that subfolder. When the folder no longer exists the candidate is skipped.

### 3.3 Window and limits

Per source, detection considers files modified within `MaxSessionAgeDays` (default 30), takes the
newest `MaxSessionsPerSource` (default 50) by modification time, and only then applies membership and
validation. Sessions already recorded in the ledger with an unchanged modification time are skipped
before parsing; when the time changed but the recomputed content hash did not, detection refreshes the
recorded time so the file is not parsed again on the next pass. Sessions that produce no import turn
are not candidates.

### 3.4 Exclusions

- `claude-code`: records with `isMeta`, `isSidechain`, or `isCompactSummary` set; files whose first
  message record has no `cwd`.
- `codex`: threads whose `session_meta.source` is not `cli`, `vscode`, or a `custom` value, or whose
  `thread_source` is not `user`; threads that contain the `<EXTERNAL SESSION IMPORTED>` marker
  (sessions Codex itself imported from another agent); threads whose id appears in any workspace
  thread's `dotcraft.externalCliSessions` metadata (sessions DotCraft spawned as external CLI
  sub-agents).
- `cursor`: sessions whose id appears in `dotcraft.externalCliSessions` metadata.

### 3.5 Candidate state

Each candidate is reported with one state:

| State | Meaning |
|-------|---------|
| `new` | Not in the ledger. |
| `changed` | In the ledger, content hash differs, and the thread can still be extended (§5.2). |
| `deferred` | In the ledger and changed, but the thread was continued in DotCraft, archived, deleted, or is running. Never extended. |
| `current` | In the ledger with the same content hash. |

Only `new` and `changed` candidates are importable. Counts shown by clients use importable candidates.
Detection may omit `current` candidates that were skipped before parsing.

---

## 4. Conversion

Conversion is deliberately lossy: only user and assistant text survive, tool activity is inlined as
tagged text, and everything else is dropped.

### 4.1 Turn assembly

Records are read in file order. A user text record starts a new import turn; subsequent assistant text
records become `AgentMessage` items of that turn until the next user text record. Assistant records
before the first user record are dropped. Empty turns are dropped.

Within one source message, text parts are joined with a blank line. Tool activity is rendered inline:

```text
[external_agent_tool_call: <name>]
<one line per known parameter: description, command, or file; otherwise "input: <json>" truncated to 2000 characters>
[/external_agent_tool_call]

[external_agent_tool_result]
<text truncated to 4000 characters>
[/external_agent_tool_result]
```

A tool result whose message carries no user text is treated as assistant text. Reasoning is dropped.
Images and unsupported blocks become `[external unsupported block: <type>]`. An error result uses the
`[external_agent_tool_result: error]` opening tag.

Source specifics:

- `claude-code`: user records use `message.content` (string or `text` blocks) and `tool_result`
  blocks; assistant records use `text` and `tool_use` blocks. Assistant rows sharing one `message.id`
  are one message. Rows are de-duplicated by `uuid`; a repeated `uuid` keeps its first position.
  A leading `<user_query>` wrapper is removed. Metadata rows (`custom-title`, `ai-title`,
  `last-prompt`, `queue-operation`, and others without `parentUuid`) do not produce items.
- `codex`: paginated rollouts use `event_msg.item_completed` items — `UserMessage` text parts,
  `AgentMessage` text, and `CommandExecution`, `FileChange`, and `McpToolCall` as tool text; `Reasoning`
  and the remaining item kinds are dropped. Legacy rollouts use `event_msg.user_message` and
  `event_msg.agent_message`, plus `response_item` `function_call`, `custom_tool_call`,
  `local_shell_call`, and their outputs as tool text. When the current file declares `history_base`,
  the ancestor file's bytes before `end_byte_offset` precede the current file's records after its own
  `session_meta`; a missing ancestor skips the thread.
- `cursor`: `role: user` records use `message.content` text blocks with the `<user_query>` wrapper and
  leading `<timestamp>` or `<cursor_commands>` blocks removed; `role: assistant` records use `text` and
  `tool_use` blocks. Transcripts carry no timestamps, so every turn uses the file modification time.

### 4.2 Timestamps and ordering

Order is the source record order; timestamps never reorder records. Thread `createdAt` is the first
turn's start, `lastActiveAt` the last turn's completion, and each turn's `startedAt` and `completedAt`
come from the source records. Session Core raises a turn's `startedAt` to the previous turn's plus one
millisecond when the source is not monotonic. Item timestamps equal their turn's timestamps.

### 4.3 Import marker

The last turn ends with one additional `AgentMessage` whose text is `<EXTERNAL SESSION IMPORTED>`.
The marker stays in place when later turns are appended or when that turn is extended (§5.2), so it
appears exactly once per thread and may then sit before the messages added later.

### 4.4 Title

Titles are taken in this order, falling back at each empty step:

- `claude-code`: the last `custom-title` row whose `sessionId` equals the file stem, then the last such
  `ai-title` row, then the first user text.
- `codex`: the last `session_index.jsonl` entry for the thread id, then the first user text.
- `cursor`: the first user text.

The user-text fallback skips leading tag-wrapped blocks (such as `<system-reminder>`,
`<ide_selection>`, or attachment blocks), unwraps a leading `<user_query>` block, takes the first
non-empty line, and truncates to 120 characters. An empty result becomes `Imported session`.

---

## 5. Threads

### 5.1 Creation

`ISessionService.ImportThreadAsync(ThreadImportRequest)` creates a thread that already contains
history. It differs from `CreateThreadAsync` in these ways:

- The thread id is deterministic: `thread_import_<source>_<hash>` where `<hash>` is the first 16 hex
  characters of SHA-256 over `<source>:<sourceId>`. A request whose thread already exists returns the
  existing thread without changes.
- No agent is built. Configuration is captured the same way `CreateThreadAsync` captures it, then
  `Cwd` is applied from the request.
- Turns are materialized as terminal `Completed` turns with `Completed` items, `originChannel`
  `session-import`, and source timestamps (§4.2).
- The thread carries `ProviderHistorySchemaVersion` at the current version and no provider or model
  history records; model-visible history is rebuilt from items on demand.
- A context usage snapshot records the request's estimated token count (total UTF-8 bytes of message
  text divided by four) so the first follow-up turn can trigger automatic compaction.
- The thread is persisted through the normal Session Core path and announced through
  `thread/started`. Contribution lifecycle start hooks do not run; they run when the thread resumes.

Identity: `WorkspacePath` is the workspace root, `UserId` is `local`, `OriginChannel` is
`session-import`, and `ChannelContext` is `workspace:<workspacePath>` so Desktop's workspace-scoped
thread discovery lists the thread. Provenance is stored in metadata:

| Key | Value |
|-----|-------|
| `dotcraft.import.source` | `claude-code`, `codex`, or `cursor` |
| `dotcraft.import.sessionId` | source session id |
| `dotcraft.import.importedAt` | RFC 3339 time of the first import |

Metadata holds stable identity only. Sync watermarks live in the ledger.

### 5.2 Extension

`ISessionService.AppendImportedTurnsAsync(ThreadImportAppendRequest)` appends turns to an existing
imported thread. It is refused, with no change, unless every existing turn has `originChannel`
`session-import` and the thread is `Active` and not running. The caller supplies the expected existing
turn count and the appended turns; the request fails when the count differs. Session Core compares the
existing turns' user and assistant text with the caller's `existingTurns`: every turn but the last must
match exactly, and the last existing turn's assistant messages must equal, or be a prefix of, the
caller's version, because a source session may have been imported while that turn was still in
progress. Extra assistant messages for that last turn are appended to it after the marker message
(§4.3), which is otherwise excluded from the comparison; the new turns follow. Any other difference
refuses the append.

A thread that has been continued in DotCraft therefore never receives external updates: the source
session stays listed as `deferred` and nothing is written.

### 5.3 Continuation

Imported threads are ordinary threads. Clients may resume them and start turns; the first turn rebuilds
model history from items and may compact first. Nothing marks an imported thread read-only.

---

## 6. Ledger and Sync

### 6.1 Ledger

`<workspace>/.craft/imports/sessions.json`:

```json
{
  "version": 1,
  "records": [
    {
      "source": "claude-code",
      "sourceId": "…",
      "sourcePath": "…",
      "threadId": "thread_import_claude-code_…",
      "contentSha256": "…",
      "sourceModifiedAt": "2026-09-23T06:00:00.123Z",
      "importedAt": "2026-09-23T07:00:00Z",
      "turnCount": 12,
      "title": "…"
    }
  ]
}
```

Records are keyed by `(source, sourceId)`. `contentSha256` hashes the source file bytes (for a
multi-file Codex thread, the concatenated lineage bytes). A top-level `lastSyncAt` records the start
of the last completed sync pass. Writes replace the whole file atomically.
The ledger is a cache: a missing or corrupt ledger is rebuilt from the workspace's imported threads'
metadata, with hashes recomputed on the next detection.

### 6.2 Import pass

For each importable candidate, in detection order:

1. `new`: convert, then `ImportThreadAsync`; record the ledger entry.
2. `changed`: convert, then `AppendImportedTurnsAsync` with the ledger's `turnCount` and the converted
   turns; on success update the ledger hash, modification time, and turn count. A refusal leaves the
   ledger untouched and reports the candidate as `deferred`.

Failures are reported per candidate and never stop the pass. Import never overwrites or deletes a
thread, and a source file that disappears leaves the thread and its ledger record in place.

### 6.3 Sync

Sync is one user-level setting with per-workspace execution:

- `SessionImport.SyncEnabled` and `SessionImport.Sources` live in the user configuration
  (`~/.craft/config.json`). A workspace configuration may set `SyncEnabled` to `false` to opt out.
- When enabled, the workspace's AppServer runs an import pass for the configured sources once after
  start and then every `SessionImport.SyncInterval` (default 12 hours). Passes never overlap; a pass
  requested while one is running is queued once.
- A workspace that is not running does not sync; it catches up when its AppServer next starts.
- Manual detection (`import/sessions/detect`) and manual import (`import/sessions/run`) are always
  available regardless of the sync setting.

### 6.4 Configuration

```json
{
  "SessionImport": {
    "Enabled": true,
    "SyncEnabled": false,
    "Sources": ["claude-code", "codex", "cursor"],
    "SyncInterval": "12:00:00",
    "MaxSessionAgeDays": 30,
    "MaxSessionsPerSource": 50
  }
}
```

`Enabled` gates the module. `SyncEnabled` and `Sources` are written by `import/settings/set` into the
user configuration file's `SessionImport` object, preserving other content.

---

## 7. AppServer Surface

Methods belong to module `session-import`, scope `workspace`, capability
`extensions.sessionImport`, and are listed in the AppServer Protocol specification. Summary:

| Method | Direction | Purpose |
|--------|-----------|---------|
| `import/sessions/detect` | client → server | Scan the configured sources for this workspace's candidates. |
| `import/sessions/run` | client → server | Import the selected candidates in the background. |
| `import/settings/get` | client → server | Read sync settings and the last sync time. |
| `import/settings/set` | client → server | Update `syncEnabled` and `sources`. |
| `import/sessions/progress` | server → client | Progress of a running import. |
| `import/sessions/completed` | server → client | Terminal result of an import, including sync passes. |

Capability payload: `{ "version": 1, "sources": ["claude-code", "codex", "cursor"] }`.

---

## 8. Desktop

Settings gains an **Import** page for the current workspace, placed in the personal group directly
after General:

- **Keep imports in sync** toggle bound to `import/settings/set`, with one description that does not
  change with the toggle state.
- **Import from other apps**: one row per detected source, with the app's icon, the importable session count, and an
  **Import** button; a status line covering checking, no importable chats, and last sync time; and a
  **Check again** action that re-runs detection.
- The **Import** dialog lists what comes over as selectable items, currently the single "Chat sessions
  (N)" row for the chosen source with its checkbox at the row's end. Confirming adds the source to
  the sync sources through `import/settings/set` when it is missing, then runs `import/sessions/run`.
- Progress and completion arrive through the notifications; the thread list receives imported threads
  through the normal `thread/started` broadcast, and newly imported threads are marked unread.
- Imported threads show the source as their origin badge. Phase 1 keeps no import log and syncs each
  enabled source as a whole.

All strings are localized in every supported Desktop locale.

---

## 9. Validation

- Adapter tests parse fixture transcripts for each source and assert the converted turns, titles,
  exclusions, and workspace membership.
- Session Core tests cover `ImportThreadAsync` idempotency, persisted rollout and index contents,
  timestamp monotonicity, `AppendImportedTurnsAsync` success, and refusal after a native turn.
- Ledger tests cover state classification (`new`, `changed`, `deferred`, `current`) and atomic writes.
- Protocol artifacts are regenerated and checked with `DotCraft.ProtocolGen`.
- Desktop tests cover the Import page's request flow and dialog state through mocked AppServer calls.
