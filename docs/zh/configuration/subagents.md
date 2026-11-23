# Subagent

概念和日常用法见 [Subagents](../guide/subagents)。

## 字段

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `SubAgent.MaxDepth` | session-backed subagent 的最大生成深度。第一级 subagent 深度为 `1` | `1` |
| `SubAgent.MaxConcurrentSubAgents` | 同一个根对话的子树内同时驻留的 session-backed subagent 上限。超出时自动关闭最旧的空闲 subagent，若驻留的 subagent 全部仍在运行则本次 spawn 失败 | `16` |
| `SubAgent.ProviderPreferences` | 按父对话 provider 保存的完整 subagent 模型偏好。条目里可选的 `ProviderId` 让不继承完整上下文的 subagent 在另一个 provider 上运行。缺少对应项时继承该对话完整的 MainAgent 偏好 | `{}` |
| `SubAgent.Models` | 最多八个 `{ ProviderId, Model }` 条目，主 Agent 可以按任务为不继承完整上下文的 subagent 挑选。为空时只提供当前 provider 的模型 | `[]` |
| `SubAgent.Roles` | 工作区自定义 subagent role。同名条目覆盖内置 role | `[]` |

## 角色

Role 示例：

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

`SubAgent.Roles` 的条目字段：

| 字段 | 说明 |
|------|------|
| `Name` | role 名称，也是 `SpawnAgent.agentRole` 使用的值 |
| `Description` | role 简短说明，会暴露给主 Agent |
| `ToolAllowList` | 精确工具允许列表。为空表示不额外限制候选工具 |
| `ToolDenyList` | 精确工具拒绝列表，会在工具集合构建完成后移除 |
| `ShellAccess` | 可达的 Shell 工具能走多远：`None` / `ReadOnly` / `Full`。与允许/拒绝列表叠加生效，而非取代。默认 `Full` |
| `AgentControlToolAccess` | AgentTools 策略：`Disabled` / `Full` / `AllowList` |
| `AllowedAgentControlTools` | `AgentControlToolAccess` 为 `AllowList` 时允许的 AgentTools 名称 |
| `Instructions` | 作为 subagent 对话角色上下文消息送达的 role instructions |
| `Mode` | 可选 mode 覆盖 |
| `Model` | 可选 model 覆盖 |
| `ProviderId` | `Model` 所属的 provider；默认为父对话的 provider |
| `OverrideBasePrompt` | 是否用 `Instructions` 覆盖基础 prompt。默认追加而不是覆盖 |
