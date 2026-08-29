# Dashboard API

Dashboard API 面向调试界面和内部工具。日常排查用 Dashboard 页面就够了，用法见[可观测性](../../features/self-hosted/observability)。本页写给需要自建集成或调试前端的人。

所有路由都挂在 `/dashboard/` 下，页面本身是 `GET /dashboard/`。

## 独立只读查看器

可以只启动 Dashboard，不启动 AppServer、Desktop、channels、Dreams、Automations、MCP 或 LSP：

```bash
dotcraft dashboard --workspace /path/to/workspace
dotcraft dashboard --workspace /path/to/workspace --host 127.0.0.1 --port 8081
```

`--workspace` 可以传工作区根目录，也可以传 `.craft` 目录。不传时使用当前目录。该模式会忽略 `DashBoard.Enabled`，但会复用配置里的 `DashBoard.Host`、`DashBoard.Port`、`Username` 和 `Password`，除非命令行传入 `--host` 或 `--port` 覆盖。

只读模式只暴露 trace、会话列表、token 用量、工具、runtime 元数据和事件流接口。它不会注册 Settings 写入接口、Dreams 接口、Automations 接口或 session/thread 删除接口，并且只读取已有的 `state.db`，不会创建或迁移工作区状态。如果 `.craft/state.db` 不存在，命令会报错退出。

## Trace 事件类型

| 类型 | 说明 |
|------|------|
| `SessionMetadata` | 会话系统提示词和工具 schema 元数据 |
| `AgentInstructions` | 会话实际选用的 `AGENTS.md` 指令快照 |
| `Request` | 用户请求 |
| `Response` | 模型响应内容段 |
| `ToolCallStarted` | 工具调用开始 |
| `ToolCallCompleted` | 工具调用完成 |
| `ToolInjection` | simulated 延迟加载向下一轮模型请求注入工具 schema |
| `DeferredToolLoading` | provider-native 延迟加载通过 `SearchTools` 激活 deferred tools |
| `TokenUsage` | 单次 LLM 请求 token 用量 |
| `Error` | 运行错误 |
| `ResponseTerminal` | 单次 streaming 模型请求的终止诊断，即使没有文本也会记录 |
| `ProviderError` | provider 返回的非致命错误内容或 stream 错误元数据 |
| `ProviderResponseDiagnostic` | 经过清洗的 provider 终止/status 元数据、stream attempt 结果和 OpenAI request ID |
| `ContextCompaction` | 上下文压缩 |
| `Thinking` | 模型思考内容段 |
| `PromptCachePoint` | prompt cache 断点摘要 |
| `PromptCacheDiagnostic` | prompt cache 命中/断裂诊断 |
| `PromptCacheRequestShape` | 用于 prompt-cache 前缀诊断的 OpenAI Responses 请求形状哈希 |
| `SubAgentPrefixDiagnostic` | native subagent 首次 Responses 请求与直接父会话 fork anchor 的一次性比较 |
| `MaintenanceForkRequest` | 维护型 fork 请求 |
| `MaintenanceForkResponse` | 维护型 fork 响应 |

Dashboard 按连续 streaming 内容段记录 `Thinking` 和 `Response` trace 事件，既不按每个 chunk 记录，也不会把整轮合并为单条。`ThinkingCount` 和 `ResponseCount` 因此表示内容段数量。实时事件流会在当前段结束并落库后发送该段事件。

`ResponseTerminal`、`ProviderError` 和 `ProviderResponseDiagnostic` 都是诊断事件，不会作为 assistant 文本写入 thread rollout。`ResponseTerminal` 会记录 finish reason 和 stream 形状元数据，即使用量-only 或空 terminal update 也会保留证据。Provider 诊断只记录经过清洗的 status、error、incomplete reason 等字段，不得持久化原始 prompt、完整请求体或大型工具参数。

**Responses** 过滤器包含 `Response` 和 `ResponseTerminal`。**Provider** 过滤器包含 `ProviderError` 和 `ProviderResponseDiagnostic`。

