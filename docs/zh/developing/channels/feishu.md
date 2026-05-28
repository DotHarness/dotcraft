# DotCraft 飞书渠道适配器

`@dotcraft/channel-feishu` 通过 WebSocket 外部渠道协议，把飞书 / Lark 机器人接入 DotCraft。

它基于：

- `@dotcraft/sdk`：DotCraft AppServer 的 JSON-RPC 协议封装
- `@larksuiteoapi/node-sdk`：飞书 Bot API 与事件长连接

## 已支持能力

- 飞书 WebSocket 事件订阅
- 基于显式 tenant token 鉴权启动并探测 Bot 信息
- 基于外部渠道身份复用 DotCraft 线程
- `/new` 开启新会话
- 群聊仅在 @机器人 时响应
- 对会处理的入站消息立即添加表情回复
- 按钮式审批卡片
- `turn/completed` 后发送静态回复卡片
- 图片消息下载后以 `localImage` 形式转发给 DotCraft
- 可选注册的 docx + wiki 工具：创建/读取/更新/插入/删除/媒体嵌入/列出/查询/移动/重命名
- 公共 `FeishuClient.sendTextMessage(...)` 与 `replyToMessage(...)`

## 当前不覆盖

- 飞书多账号
- 飞书 webhook 模式
- 流式更新卡片
- 用户级 OAuth / Open Platform 授权流程

## 前置条件

1. Node.js `>= 20`
2. 已启动并启用 WebSocket 的 DotCraft AppServer
3. 一个已开启 Bot 能力的飞书自建应用

## 1. 启用 DotCraft 外部渠道

本目录下的 `config.example.json` 是 DotCraft 工作区配置片段。

将它合并到工作区 `.craft/config.json`：

```json
{
  "AppServer": {
    "Mode": "stdioAndWebSocket",
    "WebSocket": {
      "Host": "127.0.0.1",
      "Port": 9100,
      "Token": ""
    }
  },
  "ExternalChannels": {
    "feishu": {
      "enabled": true,
      "transport": "websocket"
    }
  }
}
```

## 2. 创建飞书应用

在飞书开放平台中：

1. 创建自建应用
2. 开启机器人（Bot）能力
3. 开启长连接 / WebSocket 事件订阅
4. 配置本适配器需要的权限

建议至少具备以下机器人权限：

- `im:message`
- `im:message:send`
- 调用 `im/v1/messages/:message_id/reactions` 所需的消息表情权限
- `im:resource`
- `im:chat`

然后获取：

- `appId`
- `appSecret`

## 3. 配置适配器

在目标工作区中创建 `.craft/feishu.json`：

```json
{
  "dotcraft": {
    "wsUrl": "ws://127.0.0.1:9100/ws",
    "token": ""
  },
  "feishu": {
    "appId": "cli_your_app_id",
    "appSecret": "your_app_secret",
    "brand": "feishu",
    "cardTitle": "DotCraft",
    "approvalTimeoutMs": 120000,
    "groupMentionRequired": true,
    "ackReactionEmoji": "GLANCE",
    "downloadDir": "./tmp",
    "tools": {
      "docs": {
        "enabled": false
      }
    },
    "debug": {
      "adapterStream": false,
      "textMerge": false
    }
  }
}
```

说明：

- `feishu.debug.adapterStream`：打印 `ChannelAdapter` 流式事件调试日志（stderr，前缀 `[dotcraft-sdk:adapter-stream]`），仅 `true` 启用
- `feishu.debug.textMerge`：打印文本合并分支日志，仅 `true` 启用
- `feishu.cardTitle`：回复/进度/流式卡片头部与审批提示文案中的品牌文本，默认 `DotCraft`
- `ackReactionEmoji` 必须为飞书官方 `emoji_type`，如 `GLANCE`、`SMILE`、`OnIt`
- `downloadDir` 用于暂存图片文件，再转发给 DotCraft
- `feishu.tools.docs.enabled`：作为整组开关注册飞书 docx + wiki channel tools；由于工具列表在 initialize 时声明，修改后需要重启模块才会生效

## 4. 安装并构建

```bash
cd sdk/typescript
npm install
npm run build:all
```

## 5. 运行

推荐方式：

```bash
npx dotcraft-channel-feishu --workspace /path/to/workspace
```

