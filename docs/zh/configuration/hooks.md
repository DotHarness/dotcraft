# Hooks

Hook 命令放在 `hooks.json`，不是 `config.json`。DotCraft 会从 `~/.craft/hooks.json` 加载全局 hooks，从 `.craft/hooks.json` 加载工作区 hooks，并从已启用插件的 hook 文件加载 plugin hooks。概念说明和 Desktop 操作入口见 [生命周期 Hooks](../guide/hooks)。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Hooks.Enabled` | 是否启用 Hooks | `true` |
| `Hooks.State` | 按稳定 hook key 保存的用户态配置。Desktop toggle/trust 会写入 `Enabled` 和 `TrustedHash` | `{}` |

## Hook 文件

Hook 快速示例（`.craft/hooks.json`）：

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

## Hook 字段

Hook matcher group 字段：

| 字段 | 说明 |
|---|---|
| `matcher` | 匹配工具名的正则。为空时匹配所有工具相关事件 |
| `hooks` | 当前事件和 matcher 下按顺序执行的 hook handler 列表 |

Hook handler 字段：

| 字段 | 说明 |
|---|---|
| `type` | 支持 `"command"` |
| `command` | 要运行的 Shell 命令 |
| `timeout` | Hook 超时时间（秒） |
| `if` | 可选条件，例如 `Bash(git commit:*)` |
| `shell` | 可选 Shell 覆盖 |
| `statusMessage` | 可选 UI 状态文案 |
| `async` | 不阻塞当前动作，异步运行 |
| `asyncRewake` | 允许 hook 反馈入队为后续 turn |
| `rewakeMessage` | 后续反馈的前缀 |
| `rewakeSummary` | 简短后续反馈摘要 |

## 生命周期事件

生命周期事件：

| Event | 用途 |
|---|---|
| `SessionStart` | 新会话开始时运行 |
| `UserPromptSubmit` | 用户提交 prompt 时运行，早于 prompt 组装 |
| `PrePrompt` | DotCraft 原生兼容事件，在组装后的 prompt 发送前运行 |
| `PreToolUse` | 工具调用前检查或阻塞 |
| `PermissionRequest` | 请求权限前运行 |
| `PostToolUse` | 工具调用成功后记录、格式化或通知 |
| `PostToolUseFailure` | 工具调用失败后运行 |
| `PreCompact` / `PostCompact` | 上下文压缩前后运行 |
| `SubagentStart` / `SubagentStop` | subagent 生命周期前后运行 |
| `Stop` | assistant 响应后运行，并可入队后续反馈 |
| `StopFailure` | Stop hook 处理失败后运行 |

## 输入与输出

工具相关 Hook stdin 通常包含：

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

Turn 相关 Hook stdin 通常包含：

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

DotCraft 会同时输出 camelCase 和 snake_case 字段。JSON stdout 可以返回
`hookSpecificOutput.additionalContext` 注入模型可见上下文，也可以返回
`decision: "block"` 和 `reason` 来阻塞支持阻塞的事件，或让 `asyncRewake`
hook 入队后续反馈。完整工程协议位于 `specs/features/lifecycle-hooks.md`。

退出码语义：

| 退出码 | 含义 |
|---|---|
| `0` | 成功，继续执行 |
| `2` | 阻塞支持阻塞的事件，或为 rewake hooks 请求后续反馈 |
| 其他非零 | Hook 失败。DotCraft 记录失败并按该事件的运行时策略继续 |

Matcher 示例：

| matcher | 匹配 |
|---|---|
| `WriteFile\|EditFile` | 文件写入和编辑 |
| `Exec` | Shell 命令 |
| `.*` | 所有工具 |

## Hook 状态

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

`Enabled: false` 可以在不编辑来源文件的情况下停用单个 hook。`TrustedHash` 记录上次信任的规范化 hook 定义。来自 config 和 plugins 的 hooks 必须被信任后才会运行，修改后的 hooks 需要重新信任。Desktop 通常把插件 hooks 作为一个插件能力包整体信任，但保存的状态仍然是每条 hook 一条。

## 插件 Hooks


