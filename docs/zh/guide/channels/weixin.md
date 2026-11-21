# 将 DotCraft 接入微信

在微信中扫码，把微信机器人接入 DotCraft。

## 连接机器人

1. 在手机微信中打开**我 → 设置 → 插件**，启用 **微信 ClawBot**。
2. 在 DotCraft Desktop 的目标工作区打开**插件 → 渠道**，安装**微信**。

   ![在 DotCraft Desktop 中查看微信渠道设置](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/weixin-configuration.png)

3. 点**连接**，用微信扫描二维码。
4. 在微信 ClawBot 聊天里发一条消息。DotCraft 会在该聊天里回复。

重新连接时会复用登录状态。收到重新登录提示时，扫描新的二维码。

## 使用机器人

- 机器人所有者可用[聊天命令](./#聊天中的命令)，包括 `/model` 等管理员命令。命令选项以编号列表显示。
- Agent 可发送纯文本、图片和文件，也可发给曾给机器人发过消息的微信用户。

## 相关文档

- [微信渠道配置](../../configuration/channels#微信-weixin) — API 地址和超时设置。
- [企业微信渠道](./wecom) — 使用回调 URL 接入企业聊天机器人。
