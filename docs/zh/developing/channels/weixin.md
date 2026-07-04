# 将 DotCraft 接入微信

通过腾讯 iLink 把微信接入 DotCraft。首次登录需要扫码，普通回复会以纯文本发送。

## 快速设置

1. 确认微信账号具备腾讯 iLink 机器人接入能力。
2. 在 DotCraft Desktop 打开目标 workspace。
3. 打开 **Channels**，选择 **微信**。
4. 除非腾讯提供了不同端点，否则保持默认 iLink API 地址。
5. 保存渠道并启用。
6. 扫描 Desktop 中显示的二维码。
7. 等到 Desktop 中的微信渠道显示为 connected。
8. 给微信机器人账号发送一条消息。

## 平台设置细节

微信渠道以认证为主：

1. 使用已被腾讯 iLink 授权接入机器人的微信账号。
2. 首次二维码登录完成前，保持 Desktop 打开。
3. 正常重启 Desktop 会复用已保存会话。
4. 当 Desktop 要求重新认证时，扫描新的二维码。

Desktop 托管的微信渠道不需要公网回调 URL。

## 测试连接

1. 给微信机器人账号发送 `hello`。
2. 确认 DotCraft 在同一聊天中回复。
3. 发送 `/new`，再发送一条消息，确认它开启新的会话。
4. 让 DotCraft 执行一个需要审批的操作，并用审批关键词回复。
5. 如果工作流依赖媒体投递，测试一次返回文件或图片的任务。

## 设置后可用能力

- `/new` 会在当前微信聊天中开启新的 DotCraft 会话。
- 登录会话会保存下来，正常重启时可复用。
- 会话过期时，Desktop 会显示新的二维码。
- iLink 不提供 Markdown 渲染入口，因此普通回复和媒体 caption 会转为纯文本。
- 文件和图片投递可通过渠道投递工具使用。
- 审批回复支持 `同意`、`允许`、`yes`、`approve` 和 `reject` 等纯聊天关键词。

## 独立适配器

只有在不由 Desktop 管理渠道进程时，才需要自己运行微信适配器。

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-weixin
npx dotcraft-channel-weixin --workspace /path/to/workspace
```

当适配器配置不在 `.craft/weixin.json` 时，使用 `--config /custom/weixin.json`。终端模式会在终端中渲染二维码。

独立 WebSocket 适配器注册方式见共享的 [渠道配置参考](./reference)。

## 参考

微信的 JSON 示例、`ExternalChannels` 注册方式和字段表见 [渠道配置参考](./reference)。

## 相关文档

- [Channels 与 Bots](../../features/entry-points/channels)
- [渠道配置参考](./reference)
- [企业微信渠道](./wecom)
- [Channel adapters](../sdks/channels)
