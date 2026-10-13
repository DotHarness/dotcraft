# Subagents

For the concept and everyday use, read [Subagents](../guide/subagents).

## Fields

| Field | Description | Default |
|-------|-------------|---------|
| `SubAgent.MaxDepth` | Maximum spawn depth for session-backed subagents. The first child is depth `1` | `1` |
| `SubAgent.MaxConcurrentSubAgents` | Maximum resident session-backed subagents inside one root thread's subtree. Exceeding it auto-closes the oldest idle subagent, and the spawn fails instead when every resident subagent is still running | `16` |
| `SubAgent.ProviderPreferences` | Complete native subagent preferences keyed by the parent thread provider. A missing entry inherits that thread's complete MainAgent preference | `{}` |
| `SubAgent.MinWaitTimeoutMs` | Minimum accepted `WaitAgent.timeoutMs` value in milliseconds | `15000` |
| `SubAgent.DefaultWaitTimeoutMs` | `WaitAgent.timeoutMs` used when the tool call omits a timeout | `60000` |
| `SubAgent.MaxWaitTimeoutMs` | Maximum accepted `WaitAgent.timeoutMs` value in milliseconds | `3600000` |
| `SubAgent.EnableExternalCliSessionResume` | Allows external CLI profiles that support resume to reuse saved external sessions | `false` |
| `SubAgent.DisabledProfiles` | Subagent profile names hidden and disabled for this workspace | `[]` |
| `SubAgent.Roles` | Workspace-defined subagent roles. Entries with built-in names override built-in roles | `[]` |

## Roles

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
| `Instructions` | Role instructions delivered as the subagent thread's role context message |
| `Mode` | Optional mode override |
| `Model` | Optional model override |
| `OverrideBasePrompt` | Replaces the base prompt with `Instructions`; by default instructions are appended |

## External CLI profiles

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
