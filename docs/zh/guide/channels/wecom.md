# 将 DotCraft 接入企业微信

通过公网 HTTPS 回调，把企业微信群机器人接入 DotCraft。

## 连接机器人

1. 在企业微信管理后台创建或打开群机器人，启用回调模式。
2. 在 DotCraft Desktop 的目标工作区打开**插件 → 渠道**，安装**企业微信**，点**设置**。
3. 设置回调监听地址，默认是 `0.0.0.0:9000`。添加机器人并填写回调路径、Token 和 EncodingAESKey。至少添加一个管理员、允许用户或允许会话。

   ![在 DotCraft Desktop 中配置企业微信渠道](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/wecom-configuration.png)

4. 保存设置，点**连接**。
5. 在企业微信中，把回调 URL 设为转发到 DotCraft 监听地址的公网 HTTPS 地址，并带上机器人的回调路径。本地监听地址可通过 HTTPS 反向代理或隧道接入。Token 和 EncodingAESKey 与 DotCraft 保持一致。
6. 把机器人加入允许的会话并发一条消息。DotCraft 会在该会话里回复。

## 使用机器人

- DotCraft 回复允许的用户和会话。管理员可用 `/model` 等命令，其他允许用户可用其余[聊天命令](./#聊天中的命令)。命令选项以编号列表显示。
- Agent 可发送文本、图片、文件和 AMR 格式的语音。

## 相关文档

- [企业微信渠道配置](../../configuration/channels#企业微信-wecom) — 回调设置和白名单。
- [微信渠道](./weixin) — 扫码接入个人微信账号。
