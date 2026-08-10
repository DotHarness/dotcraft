# DotCraft Full Configuration Reference

This page collects configuration fields, defaults, JSON examples, and advanced references in one place. For first-time setup, read [Getting started](../getting-started). For product-level explanations, start from the matching Feature page and come back here for exact fields.

DotCraft reads global `~/.craft/config.json` first, then overlays workspace `.craft/config.json`; workspace fields win. String values support `$VAR` and `${VAR}` environment variable placeholders. If a variable is not set, the original placeholder is preserved.

## Basic Model and Provider

| Field | Description | Default |
|-------|-------------|---------|
| `ProviderId` | Current personal provider id. Empty means no provider is selected | Empty |
| `ProviderPreferences` | Complete MainAgent preferences keyed by provider id. The selected provider must have an effective entry | `{}` |
| `NetworkTimeoutSeconds` | Global model request timeout in seconds; providers can override it | `600` |
| `Providers` | Personal model provider dictionary, usually stored in `~/.craft/config.json` | Empty |
| `SubagentMaxConcurrency` | Maximum concurrent subagents | `3` |
| `MaxSessionQueueSize` | Maximum queued requests per session; `0` means unlimited | `3` |
| `ConsolidationModel` | Memory consolidation model. Empty uses the main model | Empty |
| `DebugMode` | Prints untruncated tool arguments in the console | `false` |
| `EnabledTools` | Globally enabled tool names. Empty enables all tools | `[]` |

Personal provider example:

```json
{
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

Workspace model selection example:

```json
{
  "ProviderId": "anthropic",
  "ProviderPreferences": {
    "anthropic": {
      "Model": "claude-sonnet-4-5",
      "Reasoning": {
        "Enabled": true,
        "Effort": "High",
        "Output": "Full"
      },
      "Speed": "Fast",
      "ContextWindow": {
        "Mode": "Max"
      }
    }
  }
}
```

`ProviderPreferences` records are atomic across config scopes. If global and workspace config both define the same provider id, the workspace record replaces the complete global record.

| Preference field | Values | Description |
|------------------|--------|-------------|
| **`Model`** | Non-empty model id | Model used for new MainAgent threads |
| **`Reasoning.Enabled`** | `true`, `false` | Enables reasoning when the model supports the choice |
| **`Reasoning.Effort`** | `Low`, `Medium`, `High`, `ExtraHigh` | Requested reasoning effort |
| **`Reasoning.Output`** | `None`, `Summary`, `Full` | Requested reasoning output |
| **`Speed`** | `Standard`, `Fast` | Requested inference speed; unsupported Fast runs as Standard |
| **`ContextWindow.Mode`** | `Default`, `Max` | Requested context-window mode; unsupported Max resets to Default |

Provider object fields:

| Field | Description | Default |
|-------|-------------|---------|
| `DisplayName` | User-facing provider name; falls back to the provider id when empty | Empty |
| `Protocol` | Provider protocol: `anthropic`, `openai-chat-completions`, or `openai-responses`. Empty values default to `openai-chat-completions`. | `openai-chat-completions` |
| `ApiKey` | Provider API key; prefer `${ENV_NAME}` environment variable references | Empty |
| `EndPoint` | Provider base URL; empty values use the protocol default endpoint | OpenAI protocols: `https://api.openai.com/v1`; `anthropic`: `https://api.anthropic.com` |
| `NetworkTimeoutSeconds` | Per-provider request timeout, overriding the global `NetworkTimeoutSeconds` | Empty |
| `StreamMaxRetries` | Per-provider streaming reconnection attempts for dropped or idle provider streams; `0` disables stream retry | `5` |
| `StreamIdleTimeoutMs` | Per-provider idle timeout for streaming responses, in milliseconds | `300000` |
| `SupportsHostedImageGeneration` | Enables hosted image generation for this provider. When omitted, ChatGPT OAuth and the official OpenAI Responses API-key endpoint default to `true`; custom OpenAI-compatible Responses endpoints default to `false`. | Provider default |

## Workspace Memory and Skills

| Field | Description | Default |
|-------|-------------|---------|
| `Memory.AutoConsolidateEnabled` | Enables automatic long-term memory consolidation | `true` |
| `Memory.ConsolidateEveryNTurns` | Successful turns per thread between long-term memory consolidation attempts | `5` |
| `Skills.SelfLearning.Enabled` | Master switch for agent skill self-learning; off hides skill editing from the model | `true` |
| `Skills.SelfLearning.MaxSkillContentChars` | Max chars for a single `SKILL.md` written through self-learning | `100000` |
| `Skills.SelfLearning.MaxSupportingFileBytes` | Max bytes for a single supporting file written through self-learning | `1048576` |

