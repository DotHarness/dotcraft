# 将 DotCraft 接入微信

通过腾讯 iLink 把微信接成 DotCraft 的一个[渠道](./)。首次登录需要扫码，普通回复以纯文本发送。

## 快速设置

1. 确认微信账号具备腾讯 iLink 机器人接入能力。
2. 在 DotCraft Desktop 打开目标 workspace。
3. 打开 **Channels**，选择 **微信**，然后选择 **Connect**。

   ![在微信渠道详情页开始连接](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/weixin-detail-light.png)

4. 除非腾讯提供了不同端点，否则保持默认 iLink API 地址。

   ![在 DotCraft Desktop 中检查微信渠道设置](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/weixin-configuration-light.png)

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
- 微信不渲染 Markdown，回复以纯文本显示。
- 文件和图片投递可通过渠道投递工具使用。
- 审批回复支持 `同意`、`允许`、`yes`、`approve`、`拒绝`、`no`、`reject` 和 `deny` 等纯聊天关键词。回复 `同意全部` 或 `approve all` 会在本会话内放行同类操作。

## 独立适配器

只有在不由 Desktop 管理渠道进程时，才需要自己运行微信适配器。

```bash
cd sdk/typescript
npm run build --workspace @dotcraft/channel-weixin
npx dotcraft-channel-weixin --workspace /path/to/workspace
```

当适配器配置不在 `.craft/weixin.json` 时，使用 `--config /custom/weixin.json`。终端模式会在终端中渲染二维码。

独立适配器的 `ExternalChannels` 注册形态见[渠道配置参考](./reference)。

## 相关文档

- [渠道配置参考](./reference)——微信配置文件的全部字段、默认值与注册形态。
- [渠道适配器](../../developing/sdks/channels)——适配器基类的消息流转与 handler 契约。
- [企业微信渠道](./wecom)——企业内部会话走另一条链路，通过回调 URL 接入。