可选配置覆盖：

```bash
npx dotcraft-channel-feishu --workspace /path/to/workspace --config /custom/feishu.json
```

## 行为说明

- 私聊：默认处理
- 群聊：默认仅被 @ 时处理；`groupMentionRequired=false` 时放开群消息
- 入站提示：消息通过过滤与解析后，会先添加配置的表情回复
- 命令：`/new` 归档当前线程并创建新会话
- 审批：通过交互式卡片处理
- 回复：在回合结束后发送静态交互卡片
- Docx 工具（`documentIdOrUrl`）支持三种输入：裸 docx token、`/docx/<token>` 文档链接、以及指向 docx 节点的 wiki 节点 URL/token
- docx 块级原语工具：`FeishuListDocxBlocks`、`FeishuGetDocxBlock`、`FeishuInsertDocxBlocks`、`FeishuUpdateDocxBlocks`、`FeishuDeleteDocxBlocks`
- 高阶编辑工具 `FeishuUpdateDocxContent` 支持 `append`、`overwrite`、`replaceRange`、`replaceAll`、`insertBefore`、`insertAfter`、`deleteRange`，并支持可选 `newTitle`
- 媒体工具 `FeishuEmbedDocxMedia` 上传本地图片/文件并嵌入 docx（后续步骤失败时会尝试回滚）
- 标题工具：`FeishuUpdateDocxTitle` 与知识库节点重命名 `FeishuRenameWikiNode`
- docx 评论工具：`FeishuListDocxComments`、`FeishuBatchQueryDocxComments`、`FeishuListDocxCommentReplies`、`FeishuAddDocxComment`、`FeishuAddDocxCommentReply`、`FeishuResolveDocxComment`
- Docx URL 请使用 `/docx/<token>` 格式；如果从飞书界面复制链接，请确认链接指向 docx 文档。
- Wiki 工具（`spaceIdOrUrl`）支持三种输入：纯数字 `space_id`、`/wiki/settings/<space_id>` 链接、以及 wiki 节点 URL/token；当传节点 URL/token 且未显式给父节点时，会自动调用 `getWikiNode` 反查 `space_id` 并把该节点作为默认父节点
- `FeishuMoveDocxToWiki` 对齐官方 Lark CLI：当接口返回 `task_id`（异步路径）时，工具会以 `30 × 2s`（约 60 秒窗口）轮询 `GET /open-apis/wiki/v2/tasks/{task_id}?task_type=move`。成功时返回 `ready=true` 并携带解析后的 `wikiToken`；超时返回 `ready=false, timedOut=true, taskId`，由调用方稍后用该 `taskId` 继续查询。传 `waitForCompletion: false` 可跳过轮询，直接拿到 `taskId`。
- `FeishuMoveWikiNode` 用于在同一或不同知识库之间移动已有节点。必填 `nodeTokenOrUrl`，并至少传 `targetParentTokenOrUrl` / `targetSpaceIdOrUrl` 其中之一；若两者都传，工具会校验它们属于同一个空间，否则抛出 `InconsistentWikiTarget`。
- `FeishuListWikiSpaces` 列出当前应用身份可访问的知识库空间（空间发现），Agent 不再需要用户手动提供 `space_id`。通过 `pageSize` / `pageToken` 分页。
- `FeishuGetWikiSpace` 获取单个知识库空间的元信息（名称、可见性、空间类型），`spaceIdOrUrl` 接受与其它 wiki 工具一致的多种格式。
- `FeishuGetWikiNodeInfo` 支持「反查」：同时传入 `objType`（`docx` / `sheet` / `bitable` / `mindnote` / `file` / `slides`）和对象 token（或对象 URL），即可定位承载它的 wiki 节点。不传 `objType`（或传 `objType="wiki"`）时保持原有的「wiki 节点 token」行为。
- `FeishuCreateWikiNode` 直接在知识库空间或父节点下创建节点，支持 `objType`（`docx` / `sheet` / `bitable` / `mindnote` / `slides` / `file`）、`nodeType`（`origin` / `shortcut`）和可选的 `title`。`docx` 节点的标题会通过后续 `update_title` 调用生效（docx 建节点接口忽略请求体中的 title），其它类型直接在请求体里带 title。
- 文档与测试中统一使用 `example.feishu.cn` 作为中立的占位域名。

