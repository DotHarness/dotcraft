# 可观测性

DotCraft 的 Dashboard 是一个网页，让你看清正在发生什么——会话、Trace、工具调用、自动化状态、配置合并结果和审批记录。想弄明白"Agent 到底做了什么""配置为什么这样生效"，就到这儿查。

## 快速开始

### 启用

在工作区配置中启用 Dashboard。字段名、默认值和 JSON 示例见 [入口与服务](../../developing/configuration#entry-points-and-services)。

### 启动

```bash
dotcraft gateway
```

### 打开

默认地址 `http://127.0.0.1:8080/dashboard`。从 CLI、Desktop 或其他入口发起一次对话后，Dashboard 即可看到会话、工具调用、错误和配置状态。

## 主要页面

| 页面 | 用途 |
|---|---|
| Dashboard | 运行摘要、入口状态、近期活动 |
| Sessions | 会话列表与详情 |
| Trace Timeline | 按时间线检查 Agent、工具和错误事件 |
| Settings | 查看配置 schema、全局配置、工作区配置和合并结果 |
| Automations | 查看本地任务、Cron 和活动状态（需 Gateway） |
| Dreams | 审阅、应用、丢弃后台生成的 Dreams |
| Approvals | 历史审批记录 |

## 三种典型用法

### 1. 第一次确认模型可用

触发一次会话后，打开 **Trace Timeline**，逐项确认：

- **agent_message_chunk** 是否产生 token 流
- **tool_call** / **tool_result** 是否成功配对
- **error** 是否在某次工具调用上中断

如果 token 流为空，多半是 Provider 凭据 / Endpoint 不匹配；可在 **Settings** 页面的 `Providers[id]` 部分查看合并结果。

Provider 侧终止诊断会和可见响应文本分开记录。排查空文本、provider 错误内容或 provider incomplete / length-limit 元数据时，优先查看 `ResponseTerminal`、`ProviderError` 和 `ProviderResponseDiagnostic` 事件。

使用 **Provider** 过滤器检查重试行为。`stream_attempt` 诊断会显示 attempt 编号、结果、
重试决策、耗时，以及是否因为已产生可见输出而停止重试。对于 OpenAI Responses，它还会
显示最终 HTTP status、上游 request ID，以及缩写后的 session、thread 和 prompt-cache
哈希。对比不同 attempt 的哈希即可确认路由是否稳定，同时不会暴露原始 identity。

### 2. 排查工具调用失败

打开会话详情：

- 切换到 **Tools** / **Errors** 过滤器
- 点击具体 tool call 看完整参数、结果、耗时、stderr
- 如果是审批失败，跳到 **Approvals** 页确认是否被自动拒绝

### 3. 检查配置为什么这样生效

**Settings** 页面会把全局 `~/.craft/config.json` 与工作区 `.craft/config.json` 的差异并排展示：

- 字段在哪一层定义
- 合并后哪个值生效
- 哪些字段属于启动级（修改后需要重启）

> [!TIP]
> 如果某个修改没生效，先在 Settings 页面确认它属于即时 / 子系统重启 / AppServer 重启 中的哪一类。详见 [设置生效层级](../../developing/lifecycle/settings-lifecycle)。

## 运行模式

| 模式 | 说明 |
|---|---|
| 本地 Dashboard | 单工作区调试 |
| Gateway Dashboard | 与 Automations 和外部渠道共用后台 |

将 `Host` 设为 `0.0.0.0` 会允许外部网络访问 Dashboard。

> [!CAUTION]
> Dashboard 可能展示 prompt、工具参数和工具结果。把它暴露到公网前，请先确认网络边界与认证策略。

## 审批审计

Dashboard 的 Approvals 页面记录每一次需要审批的工具调用：

- 谁/哪个入口发起的请求
- 决策结果（approve / deny / auto-approve / auto-deny）
- 决策依据（用户、工作区策略、Hook、API AutoApprove）
- 触发的工具与参数

相关安全配置见 [安全与沙箱](./security)。

## API 与 Trace 事件

如果你想自己消费 Trace 事件或自定义 dashboard，使用 [Dashboard API](../../developing/protocols/dashboard-api) 列出的 HTTP 端点和事件类型。这些事件也是 AppServer 协议在 Wire Protocol 上推送的同一份数据，区别只是 Dashboard 把它们渲染成 UI。

## 相关文档

- [项目工作区](../project-workspace)
- [安全与沙箱](./security)
- [Dashboard API](../../developing/protocols/dashboard-api)
- [设置生效层级](../../developing/lifecycle/settings-lifecycle)
