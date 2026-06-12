# DotCraft 完整配置参考

本页集中列出配置字段、默认值、JSON 示例和高级参考。第一次配置请先读 [项目级工作区](../features/project-first)。想理解功能本身时先读对应 Feature 页面，需要准确字段时再回到这里。

DotCraft 先读取全局 `~/.craft/config.json`，再叠加工作区 `.craft/config.json`，工作区字段优先生效。配置字符串支持 `$VAR` 和 `${VAR}` 环境变量占位；变量不存在时保留原始占位符。

## 基础模型与 Provider

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `ProviderId` | 当前选择的个人 Provider id；为空表示未选择 Provider | 空 |
| `Model` | 默认模型名称 | `gpt-4o-mini` |
| `NetworkTimeoutSeconds` | 全局模型请求超时时间，单位秒；Provider 可单独覆盖 | `600` |
| `Providers` | 个人模型 Provider 字典，通常写在 `~/.craft/config.json` | 空 |
| `SubagentMaxConcurrency` | 最大并发子 Agent 数量 | `3` |
| `MaxSessionQueueSize` | 每个 Session 最大排队请求数；`0` 表示无限制 | `3` |
| `ConsolidationModel` | 记忆整合专用模型，空值使用主模型 | 空 |
| `DebugMode` | 控制台不截断工具调用参数输出 | `false` |
| `EnabledTools` | 全局启用的工具名称列表，为空时启用所有工具 | `[]` |

个人 Provider 示例：

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

工作区模型选择示例：

```json
{
  "ProviderId": "anthropic",
  "Model": "claude-sonnet-4-5"
}
```

Provider 对象字段：

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `DisplayName` | 面向用户显示的 Provider 名称；为空时使用 provider id | 空 |
| `Protocol` | Provider 协议：`anthropic`、`openai-chat-completions` 或 `openai-responses`；空值和旧值 `openai` 按 `openai-chat-completions` 处理 | `openai-chat-completions` |
| `ApiKey` | Provider API Key；建议使用 `${ENV_NAME}` 引用环境变量 | 空 |
| `EndPoint` | Provider base URL；为空时使用协议默认地址 | OpenAI 协议：`https://api.openai.com/v1`；`anthropic`: `https://api.anthropic.com` |
| `NetworkTimeoutSeconds` | 单个 Provider 请求超时时间，覆盖全局 `NetworkTimeoutSeconds` | 空 |
| `StreamMaxRetries` | 单个 Provider 的流式响应断线重连次数；设为 `0` 可关闭 stream retry | `5` |
| `StreamIdleTimeoutMs` | 单个 Provider 的流式响应空闲超时时间，单位毫秒 | `300000` |

## Workspace Memory 与 Skills

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Memory.AutoConsolidateEnabled` | 启用长期记忆自动沉淀 | `true` |
| `Memory.ConsolidateEveryNTurns` | 每个线程成功完成多少轮后触发一次长期记忆沉淀 | `5` |
| `Skills.SelfLearning.Enabled` | Skill 自学习主开关；关闭后模型看不到 skill 编辑能力 | `true` |
| `Skills.SelfLearning.MaxSkillContentChars` | 通过自学习写入单个 `SKILL.md` 的最大字符数 | `100000` |
| `Skills.SelfLearning.MaxSupportingFileBytes` | 通过自学习写入单个 supporting file 的最大字节数 | `1048576` |

Skill 自学习示例：

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

`SkillManage(action, ...)` 参考：

| Action | 必填参数 | 用途 |
|---|---|---|
| `create` | `name`, `content` | 创建新的工作区 skill |
| `patch` | `name`, `oldString`, `newString` | 局部修补 `SKILL.md` 或 supporting file |
| `edit` | `name`, `content` | 完整替换已有工作区 skill 的 `SKILL.md` |
| `write_file` | `name`, `filePath`, `fileContent` | 写入 supporting file |
| `remove_file` | `name`, `filePath` | 删除 supporting file |

`create` 会触发 `kind: skill` 审批；破坏性删除也需要审批。自学习只写当前工作区 skill 目录。系统和个人 skills 视为只读，supporting files 只能写在 `scripts/` 或 `assets/` 下，绝对路径和 `..` 路径穿越会被拒绝。

## Compaction

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Compaction.AutoCompactEnabled` | 启用基于阈值的自动压缩 | `true` |
| `Compaction.ReactiveCompactEnabled` | 启用对 `prompt_too_long` 错误的反应式压缩 | `true` |
| `Compaction.ContextWindow` | 模型上下文窗口（Token）；未配置时按当前有效模型推导 | 模型映射值 / `256000` |
| `Compaction.MaxContextWindow` | 推导模型上下文窗口时使用的上限；显式值保留 | `256000` |
| `Compaction.SummaryReserveTokens` | 为摘要输出预留的 Token | `20000` |
| `Compaction.AutoCompactBufferTokens` | 低于硬上限多少 Token 时触发自动压缩 | `13000` |
| `Compaction.WarningBufferTokens` | 到达自动阈值前多少 Token 发出 warning | `20000` |
| `Compaction.ErrorBufferTokens` | 到达自动阈值前多少 Token 发出 error | `10000` |
| `Compaction.ManualCompactBufferTokens` | 低于硬上限多少 Token 时仍允许手动 `/compact` | `3000` |
| `Compaction.KeepRecentMinTokens` | 局部摘要后尾部至少保留的 Token 数 | `10000` |
| `Compaction.KeepRecentMinGroups` | 局部摘要后尾部至少保留的 API 轮次数 | `3` |
| `Compaction.KeepRecentMaxTokens` | 局部摘要后尾部最多保留的 Token 数 | `40000` |
| `Compaction.MicrocompactEnabled` | 启用微压缩 | `true` |
| `Compaction.MicrocompactTriggerCount` | 可压缩工具结果数量达到该值时触发微压缩 | `30` |
| `Compaction.MicrocompactKeepRecent` | 微压缩时保留的最近工具结果数 | `8` |
| `Compaction.MicrocompactGapMinutes` | 距离上次助理消息超过该分钟数也触发微压缩；`0` 表示禁用 | `20` |
| `Compaction.MaxConsecutiveFailures` | 连续失败次数达到该值时熔断 | `3` |