## 能力-权限矩阵

| 能力 | OpenAPI / 接口面 | 典型权限范围 | 是否依赖 Bot 能力 |
|---|---|---|---|
| 实时入站事件 | 长连接事件订阅 | 接收入站消息事件所需的订阅权限 | 是 |
| 历史消息读取 `listChatMessages` | `GET /open-apis/im/v1/messages` | 历史消息读取权限，如 `im:message:readonly` | 通常需要 |
| 文本发送 `sendTextMessage` | `POST /open-apis/im/v1/messages` | 发送消息权限，如 `im:message:send` | 是 |
| 消息回复 `replyToMessage` | `POST /open-apis/im/v1/messages/{message_id}/reply` | 发送 / 回复消息权限，如 `im:message:send` | 是 |
| 交互式卡片 | `im/v1/messages` 创建与更新 | 交互消息发送 / 更新权限 | 是 |
| 文件上传 / 发送 | `im/v1/files`、`im/v1/messages` | 文件/媒体上传权限与消息发送权限 | 是 |
| 图片下载 | `im/v1/messages/{message_id}/resources` | 消息资源读取权限，如 `im:resource` | 通常需要 |
| 表情 reaction | `im/v1/messages/{message_id}/reactions` | reaction 相关权限 | 是 |
| 创建 docx 文档 `createDocxDocument` | `POST /open-apis/docx/v1/documents` | `docx:document` 或 `docx:document:create` | 否 |
| 读取 docx 纯文本 `getDocxRawContent` | `GET /open-apis/docx/v1/documents/{document_id}/raw_content` | `docx:document` 或 `docx:document:readonly` | 否 |
| 追加 docx block `createDocxBlocks` | `POST /open-apis/docx/v1/documents/{document_id}/blocks/{block_id}/children` | `docx:document` 或 `docx:document:write_only` | 否 |
| 列出 docx 评论卡片 `listDocxComments` | `GET /open-apis/drive/v1/files/{file_token}/comments?file_type=docx` | `docs:document.comment:read` | 否 |
| 批量查询 docx 评论 `batchQueryDocxComments` | `POST /open-apis/drive/v1/files/{file_token}/comments/batch_query?file_type=docx` | `docs:document.comment:read` | 否 |
| 列出 docx 评论回复 `listDocxCommentReplies` | `GET /open-apis/drive/v1/files/{file_token}/comments/{comment_id}/replies?file_type=docx` | `docs:document.comment:read` | 否 |
| 创建 docx 评论 `createDocxComment` | `POST /open-apis/drive/v1/files/{file_token}/new_comments` | `docs:document.comment:create` | 否 |
| 创建 docx 评论回复 `createDocxCommentReply` | `POST /open-apis/drive/v1/files/{file_token}/comments/{comment_id}/replies?file_type=docx` | `docs:document.comment:create` | 否 |
| 解决/恢复 docx 评论 `patchDocxCommentSolved` | `PATCH /open-apis/drive/v1/files/{file_token}/comments/{comment_id}?file_type=docx` | `docs:document.comment:update` | 否 |
| 创建知识库节点 `createWikiNode` | `POST /open-apis/wiki/v2/spaces/{space_id}/nodes` | `wiki:wiki` 或 `wiki:node:create` | 否 |
| 查询知识库节点 `getWikiNode` | `GET /open-apis/wiki/v2/spaces/get_node` | `wiki:wiki` 或 `wiki:wiki:readonly` | 否 |
| 列出知识库子节点 `listWikiNodes` | `GET /open-apis/wiki/v2/spaces/{space_id}/nodes` | `wiki:wiki` 或 `wiki:wiki:readonly` | 否 |
| 列出知识库空间 `listWikiSpaces` | `GET /open-apis/wiki/v2/spaces` | `wiki:wiki` 或 `wiki:wiki:readonly` 或 `wiki:space:retrieve` | 否 |
| 查询知识库空间 `getWikiSpace` | `GET /open-apis/wiki/v2/spaces/{space_id}` | `wiki:wiki` 或 `wiki:wiki:readonly` 或 `wiki:space:read` | 否 |
| 移动 docx 到知识库 `moveDocxToWiki` | `POST /open-apis/wiki/v2/spaces/{space_id}/nodes/move_docs_to_wiki` | `wiki:wiki`（并且源文档需具备编辑权限） | 否 |
| 查询知识库移动任务 `getWikiMoveTask` | `GET /open-apis/wiki/v2/tasks/{task_id}?task_type=move` | `wiki:wiki` 或 `wiki:wiki:readonly` | 否 |
| 移动知识库节点 `moveWikiNode` | `POST /open-apis/wiki/v2/spaces/{space_id}/nodes/{node_token}/move` | `wiki:wiki`（并且源节点与目标父节点都需具备编辑权限） | 否 |
| 后续模板复制 | `POST /open-apis/drive/v1/files/{file_token}/copy` | `docs:document:copy` 或 `drive:drive` | 否 |

