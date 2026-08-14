# 将 DotCraft 接入飞书

通过自建应用和 WebSocket 事件订阅，把飞书或 Lark 机器人接入 DotCraft。

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

原生流式卡片默认启用。CardKit 权限或 API 不可用时，DotCraft 会自动改用普通卡片发送完整回复。将 `feishu.streaming.enabled` 设为 `false` 可始终使用普通渐进卡片。

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
- CardKit 可用时，回复会在同一张飞书卡片中持续更新；普通卡片是自动回退路径。
- DotCraft 可以用配置的 reaction 标记已处理消息。
- 应用具备资源权限时，可以下载图片和文件输入。

### 官方飞书 CLI

将 `feishu.cli.enabled` 设为 `true` 后，飞书来源的会话可以通过当前配置的 Bot 身份使用内置官方飞书 CLI。只为应用授予计划使用的命令所需 scope；飞书要求时，还需把目标资源分享给应用 Bot。审批和命令限制见[渠道配置参考](./reference#飞书)。

## 独立适配器

只有在不由 Desktop 管理渠道进程时，才需要自己运行飞书适配器。

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-feishu
npx dotcraft-channel-feishu --workspace /path/to/workspace
```

独立 WebSocket 适配器注册方式见共享的 [渠道配置参考](./reference)。

## 参考

飞书的 JSON 示例、`ExternalChannels` 注册方式和字段表见 [渠道配置参考](./reference)。

## 相关文档

- [Channels 与 Bots](../../features/entry-points/channels)
- [渠道配置参考](./reference)
- [Channel adapters](../sdks/channels)
- [Channel Module 集成](../integrations/typescript-module)
