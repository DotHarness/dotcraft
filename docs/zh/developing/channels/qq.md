# 将 DotCraft 接入 QQ

通过 NapCat 或其他 OneBot v11 网关，把一个 QQ 账号接入 DotCraft。

> [!CAUTION]
> 第三方 QQ 协议框架可能带来账号风险。部署前请使用专用 QQ 账号，并自行评估风险。

## 快速设置

1. 在 DotCraft Desktop 打开目标 workspace。
2. 打开 **Channels**，选择 **QQ**。
3. 设置 OneBot 监听地址。默认是 `127.0.0.1:6700`。
4. 如果希望 NapCat 连接 DotCraft 端点时带鉴权，填写 access token。
5. 至少添加一个管理员用户、允许用户或允许群。
6. 保存渠道并启用。
7. 在 NapCat WebUI 中添加反向 WebSocket 连接，地址为 `ws://127.0.0.1:6700/`。
8. 将 NapCat 消息格式设置为 `array`。

NapCat 连上 DotCraft 监听地址后，Desktop 中的 QQ 渠道应显示为 connected。

## 平台设置细节

在 NapCat 中配置代表 DotCraft 发言的 QQ 账号：

1. 使用专用 QQ 账号登录。
2. 打开 OneBot / WebSocket 客户端设置。
3. 将反向 WebSocket URL 设置为 DotCraft 监听地址。
4. 将 Token 设置为 Desktop 中填写的同一个值。
5. 将消息格式设置为 `array`。

如果 NapCat 在 Docker 或另一台机器上运行，请把 `127.0.0.1` 替换成能访问 DotCraft Desktop 所在机器的地址。

## 测试连接

1. 从允许用户向 QQ 账号发送私聊消息。
2. 在群里 @ 机器人账号。
3. 确认 DotCraft 在同一个 QQ 会话中回复。
4. 让 DotCraft 执行一个需要审批的操作，然后回复 `同意`、`允许`、`yes` 或 `approve`。

## 设置后可用能力

- 私聊会为每个 QQ 用户保留独立的 DotCraft 会话。
- 一个 QQ 群共享一条会话，每条消息会记录实际发送者。
- 群聊默认需要 @ 机器人后才响应。
- 管理员和白名单都为空时，机器人会忽略 QQ 消息。
- 审批回复支持 `同意`、`允许`、`yes`、`approve`、`拒绝`、`no`、`reject` 和 `deny`。
- 语音、视频和文件投递可通过渠道投递工具使用。
- 上传文件时，DotCraft 会先读取文件再发送给 NapCat。Docker 部署不需要把 workspace 挂载到 NapCat 容器中。

## 独立适配器

只有在不由 Desktop 管理渠道进程时，才需要自己运行 QQ 适配器。

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-qq
npx dotcraft-channel-qq --workspace /path/to/workspace
```

当适配器配置不在 `.craft/qq.json` 时，使用 `--config /custom/qq.json`。

独立 WebSocket 适配器注册方式见共享的 [渠道配置参考](./reference)。

## 参考

QQ 的 JSON 示例、`ExternalChannels` 注册方式和字段表见 [渠道配置参考](./reference)。

## 相关文档

- [Channels 与 Bots](../../features/entry-points/channels)
- [渠道配置参考](./reference)
- [Channel adapters](../sdks/channels)
- [TypeScript Module 集成](../integrations/typescript-module)