说明：

- 上表描述的是公共适配层依赖，不代表租户一定已经开通了这些权限。
- 即使公共 API 已封装，租户策略、应用发布状态或 Bot 能力状态仍可能阻塞能力调用。
- 历史消息读取是否可用，最终取决于租户是否授予对应读取权限；本包只负责 API 封装。
- 飞书文档类 API 除了 scope，还要求目标文档或文件夹资源已经授权给应用；如果资源本身没有共享给应用，即使 scope 正确也常会返回 `403`。
- 知识库 API 同样要求目标知识空间/父节点已共享给应用；即使 `space_id` 与 scope 正确，如果节点资源未授权，也常会返回 `131006/131008` 一类权限错误。
- 如果把 wiki 节点 URL/token 传给 `spaceIdOrUrl`，每次调用都会额外触发一次 `GET /open-apis/wiki/v2/spaces/get_node` 反查；请确保应用具备 `wiki:wiki` 或至少 `wiki:wiki:readonly` 权限。

## 常见问题：`code=131006 tenant needs edit permission`

现象：调用 wiki 类工具（包括 `FeishuCreateDocxAndShareToCurrentChat` 走 wiki 分支、`FeishuCreateWikiNode`、`FeishuMoveDocxToWiki`、`FeishuMoveWikiNode` 等）时失败，`FeishuApiError` 返回 `code=131006`，错误消息形如 `permission denied: no destination parent node permission` 或 `tenant needs edit permission`。

根因：这是**资源层**权限，不是 OpenAPI scope 问题。即使应用已经获得 `wiki:wiki` / `wiki:node:create` 等 scope，只要机器人用户没有被显式加到目标 wiki 空间（或父节点）并授予可编辑/可管理角色，飞书就会直接拒绝写入操作。Scope 与资源授权是独立的两道门。

处理步骤：

1. 在飞书 Web 端打开目标知识库空间（或具体父节点），进入「成员管理 / Members」。
2. 搜索当前应用名，把应用机器人添加为「可编辑 / 可管理」角色。需要整个空间可写就加在空间层；仅需对某个子树生效，可只加在节点层。
3. 对 `FeishuMoveDocxToWiki`：源 docx 也必须让机器人有读写权限——请把机器人加到源文档协作者列表。
4. 再次运行工具，`code=131006` 应当消失。若错误码变成 `99991672`，说明 OpenAPI scope 缺失（回到上文权限矩阵对照补齐）；若变成 `131008`，表示节点存在但处于锁定/归档态，请在飞书 UI 中确认节点状态。

参考文档：[飞书 wiki.spaces.get_node](https://open.feishu.cn/document/server-docs/docs/wiki-v2/space-node/get_node)。

## 认证 / 登录模型

本适配器不使用微信示例那种二维码登录流程。

飞书 Bot 使用静态应用凭据模型：

- `appId`
- `appSecret`

适配器会基于 `appId` + `appSecret` 显式获取 tenant access token，并用它访问 bot probe 与消息 API，然后再开始监听事件。

## 群聊 @ 提及说明（多机器人/多应用）

飞书 `open_id` 是 app-scoped（按应用隔离）。在多机器人群聊里，WebSocket 事件中的 mention 身份有时会和机器人自身份不一致。

本适配器采用轻量缓解策略：

- 先匹配 `mention.id.open_id === botOpenId`
- 当 `mention.name` 和 `botName` 都可用时，再要求名称一致

这能改善多数场景，但并不是跨应用身份问题的绝对解法。

## 致谢

[larksuite/openclaw-lark](https://github.com/larksuite/openclaw-lark)
