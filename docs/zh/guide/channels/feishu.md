# 将 DotCraft 接入飞书

通过自建应用和 WebSocket 事件订阅，把飞书或 Lark 机器人接成 DotCraft 的一个[渠道](./)。

## 快速设置

1. 在飞书开发者后台创建自建应用。
2. 启用 Bot 能力。
3. 启用长连接 / WebSocket 事件订阅。
4. 复制 App ID 和 App Secret。
5. 在 DotCraft Desktop 打开目标 workspace。
6. 打开 **Channels**，选择 **飞书**，然后选择 **Connect**。

   ![在飞书渠道详情页开始连接](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/feishu-detail.png)

7. 粘贴 App ID 和 App Secret。把需要使用 `/model` 等管理员命令的人的 open_id 加进**管理员用户**，然后检查平台与群消息设置。

   ![在 DotCraft Desktop 中配置飞书 Bot](https://github.com/DotHarness/resources/raw/master/dotcraft/channels/feishu-configuration.png)

8. 保存渠道并启用。

Bot 连接到飞书事件后，Desktop 中的飞书渠道应显示为 connected。

## 平台设置细节

在飞书开发者后台：

1. 将 Bot 添加到需要 DotCraft 响应的会话。
2. 授予消息事件权限，让 DotCraft 能接收 Bot 消息。
3. 授予消息发送权限，让 Agent 能发送消息，DotCraft 能发出审批卡片和状态卡片。
4. 如果用户会向 DotCraft 发送图片或文件，或者 Agent 需要发送它们，授予资源权限。
5. 如果 Bot 需要群聊上下文，授予会话元数据权限。
6. 如果希望 DotCraft 用表情标记已处理消息，授予 reaction 权限。

在群聊中测试前，请先在目标租户中发布或启用应用。即使已经选择 scope，租户策略仍可能拦截事件或消息发送。

## 测试连接

1. 给飞书 Bot 发送私聊消息。
2. 确认 DotCraft 用飞书卡片回复。
3. 将 Bot 加入群聊并 @ 它。
4. 确认 DotCraft 在群里回复。
5. 让 DotCraft 执行一个需要审批的操作，并使用审批卡片按钮。

## 设置后可用能力

- 私聊消息不需要 @ 即可处理。
- 群聊默认需要 @ 机器人后才处理。
- 在话题群里 @ 机器人，会话就留在该话题内，不同话题的对话互不影响。
- Agent 以卡片发送带格式的文本，也能发送图片、文件、音频和视频。
- DotCraft 会用配置的 reaction 标记它接手的每条消息。工作超过 20 秒时，状态卡片会显示进度，最后停在已完成、失败或已停止。
- 审批和命令选项（比如 `/model` 列出的模型）以卡片按钮显示。
- 卡片上的文字跟随你的飞书界面语言显示，支持中文、英语、日语、韩语、西班牙语、法语和德语。
- 应用具备资源权限时，可以下载图片和文件输入。

### 官方飞书 CLI

将 `feishu.cli.enabled` 设为 `true` 后，飞书来源的会话可以用当前配置的 Bot 身份调用内置官方飞书 CLI。只为应用授予计划使用的命令所需 scope。飞书要求时，还需把目标资源分享给应用 Bot。审批和命令限制见[渠道配置](../../configuration/channels#飞书-lark)。

### 个人资源访问

日历、个人云空间、邮箱，这些属于某个人，Bot 打不开。授权一个账号，agent 就能代你读取它们。它只读，不会以你的名义新建、修改或发送任何东西。

先在[飞书开发者后台](https://open.feishu.cn/document/server-docs/application-scope/scope-list)给应用开通对应权限，再把它们填进渠道设置的**个人资源授权范围**，一行一个：

| 想让 agent 读什么 | 填哪个 scope |
| --- | --- |
| 日历、日程和忙闲 | `calendar:calendar:readonly` |
| 云空间文件 | `drive:drive:readonly` |
| 文档 | `docs:doc:readonly` |
| 知识库 | `wiki:wiki:readonly` |
| 多维表格 | `bitable:app:readonly` |
| 企业邮箱 | `mail:user_mailbox:readonly` |

同时给应用开通**持续访问已授权的数据**（`offline_access`）。DotCraft 会自己请求它来保持授权有效，所以不用填进输入框。

然后私聊机器人发送 `/feishu-auth`，打开它回复的链接同意授权。也可以直接让 agent 去做需要个人资源的事，由它给出链接。两种方式链接都只会私聊送达——谁打开谁就成为被授权的账号。你用哪个账号同意，agent 之后就以哪个账号读取，且对渠道里所有人生效。发送 `/feishu-auth status` 查看当前绑定的账号，`/feishu-auth revoke` 解除绑定。

## 相关文档

- [渠道配置](../../configuration/channels)——飞书配置文件的全部字段、默认值与注册形态。
