# Dashboard API

Dashboard API 面向调试界面和内部工具。日常排查用 Dashboard 页面就够了，用法见[可观测性](../../self-hosting/observability)。本页写给需要自建集成或调试前端的人。

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
| `WorldStateDiagnostic` | 一次采样中 world state 告诉模型了什么，包括什么都没发的那些采样 |

Dashboard 按连续 streaming 内容段记录 `Thinking` 和 `Response` trace 事件，既不按每个 chunk 记录，也不会把整轮合并为单条。`ThinkingCount` 和 `ResponseCount` 因此表示内容段数量。实时事件流会在当前段结束并落库后发送该段事件。

`ResponseTerminal`、`ProviderError` 和 `ProviderResponseDiagnostic` 都是诊断事件，不会作为 assistant 文本写入 thread rollout。`ResponseTerminal` 会记录 finish reason 和 stream 形状元数据，即使用量-only 或空 terminal update 也会保留证据。Provider 诊断只记录经过清洗的 status、error、incomplete reason 等字段，不得持久化原始 prompt、完整请求体或大型工具参数。

**Responses** 过滤器包含 `Response` 和 `ResponseTerminal`。**Provider** 过滤器包含 `ProviderError` 和 `ProviderResponseDiagnostic`。**Errors** 过滤器包含 `Error` 和 `ProviderError`。**Tokens** 过滤器包含 `TokenUsage`、`PromptCacheDiagnostic` 和 `SessionMetadata`。**Prompt cache** 过滤器（`cache`）包含 `PromptCacheDiagnostic`、`SessionMetadata`、`ToolInjection` 和 `SubAgentPrefixDiagnostic`。

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

上下文压缩等维护请求会额外记录 `MaintenanceForkRequest` / `MaintenanceForkResponse`。这些事件保留维护请求的 snapshot/cache 元数据、模型原始文本、tool-call-only 响应、空响应和 fallback reason，便于从 Dashboard 诊断 `summary_unavailable` 一类问题。

`DeferredToolLoading` 用于 provider-native 延迟工具加载，目前包括 OpenAI Responses 和 Anthropic beta tool references。它记录本次由 `SearchTools` 新激活的工具、配置策略、实际生效模式、provider protocol 和 provider wire shape。该事件不代表顶层 `tools` 被注入，也不会标记为 prompt-cache tool extension。

`PromptCacheRequestShape` 记录 OpenAI Responses 请求组件的 SHA-256 哈希和计数，用于比较相邻请求的前缀稳定性。它还会记录清洗后的有效选项标记，例如请求是否设置 max output tokens、OAuth rewrite 是否会在传输前移除该字段、reasoning effort、tool-choice 类型、工具数量和 streaming 模式。

`SubAgentPrefixDiagnostic` 将 native subagent 的首次 OpenAI Responses 请求与 fork 时捕获的直接父会话请求进行比较。`status` 为 `compatible`、`staticShared`、`diverged` 或 `unavailable`。`compatible` 要求 cache identity 与前置请求组件一致，并至少保留一个父 input item。静态前缀一致但没有保留任何 input item 时为 `staticShared`。之后出现 child 专属 suffix 属于预期行为。Metadata 只包含组件哈希、请求与 attempt 序号、input 数量、匹配的前缀长度、`exactParentInputPrefix`、首个从零开始的分叉位置和 `changedFields`，不包含 prompt 文本、工具 schema 或 input item 内容。Chat Completions 和 Anthropic 会话只暴露父子关系，不推断前缀是否一致。

**World State** 过滤器包含 `WorldStateDiagnostic`。每次采样都会记录一条，包括什么都没发的那次 —— 否则事后无法区分「告诉模型的内容为空」和「这个 section 根本没跑」。`status` 取值为 `full`、`emitted`、`unchanged`，`baselineSource` 取值为 `none`、`memory`、`rollout`、`fork`。其 `metadataJson` 形状如下：

