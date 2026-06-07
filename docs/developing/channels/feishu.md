# DotCraft Feishu Channel Adapter

`@dotcraft/channel-feishu` connects a Feishu/Lark bot to DotCraft through the external channel adapter protocol over WebSocket.

It is built on:

- `@dotcraft/sdk` for DotCraft AppServer JSON-RPC protocol
- `@larksuiteoapi/node-sdk` for Feishu bot APIs and WebSocket events

## What This Adapter Supports

- Feishu WebSocket event subscription
- Startup bot probe with explicit tenant token authorization
- DotCraft thread reuse via external channel identity
- `/new` to start a fresh DotCraft thread
- Group chats that only respond when the bot is @mentioned
- One shared DotCraft thread per Feishu group chat, with the sender recorded on each turn
- Immediate reaction on handled inbound messages
- Interactive approval cards with buttons
- Static reply cards after `turn/completed`
- Image input forwarding to DotCraft as `localImage`
- Optional docx + wiki channel tools for create/read/update/insert/delete/media-embed/list/get/move/rename
- Public `FeishuClient.sendTextMessage(...)` and `replyToMessage(...)`

## What This Adapter Does Not Cover

- Multi-account Feishu configuration
- Feishu webhook mode
- Streaming card updates
- User-level OAuth / Open Platform authorization flows

## Prerequisites

1. Node.js `>= 20`
2. A running DotCraft AppServer with WebSocket enabled
3. A Feishu self-built app with bot capability enabled

## 1. Enable DotCraft External Channel

`config.example.json` in this directory is the DotCraft workspace config snippet.

Merge it into your workspace `.craft/config.json`:

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

## 2. Create the Feishu App

In the Feishu Developer Console:

1. Create a self-built app
2. Enable the Bot capability
3. Enable event subscription over long connection/WebSocket
4. Add the bot/message related permissions you need

Recommended minimum bot-side permissions:

- `im:message`
- `im:message:send`
- Message reaction permission required by `im/v1/messages/:message_id/reactions`
- `im:resource`
- `im:chat`

Then collect:

- `appId`
- `appSecret`

## 3. Configure the Adapter

Create `.craft/feishu.json` inside your target workspace:

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

Notes:

- `feishu.cardTitle`: brand text used in reply/progress/transcript card headers and approval prompt text; defaults to `DotCraft`
- `ackReactionEmoji` must be a Feishu official `emoji_type` such as `GLANCE`, `SMILE`, `OnIt`
- `downloadDir` is used for temporary image files before forwarding to DotCraft
- `feishu.tools.docs.enabled`: registers Feishu docx + wiki channel tools as one group. Restart the module after changing it, because channel tools are declared during initialization
- `feishu.debug.adapterStream` / `feishu.debug.textMerge`: set either to `true` only when diagnosing adapter behavior; both stay `false` in normal use

## 4. Install and Build

```bash
cd sdk/typescript
npm install
npm run build:all
```

## 5. Run

Primary mode:

```bash
npx dotcraft-channel-feishu --workspace /path/to/workspace
```

Optional config override:

```bash
npx dotcraft-channel-feishu --workspace /path/to/workspace --config /custom/feishu.json
```

## Behavior Notes

