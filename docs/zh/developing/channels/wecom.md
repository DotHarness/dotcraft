# 将 DotCraft 接入企业微信

使用企业微信管理后台中的回调设置，把企业微信群机器人接入 DotCraft。

## 快速设置

1. 在 DotCraft Desktop 打开目标 workspace。
2. 打开 **Channels**，选择 **企业微信**。
3. 设置回调监听 host、port 和 scheme。默认本地监听是 `0.0.0.0:9000`。
4. 添加机器人配置，填写回调 path、Token 和 EncodingAESKey。
5. 至少添加一个管理员用户、允许用户或允许会话。
6. 保存渠道并启用。
7. 在企业微信中，将群机器人回调 URL 设置为能访问 DotCraft 监听地址的公网 URL。
8. 将同一组 Token 和 EncodingAESKey 填入企业微信机器人设置。

企业微信完成回调校验后，Desktop 中的企业微信渠道应显示为 connected。

## 平台设置细节

在企业微信管理后台：

1. 创建或打开要接入 DotCraft 的群机器人。
2. 启用回调模式。
3. 将回调 URL 设置为公网 HTTPS 端点加上 Desktop 中的机器人 path。
4. 将 Token 设置为 Desktop 中的同一个机器人 token。
5. 将 EncodingAESKey 设置为 Desktop 中的同一个机器人 AES key。
6. 将机器人添加到需要 DotCraft 响应的会话。

企业微信必须能从公网访问回调地址。本地 Desktop 场景请在 Desktop 监听地址前放一个 HTTPS 反向代理或隧道。

## 测试连接

1. 在允许的企业微信会话中发送消息。
2. 确认 DotCraft 在同一会话中回复。
3. 让 DotCraft 执行一个需要审批的操作。
4. 确认审批请求出现在企业微信中，并且你的回复会被接受。

## 设置后可用能力

- 来自允许用户或允许会话的企业微信消息可以触发 DotCraft 回合。
- 管理员用户可以在企业微信中审批高风险操作。
- 审批回复支持常规 approve 和 reject 关键词。
- 文件和图片投递可通过渠道投递工具使用。
- 白名单之外的用户或会话消息会被忽略。

## 独立适配器

只有在不由 Desktop 管理渠道进程时，才需要自己运行企业微信适配器。

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-wecom
npx dotcraft-channel-wecom --workspace /path/to/workspace
```

独立 WebSocket 适配器注册方式见共享的 [渠道配置参考](./reference)。

## 参考

企业微信的 JSON 示例、`ExternalChannels` 注册方式和字段表见 [渠道配置参考](./reference)。

## 相关文档

- [Channels 与 Bots](../../features/entry-points/channels)
- [渠道配置参考](./reference)
- [微信渠道](./weixin)
- [Channel adapters](../sdks/channels)
