# 将 DotCraft 接入 QQ

通过 NapCat 或其他 OneBot v11 网关，把 QQ 账号接入 DotCraft。

> [!CAUTION]
> 第三方 QQ 框架存在账号风险，请使用专用 QQ 账号。

## 连接机器人

1. 用机器人的 QQ 账号登录 [NapCat](https://napneko.github.io/)。
2. 在 DotCraft Desktop 的目标工作区打开**插件 → 渠道**，安装 **QQ**，点**设置**。
3. 设置 OneBot 监听地址，默认是 `127.0.0.1:6700`。至少添加一个管理员、允许用户或允许群。连接需要鉴权时，在 DotCraft 和 NapCat 中填写同一个访问令牌。

   ![在 DotCraft Desktop 中配置 QQ 渠道](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/qq-configuration.png)

4. 保存设置，点**连接**。
5. 在 NapCat WebUI 中添加反向 WebSocket 连接，地址填 `ws://127.0.0.1:6700/`，消息格式选 `array`。NapCat 跑在 Docker 或另一台机器上时，填写它能访问的 DotCraft 地址。
6. 用允许的账号私聊机器人，或在允许的群里 @ 它。DotCraft 会在同一个 QQ 聊天中回复。

## 使用机器人

- 私聊中每个 QQ 用户各有一个 DotCraft 对话，每个群共享一个 DotCraft 对话。群聊默认需要 @ 机器人。
- 管理员可用 `/model` 等命令。允许用户和允许群的成员可用其他[聊天命令](./#聊天中的命令)。命令选项以编号列表显示。
- 通过 NapCat 等支持 `set_msg_emoji_like` 的网关，DotCraft 会给接手的消息加上表情回应。
- Agent 可发送文本、图片、文件、语音和视频。经你审批后，它还可发到其他群，或发给曾给机器人发过消息的 QQ 用户。

## 相关文档

- [QQ 渠道配置](../../configuration/channels#qq) — 监听地址、访问令牌和白名单。