### 模型上下文窗口映射

DotCraft 内置一份常见模型上下文窗口映射表，并会在 `Compaction.ContextWindow` 未显式配置时自动选择。可以用以下文件补充或覆盖：

- 全局：`~/.craft/model-context-windows.json`
- 工作区：`.craft/model-context-windows.json`

```json
{
  "defaultContextWindow": 256000,
  "models": {
    "my-256k-model": 256000,
    "custom-long-context-": 1048576
  }
}
```

## Reasoning 与 PromptCaching

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Reasoning.Enabled` | 是否请求 Provider 的推理支持 | `false` |
| `Reasoning.Effort` | 推理深度：`None` / `Low` / `Medium` / `High` / `ExtraHigh` | `Medium` |
| `Reasoning.Output` | 推理内容是否暴露在响应中：`None` / `Summary` / `Full` | `Full` |
| `PromptCaching.Enabled` | 是否为匹配模型注入 prompt cache marker | `true` |
| `PromptCaching.ModelPatterns` | 大小写不敏感的模型名片段；为空则不匹配任何模型 | `["claude"]` |
| `PromptCaching.Placement` | marker 放置策略；当前仅支持 `ConversationTail` | `ConversationTail` |
| `PromptCaching.Ttl` | Anthropic cache TTL；为空使用默认 5 分钟，`1h` 使用长缓存 | 空 |

Deep-thinking adapter 文件：

- 全局：`~/.craft/model-thinking-adapters.json`
- 工作区：`.craft/model-thinking-adapters.json`

```json
{
  "deepThinking": {
    "models": ["deepseek", "mimo", "my-thinking-model-"],
    "endpoints": ["deepseek", "my-thinking-gateway"]
  }
}
```

## Tools Security 与 Sandbox

| 配置项 | 说明 | 默认值 |
|---|---|---|
| `Security.BlacklistedPaths` | Agent 绝不能访问的路径；也检查子路径 | `[]` |
| `Tools.File.RequireApprovalOutsideWorkspace` | 工作区外文件操作是否需要审批 | `true` |
| `Tools.File.MaxFileSize` | 最大可读取文件大小（字节） | `10485760` |
| `Tools.File.RipgrepPath` | 可选 `rg` 路径；为空时依次尝试 `DOTCRAFT_RG_PATH`、`PATH` 和内置回退 | `""` |
| `Tools.File.SearchTimeoutSeconds` | `GrepFiles` 内容搜索最长运行时间，超时后返回超时结果 | `30` |
| `Tools.Shell.RequireApprovalOutsideWorkspace` | 工作区外 Shell 命令是否需要审批 | `true` |
| `Tools.Shell.Timeout` | Shell 命令超时时间（秒） | `300` |
| `Tools.Shell.MaxOutputLength` | Shell 命令最大输出长度（字符） | `10000` |
| `Tools.Web.MaxChars` | Web 抓取最大字符数 | `50000` |
| `Tools.Web.Timeout` | Web 请求超时时间（秒） | `300` |
| `Tools.Web.SearchMaxResults` | 联网搜索默认返回结果数 | `5` |
| `Tools.Web.SearchProvider` | 搜索引擎提供商：`Bing` / `Exa` | `Exa` |
| `Tools.Lsp.Enabled` | 是否启用内置 LSP 工具 | `false` |
| `Tools.Lsp.MaxFileSize` | LSP 打开或同步文件时允许的最大文件大小 | `10485760` |
| `Tools.Sandbox.Enabled` | 是否启用沙箱模式 | `false` |
| `Tools.Sandbox.Domain` | OpenSandbox 服务地址 | `localhost:5880` |
| `Tools.Sandbox.ApiKey` | OpenSandbox API Key | 空 |
| `Tools.Sandbox.UseHttps` | 是否使用 HTTPS | `false` |
| `Tools.Sandbox.Image` | 沙箱容器 Docker 镜像 | `ubuntu:latest` |
| `Tools.Sandbox.TimeoutSeconds` | 沙箱超时时间（秒） | `600` |
| `Tools.Sandbox.Cpu` | 容器 CPU 限制 | `1` |
| `Tools.Sandbox.Memory` | 容器内存限制 | `512Mi` |
| `Tools.Sandbox.NetworkPolicy` | 网络策略：`deny` / `allow` / `custom` | `allow` |
| `Tools.Sandbox.AllowedEgressDomains` | 自定义允许出站域名列表 | `[]` |
| `Tools.Sandbox.IdleTimeoutSeconds` | 空闲超时（秒） | `300` |
| `Tools.Sandbox.SyncWorkspace` | 是否同步 workspace 到容器 | `true` |

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
      "RequireApprovalOutsideWorkspace": true,
      "Timeout": 300
    }
  }
}
```