Self-learning example:

```json
{
  "Skills": {
    "SelfLearning": {
      "Enabled": true,
      "MaxSkillContentChars": 100000,
      "MaxSupportingFileBytes": 1048576
    }
  }
}
```

`SkillManage(action, ...)` reference:

| Action | Required parameters | Purpose |
|---|---|---|
| `create` | `name`, `content` | Create a new workspace skill |
| `patch` | `name`, `oldString`, `newString` | Local patch of `SKILL.md` or supporting file |
| `edit` | `name`, `content` | Replace an existing workspace skill's `SKILL.md` |
| `write_file` | `name`, `filePath`, `fileContent` | Write a supporting file |
| `remove_file` | `name`, `filePath` | Delete a supporting file |

`create` triggers a `kind: skill` approval; destructive delete operations also require approval. Self-learning writes only the current workspace's skill directory. System and personal skills are read-only, supporting files may only live under `scripts/` or `assets/`, and absolute paths or `..` traversal are rejected.

## Compaction

| Field | Description | Default |
|-------|-------------|---------|
| `Compaction.AutoCompactEnabled` | Enables threshold-based auto compaction | `true` |
| `Compaction.ReactiveCompactEnabled` | Enables reactive compaction for `prompt_too_long` errors | `true` |
| `Compaction.ContextWindow` | Model context window in tokens. When unset, DotCraft infers it from the current effective model | Model catalog value / `256000` |
| `Compaction.MaxContextWindow` | Upper bound used for inferred model catalog context windows; explicit values are preserved | `256000` |
| `Compaction.SummaryReserveTokens` | Tokens reserved for summary output | `20000` |
| `Compaction.AutoCompactBufferTokens` | Token buffer below the hard limit that triggers auto compaction | `13000` |
| `Compaction.WarningBufferTokens` | Token buffer before auto threshold that emits warning | `20000` |
| `Compaction.ErrorBufferTokens` | Token buffer before auto threshold that emits error | `10000` |
| `Compaction.ManualCompactBufferTokens` | Token buffer below the hard limit where manual `/compact` remains allowed | `3000` |
| `Compaction.KeepRecentMinTokens` | Minimum recent tail tokens after partial summary | `10000` |
| `Compaction.KeepRecentMinGroups` | Minimum recent API groups after partial summary | `3` |
| `Compaction.KeepRecentMaxTokens` | Maximum recent tail tokens after partial summary | `40000` |
| `Compaction.MicrocompactEnabled` | Enables micro-compaction | `true` |
| `Compaction.MicrocompactKeepRecent` | Recent tool results kept during micro-compaction | `8` |
| `Compaction.MicrocompactGapMinutes` | Also triggers after this many minutes since last assistant message; `0` disables it | `20` |
| `Compaction.MaxConsecutiveFailures` | Consecutive failures before circuit breaking compaction | `3` |

### Model capability catalog

DotCraft ships a built-in catalog for model context windows and Fast Mode support. Extend or override it with:

- Global: `~/.craft/models.json`
- Workspace: `.craft/models.json`

```json
{
  "defaultContextWindow": 256000,
  "models": {
    "my-256k-model": {
      "contextWindow": 256000
    },
    "custom-fast-model": {
      "contextWindow": 1048576,
      "fast": {
        "protocols": ["openai-responses"]
      }
    },
    "custom-anthropic-model": {
      "fast": {
        "protocols": ["anthropic"]
      }
    }
  }
}
```

Workspace entries override global entries, which override the built-in catalog. Fields merge
independently for each model pattern. Set `fast` to `null` to disable an inherited Fast capability.
Model patterns use case-insensitive longest-prefix matching and also match namespaced suffixes such
as `provider/custom-fast-model`.

## Reasoning and Prompt Caching

| Field | Description | Default |
|-------|-------------|---------|
| `Reasoning.Enabled` | Requests provider reasoning support | `false` |
| `Reasoning.Effort` | Reasoning depth: `None` / `Low` / `Medium` / `High` / `ExtraHigh` | `Medium` |
| `Reasoning.Output` | Reasoning visibility: `None` / `Summary` / `Full` | `Full` |
| `PromptCaching.Enabled` | Inject prompt cache markers for matching models | `true` |
| `PromptCaching.ModelPatterns` | Case-insensitive model name fragments. Empty matches no models | `["claude"]` |
| `PromptCaching.Placement` | Marker placement strategy. Currently only `ConversationTail` is supported | `ConversationTail` |
| `PromptCaching.Ttl` | Anthropic cache TTL. Empty uses the default 5 minutes; `1h` requests the long cache | Empty |

