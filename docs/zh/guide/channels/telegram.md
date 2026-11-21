# 将 DotCraft 接入 Telegram

用 BotFather 签发的令牌把 Telegram 机器人接入 DotCraft。

## 连接机器人

1. 在 Telegram 中向 `@BotFather` 发送 `/newbot`，按提示创建机器人并复制令牌。
2. 在 DotCraft Desktop 的目标工作区打开**插件 → 渠道**，安装 **Telegram**，点**设置**。
3. 粘贴令牌。把需要使用 `/model` 等命令的 Telegram 数字用户 ID 加入**管理员用户**。同一令牌同时用于一个 DotCraft 实例。

   ![在 DotCraft Desktop 中配置 Telegram 机器人](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/telegram-configuration.png)

4. 保存设置，点**连接**，再给机器人发一条私聊消息。

在群里使用时，把机器人加入群聊，再 @ 它或回复它。要接收所有群消息，在 BotFather 中关闭隐私模式。

## 使用机器人

- 群里每位成员各有一个与机器人的 DotCraft 对话。
- Agent 可发送带格式的文本、图片、文件、音频和视频。工作时，机器人会显示正在输入。
- 机器人菜单列出每位发送者可用的[聊天命令](./#聊天中的命令)，包括工作区自定义命令。审批、补充提问和命令选项以消息内的按钮显示。

## 相关文档

- [Telegram 渠道配置](../../configuration/channels#telegram) — 机器人令牌、代理和超时。
