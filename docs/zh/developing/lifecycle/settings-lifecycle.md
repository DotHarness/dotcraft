# 设置生效层级

本页面向集成方与贡献者，说明 Desktop 设置的三层生效模型，以及如何判断一项配置是已生效还是待生效。

![Desktop 设置变更生效的三个层级，以及远程连接的例外](/settings-tiers-overview.svg)

## 三层生效模型

Desktop 按照改动如何变成运行态，把设置分成三层：

1. **即时生效（Tier A）**
   - 保存后立即生效。
   - 典型示例：`Skills.DisabledSkills`、MCP 配置项。
2. **子系统重启（Tier B）**
   - 配置已写入，重启对应子系统后生效。
   - 典型示例：受外部通道子系统生命周期影响的配置。
3. **AppServer 重启（Tier C）**
   - Local 模式下配置已写入，重启 Hub 托管的本地 AppServer 进程后生效。
   - 典型示例：启动级 Core 配置、本地 AppServer 二进制路径与部分入口配置。

分组的动作按钮就是层级标识：即时生效的分组保存即应用，需要重启的分组给出 Restart 或 Apply & Restart。

## 代表字段与生效方式

| 配置区域 | 代表字段 | 生效方式 |
|---|---|---|
| Skills / MCP | `Skills.DisabledSkills`、MCP 服务器定义 | 即时生效 |
| External Channel | 外部通道相关配置 | 子系统重启 |
| Connection / Local AppServer | `connectionMode = local`、本地 AppServer 二进制路径、本地 WebSocket 监听配置 | Hub 托管的本地 AppServer 可 Apply & Restart |
| Connection / Remote AppServer | `connectionMode = remote`、远程 WebSocket URL、token | 先用草稿 URL/token 完成 WebSocket initialize 探测，成功后保存并切换。不会重启远端 AppServer |
| Model providers | `Providers[id]`、`ProviderId`、`ProviderPreferences`、`SubAgent.ProviderPreferences` | Desktop / AppServer 通过 Provider 管理接口即时刷新新会话默认值 |

说明：

- Desktop 的模型设置页管理 Provider 注册表。凭证与端点只属于 `Providers[id]`。
- 修改工作区 `ProviderId` 或 `ProviderPreferences` 只刷新新线程的默认值。已有线程保留创建时的模型、思考程度、速率和上下文窗口快照，除非该线程自己的 composer 原子更新完整偏好。
- 工作区文件保存 `ProviderId` 和按 provider 区分的完整偏好覆盖。Provider 凭证保留在个人 `Providers[id]` 注册表中。
- Remote AppServer 的生命周期由用户或远端环境管理。Desktop 只测试连接并切换，不提供 remote restart。
- 如果 Desktop 通过 `--remote` 启动，本次会话的连接由启动参数控制，Settings 中的持久化连接切换不可用。

## 如何判断已生效与待生效

- **已生效**：配置已写入，且对应层级的动作已完成——即时应用成功，或重启完成。
- **待生效**：界面给出需要重启的提示，表示配置已落盘但运行态尚未切换。
- **Remote 连接待应用**：远端 URL/token 的改动还不是默认连接。点击「应用并连接」后，Desktop 先探测草稿连接，成功才保存。探测失败时不保存，避免下次启动被坏配置困住。
- **按分组的脏状态**：只变更了某一分组时，只需要处理该分组对应的动作，不必全局保存。
- **已保存的 Remote 配置无效**：启动时发现保存的 Remote 连接连不上，错误页会提供「打开设置」，进入 Settings > Connection 并解除阻塞覆盖层，让你修正 URL/token 或切回 Local。

## 相关文档

- [配置参考](../configuration) — 这些层级涉及的全部字段
- [AppServer 协议](../protocols/appserver-protocol) — 客户端据以感知配置变更的 `workspace/configChanged` 事件
- [AppServer 模式](./appserver) — 远程与多客户端连接的传输与认证