**Instructions** 过滤器包含 `AgentInstructions`。其 `content` 字段保存完整渲染后的指令文本，未加载指令时为空字符串。`metadataJson` 结构如下：

```json
{
  "schemaVersion": 1,
  "kind": "agents_md.instructions",
  "role": "user",
  "fingerprint": "sha256:...",
  "sources": ["/path/to/AGENTS.md"]
}
```

fingerprint 同时覆盖内容和有序来源。等价快照会去重。该诊断既不是 system prompt、普通 request，也不是 model-history item。在线模式和独立只读模式的 Dashboard 都从持久化 Trace 读取快照，不会重新加载指令文件。

每个完成的 provider stream attempt 都会产生一条 `eventType=stream_attempt` 的 `ProviderResponseDiagnostic`。其 metadata 包含 `requestIndex`、`attemptNumber`、`retryLimit`、`outcome`、`retryDecision`、`failureKind`、`durationMs` 和 `visibleOutputEmitted`。OpenAI Responses 诊断还会包含最终 HTTP status、上游 request ID，以及实际 session、thread 和 prompt-cache identity 的 SHA-256 哈希。原始路由 identity、凭据、请求体和响应体不会写入 trace。

上下文压缩和记忆整理等维护请求会额外记录 `MaintenanceForkRequest` / `MaintenanceForkResponse`。这些事件保留维护请求的 snapshot/cache 元数据、模型原始文本、tool-call-only 响应、空响应和 fallback reason，便于从 Dashboard 诊断 `summary_unavailable` 一类问题。

`DeferredToolLoading` 用于 provider-native 延迟工具加载，目前包括 OpenAI Responses 和 Anthropic beta tool references。它记录本次由 `SearchTools` 新激活的工具、配置策略、实际生效模式、provider protocol 和 provider wire shape。该事件不代表顶层 `tools` 被注入，也不会标记为 prompt-cache tool extension。

`PromptCacheRequestShape` 记录 OpenAI Responses 请求组件的 SHA-256 哈希和计数，用于比较相邻请求的前缀稳定性。它还会记录清洗后的有效选项标记，例如请求是否设置 max output tokens、OAuth rewrite 是否会在传输前移除该字段、reasoning effort、tool-choice 类型、工具数量和 streaming 模式。

`SubAgentPrefixDiagnostic` 将 native subagent 的首次 OpenAI Responses 请求与 fork 时捕获的直接父会话请求进行比较。`status` 为 `compatible`、`staticShared`、`diverged` 或 `unavailable`。`compatible` 要求 cache identity 与前置请求组件一致，并至少保留一个父 input item。静态前缀一致但没有保留任何 input item 时为 `staticShared`。之后出现 child 专属 suffix 属于预期行为。Metadata 只包含组件哈希、请求与 attempt 序号、input 数量、匹配的前缀长度、`exactParentInputPrefix`、首个从零开始的分叉位置和 `changedFields`，不包含 prompt 文本、工具 schema 或 input item 内容。Chat Completions 和 Anthropic 会话只暴露父子关系，不推断前缀是否一致。

## 端点

### `GET /dashboard/`

返回 Dashboard 页面。

### `GET /dashboard/api/summary`

返回运行摘要，包括会话数量、最近事件和模块状态。

### `GET /dashboard/api/sessions`

返回 Dashboard 可见的会话列表。子会话包含 `parentSessionKey`。`parentPrefix` 在没有诊断记录时为 `null`。存在诊断时包含 `status`、input 数量、`matchedInputItemCount`、`exactParentInputPrefix`、`expectedSharedPrefix`、cache/static 兼容标记、`divergenceIndex` 和 `changedFields` 摘要。`status` 取值包括：静态前缀一致且保留了有序 input 前缀时为 `compatible`，静态前缀一致但没有保留任何 input 项时为 `staticShared`，前导请求组件发生变化时为 `diverged`，缺少父会话形状时为 `unavailable`。`expectedSharedPrefix` 只有在子会话继承了父会话轮次时为 true，因此全新启动的子会话出现 `staticShared` 不是缺陷。父会话关系由返回列表中的 child 记录表达，Dashboard 据此计算显示的 child 数量。

