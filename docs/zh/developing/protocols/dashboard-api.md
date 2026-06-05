# DotCraft Dashboard API

Dashboard API 面向调试界面和内部工具。普通用户通常只需要使用 Dashboard 页面；需要写集成或排查前端问题时再查本页。

## 独立只读查看器

可以只启动 Dashboard，不启动 AppServer、Desktop、TUI、channels、Dreams、Automations、MCP 或 LSP：

```bash
dotcraft dashboard --workspace /path/to/workspace
dotcraft dashboard --workspace /path/to/workspace --host 127.0.0.1 --port 8081
```

`--workspace` 可以传工作区根目录，也可以传 `.craft` 目录；不传时使用当前目录。该模式会忽略 `DashBoard.Enabled`，但会复用配置里的 `DashBoard.Host`、`DashBoard.Port`、`Username` 和 `Password`，除非命令行传入 `--host` 或 `--port` 覆盖。

只读模式只暴露 trace、会话列表、token 用量、工具、runtime 元数据和事件流接口。它不会注册 Settings 写入接口、Dreams 接口、Automations 接口或 session/thread 删除接口，并且只读取已有的 `state.db`，不会创建或迁移工作区状态。

## Trace 事件类型

| 类型 | 说明 |
|------|------|
| `SessionMetadata` | 会话系统提示词和工具 schema 元数据 |
| `Request` | 用户请求 |
| `Response` | 模型响应内容段 |
| `ToolCallStarted` | 工具调用开始 |
| `ToolCallCompleted` | 工具调用完成 |
| `ToolInjection` | simulated 延迟加载向下一轮模型请求注入工具 schema |
| `DeferredToolLoading` | Responses native 延迟加载通过 `tool_search` 激活 deferred tools |
| `TokenUsage` | 单次 LLM 请求 token 用量 |
| `Error` | 运行错误 |
| `ContextCompaction` | 上下文压缩 |
| `Thinking` | 模型思考内容段 |
| `PromptCachePoint` | prompt cache 断点摘要 |
| `PromptCacheDiagnostic` | prompt cache 命中/断裂诊断 |
| `PromptCacheRequestShape` | 用于 prompt-cache 前缀诊断的 OpenAI Responses 请求形状哈希 |
| `MaintenanceForkRequest` | 维护型 fork 请求 |
| `MaintenanceForkResponse` | 维护型 fork 响应 |

Dashboard 的 `Thinking` 和 `Response` trace 事件按连续 streaming 内容段记录，而不是按每个 chunk 记录，也不是整轮强制合并为单条。`ThinkingCount` 和 `ResponseCount` 因此表示对应内容段数量。实时事件流会在当前段结束并落库时发送该段事件；历史 trace 不迁移，旧数据可能仍保留旧粒度。

上下文压缩和记忆整理等维护请求会额外记录 `MaintenanceForkRequest` / `MaintenanceForkResponse`。这些事件保留维护请求的 snapshot/cache 元数据、模型原始文本、tool-call-only 响应、空响应和 fallback reason，便于从 Dashboard 诊断 `summary_unavailable` 一类问题。

`DeferredToolLoading` 用于 Responses native 延迟工具加载。它记录本次由 `tool_search` 新激活的工具、配置策略、实际生效模式和 provider protocol；该事件不代表顶层 `tools` 被注入，也不会标记为 prompt-cache tool extension。

`PromptCacheRequestShape` 记录 OpenAI Responses 请求组件的 SHA-256 哈希和计数，用于比较相邻请求的前缀稳定性。

## 端点

### `GET /DashBoard`

返回 Dashboard 页面。

### `GET /DashBoard/api/summary`

返回运行摘要，包括会话数量、最近事件和模块状态。

### `GET /DashBoard/api/sessions`

返回 Dashboard 可见的会话列表。

### `GET /DashBoard/api/sessions/{sessionKey}/events`

返回指定会话的 Trace 事件。

### `GET /dashboard/api/runtime`

返回 Dashboard 宿主模式和能力标记。在独立只读模式下，`mode` 为 `readOnly`，`readOnly` 为 `true`，并且 `settings`、`dreams`、`automations`、`sessionDeletion` 能力均为 `false`。

### `GET /dashboard/api/orchestrators/automations/state`

返回 Automations 编排器状态，包括本地任务和 Cron 摘要。

### `POST /dashboard/api/orchestrators/automations/refresh`

请求刷新 Automations 状态。

### `GET /dashboard/api/config/schema`

返回 Dashboard Settings 页面使用的配置 schema。

### `GET /dashboard/api/dreams/status`

返回当前工作区 Dreams 配置、运行状态、active store 和最近一次运行。

### `GET /dashboard/api/dreams/runs`

返回 Dreams 运行记录。默认不包含 archived 运行。

### `GET /dashboard/api/dreams/runs/{runId}`

返回单次 Dreams 运行详情、active/output index 预览和 topic 路径，供 Dashboard 审阅页使用。

### `POST /dashboard/api/dreams/run`

请求立即运行一次 Dreams。

### `POST /dashboard/api/dreams/runs/{runId}/{action}`

执行 Dreams 审阅动作。`action` 支持 `apply`、`discard`、`archive`、`cancel`。
`apply` 也用于将任意成功且未丢弃/归档的 run 设为 active store。

### `DELETE /api/sessions/{sessionKey}`

删除指定 Dashboard 会话记录。

### `DELETE /api/sessions`

清空 Dashboard 会话记录。

### `GET /api/events/stream`

返回 Dashboard 使用的事件流。

## 使用建议

- API 路径大小写沿用现有 Dashboard 路由。
- 独立只读模式下，被禁用的功能和写入接口会返回 404 或 405，因为这些路由不会被注册。
- 调试本地页面时优先绑定 `127.0.0.1`。
- 生产或共享网络环境中不要暴露未加保护的 Dashboard。
