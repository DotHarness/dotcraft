# DotCraft 完整配置参考

本页集中列出配置字段、默认值、JSON 示例和高级参考。第一次配置请先读[项目工作区](../features/project-workspace)。想理解功能本身时先读对应 Feature 页面，需要准确字段时再回到这里。

DotCraft 先读取全局 `~/.craft/config.json`，再叠加工作区 `.craft/config.json`，工作区字段优先生效。配置字符串支持 `$VAR` 和 `${VAR}` 环境变量占位；变量不存在时保留原始占位符。

## 基础模型与 Provider

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `ProviderId` | 当前选择的个人 Provider id；为空表示未选择 Provider | 空 |
| `ProviderPreferences` | 按 provider id 保存的完整 MainAgent 偏好；当前 provider 必须存在有效条目 | `{}` |
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

不同配置层级中的 `ProviderPreferences` 记录按整条覆盖。全局和工作区同时配置同一 provider id 时，工作区记录会完整替换全局记录。

| 偏好字段 | 可选值 | 说明 |
|----------|--------|------|
| **`Model`** | 非空模型 id | 新 MainAgent 线程使用的模型 |
| **`Reasoning.Enabled`** | `true`、`false` | 模型支持时启用或关闭 reasoning |
| **`Reasoning.Effort`** | `Low`、`Medium`、`High`、`ExtraHigh` | 请求的思考程度 |
| **`Reasoning.Output`** | `None`、`Summary`、`Full` | 请求的 reasoning 输出 |
| **`Speed`** | `Standard`、`Fast` | 请求的推理速率；不支持 Fast 时按 Standard 执行 |
| **`ContextWindow.Mode`** | `Default`、`Max` | 请求的上下文窗口模式；不支持 Max 时恢复 Default |

Provider 对象字段：

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `DisplayName` | 面向用户显示的 Provider 名称；为空时使用 provider id | 空 |
| `Protocol` | Provider 协议：`anthropic`、`openai-chat-completions` 或 `openai-responses`；空值默认使用 `openai-chat-completions` | `openai-chat-completions` |
| `ApiKey` | Provider API Key；建议使用 `${ENV_NAME}` 引用环境变量 | 空 |
| `EndPoint` | Provider base URL；为空时使用协议默认地址 | OpenAI 协议：`https://api.openai.com/v1`；`anthropic`: `https://api.anthropic.com` |
| `NetworkTimeoutSeconds` | 单个 Provider 请求超时时间，覆盖全局 `NetworkTimeoutSeconds` | 空 |
| `StreamMaxRetries` | 单个 Provider 的流式响应断线重连次数；设为 `0` 可关闭 stream retry | `5` |
| `StreamIdleTimeoutMs` | 单个 Provider 的流式响应空闲超时时间，单位毫秒 | `300000` |
| `SupportsHostedImageGeneration` | 是否为该提供商启用 hosted image generation。省略时，ChatGPT OAuth 和官方 OpenAI Responses API-key endpoint 默认按 `true` 处理；OpenAI-compatible 自定义 Responses endpoint 默认按 `false` 处理。 | 提供商默认值 |

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
| `Compaction.MicrocompactKeepRecent` | 微压缩时保留的最近工具结果数 | `8` |
| `Compaction.MicrocompactGapMinutes` | 距离上次助理消息超过该分钟数也触发微压缩；`0` 表示禁用 | `20` |
| `Compaction.MaxConsecutiveFailures` | 连续失败次数达到该值时熔断 | `3` |

### 模型能力目录

DotCraft 内置一份模型能力目录，用于配置上下文窗口和 Fast Mode 支持。使用以下文件补充或覆盖：

- 全局：`~/.craft/models.json`
- 工作区：`.craft/models.json`

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

workspace 条目覆盖全局条目，全局条目覆盖内置目录。同一模型规则的字段会独立合并。将
`fast` 设为 `null` 可禁用继承的 Fast 能力。模型规则使用不区分大小写的最长前缀匹配，
也会匹配 `provider/custom-fast-model` 这样的命名空间后缀。

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

内置 catalog 会为未列入的 Anthropic 协议模型开放完整思考选项，但不会假设它们支持 Anthropic `adaptive` 请求形状。只有明确支持该形状的模型或 endpoint，才应添加 `anthropicThinking` 条目。

