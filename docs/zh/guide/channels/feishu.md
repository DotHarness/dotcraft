# 将 DotCraft 接入飞书

通过自建应用和 WebSocket 事件订阅，把飞书或 Lark 机器人接入 DotCraft。

## 连接机器人

1. 在[飞书开放平台](https://open.feishu.cn/app)创建企业自建应用，添加机器人能力，授予接收和发送消息的权限。
2. 在 DotCraft Desktop 的目标工作区打开**插件 → 渠道**，安装**飞书**，点**设置**。填写应用的 App ID 和 App Secret。把需要使用 `/model` 等管理员命令的用户 `open_id` 添加到**管理员用户**。

   ![在 DotCraft Desktop 中配置飞书机器人](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/feishu-configuration.png)

3. 保存设置，点**连接**。
4. 在**事件与回调**中选择长连接，订阅**接收消息**事件。
5. 在目标租户发布应用，把机器人加入需要回复的会话。私聊机器人或在群里 @ 它，测试连接。

收发图片和文件需要资源权限，获取群聊上下文需要会话元数据权限，标记已接手的消息需要表情回应权限。

## 使用机器人

- 群消息默认需要 @ 机器人。每个群话题各有一个 DotCraft 对话。
- Agent 以卡片发送带格式的文本，也可发送图片、文件、音频和视频。审批和[命令选项](./#聊天中的命令)以卡片按钮显示。
- DotCraft 用表情回应标记已接手的消息。工作超过 20 秒时显示状态卡片。
- 卡片文字跟随飞书界面语言。

### 官方飞书 CLI

在渠道设置中启用**官方飞书 CLI**。给应用开通所用命令需要的权限，并把相关资源分享给机器人。命令权限和审批见[飞书渠道配置](../../configuration/channels#飞书-lark)。

### 个人资源访问

授权一个账号后，Agent 可读取它的个人日历、云空间和邮箱等资源。整个渠道共用此账号的只读权限。

1. 在渠道设置中选**个人访问权限 → 编辑**，勾选 Agent 可读取的内容。在[飞书开发者后台](https://open.feishu.cn/document/server-docs/application-scope/scope-list)开通所选权限和**持续访问已授权的数据**（`offline_access`），然后保存渠道设置。
2. 私聊机器人发送 `/feishu-auth`，打开它返回的链接并授权账号。权限变更后重新授权。

发送 `/feishu-auth status` 查看已授权账号，或发送 `/feishu-auth revoke` 撤销授权。

## 相关文档

- [飞书渠道配置](../../configuration/channels#飞书-lark) — 应用设置和 CLI 权限。
