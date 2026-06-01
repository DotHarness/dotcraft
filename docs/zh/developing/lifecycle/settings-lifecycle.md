# DotCraft 设置生效层级指南

本文档说明 Desktop 设置中的三层生效模型，以及如何判断配置是已生效还是待生效。

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
| Model Providers | `Providers[id]`、`ProviderId`、`Model` | Desktop / AppServer 通过 Provider 管理接口即时刷新新会话默认值 |

说明：

- Desktop 的模型设置页管理 Provider 注册表；凭证与端点只属于 `Providers[id]`。
- 工作区只保存当前 `ProviderId` 和 `Model` 覆盖，不再保存根级 `ApiKey` / `EndPoint`。
- Remote AppServer 的生命周期由用户或远端环境管理。Desktop 只测试连接并切换，不提供 remote restart。
- 如果 Desktop 通过 `--remote` 启动，本次会话的连接由启动参数控制，Settings 中的持久化连接切换不可用。

## 3. 如何判断“已生效”与“待生效”

可以从以下信号判断状态：

- **已生效**：配置已写入且对应层级动作已完成（即时应用成功，或重启完成）。
- **待生效**：出现需要重启的提示，表示配置已落盘但运行态尚未切换。
- **Remote 连接待应用**：远端 URL/token 变更尚未写入默认连接；点击“应用并连接”后，Desktop 先探测草稿连接，成功才保存。探测失败时不保存，避免下次启动被坏配置困住。
- **按分组脏状态**：仅变更了某一分组时，只需要处理该分组对应动作，不必全局保存。
- **已有坏 Remote 配置**：如果启动时发现已持久化的 Remote URL 无效，错误页主操作应进入 Settings > Connection 并解除阻塞覆盖层，让用户修正 URL/token 或切回 Local。

## 常见问题

**问：我改了 Model，为什么没有立即生效？**  
答：Desktop 模型 Provider 页面会通过 AppServer 接口刷新新会话默认值；已经存在的线程通常保留创建时记录的模型，除非客户端显式为该线程设置新的模型。

客户端事件细节请参阅 [AppServer Protocol](../protocols/appserver-protocol) 的 `workspace/configChanged` 说明。
