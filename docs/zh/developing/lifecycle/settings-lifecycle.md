# DotCraft 设置生效层级指南

本页面向集成方与贡献者。它说明 Desktop 设置中的三层生效模型，以及如何判断配置是已生效还是待生效。

## 1. 三层生效模型

Desktop 将设置项按生效方式划分为三类：

1. **即时生效（Tier A / Live Apply）**
   - 保存后立即生效。
   - 典型示例：`Skills.DisabledSkills`、MCP 配置项。
2. **子系统重启（Tier B / Subsystem Restart）**
   - 配置已写入，但需要重启对应子系统后生效。
   - 典型示例：受外部通道子系统生命周期影响的配置。
3. **AppServer 重启（Tier C / Process Restart）**
   - Local 模式下配置已写入，但需要重启 Hub 托管的本地 AppServer 进程后生效。
   - 典型示例：启动级 Core 配置、本地 AppServer 二进制路径与部分入口配置。

你可以通过设置分组中的动作按钮识别层级：即时应用、重启、或“应用并重启”。

## 2. 代表字段与生效方式

| 配置区域 | 代表字段 | 生效方式 |
|---|---|---|
| Skills / MCP | `Skills.DisabledSkills`、MCP 服务器定义 | 即时生效 |
| External Channel | 外部通道相关配置 | 子系统重启 |
| Connection / Local AppServer | `connectionMode = local`、本地 AppServer 二进制路径、本地 WebSocket 监听配置 | Hub 托管的本地 AppServer 可 Apply & Restart |
| Connection / Remote AppServer | `connectionMode = remote`、远程 WebSocket URL、token | 先用草稿 URL/token 完成 WebSocket initialize 探测，成功后保存并切换；不重启远端 AppServer |
| Model Providers | `Providers[id]`、`ProviderId`、`ProviderPreferences`、`SubAgent.ProviderPreferences` | Desktop / AppServer 通过 Provider 管理接口即时刷新新会话默认值 |

说明：

- Desktop 的模型设置页管理 Provider 注册表；凭证与端点只属于 `Providers[id]`。
- 修改工作区 `ProviderId` 或 `ProviderPreferences` 只会刷新新线程默认值；已有线程保留创建时的模型、思考程度、速率和上下文窗口快照，除非该线程自己的 composer 原子更新完整偏好。
- 工作区文件保存 `ProviderId` 和按 provider 区分的完整偏好覆盖；Provider 凭证保留在个人 `Providers[id]` 注册表中。
- Remote AppServer 的生命周期由用户或远端环境管理。Desktop 只测试连接并切换，不提供 remote restart。
- 如果 Desktop 通过 `--remote` 启动，本次会话的连接由启动参数控制，Settings 中的持久化连接切换不可用。

## 3. 如何判断“已生效”与“待生效”

可以从以下信号判断状态：

- **已生效**：配置已写入且对应层级动作已完成（即时应用成功，或重启完成）。
- **待生效**：出现需要重启的提示，表示配置已落盘但运行态尚未切换。
- **Remote 连接待应用**：远端 URL/token 变更尚未写入默认连接；点击“应用并连接”后，Desktop 先探测草稿连接，成功才保存。探测失败时不保存，避免下次启动被坏配置困住。
- **按分组脏状态**：仅变更了某一分组时，只需要处理该分组对应动作，不必全局保存。
- **已有坏 Remote 配置**：如果启动时发现已保存的 Remote 连接无效，错误页会提供「打开连接设置」，进入 Settings > Connection 并解除阻塞覆盖层，让你修正 URL/token 或切回 Local。

## 相关入口

- [AppServer 协议](../protocols/appserver-protocol) — `workspace/configChanged` 客户端事件
- [配置参考](../configuration) — 这些层级涉及的全部字段
- [AppServer 模式](./appserver) — 远程 / 多客户端连接