- DM: always handled
- Group: handled only when the bot is mentioned, unless `groupMentionRequired` is `false`
- Thread identity: DMs are scoped to the sender; group chats are scoped to the Feishu chat id.
- Inbound ack: after filtering/parsing, the adapter adds the configured reaction first
- Commands: `/new` archives the current thread and starts a new one
- Approvals: rendered as interactive cards
- Replies: sent as static interactive cards after the turn finishes
- Docx tools (`documentIdOrUrl`) accept a raw docx token, a docx URL (`/docx/<token>`), or a wiki node URL/token that points to a docx-backed node. When you copy a link from Feishu, make sure it points to a docx document
- Docx block primitives: `FeishuListDocxBlocks`, `FeishuGetDocxBlock`, `FeishuInsertDocxBlocks`, `FeishuUpdateDocxBlocks`, `FeishuDeleteDocxBlocks`
- High-level edit tool `FeishuUpdateDocxContent` supports `append`, `overwrite`, `replaceRange`, `replaceAll`, `insertBefore`, `insertAfter`, `deleteRange`, with optional `newTitle`
- Media tool `FeishuEmbedDocxMedia` uploads a local image/file and inserts it into a docx block flow, rolling back on downstream failure
- Title tools: `FeishuUpdateDocxTitle` and wiki node rename `FeishuRenameWikiNode`
- Docx comment tools: `FeishuListDocxComments`, `FeishuBatchQueryDocxComments`, `FeishuListDocxCommentReplies`, `FeishuAddDocxComment`, `FeishuAddDocxCommentReply`, `FeishuResolveDocxComment`
- Wiki tools (`spaceIdOrUrl`) accept a numeric `space_id`, a wiki settings URL (`/wiki/settings/<space_id>`), or a wiki node URL/token; node URLs/tokens are auto-resolved by calling `getWikiNode` and default that node as parent when parent is omitted
- `FeishuMoveDocxToWiki` aligns with the official Lark CLI: when the API returns a `task_id` (async path), the tool polls `GET /open-apis/wiki/v2/tasks/{task_id}?task_type=move` up to 30 times at 2 s intervals (~60 s window). On success it returns `ready=true` with the resolved `wikiToken`; on timeout it returns `ready=false, timedOut=true, taskId` so the caller can re-query later. Pass `waitForCompletion: false` to skip polling and get the raw `taskId` immediately.
- `FeishuMoveWikiNode` moves an existing wiki node inside or across wiki spaces. Provide `nodeTokenOrUrl` plus at least one of `targetParentTokenOrUrl` / `targetSpaceIdOrUrl`; when both targets are given, the tool verifies they belong to the same space and otherwise raises `InconsistentWikiTarget`.
- `FeishuListWikiSpaces` enumerates wiki spaces the app identity can access (wiki discovery), so the agent no longer needs the user to hand over a `space_id` manually. Paginate with `pageSize` / `pageToken`.
- `FeishuGetWikiSpace` returns metadata (name, visibility, space type) for a single wiki space; accepts the same `spaceIdOrUrl` formats as other wiki tools.
- `FeishuGetWikiNodeInfo` supports reverse lookup: pass `objType` (`docx` / `sheet` / `bitable` / `mindnote` / `file` / `slides`) together with the object token (or object URL) to find the wiki node that hosts it. Without `objType` (or with `objType="wiki"`) the tool keeps its previous wiki-node-token behavior.
- `FeishuCreateWikiNode` creates a wiki node directly under a wiki space or parent node. It supports `objType` (`docx` / `sheet` / `bitable` / `mindnote` / `slides` / `file`), `nodeType` (`origin` or `shortcut`), and an optional `title`. For `docx` nodes the title is applied via a follow-up `update_title` call (the docx create API ignores body titles); for other types the title is sent inline.

## Capability Permission Matrix