Deep-thinking adapter catalog files:

- Global: `~/.craft/model-thinking-adapters.json`
- Workspace: `.craft/model-thinking-adapters.json`

The built-in catalog exposes full reasoning choices for unlisted Anthropic-protocol models, but does not assume they support Anthropic `adaptive` request shaping. Add `anthropicThinking` entries for models or endpoints that explicitly support that shape.

For Anthropic-compatible providers, `anthropicMessageContent` can declare how DotCraft reasoning history should be represented. The built-in DeepSeek Anthropic adapter maps historical `TextReasoningContent` to Anthropic-compatible `thinking` blocks before sending history; it is not a generic unsupported-block filter.

```json
{
  "deepThinking": {
    "models": ["deepseek", "mimo", "my-thinking-model-"],
    "endpoints": ["deepseek", "my-thinking-gateway"]
  },
  "anthropicThinking": {
    "adapters": [
      {
        "models": ["my-adaptive-anthropic-model-"],
        "thinking": { "type": "adaptive", "display": "fromReasoningOutput" },
        "outputConfig": { "effort": "fromReasoningEffort" }
      }
    ]
  },
  "anthropicMessageContent": {
    "adapters": [
      {
        "models": ["deepseek"],
        "endpoints": ["deepseek"],
        "reasoningHistory": { "blockType": "thinking" }
      }
    ]
  }
}
```

## Tools Security and Sandbox

| Field | Description | Default |
|---|---|---|
| `Security.BlacklistedPaths` | Paths the agent must not access; subpaths are also checked | `[]` |
| `Tools.File.RequireApprovalOutsideWorkspace` | Approve file ops outside workspace | `true` |
| `Tools.File.MaxFileSize` | Max readable file size in bytes | `10485760` |
| `Tools.File.RipgrepPath` | Optional `rg` path; empty tries `DOTCRAFT_RG_PATH`, `PATH`, then fallback | `""` |
| `Tools.File.SearchTimeoutSeconds` | Max `GrepFiles` content-search time before timeout | `30` |
| `Tools.Shell.RequireApprovalOutsideWorkspace` | Approve shell commands outside workspace | `true` |
| `Tools.Shell.Timeout` | Shell timeout in seconds | `300` |
| `Tools.Shell.MaxOutputLength` | Max shell output length in characters | `10000` |
| `Tools.Shell.Background.Enabled` | Enable background terminal sessions | `true` |
| `Tools.Shell.Background.DefaultYieldTimeMs` | Default wait before a running command returns a background-session snapshot | `1000` |
| `Tools.Shell.Background.MaxYieldTimeMs` | Maximum wait accepted for a background-session read or write | `30000` |
| `Tools.Shell.Background.MaxSessionsPerThread` | Maximum concurrent background terminals per thread | `8` |
| `Tools.Shell.Background.MaxSessionsPerWorkspace` | Maximum concurrent background terminals in one workspace | `32` |
| `Tools.Shell.Background.IdleTimeoutSeconds` | Reserved; not currently enforced by the background terminal service | `1800` |
| `Tools.Shell.Background.OutputMaxBytes` | Reserved; not currently enforced by the background terminal service | `67108864` |
| `Tools.Shell.Background.OutputRetentionDays` | Retention window for completed or lost terminal metadata and output; running terminals are excluded | `7` |
| `Tools.Shell.Background.StallWatchdogSeconds` | Reserved; not currently enforced by the background terminal service | `45` |
| `Tools.Shell.Background.DefaultReadMaxOutputChars` | Default maximum characters returned in a terminal snapshot | `10000` |
| `Tools.Web.MaxChars` | Max chars for web fetch | `50000` |
| `Tools.Web.Timeout` | Web request timeout in seconds | `300` |
| `Tools.Web.SearchMaxResults` | Default search result count | `5` |
| `Tools.Web.SearchProvider` | `Bing` / `Exa` | `Exa` |
| `Tools.Lsp.Enabled` | Enables built-in LSP tools | `false` |
| `Tools.Lsp.MaxFileSize` | Max LSP file size | `10485760` |
| `Tools.ImageGeneration.Enabled` | Allows supported OpenAI Responses providers to generate images in conversation | `true` |
| `Tools.ImageGeneration.Model` | Reserved for image-client integrations; conversation image generation uses the active Responses model | `gpt-image-2` |
| `Tools.ImageGeneration.MaxReferenceImages` | Reserved for image-client integrations that accept reference images | `5` |
| `Tools.Sandbox.Enabled` | Enable sandbox | `false` |
| `Tools.Sandbox.Domain` | OpenSandbox service address | `localhost:5880` |
| `Tools.Sandbox.ApiKey` | OpenSandbox API key | Empty |
| `Tools.Sandbox.UseHttps` | Use HTTPS | `false` |
| `Tools.Sandbox.Image` | Container Docker image | `ubuntu:latest` |
| `Tools.Sandbox.TimeoutSeconds` | Sandbox timeout in seconds | `600` |
| `Tools.Sandbox.Cpu` | Container CPU limit | `1` |
| `Tools.Sandbox.Memory` | Container memory limit | `512Mi` |
| `Tools.Sandbox.NetworkPolicy` | `deny` / `allow` / `custom` | `allow` |
| `Tools.Sandbox.AllowedEgressDomains` | Custom allowed egress domains | `[]` |
| `Tools.Sandbox.IdleTimeoutSeconds` | Idle timeout in seconds | `300` |
| `Tools.Sandbox.SyncWorkspace` | Sync workspace into container | `true` |