### `GET /dashboard/api/sessions/{sessionKey}/events`

返回指定会话的 Trace 事件。

### `GET /dashboard/api/runtime`

返回 Dashboard 宿主模式、完整 workspace 路径和能力标记。在独立只读模式下，`mode` 为 `readOnly`，`readOnly` 为 `true`，并且 `settings`、`dreams`、`automations`、`sessionDeletion` 能力均为 `false`。

### `GET /dashboard/api/orchestrators/automations/state`

返回 Automations 编排器状态，包括本地任务和 Cron 摘要。

### `POST /dashboard/api/orchestrators/automations/refresh`

请求刷新 Automations 状态。

### `GET /dashboard/api/config/schema`

返回 Dashboard Settings 页面使用的配置 schema。

### `GET /dashboard/api/dreams/status`

返回当前工作区 Dreams 配置、运行状态、active store 和最近一次运行。

### `GET /dashboard/api/dreams/runs`

返回 Dreams 运行记录。默认不包含 archived 运行，传 `?includeArchived=true` 才一并返回。

### `GET /dashboard/api/dreams/runs/{runId}`

返回单次 Dreams 运行详情、active/output index 预览和 topic 路径，供 Dashboard 审阅页使用。

### `POST /dashboard/api/dreams/run`

请求立即运行一次 Dreams。

### `POST /dashboard/api/dreams/runs/{runId}/{action}`

执行 Dreams 审阅动作。`action` 支持 `apply`、`discard`、`archive`、`cancel`。
`apply` 也用于将任意成功且未丢弃、未归档的 run 设为 active store。
`archive` 保留 run 目录、输入快照、输出 store、内部线程和 trace。Desktop 的 Archive 与 Archive all 都调用这个动作，Archive all 会对每个符合条件的 run 各发一次请求。

### `DELETE /dashboard/api/dreams/runs/{runId}`

永久删除一次未在运行的 Dreams run。删除会移除 run 目录和输入快照，移除它的输出 store（active store 除外），并清理关联的内部线程与 trace。即使被删除的 run 正是 active store 的产出者，active store 也会保留。

run 不存在时返回 `404 Not Found`。run 正在运行时返回 `409 Conflict`，且不删除任何内容。

### `DELETE /dashboard/api/dreams/runs`

永久删除全部 Dreams run，包含 archived run，清理规则与单次删除一致。只要有任何一个 run 正在运行，接口在删除任何内容之前先返回 `409 Conflict`。

两个接口成功后，最近一次 Dreams 状态会用剩下最新的 run 重建，一个 run 都不剩时清空。

单次删除成功返回：

```json
{
  "deleted": true,
  "runId": "dream_20260511000000_abc123",
  "outputStoreDeleted": true,
  "activeStorePreserved": false,
  "traceDeleted": true,
  "partial": false,
  "cleanupWarnings": []
}
```

run 目录、输入快照和可删除的输出 store 是权威结果，内部线程与 trace 清理是 best effort。Dreams 文件已删除但这部分清理失败时，接口仍返回成功，同时把 `partial` 置为 `true`，并在 `cleanupWarnings` 中列出每条失败原因。

全量删除返回 `deletedCount` 和 `traceDeletedCount`，其余字段与单次删除一致。

### `DELETE /dashboard/api/sessions/{sessionKey}`

删除指定 Dashboard 会话记录。

### `DELETE /dashboard/api/sessions`

清空 Dashboard 会话记录。

### `GET /dashboard/api/events/stream`

返回 Dashboard 使用的事件流。

## 使用建议

- 独立只读模式下，被禁用的功能和写入接口会返回 404 或 405，因为这些路由不会被注册。
- 调试本地页面时优先绑定 `127.0.0.1`。
- 生产或共享网络环境中不要暴露未加保护的 Dashboard。

## 相关文档

- [AppServer 协议](./appserver-protocol)——产生这些 trace 的 JSON-RPC 接口。
- [Hub 协议](./hub-protocol)——本地启动时 Dashboard 地址随 AppServer 一起返回的位置。