工具 allow-list 示例：

```json
{
  "EnabledTools": ["ReadFile", "GrepFiles", "WebSearch"]
}
```

OpenSandbox 示例：

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

## Automations Goals 与 Hooks

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Automations.Enabled` | 是否启用 Automations 编排器 | `true` |
| `Automations.LocalTasksRoot` | 本地任务根目录，留空使用 `.craft/tasks/` | 空 |
| `Automations.PollingInterval` | 轮询间隔 | `00:00:30` |
| `Automations.MaxConcurrentTasks` | 本地任务最大并发数 | `3` |
| `Automations.TurnTimeout` | 单轮对话超时时间 | `00:30:00` |
| `Automations.StallTimeout` | 停顿超时时间 | `00:10:00` |
| `Automations.MaxRetries` | 最大重试次数 | `3` |
| `Automations.RetryInitialDelay` | 重试初始延迟 | `00:00:30` |
| `Automations.RetryMaxDelay` | 重试最大延迟 | `00:10:00` |
| `Automations.WorktreeRetentionEnabled` | 是否启用空闲自动化任务 worktree 清理 | `true` |
| `Automations.WorktreeRetentionIdlePeriod` | 干净自动化任务 worktree 进入清理候选前的空闲时间 | `21.00:00:00` |
| `Goals.Enabled` | 启用目标存储、AppServer 方法、目标上下文注入、用量统计和模型 Goal 工具 | `true` |
| `Goals.AutoContinueEnabled` | 允许 active 目标在线程空闲时自动继续 | `true` |
| `Hooks.Enabled` | 是否启用 Hooks | `true` |
| `Hooks.Events` | Hook 事件配置列表 | `[]` |
| `Cron.Enabled` | 是否启用 Cron 定时任务服务 | `true` |
| `Heartbeat.Enabled` | 是否启用心跳服务 | `false` |
| `Heartbeat.IntervalSeconds` | 检查间隔（秒） | `1800` |
| `Heartbeat.NotifyAdmin` | 社交渠道下是否将结果通知管理员 | `true` |

`Automations.WorktreeRetentionIdlePeriod` 必须至少为 `14.00:00:00`。Retention sweep 只会移除空闲、干净、且没有领先基础版本提交的受管自动化任务 worktree。

Automation AppServer 方法：

| Method | 说明 |
|---|---|
| `automation/task/list` | 列出本地任务 |
| `automation/task/read` | 读取单个本地任务 |
| `automation/task/create` | 创建本地任务 |
| `automation/task/run` | 立即运行本地任务 |
| `automation/task/updateBinding` | 更新或清除线程绑定 |
| `automation/task/discardWorktree` | 移除任务的受管 worktree 和分支，但保留任务本身 |
| `automation/task/delete` | 删除本地任务 |
| `automation/template/list` | 列出模板 |
| `automation/template/save` | 保存用户模板 |
| `automation/template/delete` | 删除用户模板 |

Goal AppServer 方法：

| Method | 说明 |
|---|---|
| `thread/goal/set` | 设置、替换、暂停或恢复 Thread goal |
| `thread/goal/get` | 读取当前 Thread goal 状态 |
| `thread/goal/clear` | 清除当前 Thread goal |

Hook 快速示例：

```json
{
  "Hooks": {
    "Enabled": true,
    "Events": {
      "AfterToolCall": [
        {
          "name": "log-tool-call",
          "type": "command",
          "command": "node .craft/hooks/log-tool-call.js",
          "matcher": ".*",
          "timeoutSeconds": 10
        }
      ]
    }
  }
}
```

Hook 条目字段：

| 字段 | 说明 |
|---|---|
| `name` | Hook 名称，用于日志和排查 |
| `type` | 支持 `"command"` |
| `command` | 要运行的 Shell 命令 |
| `matcher` | 匹配工具名的正则；为空时匹配所有工具相关事件 |
| `timeoutSeconds` | Hook 超时时间 |

生命周期事件：

| Event | 用途 |
|---|---|
| `BeforeToolCall` | 工具调用前检查或阻塞 |
| `AfterToolCall` | 工具调用后记录、格式化或通知 |
| `BeforeTurn` | Agent turn 前准备上下文 |
| `AfterTurn` | Agent turn 后记录结果或通知 |
| `BeforeAutomationTask` | 自动化任务运行前检查 |
| `AfterAutomationTask` | 自动化任务完成后同步状态 |

工具相关 Hook stdin 通常包含：

```json
{
  "event": "BeforeToolCall",
  "workspace": "F:\\project",
  "sessionId": "thread-id",
  "toolName": "Exec",
  "arguments": {
    "command": "dotnet test"
  }
}
```

Turn 相关 Hook stdin 通常包含：

```json
{
  "event": "AfterTurn",
  "workspace": "F:\\project",
  "sessionId": "thread-id",
  "summary": "Agent completed the turn"
}
```

退出码语义：

| 退出码 | 含义 |
|---|---|
| `0` | 成功，继续执行 |
| 非零 | Hook 失败；`Before*` 事件可以阻塞当前操作 |

Matcher 示例：

| matcher | 匹配 |
|---|---|
| `WriteFile\|EditFile` | 文件写入和编辑 |
| `Exec` | Shell 命令 |
| `.*` | 所有工具 |

## Entry Points and Services

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Acp.Enabled` | 是否启用 ACP 模式 | `false` |
| `DashBoard.Enabled` | 是否启用 Dashboard | `false` |
| `DashBoard.Host` | Dashboard 监听地址 | `127.0.0.1` |
| `DashBoard.Port` | Dashboard 监听端口 | `8080` |
| `Gateway.Enabled` | 是否启用 Gateway Host | `false` |
| `AppServer.Mode` | AppServer transport mode，例如 stdio 或 WebSocket | 空 |
| `AppServer.WebSocket.Host` | WebSocket 监听地址 | `127.0.0.1` |
| `AppServer.WebSocket.Port` | WebSocket 监听端口 | `9100` |
| `AppServer.WebSocket.Token` | 远程 WebSocket 客户端需要使用的 token | 空 |
| `ExternalChannels` | 外部渠道注册表 | `{}` |