```json
{
  "schemaVersion": 1,
  "status": "unchanged",
  "turnId": "turn_002",
  "baselineSource": "memory",
  "changedSections": [],
  "unchangedSections": ["environment", "mode"],
  "suppressedSections": [{ "id": "acme_review", "reason": "render_failed" }]
}
```

只记录 section id，不记录渲染出的正文。

## 端点

### `GET /dashboard/`

返回 Dashboard 页面。

### `GET /dashboard/api/summary`

返回运行摘要，包括会话数量、最近事件和模块状态。

### `GET /dashboard/api/sessions`

返回 Dashboard 可见的会话列表。子会话包含 `parentSessionKey`、`forkMode` 和 `firstRequest`。

`forkMode` 是 SubAgent 的 `forkTurns` 值（`all`、`none` 或正整数字符串），或缓存共享分叉的用途（`promptSuggestion`、`memoryExtraction` 或 `sideChat`），未记录时为 `null`。只有 `all` 和这些分叉用途会继承父会话的缓存前缀；`none` 和轮数会建立独立的上下文。

`firstRequest` 包含子会话第一次模型请求的 `inputTokens` 和 `cachedInputTokens`，第一次请求完成前为 `null`。

`parentPrefix` 在没有诊断记录时为 `null`。存在诊断时包含 `status`、input 数量、`matchedInputItemCount`、`exactParentInputPrefix`、`expectedSharedPrefix`、cache/static 兼容标记、`divergenceIndex` 和 `changedFields` 摘要。`status` 取值包括：静态前缀一致且子会话 input 以父会话请求的完整 input 开头时为 `compatible`，静态前缀一致但继承的 input 没有完整复制时为 `staticShared`，前导请求组件发生变化时为 `diverged`，缺少父会话形状时为 `unavailable`。父会话关系由返回列表中的 child 记录表达，Dashboard 据此计算显示的 child 数量。

### `GET /dashboard/api/sessions/{sessionKey}/events`

返回指定会话的 Trace 事件。

### `GET /dashboard/api/runtime`

返回 Dashboard 宿主模式、完整 workspace 路径和能力标记。在独立只读模式下，`mode` 为 `readOnly`，`readOnly` 为 `true`，并且 `settings`、`dreams`、`automations`、`sessionDeletion` 能力均为 `false`。

### `GET /dashboard/api/orchestrators/automations/state`

返回统一自动化服务的 `{ automations, countsByStatus, generatedAt }`。统计按定义状态（`active`、`paused`、`completed`）分组。逐次运行结果通过 `automation/runs/list` 查询。

### `POST /dashboard/api/orchestrators/automations/refresh`

请求刷新 Automations 状态。

### `GET /dashboard/api/config/schema`

返回 Dashboard Settings 页面使用的配置 schema。

### `GET /dashboard/api/config/edit`

返回用户层（`global`）、工作区层（`workspace`）和生效配置（`merged`），其中敏感值遮蔽为 `***`，并附带每层的文件路径。

### `POST /dashboard/api/config/workspace`

用提交的 JSON 对象替换工作区配置文件。值为 `***` 的位置会保留已存储的密钥。文档必须能加载为有效配置，每个变更字段都要满足其类型、范围和可选值。通过校验的保存会应用到正在运行的 AppServer，并通过 `config/changed` 广播变更的 key path。无效文档返回 `400` 和英文 `error`，文件保持不变。

### `GET /dashboard/api/dreams/status`

返回工作区的 Dreams 设置（`enabled`、`interval`、`autoApply`、`minSessionsSinceLastRun`）、`running`、`nextRunAt`、最近一次运行 `lastRun`（Dreams 从未运行时为 `null`），以及等待审阅的运行数 `pendingCount`。