对 Anthropic-compatible provider，`anthropicMessageContent` 可以声明 DotCraft 推理历史应如何表示。内置 DeepSeek Anthropic adapter 会在发送历史前，把历史 `TextReasoningContent` 映射为 Anthropic-compatible `thinking` block；它不是通用的 unsupported-block 过滤器。

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
| `Tools.Shell.Background.Enabled` | 是否启用后台终端会话 | `true` |
| `Tools.Shell.Background.DefaultYieldTimeMs` | 运行中命令返回后台会话快照前的默认等待时间 | `1000` |
| `Tools.Shell.Background.MaxYieldTimeMs` | 后台会话读取或写入可接受的最长等待时间 | `30000` |
| `Tools.Shell.Background.MaxSessionsPerThread` | 每个线程可同时运行的后台终端上限 | `8` |
| `Tools.Shell.Background.MaxSessionsPerWorkspace` | 每个工作区可同时运行的后台终端上限 | `32` |
| `Tools.Shell.Background.IdleTimeoutSeconds` | 预留字段；后台终端服务当前不执行该限制 | `1800` |
| `Tools.Shell.Background.OutputMaxBytes` | 预留字段；后台终端服务当前不执行该限制 | `67108864` |
| `Tools.Shell.Background.OutputRetentionDays` | 已完成或丢失终端的元数据与输出保留天数；不清理正在运行的终端 | `7` |
| `Tools.Shell.Background.StallWatchdogSeconds` | 预留字段；后台终端服务当前不执行该限制 | `45` |
| `Tools.Shell.Background.DefaultReadMaxOutputChars` | 终端快照默认返回的最大字符数 | `10000` |
| `Tools.Web.MaxChars` | Web 抓取最大字符数 | `50000` |
| `Tools.Web.Timeout` | Web 请求超时时间（秒） | `300` |
| `Tools.Web.SearchMaxResults` | 联网搜索默认返回结果数 | `5` |
| `Tools.Web.SearchProvider` | 搜索引擎提供商：`Bing` / `Exa` | `Exa` |
| `Tools.Lsp.Enabled` | 是否启用内置 LSP 工具 | `false` |
| `Tools.Lsp.MaxFileSize` | LSP 打开或同步文件时允许的最大文件大小 | `10485760` |
| `Tools.ImageGeneration.Enabled` | 允许支持的 OpenAI Responses 提供商在对话中生成图片 | `true` |
| `Tools.ImageGeneration.Model` | 预留给图片客户端集成；对话生图使用当前 Responses 模型 | `gpt-image-2` |
| `Tools.ImageGeneration.MaxReferenceImages` | 预留给支持参考图的图片客户端集成 | `5` |
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

使用支持的 OpenAI Responses 提供商时，你可以在普通对话里直接让 DotCraft 生成图片。DotCraft 会向提供商请求 PNG 输出，并在支持富内容的客户端中以内联图片展示。

`Tools.ImageGeneration.Enabled` 是图片生成全局开关，默认值为 `true`。提供商也必须将 `SupportsHostedImageGeneration` 设为 `true`，DotCraft 才会注入 hosted `image_generation` tool。省略该字段时，ChatGPT OAuth 和官方 OpenAI Responses API-key endpoint 默认按 `true` 处理；OpenAI-compatible 自定义 Responses endpoint 默认按 `false` 处理，只有确认支持 hosted tool 时再开启。

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
| `Hooks.State` | 按稳定 hook key 保存的用户态配置。Desktop toggle/trust 会写入 `Enabled` 和 `TrustedHash` | `{}` |
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

Hook 命令放在 `hooks.json`，不是 `config.json`。DotCraft 会从 `~/.craft/hooks.json` 加载全局 hooks，从 `.craft/hooks.json` 加载工作区 hooks，并从已启用插件的 hook 文件加载 plugin hooks。概念说明和 Desktop 操作入口见 [生命周期 Hooks](../features/agent-system/hooks)。

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

Hook matcher group 字段：

| 字段 | 说明 |
|---|---|
| `matcher` | 匹配工具名的正则；为空时匹配所有工具相关事件 |
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
| `SubagentStart` / `SubagentStop` | 子 Agent 生命周期前后运行 |
| `Stop` | assistant 响应后运行，并可入队后续反馈 |
| `StopFailure` | Stop hook 处理失败后运行 |

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
| 其他非零 | Hook 失败；DotCraft 记录失败并按该事件的运行时策略继续 |

Matcher 示例：

| matcher | 匹配 |
|---|---|
| `WriteFile\|EditFile` | 文件写入和编辑 |
| `Exec` | Shell 命令 |
| `.*` | 所有工具 |

Desktop 会把每个 hook 的用户态写入 `~/.craft/config.json`：

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

`Enabled: false` 可以在不编辑来源文件的情况下停用单个 hook。`TrustedHash` 记录上次信任的规范化 hook 定义。来自 config 和 plugins 的 hooks 必须被信任后才会运行；修改后的 hooks 需要重新信任。Desktop 通常把插件 hooks 作为一个插件能力包整体信任，但保存的状态仍然是每条 hook 一条。

Plugin hook 文件使用同样的 `hooks.json` 结构。在 plugin hook 命令中，DotCraft 会展开 `${DOTCRAFT_PLUGIN_ROOT}` 和 `${DOTCRAFT_PLUGIN_DATA}`，并注入同名环境变量。

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

