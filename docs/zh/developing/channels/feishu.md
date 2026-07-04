# 将 DotCraft 接入飞书

通过自建应用和 WebSocket 事件订阅，把飞书或 Lark 机器人接入 DotCraft。

## 快速设置

1. 在飞书开发者后台创建自建应用。
2. 启用 Bot 能力。
3. 启用长连接 / WebSocket 事件订阅。
4. 复制 App ID 和 App Secret。
5. 在 DotCraft Desktop 打开目标 workspace。
6. 打开 **Channels**，选择 **飞书**。
7. 粘贴 App ID 和 App Secret。
8. 保存渠道并启用。

Bot 连接到飞书事件后，Desktop 中的飞书渠道应显示为 connected。

## 平台设置细节

在飞书开发者后台：

1. 将 Bot 添加到需要 DotCraft 响应的会话。
2. 授予消息事件权限，让 DotCraft 能接收 Bot 消息。
3. 授予消息发送权限，让 DotCraft 能用卡片回复。
4. 如果用户会向 DotCraft 发送图片或文件，授予资源权限。
5. 如果 Bot 需要群聊上下文，授予会话元数据权限。
6. 如果希望 DotCraft 用表情标记已处理消息，授予 reaction 权限。

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
- 回复、进度、审批和用户输入请求会以飞书卡片发送。
- DotCraft 可以用配置的 reaction 标记已处理消息。
- 应用具备资源权限时，可以下载图片和文件输入。

### 高级 docx 与 wiki 工具

飞书 docx 和 wiki 工具是可选能力。只有在应用具备所需文档 scope，并且目标文档、文件夹或知识库空间已分享给应用 Bot 后才启用。

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
- [TypeScript Module 集成](../integrations/typescript-module)