With a supported OpenAI Responses provider, ask DotCraft to generate an image in a normal conversation. DotCraft requests PNG output from the provider and shows the image inline in clients that support rich content.

`Tools.ImageGeneration.Enabled` is the global image generation switch and defaults to `true`. A provider must also have `SupportsHostedImageGeneration` set to `true` before DotCraft injects the hosted `image_generation` tool. When the provider field is omitted, ChatGPT OAuth and the official OpenAI Responses API-key endpoint default to `true`; custom OpenAI-compatible Responses endpoints default to `false` and should be enabled only when they support the hosted tool.

Personal local hardening example:

```json
{
  "Security": {
    "BlacklistedPaths": [
      "~/.ssh",
      "~/.gnupg",
      "~/.aws"
    ]
  },
  "Tools": {
    "File": {
      "RequireApprovalOutsideWorkspace": true
    },
    "Shell": {
      "RequireApprovalOutsideWorkspace": true,
      "Timeout": 300
    }
  }
}
```

Tool allow-list example:

```json
{
  "EnabledTools": ["ReadFile", "GrepFiles", "WebSearch"]
}
```

OpenSandbox example:

```json
{
  "Tools": {
    "Sandbox": {
      "Enabled": true,
      "Domain": "localhost:5880",
      "Image": "ubuntu:latest",
      "NetworkPolicy": "allow",
      "SyncWorkspace": true
    }
  }
}
```

## Automations Goals and Hooks

| Field | Description | Default |
|-------|-------------|---------|
| `Automations.Enabled` | Enables the Automations orchestrator | `true` |
| `Automations.LocalTasksRoot` | Local task root. Empty uses `.craft/tasks/` | Empty |
| `Automations.PollingInterval` | Polling interval | `00:00:30` |
| `Automations.MaxConcurrentTasks` | Maximum concurrent local tasks | `3` |
| `Automations.TurnTimeout` | Single-turn timeout | `00:30:00` |
| `Automations.StallTimeout` | Stall timeout without response | `00:10:00` |
| `Automations.MaxRetries` | Maximum retry count | `3` |
| `Automations.RetryInitialDelay` | Initial retry delay | `00:00:30` |
| `Automations.RetryMaxDelay` | Maximum retry delay | `00:10:00` |
| `Automations.WorktreeRetentionEnabled` | Enables retention cleanup for idle automation task worktrees | `true` |
| `Automations.WorktreeRetentionIdlePeriod` | Idle period before a clean automation task worktree is eligible for cleanup | `21.00:00:00` |
| `Goals.Enabled` | Enables goal storage, AppServer methods, goal context injection, usage accounting, and model goal tools | `true` |
| `Goals.AutoContinueEnabled` | Allows active goals to continue when a Thread is idle | `true` |
| `Hooks.Enabled` | Enables Hooks | `true` |
| `Hooks.State` | Per-hook user state keyed by stable hook key. Stores `Enabled` and `TrustedHash` for Desktop toggle/trust actions | `{}` |
| `Cron.Enabled` | Enables Cron scheduled tasks | `true` |
| `Heartbeat.Enabled` | Enables heartbeat service | `false` |
| `Heartbeat.IntervalSeconds` | Check interval in seconds | `1800` |
| `Heartbeat.NotifyAdmin` | Sends results to admin in social channels | `true` |