Desktop 托管的内置 TypeScript 渠道：

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

独立适配器：

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

平台连接、权限白名单和审批超时等渠道专属设置分别放在 `.craft/qq.json`、`.craft/wecom.json` 等适配器配置文件中。TypeScript 渠道示例见 [渠道配置参考](./channels/reference)。

## Plugins MCP 与 LSP

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Plugins.PluginRoots` | `.craft/plugins/` 之外额外维护的 plugin root 目录 | `[]` |
| `Plugins.PluginRegistries` | 用于发现插件目录的 plugin marketplace 来源 | `[]` |
| `Plugins.DisableDefaultPluginRegistry` | 忽略宿主提供的默认官方 plugin registry | `false` |
| `McpServers` | MCP 服务配置集合 | `{}` |
| `Tools.DeferredLoading.Strategy` | 工具延迟加载策略：`Off`、`Auto`、`Simulated` 或 `Native` | `Auto` |
| `Tools.DeferredLoading.AlwaysLoadedTools` | 始终预加载的 MCP 工具名列表 | `[]` |
| `Tools.DeferredLoading.DeferThreshold` | MCP 工具数量达到该阈值后才延迟加载 MCP 工具 | `10` |
| `Tools.DeferredLoading.MaxSearchResults` | 每次延迟工具搜索最多返回的结果数 | `5` |
| `LspServers` | LSP 服务配置集合 | `{}` |
| `Tools.Lsp.Enabled` | 是否启用内置 LSP 工具 | `false` |

DotCraft Desktop 会通过 `DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL` 提供默认官方插件市场。在 Desktop 中添加的市场来源保存在全局配置中；工作区中的 `PluginRegistries` 值遵循普通的工作区覆盖全局规则。

每个 `McpServers` 和 `LspServers` 条目只接受当前 schema 定义的字段。出现未知属性时，配置解析会失败。

`Plugins.PluginRegistries` 条目字段：

| 字段 | 说明 | 默认值 |
|---|---|---|
| `Name` | 市场标识。手动配置时必填且必须与市场文档一致；通过 Desktop 或 AppServer 添加时自动维护 | 空 |
| `SourceType` | 来源类型：`git`、`local` 或 `archive` | 省略时自动推断 |
| `Url` | Git URL、本地目录、归档 URL 或归档文件 | 空 |
| `Ref` | 要检出的 Git 分支、标签或 commit | 来源默认值 |
| `SparsePaths` | Git checkout 中包含的仓库内相对路径 | `[]` |
| `MarketplacePath` | 来源根目录内的市场文档路径 | `.craft/plugins/marketplace.json` |
| `LastUpdated` | 最近一次成功添加或刷新的 UTC 时间 | 空 |
| `LastRevision` | 最近一次成功获取的 Git revision | 空 |

省略 `SourceType` 时，已存在的目录或归档文件按本地来源读取，其他值按归档 URL 处理。`Ref` 和 `SparsePaths` 只适用于 Git 来源。

来源格式、市场文档和生命周期见[插件市场](./integrations/plugin-market)。

本地插件开发覆盖示例：

```json
{
  "Plugins": {
    "PluginRoots": ["/path/to/local/plugins"]
  }
}
```

MCP 示例：

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

`Tools.DeferredLoading.Strategy = Auto` 时，`openai-responses` 和 Anthropic 使用原生 client-executed `tool_search`，chat-completions 使用模拟 `SearchTools`。

## SubAgent 与 External CLI Profiles

入门说明见 [SubAgents](../features/agent-system/subagents)。

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `SubAgent.MaxDepth` | session-backed SubAgent 最大生成深度；第一级子代理深度为 `1` | `1` |
| `SubAgent.ProviderPreferences` | 按父线程 provider 保存的完整原生 SubAgent 偏好；缺少对应项时继承该线程完整的 MainAgent 偏好 | `{}` |
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
| `ToolAllowList` | 精确工具允许列表；为空表示不额外限制候选工具 |
| `ToolDenyList` | 精确工具拒绝列表，会在工具集合构建完成后移除 |
| `ShellAccess` | 可达的 Shell 工具能走多远：`None` / `ReadOnly` / `Full`。与允许/拒绝列表叠加生效，而非取代。默认 `Full` |
| `AgentControlToolAccess` | AgentTools 策略：`Disabled` / `Full` / `AllowList` |
| `AllowedAgentControlTools` | `AgentControlToolAccess` 为 `AllowList` 时允许的 AgentTools 名称 |
| `Instructions` | 作为 SubAgent 线程角色上下文消息送达的 role instructions |
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

- [项目工作区](../features/project-workspace) — 首次配置与 `.craft/` 目录结构
- [AppServer 模式](./lifecycle/appserver) — 结合场景理解 `AppServer.*` / `CLI.*` 字段
- [设置生效层级](./lifecycle/settings-lifecycle) — 字段变更何时生效
