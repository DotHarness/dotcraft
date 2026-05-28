# 可观测性

DotCraft 的 Dashboard 是 Web 端可视化排查界面，用于查看会话、Trace、工具调用、自动化状态、配置合并结果和审批记录。它适合排查"Agent 做了什么"和"配置为什么这样生效"。

## 快速开始

### 启用

在工作区配置中启用 Dashboard。字段名、默认值和 JSON 示例见 [入口与服务](../developing/configuration.md#entry-points-and-services)。

### 启动

```bash
dotcraft gateway
```

### 打开

默认地址 `http://127.0.0.1:8080/dashboard`。从 CLI、Desktop、TUI 或其他入口发起一次对话后，Dashboard 即可看到会话、工具调用、错误和配置状态。

## 主要页面

| 页面 | 用途 |
|---|---|
| Dashboard | 运行摘要、入口状态、近期活动 |
| Sessions | 会话列表与详情 |
| Trace Timeline | 按时间线检查 Agent、工具和错误事件 |
| Settings | 查看配置 schema、全局配置、工作区配置和合并结果 |
| Automations | 查看本地任务、Cron 和活动状态（需 Gateway） |
| Dreams | 审阅、应用、丢弃后台整理出的 Dream store |
| Approvals | 历史审批记录 |

## 三种典型用法

### 1. 第一次确认模型可用

触发一次会话后，打开 **Trace Timeline**，逐项确认：

- **agent_message_chunk** 是否产生 token 流
- **tool_call** / **tool_result** 是否成功配对
- **error** 是否在某次工具调用上中断

如果 token 流为空，多半是 Provider 凭据 / Endpoint 不匹配；可在 **Settings** 页面的 `Providers[id]` 部分查看合并结果。

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
> 如果某个修改没生效，先在 Settings 页面确认它属于即时 / 子系统重启 / AppServer 重启 中的哪一类。详见 [设置生效层级](../developing/settings-lifecycle.md)。

## 运行模式

| 模式 | 说明 |
|---|---|
| 本地 Dashboard | 单工作区调试 |
| Gateway Dashboard | 与 Automations 和外部渠道共用后台 |

将 `Host` 设为 `0.0.0.0` 会允许外部网络访问 Dashboard，**注意**：Dashboard 可能展示 prompt、工具参数和工具结果。把它暴露到公网前请先确认网络边界与认证策略。

## 审批审计

Dashboard 的 Approvals 页面记录每一次需要审批的工具调用：

- 谁/哪个入口发起的请求
- 决策结果（approve / deny / auto-approve / auto-deny）
- 决策依据（用户、工作区策略、Hook、API AutoApprove）
- 触发的工具与参数

相关安全配置见 [安全与沙箱](./security.md)。

## API 与 Trace 事件

如果你想自己消费 Trace 事件或自定义 dashboard，使用 [Dashboard API](../developing/dashboard-api.md) 列出的 HTTP 端点和事件类型。这些事件也是 AppServer 协议在 Wire Protocol 上推送的同一份数据，区别只是 Dashboard 把它们渲染成 UI。

## 故障排查

### 浏览器打不开 Dashboard

确认配置中已启用 Dashboard，并使用控制台输出的地址（默认 `http://127.0.0.1:8080/dashboard`）。

### Automations 面板不显示

Automations 面板需要 Gateway 加载 Automations 模块。本地 Dashboard 适合调试单工作区，不负责完整自动化编排状态。

### 修改 Settings 后没有变化

模型类字段通常影响新会话；AppServer、端口、Gateway 和外部渠道等启动级字段需要重启 DotCraft。具体规则见 [设置生效层级](../developing/settings-lifecycle.md)。

## 相关入口

- [项目级工作区](./workspace.md)
- [安全与沙箱](./security.md)
- [Dashboard API](../developing/dashboard-api.md)
- [设置生效层级](../developing/settings-lifecycle.md)