`Automations.WorktreeRetentionIdlePeriod` must be at least `14.00:00:00`. The retention sweep only removes managed automation task worktrees that are idle, clean, and have no commits ahead of their base.

Automation AppServer methods:

| Method | Description |
|---|---|
| `automation/task/list` | List local tasks |
| `automation/task/read` | Read one local task |
| `automation/task/create` | Create a local task |
| `automation/task/run` | Run a local task immediately |
| `automation/task/updateBinding` | Update or clear thread binding |
| `automation/task/discardWorktree` | Remove a task's managed worktree and branch while keeping the task |
| `automation/task/delete` | Delete a local task |
| `automation/template/list` | List templates |
| `automation/template/save` | Save a user template |
| `automation/template/delete` | Delete a user template |

Goal AppServer methods:

| Method | Description |
|---|---|
| `thread/goal/set` | Set, replace, pause, or resume a Thread goal |
| `thread/goal/get` | Read current Thread goal state |
| `thread/goal/clear` | Clear current Thread goal |

Hook commands live in `hooks.json`, not in `config.json`. DotCraft loads global hooks from `~/.craft/hooks.json`, workspace hooks from `.craft/hooks.json`, and plugin hooks from enabled plugin hook files. For a user-facing overview and the Desktop workflow, start with [Lifecycle Hooks](../features/agent-system/hooks).

