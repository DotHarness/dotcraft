# Subagents

For the concept and everyday use, read [Subagents](../guide/subagents).

## Fields

| Field | Description | Default |
|-------|-------------|---------|
| `SubAgent.MaxDepth` | Maximum spawn depth for session-backed subagents. The first child is depth `1` | `1` |
| `SubAgent.MaxConcurrentSubAgents` | Maximum resident session-backed subagents inside one root chat's subtree. Exceeding it auto-closes the oldest idle subagent, and the spawn fails instead when every resident subagent is still running | `16` |
| `SubAgent.ProviderPreferences` | Complete subagent model preferences keyed by the parent chat's provider. An entry's optional `ProviderId` runs fresh and bounded subagents on another provider. A missing entry inherits that chat's complete MainAgent preference | `{}` |
| `SubAgent.Models` | Up to eight `{ ProviderId, Model }` entries the main Agent may pick per task for fresh and bounded subagents. Empty offers models from the current provider | `[]` |
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
| `Instructions` | Role instructions delivered as the subagent chat's role context message |
| `Mode` | Optional mode override |
| `Model` | Optional model override |
| `ProviderId` | Provider for `Model`; defaults to the parent chat's provider |
| `OverrideBasePrompt` | Replaces the base prompt with `Instructions`; by default instructions are appended |
