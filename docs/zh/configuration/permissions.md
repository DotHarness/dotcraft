# 权限

Agent 的工具能运行什么、能碰到哪里。这些检查如何配合见[安全](../self-hosting/security)。

## 字段

| 配置项 | 说明 | 默认值 |
|---|---|---|
| `EnabledTools` | 全局启用的工具名称列表，为空时启用所有工具 | `[]` |
| `Security.BlacklistedPaths` | 文件工具不能访问的路径，包含子路径 | `[]` |
| `Tools.File.RequireApprovalOutsideWorkspace` | 文件工具越界访问和 Shell 从工作区外启动是否需要审批，`false` 时直接拒绝 | `true` |
| `Tools.Shell.Policy.Rules` | 先于危险操作和启动目录检查的前缀规则。每条形如 `{ "prefix": ["git", "push"], "decision": "allow" \| "prompt" \| "forbidden", "justification": "..." }`。前缀匹配的规则全部生效，最严格的决策胜出 | `[]` |

## 示例

个人本地 hardening 示例：

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
          { "prefix": ["git", "push"], "decision": "prompt", "justification": "推送会离开本机" },
          { "prefix": ["rm"], "decision": "forbidden" }
        ]
      }
    }
  }
}
```

**永久允许**学到的规则会追加到工作区数据目录的 `.craft/security/shell-rules.json`，并立即生效。

工具 allow-list 示例：

```json
{
  "EnabledTools": ["ReadFile", "GrepFiles", "WebSearch"]
}
```