Hook quick start (`.craft/hooks.json`):

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Exec",
        "hooks": [
          {
            "type": "command",
            "command": "node .craft/hooks/log-tool-call.js",
            "timeout": 10
          }
        ]
      }
    ]
  }
}
```

Hook matcher group fields:

| Field | Description |
|---|---|
| `matcher` | Regex for matching tool names. Empty matches all tool-related events |
| `hooks` | Ordered list of hook handlers for the event and matcher |

Hook handler fields:

| Field | Description |
|---|---|
| `type` | Supports `"command"` |
| `command` | Shell command to run |
| `timeout` | Hook timeout in seconds |
| `if` | Optional condition such as `Bash(git commit:*)` |
| `shell` | Optional shell override |
| `statusMessage` | Optional UI status label |
| `async` | Run without blocking the current action |
| `asyncRewake` | Allows hook feedback to enqueue a follow-up turn |
| `rewakeMessage` | Prefix for follow-up feedback |
| `rewakeSummary` | Short follow-up summary |

Lifecycle events:

| Event | Purpose |
|---|---|
| `SessionStart` | Runs when a new session starts |
| `UserPromptSubmit` | Runs when a user prompt is submitted, before prompt assembly |
| `PrePrompt` | DotCraft-native compatibility event before the assembled prompt is sent |
| `PreToolUse` | Checks or blocks before tool calls |
| `PermissionRequest` | Runs before a permission request is shown |
| `PostToolUse` | Logs, formats, or notifies after successful tool calls |
| `PostToolUseFailure` | Runs after a tool call fails |
| `PreCompact` / `PostCompact` | Run around context compaction |
| `SubagentStart` / `SubagentStop` | Run around subagent lifecycle |
| `Stop` | Runs after the assistant response and can enqueue follow-up feedback |
| `StopFailure` | Runs after Stop hook handling fails |

Tool-related Hook stdin usually includes:

```json
{
  "hook_event_name": "PreToolUse",
  "cwd": "/workspace/example",
  "sessionId": "thread-id",
  "session_id": "thread-id",
  "toolName": "Bash",
  "tool_name": "Bash",
  "toolArgs": {
    "command": "dotnet test"
  },
  "tool_input": {
    "command": "dotnet test"
  }
}
```

Turn-related Hook stdin usually includes:

```json
{
  "hook_event_name": "Stop",
  "cwd": "/workspace/example",
  "sessionId": "thread-id",
  "session_id": "thread-id",
  "last_assistant_message": "Agent completed the turn",
  "stop_hook_active": false
}
```

DotCraft emits both camelCase and snake_case field names. JSON stdout can return
`hookSpecificOutput.additionalContext` to inject model-visible context, or
`decision: "block"` plus `reason` to block supported events or request a rewake
follow-up from an `asyncRewake` hook. The complete engineering contract lives in
`specs/features/lifecycle-hooks.md`.

Exit code semantics:

| Exit code | Meaning |
|---|---|
| `0` | Success, continue |
| `2` | Block supported events, or request follow-up feedback for rewake hooks |
| Other non-zero | Hook failed; DotCraft records the failure and continues according to the event's runtime policy |

Matcher examples:

| matcher | Matches |
|---|---|
| `WriteFile\|EditFile` | File writes and edits |
| `Exec` | Shell commands |
| `.*` | All tools |

Desktop manages per-user hook state in `~/.craft/config.json`:

```json
{
  "Hooks": {
    "State": {
      "/workspace/.craft/hooks.json:pre_tool_use:0:0": {
        "Enabled": false,
        "TrustedHash": "sha256:..."
      }
    }
  }
}
```

`Enabled: false` disables one hook without editing the source file. `TrustedHash` records the last trusted normalized hook definition. Hooks from config and plugins must be trusted before they run, and modified hooks must be trusted again. Plugin hooks are usually trusted as one plugin bundle in Desktop, while the saved state remains per hook.

Plugin hook files use the same `hooks.json` structure. In plugin hook commands, DotCraft expands `${DOTCRAFT_PLUGIN_ROOT}` and `${DOTCRAFT_PLUGIN_DATA}` and injects the same names as environment variables.

## Operational logging

DotCraft writes workspace host diagnostics to `<workspace>/.craft/logs` and Hub
diagnostics to `~/.craft/logs`.

| Field | Description | Default |
|---|---|---|
| **`Logging.Enabled`** | Writes operational diagnostics to rolling files | `true` |
| **`Logging.Console`** | Also writes diagnostics to the console. Protocol hosts use stderr so stdout remains protocol-only | `false` |
| **`Logging.MinLevel`** | Minimum level: `Trace`, `Debug`, `Information`, `Warning`, `Error`, or `Critical` | `Information` |
| **`Logging.Directory`** | Log directory relative to the host's `.craft` directory | `logs` |
| **`Logging.RetentionDays`** | Deletes older rolled files at startup; `0` disables cleanup | `7` |

```json
{
  "Logging": {
    "Enabled": true,
    "Console": false,
    "MinLevel": "Information",
    "Directory": "logs",
    "RetentionDays": 7
  }
}
```

Operational logs contain timestamps, severity, process ID, category, messages, exceptions, and
active diagnostic scopes. Raw ACP traffic and opt-in session stream debug records use separate
files because they can contain sensitive or high-volume payloads.

## Entry Points and Services

| Field | Description | Default |
|-------|-------------|---------|
| `Acp.Enabled` | Enables ACP mode | `false` |
| `DashBoard.Enabled` | Enables Dashboard | `false` |
| `DashBoard.Host` | Dashboard listen address | `127.0.0.1` |
| `DashBoard.Port` | Dashboard listen port | `8080` |
| `AppServer.Mode` | AppServer transport mode, such as stdio or WebSocket | Empty |
| `AppServer.WebSocket.Host` | WebSocket listen host | `127.0.0.1` |
| `AppServer.WebSocket.Port` | WebSocket listen port | `9100` |
| `AppServer.WebSocket.Token` | Token required by remote WebSocket clients | Empty |
| `ExternalChannels` | External channel registration map | `{}` |

Dashboard example:

```json
{
  "DashBoard": {
    "Enabled": true,
    "Host": "127.0.0.1",
    "Port": 8080
  }
}
```

External channel registration examples:

Desktop-managed built-in TypeScript channel:

```json
{
  "ExternalChannels": {
    "qq": {
      "enabled": true,
      "transport": "managedWebsocket",
      "builtinModule": "channel-qq"
    }
  }
}
```

Standalone adapter:

```json
{
  "AppServer": {
    "Mode": "WebSocket",
    "WebSocket": {
      "Host": "127.0.0.1",
      "Port": 9100,
      "Token": ""
    }
  },
  "ExternalChannels": {
    "wecom": {
      "enabled": true,
      "transport": "websocket"
    }
  }
}
```

Platform connections, allowlists, and approval timeouts live in adapter-specific files such as `.craft/qq.json` and `.craft/wecom.json`. See [Channel configuration reference](./channels/reference) for TypeScript channel examples.

## Plugins MCP and LSP

| Field | Description | Default |
|-------|-------------|---------|
| `Plugins.PluginRoots` | Extra plugin root directories maintained outside `.craft/plugins/` | `[]` |
| `Plugins.PluginRegistries` | Plugin marketplace sources available for catalog discovery | `[]` |
| `Plugins.DisableDefaultPluginRegistry` | Ignore the host-provided default official plugin registry | `false` |
| `McpServers` | MCP server configuration map | `{}` |
| `Tools.DeferredLoading.Strategy` | Deferred tool loading strategy: `Off`, `Auto`, `Simulated`, or `Native` | `Auto` |
| `Tools.DeferredLoading.AlwaysLoadedTools` | MCP tool names always loaded upfront | `[]` |
| `Tools.DeferredLoading.DeferThreshold` | Minimum MCP tool count before MCP tools are deferred | `10` |
| `Tools.DeferredLoading.MaxSearchResults` | Maximum deferred tool search results per query | `5` |
| `LspServers` | LSP server configuration map | `{}` |
| `Tools.Lsp.Enabled` | Enables built-in LSP tools | `false` |

Official DotCraft Desktop and Docker hosts supply the official plugin marketplace as the default registry through `DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL`. Marketplace sources added through Desktop are stored in the global configuration; a workspace `PluginRegistries` value follows the normal workspace-over-global precedence. Docker Stack deployments persist the global configuration and marketplace cache under `state/dotcraft`.

Each `McpServers` and `LspServers` entry accepts only the fields defined by its current schema. Unknown properties cause configuration parsing to fail.

`Plugins.PluginRegistries` entry fields:

| Field | Description | Default |
|---|---|---|
| `Name` | Marketplace identity. Required for manual entries and must match the marketplace document; maintained automatically when added through Desktop or AppServer | Empty |
| `SourceType` | Source kind: `git`, `local`, or `archive` | Inferred when omitted |
| `Url` | Git URL, local directory, archive URL, or archive file | Empty |
| `Ref` | Git branch, tag, or commit to check out | Source default |
| `SparsePaths` | Repository-relative paths included in a Git checkout | `[]` |
| `MarketplacePath` | Marketplace document path inside the source root | `.craft/plugins/marketplace.json` |
| `LastUpdated` | UTC timestamp of the last successful add or refresh | Empty |
| `LastRevision` | Resolved Git revision from the last successful fetch | Empty |

When `SourceType` is omitted, an existing directory or archive file is read locally; other values are treated as archive URLs. `Ref` and `SparsePaths` apply only to Git sources.

See [Plugin Market](./integrations/plugin-market) for source syntax, the marketplace document, and lifecycle behavior.

Local plugin development override example:

```json
{
  "Plugins": {
    "PluginRoots": ["/path/to/local/plugins"]
  }
}
```

MCP example:

```json
{
  "McpServers": {
    "everything": {
      "command": "npx",
      "arguments": ["-y", "@modelcontextprotocol/server-everything"]
    }
  },
  "Tools": {
    "DeferredLoading": {
      "Strategy": "Auto",
      "DeferThreshold": 10
    }
  }
}
```

With `Tools.DeferredLoading.Strategy = Auto`, `openai-responses` and Anthropic use native client-executed `tool_search`; chat-completions uses simulated `SearchTools`.

## SubAgent and External CLI Profiles

For the beginner path, read [SubAgents](../features/agent-system/subagents).

| Field | Description | Default |
|-------|-------------|---------|
| `SubAgent.MaxDepth` | Maximum session-backed SubAgent spawn depth; the first child is depth `1` | `1` |
| `SubAgent.ProviderPreferences` | Complete native SubAgent preferences keyed by the parent thread provider; a missing entry inherits that thread's complete MainAgent preference | `{}` |
| `SubAgent.MinWaitTimeoutMs` | Minimum accepted `WaitAgent.timeoutMs` value in milliseconds | `15000` |
| `SubAgent.DefaultWaitTimeoutMs` | `WaitAgent.timeoutMs` used when the tool call omits a timeout | `60000` |
| `SubAgent.MaxWaitTimeoutMs` | Maximum accepted `WaitAgent.timeoutMs` value in milliseconds | `3600000` |
| `SubAgent.EnableExternalCliSessionResume` | Allows external CLI profiles that support resume to reuse saved external sessions | `false` |
| `SubAgent.DisabledProfiles` | SubAgent profile names hidden and disabled for this workspace | `[]` |
| `SubAgent.Roles` | Workspace-defined SubAgent roles; entries with built-in names override built-in roles | `[]` |

Role example:

```json
{
  "SubAgent": {
    "MaxDepth": 2,
    "Roles": [
      {
        "Name": "docs-explorer",
        "Description": "Read-only documentation and code explorer.",
        "ToolAllowList": ["ReadFile", "GrepFiles", "FindFiles", "WebSearch", "WebFetch", "SkillView", "Exec"],
        "ShellAccess": "ReadOnly",
        "AgentControlToolAccess": "Disabled",
        "Instructions": "Inspect files, web sources, and non-mutating shell output such as `git diff`. Do not edit files, manage skills, or spawn agents."
      }
    ]
  }
}
```

Fields inside each `SubAgent.Roles` entry:

| Field | Description |
|-------|-------------|
| `Name` | Role name, also the value used by `SpawnAgent.agentRole` |
| `Description` | Short role description exposed to the main Agent |
| `ToolAllowList` | Exact tool allow-list; empty means no additional restriction on eligible tools |
| `ToolDenyList` | Exact tool deny-list removed after the tool set is assembled |
| `ShellAccess` | How far a reachable shell tool may go: `None` / `ReadOnly` / `Full`. Applied in addition to the allow/deny lists, not instead of them. Defaults to `Full` |
| `AgentControlToolAccess` | AgentTools policy: `Disabled` / `Full` / `AllowList` |
| `AllowedAgentControlTools` | AgentTools names allowed when `AgentControlToolAccess` is `AllowList` |
| `Instructions` | Role instructions delivered as the SubAgent thread's role context message |
| `Mode` | Optional mode override |
| `Model` | Optional model override |
| `OverrideBasePrompt` | Replaces the base prompt with `Instructions`; by default instructions are appended |

Custom external CLI profiles live under `SubAgentProfiles`. Workspace config overrides same-named global profiles.

```json
{
  "SubAgent": {
    "EnableExternalCliSessionResume": true
  },
  "SubAgentProfiles": {
    "my-cli": {
      "runtime": "cli-oneshot",
      "bin": "my-cli",
      "workingDirectoryMode": "workspace",
      "inputMode": "arg",
      "outputFormat": "text",
      "supportsResume": true,
      "resumeArgTemplate": "--resume {sessionId}",
      "resumeSessionIdJsonPath": "session_id"
    }
  }
}
```

Fields inside each `SubAgentProfiles` entry:

| Field | Description |
|---|---|
| `runtime` | Runtime type; external short-process CLIs use `cli-oneshot` |
| `bin` | CLI executable name or absolute path |
| `args` | Fixed argument list |
| `workingDirectoryMode` | `workspace` / `specified` |
| `inputMode` | `stdin` / `arg` / `arg-template` / `env` |
| `inputArgTemplate` | Template for `arg-template` mode |
| `inputEnvKey` | Env-var name receiving task text in `env` mode |
| `env` | Fixed env vars injected into the subprocess |
| `envPassthrough` | Names of env vars to copy from parent |
| `outputFormat` | `text` or `json` |
| `outputJsonPath` | JSON path to extract the final result in `json` mode |
| `readOutputFile` | Prefer reading the output file as the final result |
| `outputFileArgTemplate` | Output-file argument template, supports `{path}` |
| `supportsResume` | Allow DotCraft to store and reuse the external session id |
| `resumeArgTemplate` | Resume argument template, supports `{sessionId}` |
| `resumeSessionIdJsonPath` | JSON path to extract session id from stdout |
| `resumeSessionIdRegex` | Regex fallback when stdout is not a single JSON object |
| `timeout` | Per-run timeout in seconds |
| `maxOutputBytes` | Maximum captured output bytes |
| `trustLevel` | `trusted` / `prompt` / `restricted` |
| `permissionModeMapping` | Map DotCraft approval modes to CLI arguments |

Vendor headless notes:

| Profile | Behavior |
|---|---|
| `cursor-cli` | DotCraft injects `-p --output-format json` and appends `--resume {sessionId}` when resuming |
| `codex-cli` | DotCraft injects `exec` plus output-file arguments; resume becomes `exec resume {sessionId}` |

## Custom Commands

`CustomCommands` stores reusable prompts or workflows as commands. Command content usually lives under workspace `.craft/commands/` or the corresponding configuration entry, then can be reused from CLI, Desktop, and other entry points.

## Related docs

- [Getting started](../getting-started) — first-time setup
- [AppServer Mode](./lifecycle/appserver) — `AppServer.*` / `CLI.*` fields in context
- [Settings Lifecycle](./lifecycle/settings-lifecycle) — when a changed field takes effect
