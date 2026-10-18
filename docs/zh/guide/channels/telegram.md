# 将 DotCraft 接入 Telegram

用 BotFather 签发的 token 把 Telegram Bot 接成 DotCraft 的一个[渠道](./)。不需要公网 URL。

## 快速设置

1. 打开 Telegram，并与 `@BotFather` 开始聊天。
2. 发送 `/newbot`，按 BotFather 提示完成创建。
3. 复制 bot token。
4. 在 DotCraft Desktop 打开目标 workspace。
5. 打开 **Channels**，选择 **Telegram**，然后选择 **Connect**。

   ![在 Telegram 渠道详情页开始连接](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/telegram-detail.png)

6. 粘贴 bot token。想使用 `/model` 等管理员命令时，把你的 Telegram 数字用户 ID 加进**管理员用户**，然后检查可选代理和超时设置。

   ![在 DotCraft Desktop 中配置 Telegram Bot](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/telegram-configuration.png)

7. 保存渠道并启用。
8. 给 Telegram Bot 发送一条消息。

连上 Telegram 后，Desktop 中的 Telegram 渠道应显示为 connected。

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

- Telegram 送达给 Bot 的私聊和群聊消息，DotCraft 都会回复。在群聊里，每个成员与 Agent 各有一段会话。
- Agent 可以发送带格式的文本、图片、文件、音频和视频。Agent 工作时，Bot 会显示正在输入。
- Telegram 命令菜单列出每个人能用的[聊天命令](./#聊天中的命令)，包括 workspace 自己的命令。管理员命令只对**管理员用户**里的人显示。
- 审批、用户输入请求，以及 `/model` 列出的模型这类命令选项，都使用 Telegram inline buttons。
- 同一个 bot token 同一时间只能给一个 DotCraft 实例使用。

## 相关文档

- [渠道配置](../../configuration/channels)——Telegram 配置文件的全部字段、默认值与注册形态。