Dashboard 示例：

```json
{
  "DashBoard": {
    "Enabled": true,
    "Host": "127.0.0.1",
    "Port": 8080
  }
}
```

外部渠道注册示例：

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
    "qq": {
      "enabled": true,
      "transport": "subprocess",
      "builtinModule": "channel-qq"
    },
    "wecom": {
      "enabled": true,
      "transport": "websocket"
    }
  }
}
```

平台连接、权限白名单和审批超时等渠道专属设置分别放在 `.craft/qq.json`、`.craft/wecom.json` 等适配器配置文件中。

## Plugins MCP 与 LSP

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Plugins.PluginRoots` | `.craft/plugins/` 之外额外维护的 plugin root 目录 | `[]` |
| `McpServers` | MCP 服务配置集合 | `{}` |
| `Tools.DeferredLoading.Strategy` | 工具延迟加载策略：`Off`、`Auto`、`Simulated` 或 `Native` | `Auto` |
| `Tools.DeferredLoading.AlwaysLoadedTools` | 始终预加载的 MCP 工具名列表 | `[]` |
| `Tools.DeferredLoading.DeferThreshold` | MCP 工具数量达到该阈值后才延迟加载 MCP 工具 | `10` |
| `Tools.DeferredLoading.MaxSearchResults` | 每次延迟工具搜索最多返回的结果数 | `5` |
| `LspServers` | LSP 服务配置集合 | `{}` |
| `Tools.Lsp.Enabled` | 是否启用内置 LSP 工具 | `false` |

