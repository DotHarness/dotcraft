# 将 DotCraft 接入飞书

通过自建应用和 WebSocket 事件订阅，把飞书或 Lark 机器人接成 DotCraft 的一个[渠道](./)。

## 快速设置

1. 在飞书开发者后台创建自建应用。
2. 启用 Bot 能力。
3. 启用长连接 / WebSocket 事件订阅。
4. 复制 App ID 和 App Secret。
5. 在 DotCraft Desktop 打开目标 workspace。
6. 打开 **Channels**，选择 **飞书**，然后选择 **Connect**。

   ![在飞书渠道详情页开始连接](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/feishu-detail-light.png)

7. 粘贴 App ID 和 App Secret，然后检查平台与群消息设置。

   ![在 DotCraft Desktop 中配置飞书 Bot](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/feishu-configuration-light.png)

8. 保存渠道并启用。

Bot 连接到飞书事件后，Desktop 中的飞书渠道应显示为 connected。

## 平台设置细节

在飞书开发者后台：

1. 将 Bot 添加到需要 DotCraft 响应的会话。
2. 授予消息事件权限，让 DotCraft 能接收 Bot 消息。
3. 授予消息发送权限，让 DotCraft 能用卡片回复。
4. 授予 `cardkit:card:write`（**创建与更新卡片**）权限，让回复能够使用原生打字机效果流式显示。
5. 如果用户会向 DotCraft 发送图片或文件，授予资源权限。
6. 如果 Bot 需要群聊上下文，授予会话元数据权限。
7. 如果希望 DotCraft 用表情标记已处理消息，授予 reaction 权限。

在群聊中测试前，请先在目标租户中发布或启用应用。即使已经选择 scope，租户策略仍可能拦截事件或消息发送。

## 测试连接

1. 给飞书 Bot 发送私聊消息。
2. 确认 DotCraft 用飞书卡片回复。
3. 将 Bot 加入群聊并 @ 它。
4. 确认 DotCraft 在群里回复。
5. 让 DotCraft 执行一个需要审批的操作，并使用审批卡片按钮。

## 设置后可用能力

- 私聊消息不需要 @ 即可处理。
- 群聊默认需要 @ 机器人后才处理。
- 在话题群里 @ 机器人，DotCraft 会在该话题内回复，不同话题的对话互不影响。
- DotCraft 可以用配置的 reaction 标记已处理消息。
- 卡片上的文字跟随你的飞书界面语言显示，支持中文、英语、日语、韩语、西班牙语、法语和德语。
- 应用具备资源权限时，可以下载图片和文件输入。

### 官方飞书 CLI

将 `feishu.cli.enabled` 设为 `true` 后，飞书来源的会话可以用当前配置的 Bot 身份调用内置官方飞书 CLI。只为应用授予计划使用的命令所需 scope。飞书要求时，还需把目标资源分享给应用 Bot。审批和命令限制见[渠道配置参考](./reference#飞书-lark)。

### 个人资源访问

日历、个人云空间、邮箱，这些属于某个人，Bot 打不开。授权一个账号，agent 就能代你读取它们。它只读，不会以你的名义新建、修改或发送任何东西。

先在[飞书开发者后台](https://open.feishu.cn/document/server-docs/application-scope/scope-list)给应用开通对应权限，再把它们填进渠道设置的**个人资源授权范围**，一行一个：

| 想让 agent 读什么 | 填哪个 scope |
| --- | --- |
| 日历、日程和忙闲 | `calendar:calendar:readonly` |
| 云空间文件 | `drive:drive:readonly` |
| 文档 | `docs:doc:readonly` |
| 知识库 | `wiki:wiki:readonly` |
| 多维表格 | `bitable:app:readonly` |
| 企业邮箱 | `mail:user_mailbox:readonly` |

同时给应用开通**持续访问已授权的数据**（`offline_access`）。DotCraft 会自己请求它来保持授权有效，所以不用填进输入框。

然后私聊机器人发送 `/feishu-auth`，打开它回复的链接同意授权。你用哪个账号同意，agent 之后就以哪个账号读取，且对渠道里所有人生效。发送 `/feishu-auth status` 查看当前绑定的账号，`/feishu-auth revoke` 解除绑定。

## 独立适配器

只有在不由 Desktop 管理渠道进程时，才需要自己运行飞书适配器。

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-feishu
npx dotcraft-channel-feishu --workspace /path/to/workspace
```

独立适配器的 `ExternalChannels` 注册形态见[渠道配置参考](./reference)。

## 相关文档

- [渠道配置参考](./reference)——飞书配置文件的全部字段、默认值与注册形态。
- [渠道适配器](../../developing/sdks/channels)——适配器基类的消息流转与 handler 契约。
- [渠道模块集成](../../developing/integrations/typescript-module)——飞书模块就是这份契约的完整实现示例。
