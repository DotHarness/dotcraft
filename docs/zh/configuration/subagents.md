# Subagent

概念和日常用法见 [Subagents](../guide/subagents)。

## 字段

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `SubAgent.MaxDepth` | session-backed subagent 的最大生成深度。第一级 subagent 深度为 `1` | `1` |
| `SubAgent.MaxConcurrentSubAgents` | 同一根线程子树内同时驻留的 session-backed subagent 上限。超出时自动关闭最旧的空闲 subagent，若驻留的 subagent 全部仍在运行则本次 spawn 失败 | `16` |
| `SubAgent.ProviderPreferences` | 按父线程 provider 保存的完整原生 subagent 偏好。缺少对应项时继承该线程完整的 MainAgent 偏好 | `{}` |
| `SubAgent.MinWaitTimeoutMs` | `WaitAgent.timeoutMs` 接受的最小值，单位毫秒 | `15000` |
| `SubAgent.DefaultWaitTimeoutMs` | `WaitAgent` 调用未传 timeout 时使用的默认毫秒数 | `60000` |
| `SubAgent.MaxWaitTimeoutMs` | `WaitAgent.timeoutMs` 接受的最大值，单位毫秒 | `3600000` |
| `SubAgent.EnableExternalCliSessionResume` | 是否允许支持 resume 的 external CLI profile 复用已保存外部会话 | `false` |
| `SubAgent.DisabledProfiles` | 当前工作区隐藏和禁用的 subagent profile 名称列表 | `[]` |
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
| `Instructions` | 作为 subagent 线程角色上下文消息送达的 role instructions |
| `Mode` | 可选 mode 覆盖 |
| `Model` | 可选 model 覆盖 |
| `OverrideBasePrompt` | 是否用 `Instructions` 覆盖基础 prompt。默认追加而不是覆盖 |

## 外部 CLI Profile

自定义外部 CLI profile 位于 `SubAgentProfiles`。工作区配置覆盖同名全局 profile。

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

`SubAgentProfiles` 条目字段：

| 字段 | 说明 |
|---|---|
| `runtime` | Runtime 类型。外部短进程 CLI 使用 `cli-oneshot` |
| `bin` | CLI 可执行文件名或绝对路径 |
| `args` | 固定参数列表 |
| `workingDirectoryMode` | `workspace` / `specified` |
| `inputMode` | `stdin` / `arg` / `arg-template` / `env` |
| `inputArgTemplate` | `arg-template` 模式下的输入参数模板 |
| `inputEnvKey` | `env` 模式下接收任务文本的环境变量名 |
| `env` | 注入子进程的固定环境变量 |
| `envPassthrough` | 从父进程透传的环境变量名 |
| `outputFormat` | `text` 或 `json` |
| `outputJsonPath` | `json` 模式下提取最终结果的路径 |
| `readOutputFile` | 优先读取输出文件作为最终结果 |
| `outputFileArgTemplate` | 输出文件参数模板，支持 `{path}` |
| `supportsResume` | 是否允许 DotCraft 保存和复用外部 session id |
| `resumeArgTemplate` | Resume 参数模板，支持 `{sessionId}` |
| `resumeSessionIdJsonPath` | 从 stdout 提取 session id 的 JSON path |
| `resumeSessionIdRegex` | stdout 不是单个 JSON object 时的正则 fallback |
| `timeout` | 单次运行超时时间（秒） |
| `maxOutputBytes` | 最大捕获输出字节数 |
| `trustLevel` | `trusted` / `prompt` / `restricted` |
| `permissionModeMapping` | 将 DotCraft 审批模式映射为 CLI 参数 |

厂商 headless 参考：

| Profile | 行为 |
|---|---|
| `cursor-cli` | DotCraft 注入 `-p --output-format json`，恢复时追加 `--resume {sessionId}` |
| `codex-cli` | DotCraft 注入 `exec` 和输出文件参数。恢复时使用 `exec resume {sessionId}` |
