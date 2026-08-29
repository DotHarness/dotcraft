# 将 DotCraft 接入 Telegram

用 BotFather 签发的 token 把 Telegram Bot 接成 DotCraft 的一个[渠道](./)。DotCraft 通过 long polling 接收消息，不需要公网 webhook URL。

## 快速设置

1. 打开 Telegram，并与 `@BotFather` 开始聊天。
2. 发送 `/newbot`，按 BotFather 提示完成创建。
3. 复制 bot token。
4. 在 DotCraft Desktop 打开目标 workspace。
5. 打开 **Channels**，选择 **Telegram**，然后选择 **Connect**。

   ![在 Telegram 渠道详情页开始连接](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/telegram-detail-light.png)

6. 粘贴 bot token，然后检查可选代理和超时设置。

   ![在 DotCraft Desktop 中配置 Telegram Bot](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/telegram-configuration-light.png)

7. 保存渠道并启用。
8. 给 Telegram Bot 发送一条消息。

polling 启动后，Desktop 中的 Telegram 渠道应显示为 connected。

## 平台设置细节

Telegram 的平台设置主要在 BotFather 中完成：

1. 如果希望 Bot 资料更完整，使用 `/setdescription`、`/setabouttext` 和 `/setuserpic`。
2. 如果需要 DotCraft 在群里回复，将 Bot 加入群聊。
3. 检查 BotFather 中的群聊 privacy mode。

启用 Telegram privacy mode 时，Bot 可能只会收到命令、回复以及明确提到它的消息。如果需要 DotCraft 看到所有群消息，请在 BotFather 中调整 Bot privacy 设置。

## 测试连接

1. 在 Bot 私聊中发送 `hello`。
2. 确认 DotCraft 回复。
3. 发送 `/help`，确认命令已注册。
4. 发送 `/new`，再发送一条消息，确认它开启新的会话。
5. 在群聊中，如果启用了 privacy mode，请 @ 或回复 Bot。

## 设置后可用能力

- Telegram 将私聊或群聊消息投递给 Bot 后，这些消息可以触发 DotCraft 回合。
- `/new` 开启新的 DotCraft 会话，`/help` 列出可用命令。适配器还会把 workspace 中定义的斜杠命令一并注册到 Telegram 命令菜单。
- 审批和用户输入请求会尽量使用 Telegram inline buttons。
- 文件和图片投递可通过渠道投递工具使用。
- 同一个 bot token 同一时间只能被一个 long-polling 进程使用。

## 独立适配器

只有在不由 Desktop 管理渠道进程时，才需要自己运行 Telegram 适配器。

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-telegram
npx dotcraft-channel-telegram --workspace /path/to/workspace
```

独立适配器的 `ExternalChannels` 注册形态见[渠道配置参考](./reference)。

## 相关文档

- [渠道配置参考](./reference)——Telegram 配置文件的全部字段、默认值与注册形态。
- [渠道适配器](../../developing/sdks/channels)——适配器基类的消息流转与 handler 契约。