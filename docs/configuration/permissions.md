# Permissions

What the agent's tools may run and reach. How these checks fit together is in [Security](../self-hosting/security).

## Fields

| Field | Description | Default |
|---|---|---|
| `EnabledTools` | Globally enabled tool names. Empty enables all tools | `[]` |
| `Security.BlacklistedPaths` | Paths file tools must not access; subpaths are also checked | `[]` |
| `Tools.File.RequireApprovalOutsideWorkspace` | Approve file-tool access and shell launches outside workspace; `false` blocks them | `true` |
| `Tools.Shell.Policy.Rules` | Prefix rules checked before dangerous-operation and launch-directory checks; each entry is `{ "prefix": ["git", "push"], "decision": "allow" \| "prompt" \| "forbidden", "justification": "..." }`. Every rule whose prefix matches applies and the most restrictive decision wins | `[]` |

## Examples

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
      "Timeout": 300,
      "Policy": {
        "Rules": [
          { "prefix": ["git", "push"], "decision": "prompt", "justification": "pushes leave the machine" },
          { "prefix": ["rm"], "decision": "forbidden" }
        ]
      }
    }
  }
}
```

Rules learned from **Always allow** decisions are appended to `.craft/security/shell-rules.json` in the workspace data directory and take effect immediately.

Tool allow-list example:

```json
{
  "EnabledTools": ["ReadFile", "GrepFiles", "WebSearch"]
}
```