外部 plugin root 示例：

```json
{
  "Plugins": {
    "PluginRoots": ["./samples/plugins"]
  }
}
```

MCP 示例：

```json
{
  "McpServers": {
    "everything": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-everything"]
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

`Tools.DeferredLoading.Strategy = Auto` 时，`openai-responses` 使用原生 client-executed `tool_search`，chat-completions / Anthropic 使用模拟 `SearchTools`。

## SubAgent 与 External CLI Profiles

入门说明见 [SubAgents](../features/agent-system/subagents)。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `SubAgent.MaxDepth` | session-backed SubAgent 最大生成深度；第一级子代理深度为 `1` | `1` |
| `SubAgent.Model` | DotCraft 原生 SubAgent 使用的模型；空值继承当前线程的有效主模型 | 空 |
| `SubAgent.MinWaitTimeoutMs` | `WaitAgent.timeoutMs` 接受的最小值，单位毫秒 | `15000` |
| `SubAgent.DefaultWaitTimeoutMs` | `WaitAgent` 调用未传 timeout 时使用的默认毫秒数 | `60000` |
| `SubAgent.MaxWaitTimeoutMs` | `WaitAgent.timeoutMs` 接受的最大值，单位毫秒 | `3600000` |
| `SubAgent.EnableExternalCliSessionResume` | 是否允许支持 resume 的 external CLI profile 复用已保存外部会话 | `false` |
| `SubAgent.DisabledProfiles` | 当前工作区隐藏和禁用的 SubAgent profile 名称列表 | `[]` |
| `SubAgent.Roles` | 工作区自定义 SubAgent role；同名条目覆盖内置 role | `[]` |

Role 示例：

```json
{
  "SubAgent": {
    "MaxDepth": 2,
    "Roles": [
      {
        "Name": "docs-explorer",
        "Description": "Read-only documentation and code explorer.",
        "ToolAllowList": ["ReadFile", "GrepFiles", "FindFiles", "WebSearch", "WebFetch", "SkillView"],
        "AgentControlToolAccess": "Disabled",
        "PromptProfile": "subagent-light",
        "Instructions": "Inspect files and web sources only. Do not edit files, execute shell commands, manage skills, or spawn agents."
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
| `ToolAllowList` | 精确工具允许列表；为空表示不额外限制候选工具 |
| `ToolDenyList` | 精确工具拒绝列表，会在工具集合构建完成后移除 |
| `AgentControlToolAccess` | AgentTools 策略：`Disabled` / `Full` / `AllowList` |
| `AllowedAgentControlTools` | `AgentControlToolAccess` 为 `AllowList` 时允许的 AgentTools 名称 |
| `PromptProfile` | 原生 SubAgent 提示词 profile：`subagent-light` / `full` |
| `Instructions` | 追加到 SubAgent prompt 的 role instructions |
| `Mode` | 可选 mode 覆盖 |
| `Model` | 可选 model 覆盖 |
| `OverrideBasePrompt` | 是否用 `Instructions` 覆盖基础 prompt；默认追加而不是覆盖 |

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
| `runtime` | Runtime 类型；外部短进程 CLI 使用 `cli-oneshot` |
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
| `codex-cli` | DotCraft 注入 `exec` 和输出文件参数；恢复时使用 `exec resume {sessionId}` |

## 自定义命令

`CustomCommands` 可把常用提示词或工作流保存为命令。命令内容通常放在工作区 `.craft/commands/` 或对应配置项中，供 CLI、Desktop 或其他入口复用。

## 相关入口

- [项目级工作区](../features/project-first) — 首次配置与 `.craft/` 目录结构
- [AppServer 模式](./lifecycle/appserver) — 结合场景理解 `AppServer.*` / `CLI.*` 字段
- [设置生效层级](./lifecycle/settings-lifecycle) — 字段变更何时生效