| Capability | OpenAPI / Surface | Typical permission scope | Bot capability required |
|---|---|---|---|
| Real-time inbound events | Event subscription over long connection | Message/event subscription permissions for inbound receive events | Yes |
| History message read `listChatMessages` | `GET /open-apis/im/v1/messages` | Historical message read scope such as `im:message:readonly` | Usually yes |
| Send text `sendTextMessage` | `POST /open-apis/im/v1/messages` | Message send scope such as `im:message:send` | Yes |
| Reply to message `replyToMessage` | `POST /open-apis/im/v1/messages/{message_id}/reply` | Message send / reply scope such as `im:message:send` | Yes |
| Interactive cards | `im/v1/messages` create + patch | Message send/update permissions for interactive messages | Yes |
| File upload / send | `im/v1/files`, `im/v1/messages` | File/media upload plus message send permissions | Yes |
| Image download | `im/v1/messages/{message_id}/resources` | Message resource read scope such as `im:resource` | Usually yes |
| Reaction | `im/v1/messages/{message_id}/reactions` | Reaction-specific permission granted to the app | Yes |
| Create docx `createDocxDocument` | `POST /open-apis/docx/v1/documents` | `docx:document` or `docx:document:create` | No |
| Read docx raw content `getDocxRawContent` | `GET /open-apis/docx/v1/documents/{document_id}/raw_content` | `docx:document` or `docx:document:readonly` | No |
| Append docx blocks `createDocxBlocks` | `POST /open-apis/docx/v1/documents/{document_id}/blocks/{block_id}/children` | `docx:document` or `docx:document:write_only` | No |
| List docx comments `listDocxComments` | `GET /open-apis/drive/v1/files/{file_token}/comments?file_type=docx` | `docs:document.comment:read` | No |
| Batch query docx comments `batchQueryDocxComments` | `POST /open-apis/drive/v1/files/{file_token}/comments/batch_query?file_type=docx` | `docs:document.comment:read` | No |
| List docx comment replies `listDocxCommentReplies` | `GET /open-apis/drive/v1/files/{file_token}/comments/{comment_id}/replies?file_type=docx` | `docs:document.comment:read` | No |
| Create docx comment `createDocxComment` | `POST /open-apis/drive/v1/files/{file_token}/new_comments` | `docs:document.comment:create` | No |
| Create docx comment reply `createDocxCommentReply` | `POST /open-apis/drive/v1/files/{file_token}/comments/{comment_id}/replies?file_type=docx` | `docs:document.comment:create` | No |
| Resolve/unresolve docx comment `patchDocxCommentSolved` | `PATCH /open-apis/drive/v1/files/{file_token}/comments/{comment_id}?file_type=docx` | `docs:document.comment:update` | No |
| Create wiki node `createWikiNode` | `POST /open-apis/wiki/v2/spaces/{space_id}/nodes` | `wiki:wiki` or `wiki:node:create` | No |
| Get wiki node `getWikiNode` | `GET /open-apis/wiki/v2/spaces/get_node` | `wiki:wiki` or `wiki:wiki:readonly` | No |
| List wiki child nodes `listWikiNodes` | `GET /open-apis/wiki/v2/spaces/{space_id}/nodes` | `wiki:wiki` or `wiki:wiki:readonly` | No |
| List wiki spaces `listWikiSpaces` | `GET /open-apis/wiki/v2/spaces` | `wiki:wiki` or `wiki:wiki:readonly` or `wiki:space:retrieve` | No |
| Get wiki space `getWikiSpace` | `GET /open-apis/wiki/v2/spaces/{space_id}` | `wiki:wiki` or `wiki:wiki:readonly` or `wiki:space:read` | No |
| Move docx to wiki `moveDocxToWiki` | `POST /open-apis/wiki/v2/spaces/{space_id}/nodes/move_docs_to_wiki` | `wiki:wiki` (+ source docx edit permission) | No |
| Wiki move task status `getWikiMoveTask` | `GET /open-apis/wiki/v2/tasks/{task_id}?task_type=move` | `wiki:wiki` or `wiki:wiki:readonly` | No |
| Move wiki node `moveWikiNode` | `POST /open-apis/wiki/v2/spaces/{space_id}/nodes/{node_token}/move` | `wiki:wiki` (+ edit permission on source & target parent nodes) | No |

Notes:

- The matrix above documents public adapter dependencies, not a guarantee that any tenant has already enabled them.
- Feishu tenant policy and app publication state can still block a capability even when the API wrapper exists.
- History read support depends on the tenant granting the required read scope; this package only wraps the API.
- Feishu doc APIs also require the target document or folder resource to be shared with the app. Missing resource-level authorization commonly returns `403` even when the scope itself is present.
- Wiki APIs need resource-level access on top of scopes: add the app's bot as a member of the target wiki space (or parent node) with an **edit/manage** role. With the scope present but the node unshared, Feishu returns `131006/131008` permission errors. Add the bot at the space level for space-wide write access, or at a single node to scope the grant to one subtree. For `moveDocxToWiki`, also make the bot a collaborator on the source docx.
- If you pass wiki node URLs/tokens into `spaceIdOrUrl`, each call performs one extra `GET /open-apis/wiki/v2/spaces/get_node` lookup; ensure the app has `wiki:wiki` or at least `wiki:wiki:readonly` for this lookup path.

## Auth / Login Model

This adapter does not use a QR login flow like the WeChat example.

Feishu bots use a static app credential model:

- `appId`
- `appSecret`

The adapter obtains a tenant access token from `appId` + `appSecret` and uses it explicitly for bot probe and message APIs before listening for events.

## Credits

[larksuite/openclaw-lark](https://github.com/larksuite/openclaw-lark)

## Related docs

- [Channels & Bots](../../features/entry-points/channels) — overview of every chat channel.
- [Channel adapters](../sdks/channels) — the adapter base class and wire contract.
- [TypeScript Module Integration](../integrations/typescript-module) — the host contract these modules implement.