一次运行包含 `id`、`scope`（工作区记忆 store 不带此字段，否则是某个 Agent Profile 自有 store 的 scope）、`status`（`running`、`succeeded`、`skipped`、`failed` 或 `canceled`）、`trigger`（`manual` 或 `scheduled`）、时间戳、`changes`（这次运行新增、更新和删除的记忆文件，以及 `MEMORY.md` 是否变化）、`reviewStatus`（`pending`、`applied`、`discarded` 或 `archived`）、`autoApplied`、`summary`、`errorType` 和 `message`，以及内部的 `threadId` 和 `turnId`。完整字段见 [AppServer 协议规范](https://github.com/DotHarness/dotcraft/blob/main/specs/protocols/appserver-protocol.md#271-run-object)。

### `GET /dashboard/api/dreams/runs`

返回 `{ runs }`，包含全部运行，archived 运行也在其中。

### `GET /dashboard/api/dreams/runs/{runId}`

以 `{ run, files, manifest }` 返回单次运行及其审阅材料。`files` 列出这次运行改动的每个文件（包括 `MEMORY.md`），带有 `name`、`change`（`added`、`updated` 或 `deleted`）、运行开始时的 `baseline` 文本和提议的 `candidate` 文本。新增文件没有 `baseline`，删除的文件没有 `candidate`。`manifest` 是这次运行的输入清单 Markdown，没有时为空字符串。run 不存在时返回 `404 Not Found`。

### `POST /dashboard/api/dreams/run`

为 Dreams 整理的每个 store 请求一次手动运行，前提是它有记忆文件，或自上次整理以来有会话使用过它。这些 store 是工作区记忆 store，以及每个声明了 `memory: own` 的 Agent Profile 自有 store。这些运行在后台逐个执行，并跳过 `Dreams.Interval` 和 `Dreams.MinSessionsSinceLastRun`。

返回 `{ run, status }`。`run` 是请求的第一次运行，已有运行在进行时则是那次运行，记忆或 Dreams 关闭时为 `null`。`status` 与 status 接口的返回结构相同。

### `POST /dashboard/api/dreams/runs/{runId}/{action}`

对单次运行执行审阅动作，返回更新后的 `{ run }`。

| `action` | 适用于 | 效果 |
|----------|--------|------|
| `apply` | pending 的运行 | 用这次运行的 candidate 替换 store 的记忆文件和 `MEMORY.md`，前提是 store 仍与运行的 baseline 一致 |
| `restore` | applied 的运行 | 写回这次运行的 baseline，前提是 store 仍与运行的 candidate 一致。运行随之变为 `discarded` |
| `discard` | pending 的运行 | 把运行标记为 `discarded`，store 保持不变 |
| `archive` | 未在运行的运行 | 把运行标记为 `archived`，AppServer 默认列表不再显示它。运行文件、内部线程和 trace 都保留 |
| `cancel` | 正在运行的运行 | 尽力取消这次运行 |

运行开始后 store 有变化时，`apply` 返回 `409 Conflict` 和 `code: "storeChanged"`，并把这个错误记在运行上，运行保持 pending，store 不受影响。store 与运行结果不再一致时，`restore` 同样返回 `409 Conflict`。错误返回 `{ error, code }`。run 不存在时返回 `404 Not Found`，动作未知或运行状态不符时返回 `400 Bad Request`。

### `DELETE /dashboard/api/dreams/runs/{runId}`

永久删除一次运行。run 不存在时返回 `404 Not Found`，run 正在运行时返回 `409 Conflict`。

### `DELETE /dashboard/api/dreams/runs`

永久删除全部运行，包括 archived 运行。只要有运行正在进行，接口在删除任何内容之前先返回 `409 Conflict`。

删除会移除每次运行的目录，并尽力清理它的内部线程和 trace，从不改动任何记忆 store。之后最近一次 Dreams 状态会用剩下最新的运行重建，一个都不剩时清空。两个接口都返回：

```json
{
  "deletedRunIds": ["dream_20261013000000_abc123"],
  "deletedCount": 1,
  "partial": false,
  "traceCleanupFailures": []
}
```

线程或 trace 清理失败时，请求仍然成功，`partial` 为 `true`，`traceCleanupFailures` 的每一项都带有 `runId`、`threadId` 和 `error`。

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

- [AppServer 协议](./appserver)——产生这些 trace 的 JSON-RPC 接口。
- [Hub 协议](./hub)——本地启动时 Dashboard 地址随 AppServer 一起返回的位置。
